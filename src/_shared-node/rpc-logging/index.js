/*
 * Shared per-RPC structured JSON logging interceptor for Node.js gRPC services.
 * Added by M2.1 (microservice-changes-todo.md). Used by paymentservice
 * and currencyservice.
 *
 * Emits one log line per inbound RPC, matching the field shape used by the
 * Go, .NET, Python, and Java shared interceptors:
 *
 *   {"trace_id":"...","span_id":"...","method":"/hipstershop.PaymentService/Charge",
 *    "peer_service":"<addr>","latency_ms":42,"status_code":"OK","kind":"rpc.server"}
 *
 * Field discipline matches microservice-changes.md L1 specification.
 * Never includes scenario_id, fault_*, triage_*, or DATASET_RUN_ID.
 */

const grpc = require('@grpc/grpc-js');
const otelApi = require('@opentelemetry/api');

const GRPC_STATUS_TO_NAME = {
  0: 'OK', 1: 'CANCELLED', 2: 'UNKNOWN', 3: 'INVALID_ARGUMENT',
  4: 'DEADLINE_EXCEEDED', 5: 'NOT_FOUND', 6: 'ALREADY_EXISTS',
  7: 'PERMISSION_DENIED', 8: 'RESOURCE_EXHAUSTED', 9: 'FAILED_PRECONDITION',
  10: 'ABORTED', 11: 'OUT_OF_RANGE', 12: 'UNIMPLEMENTED', 13: 'INTERNAL',
  14: 'UNAVAILABLE', 15: 'DATA_LOSS', 16: 'UNAUTHENTICATED'
};

function statusName(code) {
  if (code === undefined || code === null) return 'OK';
  return GRPC_STATUS_TO_NAME[code] || String(code);
}

/**
 * Wrap a grpc handler function with per-RPC structured logging.
 *
 *   server.addService(SvcPackage.MyService.service, {
 *     charge: wrap(logger, '/hipstershop.PaymentService/Charge',
 *                  HipsterShopServer.ChargeServiceHandler),
 *   });
 *
 * Returns a function with the same (call, callback) signature.
 */
function wrap(logger, fullMethod, handler) {
  return function wrappedHandler(call, callback) {
    const start = process.hrtime.bigint();
    const peer = (call && call.getPeer && call.getPeer()) || 'unknown';

    function emit(statusCode, errClass) {
      const latencyMs = Number(process.hrtime.bigint() - start) / 1e6;
      const span = otelApi.trace.getActiveSpan();
      const ctx = span && span.spanContext && span.spanContext();
      const fields = {
        method: fullMethod,
        peer_service: peer,
        latency_ms: Math.round(latencyMs),
        status_code: statusCode,
        kind: 'rpc.server',
      };
      if (ctx && ctx.traceId) fields.trace_id = ctx.traceId;
      if (ctx && ctx.spanId) fields.span_id = ctx.spanId;
      if (errClass) fields.err_class = errClass;
      logger.info(fields, 'rpc');
    }

    function wrappedCallback(err, response) {
      if (err) {
        const code = err.code !== undefined ? err.code : 2; // UNKNOWN
        emit(statusName(code), statusName(code));
      } else {
        emit('OK', null);
      }
      callback(err, response);
    }

    try {
      handler(call, wrappedCallback);
    } catch (ex) {
      emit('INTERNAL', ex.name || 'Error');
      throw ex;
    }
  };
}

module.exports = { wrap, statusName };
