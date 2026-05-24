// Package rpclog provides shared gRPC interceptors that emit one structured
// JSON log line per RPC, with trace correlation fields.
//
// Added by M2.1 (microservice-changes-todo.md). Wired into the Go services
// (frontend, checkoutservice, productcatalogservice, shippingservice).
//
// Each log line carries:
//
//	{
//	  "trace_id":    "<W3C trace id>",
//	  "span_id":     "<W3C span id>",
//	  "method":      "/hipstershop.CartService/AddItem",
//	  "peer_service": "frontend",
//	  "latency_ms":  42,
//	  "status_code": "OK",
//	  "err_class":   ""   // only present when status != OK
//	}
//
// Field discipline matches microservice-changes.md L1 specification.
// Never include: scenario_id, fault_*, triage_*, or any DATASET_RUN_ID
// even though those env vars are visible to the container.
package rpclog

import (
	"context"
	"path"
	"strings"
	"sync"
	"time"

	"github.com/sirupsen/logrus"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/metric"
	"go.opentelemetry.io/otel/trace"
	"google.golang.org/grpc"
	"google.golang.org/grpc/peer"
	"google.golang.org/grpc/status"
)

// Logger is the minimal logrus interface we depend on. Accepting a *logrus.Logger
// directly avoids forcing each caller to wrap it.
type Logger = logrus.Logger

// M4.2 + M4.3: RED metrics. Initialized lazily on first interceptor build
// so callers that do not need metrics (tests) pay nothing.
var (
	metricsOnce       sync.Once
	rpcServerDuration metric.Float64Histogram
	rpcServerRequests metric.Int64Counter
	rpcClientDuration metric.Float64Histogram
	rpcClientErrors   metric.Int64Counter
)

func ensureMetrics() {
	metricsOnce.Do(func() {
		meter := otel.GetMeterProvider().Meter("hipstershop/rpclog")
		rpcServerDuration, _ = meter.Float64Histogram(
			"rpc_server_duration_seconds",
			metric.WithDescription("Latency of inbound RPCs in seconds."),
			metric.WithUnit("s"),
		)
		rpcServerRequests, _ = meter.Int64Counter(
			"rpc_server_requests_total",
			metric.WithDescription("Count of inbound RPCs by service, method, status."),
		)
		rpcClientDuration, _ = meter.Float64Histogram(
			"rpc_client_duration_seconds",
			metric.WithDescription("Latency of outbound RPCs in seconds."),
			metric.WithUnit("s"),
		)
		rpcClientErrors, _ = meter.Int64Counter(
			"rpc_client_errors_total",
			metric.WithDescription("Count of outbound RPC errors by peer_service, operation, status."),
		)
	})
}

// fields built into every log line. We pre-allocate the keys as constants so
// downstream tooling (the Drain-lite template miner) sees identical token
// templates across services.
const (
	keyTraceID     = "trace_id"
	keySpanID      = "span_id"
	keyMethod      = "method"
	keyPeerService = "peer_service"
	keyLatencyMs   = "latency_ms"
	keyStatusCode  = "status_code"
	keyErrClass    = "err_class"
	keyKind        = "kind" // "rpc.server" or "rpc.client"
)

// UnaryServerInterceptor returns a grpc.UnaryServerInterceptor that logs one
// JSON line per inbound RPC AND records RED metrics (M4.2).
func UnaryServerInterceptor(log *Logger) grpc.UnaryServerInterceptor {
	ensureMetrics()
	return func(
		ctx context.Context,
		req any,
		info *grpc.UnaryServerInfo,
		handler grpc.UnaryHandler,
	) (any, error) {
		start := time.Now()
		resp, err := handler(ctx, req)
		dur := time.Since(start).Seconds()
		statusCode := "OK"
		if err != nil {
			s, _ := status.FromError(err)
			statusCode = s.Code().String()
		}
		// Bounded labels: method comes from proto (bounded), status is the
		// gRPC enum. No scenario/fault labels.
		attrs := metric.WithAttributes(
			attribute.String("method", info.FullMethod),
			attribute.String("status", statusCode),
		)
		if rpcServerDuration != nil {
			rpcServerDuration.Record(ctx, dur, attrs)
		}
		if rpcServerRequests != nil {
			rpcServerRequests.Add(ctx, 1, attrs)
		}
		emit(log, ctx, info.FullMethod, start, err, "rpc.server", peerServiceFromContext(ctx))
		return resp, err
	}
}

