using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using cartservice.cartstore;
using cartservice.services;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using StackExchange.Redis;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Instrumentation.StackExchangeRedis;
using Hipstershop.RpcLogging;

namespace cartservice
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            string redisAddress = Configuration["REDIS_ADDR"];
            string spannerProjectId = Configuration["SPANNER_PROJECT"];
            string spannerConnectionString = Configuration["SPANNER_CONNECTION_STRING"];
            string alloyDBConnectionString = Configuration["ALLOYDB_PRIMARY_IP"];

            if (!string.IsNullOrEmpty(redisAddress))
            {
                // Register a singleton IConnectionMultiplexer so the
                // OTel StackExchange.Redis instrumentation can hook
                // every command. Microsoft.Extensions.Caching.StackExchangeRedis
                // otherwise uses an internal connection that the
                // instrumentation cannot see.
                var redisMux = ConnectionMultiplexer.Connect(redisAddress);
                services.AddSingleton<IConnectionMultiplexer>(redisMux);
                services.AddStackExchangeRedisCache(options =>
                {
                    options.ConnectionMultiplexerFactory =
                        () => Task.FromResult<IConnectionMultiplexer>(redisMux);
                });
                services.AddSingleton<ICartStore, RedisCartStore>();
            }
            else if (!string.IsNullOrEmpty(spannerProjectId) || !string.IsNullOrEmpty(spannerConnectionString))
            {
                services.AddSingleton<ICartStore, SpannerCartStore>();
            }
            else if (!string.IsNullOrEmpty(alloyDBConnectionString))
            {
                Console.WriteLine("Creating AlloyDB cart store");
                services.AddSingleton<ICartStore, AlloyDBCartStore>();
            }
            else
            {
                Console.WriteLine("Redis cache host(hostname+port) was not specified. Starting a cart service using in memory store");
                services.AddDistributedMemoryCache();
                services.AddSingleton<ICartStore, RedisCartStore>();
            }


            // M2.1: register the shared RPC logging interceptor for every
            // gRPC server method.
            services.AddGrpc(options =>
            {
                options.Interceptors.Add<RpcLoggingInterceptor>();
            });
            services.AddSingleton<RpcLoggingInterceptor>();

            // ---------------------------------------------------------------
            // OpenTelemetry (added by M1.1; see microservice-changes-todo.md)
            //
            // Wires:
            //   - W3C TraceContext + Baggage propagator (matches the rest of
            //     the fleet)
            //   - AspNetCore + GrpcNetClient + StackExchangeRedis auto-spans
            //   - AlwaysOnSampler (research density; documented in M3.4)
            //   - OTLP gRPC exporter to COLLECTOR_SERVICE_ADDR
            //   - Prometheus /metrics endpoint (used in M4)
            //   - Runtime metrics (process_cpu, memory, GC) for M4.5
            //
            // NOTE: no scenario.id, fault.injected, or triage-leaking fields
            // appear in resource attributes. Compliance with the bias-avoidance
            // list in microservice-changes.md.
            // ---------------------------------------------------------------

            var enableTracing = Configuration["ENABLE_TRACING"] == "1"
                || string.Equals(Configuration["ENABLE_TRACING"], "true",
                    StringComparison.OrdinalIgnoreCase);

            if (enableTracing)
            {
                var collectorEndpoint = Configuration["COLLECTOR_SERVICE_ADDR"]
                    ?? "opentelemetrycollector.observability.svc.cluster.local:4317";
                if (!collectorEndpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    && !collectorEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    collectorEndpoint = "http://" + collectorEndpoint;
                }

                var serviceVersion = Environment.GetEnvironmentVariable("SERVICE_VERSION")
                    ?? "0.0.0-dev";

                var resourceBuilder = ResourceBuilder.CreateDefault()
                    .AddService(serviceName: "cartservice", serviceVersion: serviceVersion)
                    .AddAttributes(new[]
                    {
                        new KeyValuePair<string, object>("service.namespace", "online-boutique"),
                        new KeyValuePair<string, object>("deployment.environment",
                            Environment.GetEnvironmentVariable("DEPLOYMENT_ENVIRONMENT") ?? "research-local"),
                    });

                services.AddOpenTelemetry()
                    .ConfigureResource(rb => rb
                        .AddService(serviceName: "cartservice", serviceVersion: serviceVersion))
                    .WithTracing(tracing =>
                    {
                        tracing
                            .SetResourceBuilder(resourceBuilder)
                            .SetSampler(new AlwaysOnSampler())
                            .AddAspNetCoreInstrumentation()
                            .AddGrpcClientInstrumentation()
                            .AddRedisInstrumentation(options =>
                            {
                                options.SetVerboseDatabaseStatements = false;
                                options.EnrichActivityWithTimingEvents = true;
                            })
                            .AddSource("cartservice")
                            .AddOtlpExporter(otlp =>
                            {
                                otlp.Endpoint = new Uri(collectorEndpoint);
                                otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                            });
                    })
                    .WithMetrics(metrics =>
                    {
                        metrics
                            .SetResourceBuilder(resourceBuilder)
                            .AddAspNetCoreInstrumentation()
                            .AddRuntimeInstrumentation()
                            .AddMeter("cartservice")
                            .AddPrometheusExporter();
                    });
            }
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            // Prometheus scrape endpoint at /metrics (M4.1).
            // Bound to the dedicated HTTP/1 Kestrel endpoint on port 9100
            // (see appsettings.json Kestrel.Endpoints.Metrics). The gRPC
            // server stays HTTP/2-only on port 7070, so a Prometheus
            // HTTP/1 scrape against /metrics on 9100 is what works.
            //
            // Guarded by the same ENABLE_TRACING gate that wires OTel in
            // ConfigureServices. Without the gate, this throws at startup
            // when ENABLE_TRACING is unset because MeterProvider is missing
            // from DI.
            var enableTracing = Configuration["ENABLE_TRACING"] == "1"
                || string.Equals(Configuration["ENABLE_TRACING"], "true",
                    StringComparison.OrdinalIgnoreCase);
            if (enableTracing)
            {
                app.UseOpenTelemetryPrometheusScrapingEndpoint(
                    context => context.Connection.LocalPort == 9100);
            }

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<CartService>();
                endpoints.MapGrpcService<cartservice.services.HealthCheckService>();

                endpoints.MapGet("/", async context =>
                {
                    await context.Response.WriteAsync("Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
                });
            });
        }
    }
}
