# Copyright 2021 Google LLC
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

# Build context: microservices-demo-google/src/ (parent of cartservice/ and
# _shared-dotnet/). The ProjectReference in cartservice.csproj resolves
# ../../_shared-dotnet/RpcLogging/RpcLogging.csproj relative to
# cartservice/src/, so both trees must be visible to the builder.

# Define a default value so it's not empty if the builder fails to provide it
ARG BUILDPLATFORM=linux/amd64

# https://mcr.microsoft.com/product/dotnet/sdk
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.100-noble@sha256:c7445f141c04f1a6b454181bd098dcfa606c61ba0bd213d0a702489e5bd4cd71 AS builder
ARG TARGETARCH=amd64
WORKDIR /src

# Copy csproj files first to maximise restore-layer cache.
COPY cartservice/src/cartservice.csproj cartservice/src/
COPY _shared-dotnet/RpcLogging/RpcLogging.csproj _shared-dotnet/RpcLogging/

RUN dotnet restore cartservice/src/cartservice.csproj -a $TARGETARCH

# Now copy the actual sources for both projects.
COPY cartservice/src/ cartservice/src/
COPY _shared-dotnet/RpcLogging/ _shared-dotnet/RpcLogging/

RUN dotnet publish cartservice/src/cartservice.csproj \
    -p:PublishSingleFile=true \
    -a $TARGETARCH \
    --self-contained true \
    -p:PublishTrimmed=true \
    -p:TrimMode=full \
    -c release \
    -o /cartservice

# https://mcr.microsoft.com/product/dotnet/runtime-deps
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0.0-noble-chiseled@sha256:b857c8cb8d929183cfe4c6dd9994abba92a2639dd2dbaf06005379f815991604

WORKDIR /app
COPY --from=builder /cartservice .
EXPOSE 7070
ENV DOTNET_EnableDiagnostics=0 \
    ASPNETCORE_HTTP_PORTS=7070
USER 1000
ENTRYPOINT ["/app/cartservice"]