// StreamServerInterceptor mirrors UnaryServerInterceptor for stream RPCs.
func StreamServerInterceptor(log *Logger) grpc.StreamServerInterceptor {
	ensureMetrics()
	return func(
		srv any,
		ss grpc.ServerStream,
		info *grpc.StreamServerInfo,
		handler grpc.StreamHandler,
	) error {
		start := time.Now()
		err := handler(srv, ss)
		dur := time.Since(start).Seconds()
		statusCode := "OK"
		if err != nil {
			s, _ := status.FromError(err)
			statusCode = s.Code().String()
		}
		attrs := metric.WithAttributes(
			attribute.String("method", info.FullMethod),
			attribute.String("status", statusCode),
		)
		if rpcServerDuration != nil {
			rpcServerDuration.Record(ss.Context(), dur, attrs)
		}
		if rpcServerRequests != nil {
			rpcServerRequests.Add(ss.Context(), 1, attrs)
		}
		emit(log, ss.Context(), info.FullMethod, start, err, "rpc.server", peerServiceFromContext(ss.Context()))
		return err
	}
}

// UnaryClientInterceptor returns a grpc.UnaryClientInterceptor that logs one
// JSON line per outbound RPC AND records M4.3 client metrics.
func UnaryClientInterceptor(log *Logger, callerService string) grpc.UnaryClientInterceptor {
	_ = callerService // included for parity with other languages; not used in field set
	ensureMetrics()
	return func(
		ctx context.Context,
		method string,
		req, reply any,
		cc *grpc.ClientConn,
		invoker grpc.UnaryInvoker,
		opts ...grpc.CallOption,
	) error {
		start := time.Now()
		err := invoker(ctx, method, req, reply, cc, opts...)
		dur := time.Since(start).Seconds()
		statusCode := "OK"
		if err != nil {
			s, _ := status.FromError(err)
			statusCode = s.Code().String()
		}
		peerSvc := peerServiceFromMethod(method)
		attrs := metric.WithAttributes(
			attribute.String("peer_service", peerSvc),
			attribute.String("operation", method),
			attribute.String("status", statusCode),
		)
		if rpcClientDuration != nil {
			rpcClientDuration.Record(ctx, dur, attrs)
		}
		if err != nil && rpcClientErrors != nil {
			rpcClientErrors.Add(ctx, 1, attrs)
		}
		emit(log, ctx, method, start, err, "rpc.client", peerSvc)
		return err
	}
}

// emit builds the structured log line. Branchless on the happy path so it
// stays cheap at production traffic.
func emit(log *Logger, ctx context.Context, fullMethod string, start time.Time, err error, kind, peerService string) {
	if log == nil {
		return
	}
	latencyMs := time.Since(start).Milliseconds()
	statusCode := "OK"
	errClass := ""
	if err != nil {
		s, _ := status.FromError(err)
		statusCode = s.Code().String()
		errClass = errClassOf(err)
	}

	span := trace.SpanContextFromContext(ctx)
	fields := logrus.Fields{
		keyMethod:      fullMethod,
		keyPeerService: peerService,
		keyLatencyMs:   latencyMs,
		keyStatusCode:  statusCode,
		keyKind:        kind,
	}
	if span.IsValid() {
		fields[keyTraceID] = span.TraceID().String()
		fields[keySpanID] = span.SpanID().String()
	}
	if errClass != "" {
		fields[keyErrClass] = errClass
	}

	if err != nil {
		log.WithFields(fields).Info("rpc")
	} else {
		log.WithFields(fields).Info("rpc")
	}
}

// errClassOf returns a short identifier for the error kind. Bounded and
// production-realistic; never includes a scenario or fault label.
func errClassOf(err error) string {
	if err == nil {
		return ""
	}
	// Reduce to "<gRPCStatus>" — bounded set; the gRPC code already carries
	// the most semantically useful classification.
	s, _ := status.FromError(err)
	return s.Code().String()
}

// peerServiceFromContext extracts a peer service identifier from gRPC peer
// metadata. Falls back to "unknown" — never inspects scenario/dataset env vars.
func peerServiceFromContext(ctx context.Context) string {
	p, ok := peer.FromContext(ctx)
	if !ok || p == nil || p.Addr == nil {
		return "unknown"
	}
	return p.Addr.String()
}

// peerServiceFromMethod extracts the target service name from the full method
// string (e.g. "/hipstershop.CartService/AddItem" -> "CartService").
func peerServiceFromMethod(fullMethod string) string {
	// fullMethod is "/pkg.Service/Method"
	parts := strings.SplitN(strings.TrimPrefix(fullMethod, "/"), "/", 2)
	if len(parts) == 0 {
		return "unknown"
	}
	svc := parts[0]
	// Drop the package prefix; keep only the last dotted segment.
	return path.Base(strings.ReplaceAll(svc, ".", "/"))
}
