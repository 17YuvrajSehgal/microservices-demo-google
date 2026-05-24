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

                // Structured log via Microsoft.Extensions.Logging. The JSON
                // console formatter on the cartservice host will emit each
                // field as a top-level JSON key, matching the Go/Node/Python
                // shape.
                _logger.Log(
                    LogLevel.Information,
                    new EventId(1, "rpc"),
                    new RpcLogState(
                        traceId: traceId,
                        spanId: spanId,
                        method: context.Method,
                        peerService: context.Peer ?? "unknown",
                        latencyMs: sw.ElapsedMilliseconds,
                        statusCode: statusCode,
                        errClass: errClass,
                        kind: "rpc.server"),
                    exception: null,
                    formatter: (state, _) => state.ToString());
            }
        }
    }

    /// <summary>
    /// Strongly-typed log state so the JSON console formatter emits each
    /// field as a top-level JSON key.
    /// </summary>
    internal sealed class RpcLogState
    {
        public string trace_id { get; }
        public string span_id { get; }
        public string method { get; }
        public string peer_service { get; }
        public long latency_ms { get; }
        public string status_code { get; }
        public string err_class { get; }
        public string kind { get; }

        public RpcLogState(string traceId, string spanId, string method,
            string peerService, long latencyMs, string statusCode,
            string errClass, string kind)
        {
            trace_id = traceId;
            span_id = spanId;
            this.method = method;
            peer_service = peerService;
            latency_ms = latencyMs;
            status_code = statusCode;
            err_class = errClass;
            this.kind = kind;
        }

        public override string ToString() =>
            $"rpc method={method} status={status_code} latency_ms={latency_ms}";
    }
}
