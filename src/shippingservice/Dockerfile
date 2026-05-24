# Copyright 2020 Google LLC
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#      http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

# Build context: microservices-demo-google/src/

ARG BUILDPLATFORM=linux/amd64

FROM --platform=$BUILDPLATFORM golang:1.26.2-alpine@sha256:f85330846cde1e57ca9ec309382da3b8e6ae3ab943d2739500e08c86393a21b1 AS builder
ARG TARGETOS=linux
ARG TARGETARCH=amd64
WORKDIR /src

COPY _shared-go/ _shared-go/
COPY shippingservice/go.mod shippingservice/go.sum shippingservice/

WORKDIR /src/shippingservice
RUN go mod download

COPY shippingservice/ ./

RUN go mod tidy

ARG SKAFFOLD_GO_GCFLAGS
RUN GOOS=${TARGETOS} GOARCH=${TARGETARCH} CGO_ENABLED=0 go build -ldflags="-s -w" -gcflags="${SKAFFOLD_GO_GCFLAGS}" -o /go/bin/shippingservice .

FROM gcr.io/distroless/static
WORKDIR /src
COPY --from=builder /go/bin/shippingservice /src/shippingservice

ENV GOTRACEBACK=single APP_PORT=50051

EXPOSE 50051
ENTRYPOINT ["/src/shippingservice"]
