// M4.1 helper: set up a Prometheus MeterProvider + /metrics HTTP endpoint.
// Each Go service calls InitMetrics(port) once at startup.

package rpclog

import (
	"context"
	"fmt"
	"net/http"

	"github.com/sirupsen/logrus"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/exporters/prometheus"
	"go.opentelemetry.io/otel/sdk/metric"
)

// InitMetrics installs a Prometheus exporter as the global MeterProvider and
// starts an HTTP server on metricsPort exposing /metrics. Safe to call once
// per process. Returns the underlying MeterProvider so callers can register
// extra meters / shutdown.
//
// Field discipline note: every metric registered through the global meter
// inherits resource attributes set by the OTel collector (k8sattributes
// processor). We do NOT add scenario / fault attrs at the metric level.
func InitMetrics(log *logrus.Logger, metricsPort int) (*metric.MeterProvider, error) {
	exporter, err := prometheus.New()
	if err != nil {
		return nil, fmt.Errorf("prometheus exporter: %w", err)
	}
	provider := metric.NewMeterProvider(metric.WithReader(exporter))
	otel.SetMeterProvider(provider)

	mux := http.NewServeMux()
	mux.Handle("/metrics", promHandler())
	addr := fmt.Sprintf(":%d", metricsPort)
	srv := &http.Server{Addr: addr, Handler: mux}
	go func() {
		log.Infof("Prometheus /metrics endpoint listening on %s", addr)
		if err := srv.ListenAndServe(); err != nil && err != http.ErrServerClosed {
			log.Warnf("metrics http server: %v", err)
		}
	}()

	return provider, nil
}

// ShutdownMetrics flushes pending metrics on graceful shutdown.
func ShutdownMetrics(ctx context.Context, mp *metric.MeterProvider) error {
	if mp == nil {
		return nil
	}
	return mp.Shutdown(ctx)
}
