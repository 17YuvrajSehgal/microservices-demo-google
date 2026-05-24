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

# Define a default value so it's not empty if the builder fails to provide it
ARG BUILDPLATFORM=linux/amd64

FROM --platform=$BUILDPLATFORM eclipse-temurin:24.0.2_12-jdk-noble@sha256:dacac8e9a0df0d2bd24e702b4431132875c249930b70555ebd7ca285b5bee684 AS builder

WORKDIR /app

COPY ["build.gradle", "gradlew", "./"]
COPY gradle gradle
RUN chmod +x gradlew
RUN ./gradlew downloadRepos

COPY . .
RUN chmod +x gradlew
RUN ./gradlew installDist

FROM eclipse-temurin:25.0.2_10-jre-alpine@sha256:5fcc27581b238efbfda93da3a103f59e0b5691fe522a7ac03fe8057b0819c888

# @TODO: https://github.com/GoogleCloudPlatform/microservices-demo/issues/2517
# Download Stackdriver Profiler Java agent
# RUN mkdir -p /opt/cprof && \
#     wget -q -O- https://storage.googleapis.com/cloud-profiler/java/latest/profiler_java_agent_alpine.tar.gz \
#     | tar xzv -C /opt/cprof && \
#     rm -rf profiler_java_agent.tar.gz

# OpenTelemetry Java agent (added by M1.2; see microservice-changes-todo.md).
# Drop-in JAR approach: zero application code changes. Activated by
# JAVA_TOOL_OPTIONS=-javaagent:/opt/otel/javaagent.jar in the deployment.
# Version pinned to a known-good release; bump in sync with the rest of
# the OTel libraries during quarterly cherry-pick (M0.2 cadence).
ARG OTEL_AGENT_VERSION=2.8.0
RUN apk add --no-cache wget \
 && mkdir -p /opt/otel \
 && wget -q -O /opt/otel/javaagent.jar \
    https://github.com/open-telemetry/opentelemetry-java-instrumentation/releases/download/v${OTEL_AGENT_VERSION}/opentelemetry-javaagent.jar \
 && apk del wget

WORKDIR /app
COPY --from=builder /app .

# Activate the agent for every JVM started in this image. The deployment
# manifest provides OTEL_SERVICE_NAME / OTEL_EXPORTER_OTLP_ENDPOINT etc.
ENV JAVA_TOOL_OPTIONS="-javaagent:/opt/otel/javaagent.jar"

EXPOSE 9555
ENTRYPOINT ["/app/build/install/hipstershop/bin/AdService"]
