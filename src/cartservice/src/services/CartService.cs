// Copyright 2020 Google LLC
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
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using cartservice.cartstore;
using Hipstershop;

namespace cartservice.services
{
    public class CartService : Hipstershop.CartService.CartServiceBase
    {
        private readonly static Empty Empty = new Empty();
        private readonly ICartStore _cartStore;
        private readonly ILogger<CartService> _log;

        // M4.4: cart_operations_total{op,result}. Bounded labels.
        private static readonly Meter _meter = new("cartservice");
        private static readonly Counter<long> _cartOps = _meter.CreateCounter<long>(
            "cart_operations_total",
            description: "Cart operations by op (add|get|empty) and result (success|error).");

        public CartService(ICartStore cartStore, ILogger<CartService> log)
        {
            _cartStore = cartStore;
            _log = log;
        }

        private void Record(string op, string result)
        {
            _cartOps.Add(1,
                new KeyValuePair<string, object?>("op", op),
                new KeyValuePair<string, object?>("result", result));
        }

        public async override Task<Empty> AddItem(AddItemRequest request, ServerCallContext context)
        {
            try
            {
                await _cartStore.AddItemAsync(request.UserId, request.Item.ProductId, request.Item.Quantity);
                Record("add", "success");
                // M2.3 business event log
                _log.LogInformation(new EventId(3, "cart_size_changed"),
                    "cart_size_changed op=add product_id_present={present}",
                    !string.IsNullOrEmpty(request.Item.ProductId));
                return Empty;
            }
            catch
            {
                Record("add", "error");
                throw;
            }
        }

        public override async Task<Cart> GetCart(GetCartRequest request, ServerCallContext context)
        {
            try
            {
                var cart = await _cartStore.GetCartAsync(request.UserId);
                Record("get", "success");
                return cart;
            }
            catch
            {
                Record("get", "error");
                throw;
            }
        }

        public async override Task<Empty> EmptyCart(EmptyCartRequest request, ServerCallContext context)
        {
            try
            {
                await _cartStore.EmptyCartAsync(request.UserId);
                Record("empty", "success");
                _log.LogInformation(new EventId(3, "cart_size_changed"),
                    "cart_size_changed op=empty");
                return Empty;
            }
            catch
            {
                Record("empty", "error");
                throw;
            }
        }
    }
}