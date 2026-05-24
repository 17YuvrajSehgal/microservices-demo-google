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
# Required so the `@hipstershop/rpc-logging: file:../_shared-node/rpc-logging`
# entry in package.json resolves against the sibling in the build context.

ARG BUILDPLATFORM=linux/amd64

FROM --platform=$BUILDPLATFORM node:20.20.2-alpine@sha256:fb4cd12c85ee03686f6af5362a0b0d56d50c58a04632e6c0fb8363f609372293 AS builder

RUN apk add --update --no-cache \
    python3 \
    make \
    g++

WORKDIR /src

COPY _shared-node/ _shared-node/

# Install the shared package's deps inside its own tree. Required because
# Node module resolution from /src/_shared-node/rpc-logging/index.js walks
# UP from there, never into the consumer's node_modules. Without this,
# `require('@grpc/grpc-js')` inside rpc-logging fails at runtime even
# though paymentservice has its own copy.
RUN cd _shared-node/rpc-logging && npm install --omit=dev --no-fund --no-audit

COPY paymentservice/package*.json paymentservice/

WORKDIR /src/paymentservice
RUN npm install --only=production

FROM alpine:3.23.4@sha256:5b10f432ef3da1b8d4c7eb6c487f2f5a8f096bc91145e68878dd4a5019afde11

RUN apk add --no-cache nodejs

# Keep paths aligned with the builder stage so the npm-created symlink
# at node_modules/@hipstershop/rpc-logging (whose target path is
# ../../../_shared-node/rpc-logging, relative to the symlink's location
# inside node_modules) resolves correctly.
COPY --from=builder /src/_shared-node /src/_shared-node
COPY --from=builder /src/paymentservice/node_modules /src/paymentservice/node_modules

WORKDIR /src/paymentservice
COPY paymentservice/ ./

EXPOSE 50051

ENTRYPOINT [ "node", "index.js" ]
