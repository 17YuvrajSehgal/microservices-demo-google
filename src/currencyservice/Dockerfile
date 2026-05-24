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

FROM --platform=$BUILDPLATFORM node:20.20.2-alpine@sha256:fb4cd12c85ee03686f6af5362a0b0d56d50c58a04632e6c0fb8363f609372293 AS builder

RUN apk add --update --no-cache \
    python3 \
    make \
    g++

WORKDIR /src

COPY _shared-node/ _shared-node/

# Install the shared package's own deps so Node's module resolution from
# /src/_shared-node/rpc-logging/index.js can find @grpc/grpc-js etc.
RUN cd _shared-node/rpc-logging && npm install --omit=dev --no-fund --no-audit

COPY currencyservice/package*.json currencyservice/

WORKDIR /src/currencyservice
RUN npm install --only=production

FROM alpine:3.23.4@sha256:5b10f432ef3da1b8d4c7eb6c487f2f5a8f096bc91145e68878dd4a5019afde11

RUN apk add --no-cache nodejs

# Keep paths aligned with the builder stage so the npm-created symlink
# at node_modules/@hipstershop/rpc-logging resolves correctly.
COPY --from=builder /src/_shared-node /src/_shared-node
COPY --from=builder /src/currencyservice/node_modules /src/currencyservice/node_modules

WORKDIR /src/currencyservice
COPY currencyservice/ ./

EXPOSE 7000

ENTRYPOINT [ "node", "server.js" ]
