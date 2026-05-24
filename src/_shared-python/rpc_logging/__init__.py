"""Shared per-RPC structured JSON logging interceptor for Python gRPC services.

Added by M2.1 (microservice-changes-todo.md). Used by recommendationservice
and emailservice.

Emits one log line per inbound RPC, matching the field shape used by the
Go, .NET, Node, and Java shared interceptors:

    {"trace_id":"...","span_id":"...",
     "method":"/hipstershop.RecommendationService/ListRecommendations",
     "peer_service":"<peer>","latency_ms":42,
     "status_code":"OK","kind":"rpc.server"}

Field discipline matches microservice-changes.md L1 specification.
Never includes scenario_id, fault_*, triage_*, or DATASET_RUN_ID.
"""

from __future__ import annotations

import time

import grpc
from opentelemetry import trace as otel_trace


def _status_name(code: object) -> str:
    """Return a stable string name for a gRPC status code."""
    if code is None:
        return "OK"
    try:
        return grpc.StatusCode(code).name
    except (ValueError, TypeError):
        return str(code)


class RpcLoggingInterceptor(grpc.ServerInterceptor):
    """gRPC server interceptor that emits one structured log per RPC.

    Usage:
        server = grpc.server(..., interceptors=[RpcLoggingInterceptor(logger)])
    """

    def __init__(self, logger):
        self._log = logger

    def intercept_service(self, continuation, handler_call_details):
        handler = continuation(handler_call_details)
        if handler is None:
            return None
        method = handler_call_details.method

        # The unary-unary path covers every RPC the demo services expose. If
        # streaming is added later, mirror this for the other variants.
        if not handler.unary_unary:
            return handler

        log = self._log

        def wrapped_unary(request, context):
            start = time.monotonic()
            status_code = "OK"
            err_class = ""
            try:
                response = handler.unary_unary(request, context)
                return response
            except grpc.RpcError as exc:
                status_code = _status_name(exc.code())
                err_class = status_code
                raise
            except Exception as exc:  # noqa: BLE001
                status_code = "INTERNAL"
                err_class = type(exc).__name__
                raise
            finally:
                latency_ms = int((time.monotonic() - start) * 1000)
                span_ctx = otel_trace.get_current_span().get_span_context()
                fields = {
                    "method": method,
                    "peer_service": context.peer() if context else "unknown",
                    "latency_ms": latency_ms,
                    "status_code": status_code,
                    "kind": "rpc.server",
                }
                if span_ctx and span_ctx.is_valid:
                    fields["trace_id"] = format(span_ctx.trace_id, "032x")
                    fields["span_id"] = format(span_ctx.span_id, "016x")
                if err_class:
                    fields["err_class"] = err_class
                # The JSON logger used by recommendationservice / emailservice
                # turns the `extra` dict into top-level JSON keys.
                log.info("rpc", extra=fields)

        return grpc.unary_unary_rpc_method_handler(
            wrapped_unary,
            request_deserializer=handler.request_deserializer,
            response_serializer=handler.response_serializer,
        )
