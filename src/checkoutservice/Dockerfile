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
# Required so the `replace ../_shared-go/rpclog` directive in go.mod
# resolves against a sibling that exists inside the build context.

ARG BUILDPLATFORM=linux/amd64

FROM --platform=$BUILDPLATFORM golang:1.26.2-alpine@sha256:f85330846cde1e57ca9ec309382da3b8e6ae3ab943d2739500e08c86393a21b1 AS builder
ARG TARGETOS=linux
ARG TARGETARCH=amd64
WORKDIR /src

# Shared rpclog package first so the replace directive can be resolved at
# `go mod download` time.
COPY _shared-go/ _shared-go/
COPY checkoutservice/go.mod checkoutservice/go.sum checkoutservice/

WORKDIR /src/checkoutservice
RUN go mod download

COPY checkoutservice/ ./

# Previous session added rpclog imports without running `go mod tidy`;
# tidy fills in missing go.sum + indirect requirements at build time.
RUN go mod tidy

ARG SKAFFOLD_GO_GCFLAGS
RUN GOOS=${TARGETOS} GOARCH=${TARGETARCH} CGO_ENABLED=0 go build -ldflags="-s -w" -gcflags="${SKAFFOLD_GO_GCFLAGS}" -o /checkoutservice .

FROM gcr.io/distroless/static

WORKDIR /src
COPY --from=builder /checkoutservice /src/checkoutservice

ENV GOTRACEBACK=single

EXPOSE 5050
ENTRYPOINT ["/src/checkoutservice"]
