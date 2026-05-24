// promHandler returns the Prometheus HTTP scrape handler.
// Split out so the otel-prometheus exporter dependency stays compartmentalized.

package rpclog

import (
	"net/http"

	"github.com/prometheus/client_golang/prometheus"
	"github.com/prometheus/client_golang/prometheus/promhttp"
)

func promHandler() http.Handler {
	// Use the default prometheus registry; the otel/exporters/prometheus
	// New() call registers its metrics there.
	return promhttp.HandlerFor(prometheus.DefaultGatherer, promhttp.HandlerOpts{
		ErrorHandling: promhttp.ContinueOnError,
	})
}
