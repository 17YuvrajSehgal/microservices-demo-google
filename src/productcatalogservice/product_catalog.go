// Copyright 2023 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

package main

import (
	"context"
	"strings"
	"time"

	pb "github.com/GoogleCloudPlatform/microservices-demo/src/productcatalogservice/genproto"
	"google.golang.org/grpc/codes"
	healthpb "google.golang.org/grpc/health/grpc_health_v1"
	"google.golang.org/grpc/status"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	otelcodes "go.opentelemetry.io/otel/codes"
	"go.opentelemetry.io/otel/metric"
	oteltrace "go.opentelemetry.io/otel/trace"
)

// M4.4: catalog_lookups_total{result}.
var catalogLookupsTotal metric.Int64Counter

func initCatalogMetrics() {
	meter := otel.GetMeterProvider().Meter("productcatalogservice")
	catalogLookupsTotal, _ = meter.Int64Counter(
		"catalog_lookups_total",
		metric.WithDescription("GetProduct lookups by result (hit/miss)."),
	)
}

type productCatalog struct {
	pb.UnimplementedProductCatalogServiceServer
	catalog pb.ListProductsResponse
}

func (p *productCatalog) Check(ctx context.Context, req *healthpb.HealthCheckRequest) (*healthpb.HealthCheckResponse, error) {
	return &healthpb.HealthCheckResponse{Status: healthpb.HealthCheckResponse_SERVING}, nil
}

func (p *productCatalog) Watch(req *healthpb.HealthCheckRequest, ws healthpb.Health_WatchServer) error {
	return status.Errorf(codes.Unimplemented, "health check via Watch not implemented")
}

func (p *productCatalog) ListProducts(ctx context.Context, _ *pb.Empty) (*pb.ListProductsResponse, error) {
	time.Sleep(extraLatency)

	return &pb.ListProductsResponse{Products: p.parseCatalog(ctx)}, nil
}

func (p *productCatalog) GetProduct(ctx context.Context, req *pb.GetProductRequest) (*pb.Product, error) {
	time.Sleep(extraLatency)

	span := oteltrace.SpanFromContext(ctx)
	catalog := p.parseCatalog(ctx)
	for _, product := range catalog {
		if req.Id == product.Id {
			span.SetAttributes(attribute.String("app.catalog.result", "hit"))
			if catalogLookupsTotal != nil {
				catalogLookupsTotal.Add(ctx, 1, metric.WithAttributes(
					attribute.String("result", "hit"),
				))
			}
			return product, nil
		}
	}

	// M3.1: NotFound is a real handler error — surface on the span.
	err := status.Errorf(codes.NotFound, "no product with ID %s", req.Id)
	span.RecordError(err)
	span.SetStatus(otelcodes.Error, "product not found")
	span.SetAttributes(attribute.String("app.catalog.result", "miss"))
	if catalogLookupsTotal != nil {
		catalogLookupsTotal.Add(ctx, 1, metric.WithAttributes(
			attribute.String("result", "miss"),
		))
	}
	return nil, err
}

func (p *productCatalog) SearchProducts(ctx context.Context, req *pb.SearchProductsRequest) (*pb.SearchProductsResponse, error) {
	time.Sleep(extraLatency)

	var ps []*pb.Product
	for _, product := range p.parseCatalog(ctx) {
		if strings.Contains(strings.ToLower(product.Name), strings.ToLower(req.Query)) ||
			strings.Contains(strings.ToLower(product.Description), strings.ToLower(req.Query)) {
			ps = append(ps, product)
		}
	}

	// M4.4: bounded result-size bucket (production search APIs typically emit
	// this kind of attribute).
	oteltrace.SpanFromContext(ctx).SetAttributes(
		attribute.String("app.search.result_count_bucket", searchResultBucket(len(ps))),
	)
	return &pb.SearchProductsResponse{Results: ps}, nil
}

func searchResultBucket(n int) string {
	switch {
	case n == 0:
		return "0"
	case n <= 3:
		return "1-3"
	case n <= 10:
		return "4-10"
	default:
		return "10+"
	}
}

func (p *productCatalog) parseCatalog(ctx context.Context) []*pb.Product {
	if reloadCatalog || len(p.catalog.Products) == 0 {
		// M3.3: this is a real cache-miss / catalog-reload code path —
		// the only such pattern in Online Boutique. Emit a span event so
		// reload frequency shows up in trace bodies / dashboards.
		if span := oteltrace.SpanFromContext(ctx); span.IsRecording() {
			span.AddEvent("catalog.reload")
		}
		err := loadCatalog(&p.catalog)
		if err != nil {
			return []*pb.Product{}
		}
	}

	return p.catalog.Products
}
