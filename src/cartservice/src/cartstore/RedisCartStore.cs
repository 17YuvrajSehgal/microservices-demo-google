// Copyright 2018 Google LLC
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

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Google.Protobuf;
using OpenTelemetry.Trace;

namespace cartservice.cartstore
{
    public class RedisCartStore : ICartStore
    {
        private readonly IDistributedCache _cache;
        private readonly ILogger<RedisCartStore> _log;

        public RedisCartStore(IDistributedCache cache, ILogger<RedisCartStore> log)
        {
            _cache = cache;
            _log = log;
        }

        // M2.2: structured dep-boundary error log. Fields {dep, op, err_class}
        // match the Go interceptor field set in src/_shared-go/rpclog.
        private void LogRedisError(string op, Exception ex)
        {
            _log.Log(LogLevel.Error, new EventId(2, "dep_error"),
                new
                {
                    dep = "redis-cart",
                    op = op,
                    err_class = ex.GetType().Name,
                    retry_attempt = 0,
                    trace_id = Activity.Current?.TraceId.ToString() ?? "",
                    span_id = Activity.Current?.SpanId.ToString() ?? "",
                },
                ex,
                (state, e) => $"dep_error dep=redis-cart op={op} err={ex.GetType().Name}");
        }

        public async Task AddItemAsync(string userId, string productId, int quantity)
        {
            Console.WriteLine($"AddItemAsync called with userId={userId}, productId={productId}, quantity={quantity}");

            try
            {
                Hipstershop.Cart cart;
                var value = await _cache.GetAsync(userId);
                if (value == null)
                {
                    cart = new Hipstershop.Cart();
                    cart.UserId = userId;
                    cart.Items.Add(new Hipstershop.CartItem { ProductId = productId, Quantity = quantity });
                }
                else
                {
                    cart = Hipstershop.Cart.Parser.ParseFrom(value);
                    var existingItem = cart.Items.SingleOrDefault(i => i.ProductId == productId);
                    if (existingItem == null)
                    {
                        cart.Items.Add(new Hipstershop.CartItem { ProductId = productId, Quantity = quantity });
                    }
                    else
                    {
                        existingItem.Quantity += quantity;
                    }
                }
                await _cache.SetAsync(userId, cart.ToByteArray());
            }
            catch (Exception ex)
            {
                // M3.1: record on the active span so the failure is visible in Tempo,
                // not just in the gRPC envelope status. Also adds a "redis.op" attribute
                // tying the error to the Redis dependency boundary (M3.2 enrichment).
                Activity.Current?.RecordException(ex);
                Activity.Current?.SetStatus(ActivityStatusCode.Error, "redis cart storage error");
                Activity.Current?.SetTag("db.system", "redis");
                Activity.Current?.SetTag("db.operation", "AddItem");
                LogRedisError("AddItem", ex);
                throw new RpcException(new Status(StatusCode.FailedPrecondition, $"Can't access cart storage. {ex}"));
            }
        }

        public async Task EmptyCartAsync(string userId)
        {
            Console.WriteLine($"EmptyCartAsync called with userId={userId}");

            try
            {
                var cart = new Hipstershop.Cart();
                await _cache.SetAsync(userId, cart.ToByteArray());
            }
            catch (Exception ex)
            {
                // M3.1 + M3.2.
                Activity.Current?.RecordException(ex);
                Activity.Current?.SetStatus(ActivityStatusCode.Error, "redis cart storage error");
                Activity.Current?.SetTag("db.system", "redis");
                Activity.Current?.SetTag("db.operation", "EmptyCart");
                LogRedisError("EmptyCart", ex);
                throw new RpcException(new Status(StatusCode.FailedPrecondition, $"Can't access cart storage. {ex}"));
            }
        }

        public async Task<Hipstershop.Cart> GetCartAsync(string userId)
        {
            Console.WriteLine($"GetCartAsync called with userId={userId}");

            try
            {
                // Access the cart from the cache
                var value = await _cache.GetAsync(userId);

                if (value != null)
                {
                    return Hipstershop.Cart.Parser.ParseFrom(value);
                }

                // We decided to return empty cart in cases when user wasn't in the cache before
                return new Hipstershop.Cart();
            }
            catch (Exception ex)
            {
                // M3.1 + M3.2.
                Activity.Current?.RecordException(ex);
                Activity.Current?.SetStatus(ActivityStatusCode.Error, "redis cart storage error");
                Activity.Current?.SetTag("db.system", "redis");
                Activity.Current?.SetTag("db.operation", "GetCart");
                LogRedisError("GetCart", ex);
                throw new RpcException(new Status(StatusCode.FailedPrecondition, $"Can't access cart storage. {ex}"));
            }
        }

        public bool Ping()
        {
            try
            {
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
