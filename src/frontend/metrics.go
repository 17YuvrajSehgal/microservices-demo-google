// M4.4: frontend business metrics. http_requests_total with bounded labels.

package main

import (
	"context"
	"net/http"

	"github.com/gorilla/mux"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/metric"
)

var httpRequestsTotal metric.Int64Counter

func initFrontendMetrics() {
	meter := otel.GetMeterProvider().Meter("frontend")
	httpRequestsTotal, _ = meter.Int64Counter(
		"http_requests_total",
		metric.WithDescription("Inbound HTTP request count by route template and status class."),
	)
}

func statusClass(status int) string {
	switch {
	case status >= 500:
		return "5xx"
	case status >= 400:
		return "4xx"
	case status >= 300:
		return "3xx"
	case status >= 200:
		return "2xx"
	default:
		return "1xx"
	}
}

// routeTemplate returns the gorilla/mux route template for r, falling back
// to "unknown" so labels stay bounded even on unmatched paths.
func routeTemplate(r *http.Request) string {
	if route := mux.CurrentRoute(r); route != nil {
		if tpl, err := route.GetPathTemplate(); err == nil {
			return tpl
		}
	}
	return "unknown"
}

func recordHTTPRequest(ctx context.Context, r *http.Request, status int) {
	if httpRequestsTotal == nil {
		return
	}
	httpRequestsTotal.Add(ctx, 1, metric.WithAttributes(
		attribute.String("route", routeTemplate(r)),
		attribute.String("method", r.Method),
		attribute.String("status_class", statusClass(status)),
	))
}
