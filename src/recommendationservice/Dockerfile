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
# Required so the `_shared-python/rpc_logging/` package is reachable
# alongside the service for `pip install` to consume it.

ARG BUILDPLATFORM=linux/amd64

FROM --platform=$BUILDPLATFORM python:3.14.4-alpine@sha256:dd4d2bd5b53d9b25a51da13addf2be586beebd5387e289e798e4083d94ca837a AS base

FROM base AS builder

ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1

RUN apk update \
    && apk add --no-cache g++ linux-headers \
    && rm -rf /var/cache/apk/*

WORKDIR /src
COPY _shared-python/ _shared-python/
COPY recommendationservice/requirements.txt recommendationservice/

# Install the shared logging package first (non-editable so the final
# stage doesn't need the source tree). M2.1.
RUN pip install /src/_shared-python/rpc_logging

# Then service-pinned deps.
RUN pip install -r recommendationservice/requirements.txt

FROM base

ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1

RUN apk update \
    && apk add --no-cache libstdc++ \
    && rm -rf /var/cache/apk/*

WORKDIR /recommendationservice

COPY --from=builder /usr/local/lib/python3.14/ /usr/local/lib/python3.14/

COPY recommendationservice/ ./

ENV PORT="8080"
EXPOSE 8080

ENTRYPOINT ["python", "recommendation_server.py"]
