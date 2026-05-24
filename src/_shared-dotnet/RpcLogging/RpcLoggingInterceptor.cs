// Copyright 2026 Hipstershop research fork.
//
// Shared per-RPC structured JSON logging interceptor for .NET gRPC services.
// Added by M2.1 (see microservice-changes-todo.md). Used by cartservice.
//
// Emits one log line per RPC, matching the field shape used by the Go,
// Node, Python, and Java shared interceptors:
//
//   {"trace_id":"...","span_id":"...","method":"/h.CartService/AddItem",
//    "peer_service":"...","latency_ms":42,"status_code":"OK","kind":"rpc.server"}
//
// Field discipline matches microservice-changes.md L1 specification.
// Never includes scenario_id, fault_*, triage_*, or DATASET_RUN_ID.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace; // for Activity.RecordException extension method

namespace Hipstershop.RpcLogging
{
    /// <summary>
    /// gRPC server interceptor that logs one JSON line per inbound RPC.
    /// Register via: services.AddGrpc(o => o.Interceptors.Add&lt;RpcLoggingInterceptor&gt;());
    /// </summary>
    public class RpcLoggingInterceptor : Interceptor
    {
        private readonly ILogger<RpcLoggingInterceptor> _logger;

        public RpcLoggingInterceptor(ILogger<RpcLoggingInterceptor> logger)
        {
            _logger = logger;
        }

        public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
            TRequest request,
            ServerCallContext context,
            UnaryServerMethod<TRequest, TResponse> continuation)
        {
            var sw = Stopwatch.StartNew();
            string statusCode = "OK";
            string errClass = "";
            try
            {
                var response = await continuation(request, context);
                return response;
            }
            catch (RpcException ex)
            {
                statusCode = ex.StatusCode.ToString();
                errClass = ex.StatusCode.ToString();
                // M3.1: enrich the active span so Tempo sees the error,
                // not just the L1 log line.
                var act = Activity.Current;
                if (act != null)
                {
                    act.RecordException(ex);
                    act.SetStatus(ActivityStatusCode.Error, ex.StatusCode.ToString());
                }
                throw;
            }
            catch (Exception ex)
            {
                statusCode = "INTERNAL";
                errClass = ex.GetType().Name;
                var act = Activity.Current;
                if (act != null)
                {
                    act.RecordException(ex);
                    act.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
                }
                throw;
            }
            finally
            {
                sw.Stop();
                var activity = Activity.Current;
                var traceId = activity?.TraceId.ToString() ?? "";
                var spanId = activity?.SpanId.ToString() ?? "";

                // D13.14d-followup: emit via a structured-logging message
                // template so the JsonConsole formatter renders each named
                // parameter as a top-level JSON key. With the default text
                // formatter these were stripped — see the M5.2d cross-check
                // gap. Field names match the Go/Node/Python shape.
                _logger.LogInformation(
                    "rpc method={method} status_code={status_code} latency_ms={latency_ms} peer_service={peer_service} kind={kind} err_class={err_class} trace_id={trace_id} span_id={span_id}",
                    context.Method,
                    statusCode,
                    sw.ElapsedMilliseconds,
                    context.Peer ?? "unknown",
                    "rpc.server",
                    errClass,
                    traceId,
                    spanId);
            }
        }
    }
}
