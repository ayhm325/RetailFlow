using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using RetailFlow.Application.Catalog;
using RetailFlow.Application.Identity;
using RetailFlow.Application.Orders;
using RetailFlow.Application.Stores;
using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Currencies;
using RetailFlow.Domain.Fulfillment;
using RetailFlow.Domain.Identity;
using RetailFlow.Domain.Inventory;
using RetailFlow.Domain.Orders;
using RetailFlow.Domain.Stores;
using RetailFlow.Infrastructure.Identity;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Api.IntegrationTests;

public sealed class FailedDeliveryApiAndServiceTests : IClassFixture<CatalogSqlTestFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Currency Jod = Currency.Create("JOD", "400", 3);
    private readonly CatalogSqlTestFixture _fixture;

    public FailedDeliveryApiAndServiceTests(CatalogSqlTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(Guid StoreId, string StoreSlug, Guid ProductAId, Guid ProductBId, Guid ZoneId, HttpClient MerchantClient, HttpClient CustomerClient)> SetupStoreAndClientsAsync(
        int onHandA = 10,
        int onHandB = 10)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var adminId = Guid.NewGuid();
        var adminEmail = $"admin-fd-{suffix}@retailflow.test";

        await using (var scope = _fixture.CreateAsyncScope())
        {
            var scopeAccessor = scope.ServiceProvider.GetRequiredService<IIdentityLookupScopeAccessor>();
            scopeAccessor.SetCurrent(IdentityLookupScope.ForSystemProvisioning());
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<RetailFlowIdentityRole>>();
            if (!await roleManager.RoleExistsAsync(IdentityRoleNames.PlatformAdmin))
            {
                await roleManager.CreateAsync(new RetailFlowIdentityRole
                {
                    Id = RetailFlowIdentityRole.PlatformAdminRoleId,
                    Name = IdentityRoleNames.PlatformAdmin,
                    NormalizedName = IdentityRoleNames.PlatformAdmin.ToUpperInvariant()
                });
            }

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<RetailFlowIdentityUser>>();
            var admin = new RetailFlowIdentityUser
            {
                Id = adminId,
                UserName = adminId.ToString("N"),
                Email = adminEmail,
                DisplayName = "Admin",
                IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            Assert.True((await userManager.CreateAsync(admin, "AdminPassphrase!1")).Succeeded);
            Assert.True((await userManager.AddToRoleAsync(admin, IdentityRoleNames.PlatformAdmin)).Succeeded);
        }

        using var adminClient = _fixture.CreateHttpsClient();
        adminClient.DefaultRequestHeaders.Add("X-CSRF", "1");
        using var adminLogin = await adminClient.PostAsJsonAsync(
            "/api/platform/auth/login",
            new LoginCommand(adminEmail, "AdminPassphrase!1"));
        Assert.Equal(HttpStatusCode.OK, adminLogin.StatusCode);

        var slug = $"fd-{suffix}";
        using var createStore = await adminClient.PostAsJsonAsync(
            "/api/platform/stores",
            new CreateStoreCommand("FD Store", slug, StoreType.Physical, "JOD", "Asia/Amman", true, true, null, "Dock"));
        Assert.Equal(HttpStatusCode.Created, createStore.StatusCode);
        var store = await createStore.Content.ReadFromJsonAsync<PlatformStoreDto>(JsonOptions);
        Assert.NotNull(store);
        await adminClient.PostAsync($"/api/platform/stores/{store!.Id}/approve", null);

        var merchEmail = $"merch-fd-{suffix}@retailflow.test";
        await adminClient.PostAsJsonAsync(
            $"/api/platform/stores/{store.Id}/merchant-owner",
            new CreateMerchantUserCommand(merchEmail, "Merchant FD", "MerchPassphrase!1"));

        var merchantClient = _fixture.CreateHttpsClient();
        merchantClient.DefaultRequestHeaders.Add("X-CSRF", "1");
        using var merchLogin = await merchantClient.PostAsJsonAsync(
            $"/api/stores/{slug}/auth/login",
            new LoginCommand(merchEmail, "MerchPassphrase!1"));
        Assert.Equal(HttpStatusCode.OK, merchLogin.StatusCode);

        var catResp = await merchantClient.PostAsJsonAsync(
            $"/api/stores/{slug}/merchant/categories",
            new CreateCategoryCommand("Coffee", "coffee", 1, true));
        var cat = await catResp.Content.ReadFromJsonAsync<MerchantCategoryDto>(JsonOptions);

        var prodAResp = await merchantClient.PostAsJsonAsync(
            $"/api/stores/{slug}/merchant/products",
            new CreateProductCommand(cat!.Id, "Beans A", "A", "A-1", 10.000m, ProductStatus.Published, onHandA));
        var prodA = await prodAResp.Content.ReadFromJsonAsync<MerchantProductDto>(JsonOptions);

        var prodBResp = await merchantClient.PostAsJsonAsync(
            $"/api/stores/{slug}/merchant/products",
            new CreateProductCommand(cat.Id, "Beans B", "B", "B-1", 5.000m, ProductStatus.Published, onHandB));
        var prodB = await prodBResp.Content.ReadFromJsonAsync<MerchantProductDto>(JsonOptions);

        var zoneResp = await merchantClient.PostAsJsonAsync(
            $"/api/stores/{slug}/merchant/delivery-zones",
            new CreateDeliveryZoneCommand("Amman", 2.000m, true));
        var zone = await zoneResp.Content.ReadFromJsonAsync<MerchantDeliveryZoneDto>(JsonOptions);

        var customerClient = _fixture.CreateHttpsClient();
        customerClient.DefaultRequestHeaders.Add("X-CSRF", "1");
        using var regResp = await customerClient.PostAsJsonAsync(
            $"/api/stores/{slug}/auth/register",
            new RegisterCustomerCommand($"cust-fd-{suffix}@retailflow.test", "Customer", "CustPassphrase!1"));
        Assert.Equal(HttpStatusCode.Created, regResp.StatusCode);

        return (store.Id, slug, prodA!.Id, prodB!.Id, zone!.Id, merchantClient, customerClient);
    }

    private static async Task<OrderDto> PlaceAndDispatchOrderAsync(
        string slug,
        Guid prodAId,
        Guid prodBId,
        Guid zoneId,
        HttpClient merchantClient,
        HttpClient customerClient,
        int qtyA = 2,
        int qtyB = 3)
    {
        await customerClient.PutAsJsonAsync($"/api/stores/{slug}/cart/items", new UpsertCartItemCommand(prodAId, qtyA));
        await customerClient.PutAsJsonAsync($"/api/stores/{slug}/cart/items", new UpsertCartItemCommand(prodBId, qtyB));

        using var coReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/checkout");
        coReq.Headers.Add("Idempotency-Key", $"co-{Guid.NewGuid():N}");
        coReq.Content = JsonContent.Create(new CheckoutCommand(FulfillmentMode.Delivery, zoneId, "Amman St 1"));
        using var coResp = await customerClient.SendAsync(coReq);
        Assert.Equal(HttpStatusCode.Created, coResp.StatusCode);
        var order = await coResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);

        using var confirm = await merchantClient.PostAsync($"/api/stores/{slug}/merchant/orders/{order!.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        using var prep = await merchantClient.PostAsJsonAsync($"/api/stores/{slug}/merchant/orders/{order.Id}/fulfillment", new { NextStatus = FulfillmentStatus.Preparing });
        Assert.Equal(HttpStatusCode.OK, prep.StatusCode);

        using var disp = await merchantClient.PostAsJsonAsync($"/api/stores/{slug}/merchant/orders/{order.Id}/fulfillment", new { NextStatus = FulfillmentStatus.OutForDelivery });
        Assert.Equal(HttpStatusCode.OK, disp.StatusCode);
        return (await disp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions))!;
    }

    [Fact]
    public async Task EndToEndGate_Fail_CancelPostDispatch_MixedDisposition_NoDoubleWriteOff_AndRetryIdempotency()
    {
        var (storeId, slug, prodAId, prodBId, zoneId, merchantClient, customerClient) = await SetupStoreAndClientsAsync(10, 10);
        try
        {
            var dispatched = await PlaceAndDispatchOrderAsync(slug, prodAId, prodBId, zoneId, merchantClient, customerClient, 2, 3);
            Assert.NotNull(dispatched.FulfillmentAttempts);
            Assert.Single(dispatched.FulfillmentAttempts!);
            Assert.Equal(1, dispatched.FulfillmentAttempts![0].AttemptNumber);

            // 1. Fail delivery with Temporary reason (Requires FailureReasonCode)
            using var badFail = await merchantClient.PostAsJsonAsync(
                $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/fulfillment",
                new { NextStatus = FulfillmentStatus.Failed });
            Assert.Equal(HttpStatusCode.BadRequest, badFail.StatusCode);

            using var failResp = await merchantClient.PostAsJsonAsync(
                $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/fulfillment",
                new { NextStatus = FulfillmentStatus.Failed, FailureReasonCode = DeliveryFailureReasonCode.CustomerUnavailable, FailureNote = "Not home" });
            Assert.Equal(HttpStatusCode.OK, failResp.StatusCode);
            var failedOrder = await failResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
            Assert.Equal(FulfillmentStatus.Failed, failedOrder!.FulfillmentStatus);
            Assert.Equal(OrderReservationSummary.ReturnPending, failedOrder.ReservationSummary);
            Assert.NotEqual(dispatched.Version, failedOrder.Version);

            // Inventory must still be reserved (2 and 3)
            await using (var db = _fixture.CreateDbContext())
            {
                Assert.Equal(2, (await db.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodAId)).Reserved);
                Assert.Equal(3, (await db.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodBId)).Reserved);
            }

            // 2. Retry blocked while ReturnPending (with valid If-Match & Idempotency-Key) -> 409
            using var blockedRetryReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/delivery/retry");
            blockedRetryReq.Headers.Add("Idempotency-Key", "retry-blocked-1");
            blockedRetryReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{failedOrder.Version}\""));
            blockedRetryReq.Content = JsonContent.Create(new { Note = "Retry early" });
            using var blockedRetryResp = await merchantClient.SendAsync(blockedRetryReq);
            Assert.Equal(HttpStatusCode.Conflict, blockedRetryResp.StatusCode);

            // 3. Resolve Restockable for both lines on Attempt 1 -> Order stays Confirmed, items become Active
            var attempt1Id = failedOrder.FulfillmentAttempts![0].Id;
            var itemAId = failedOrder.Items.Single(i => i.ProductId == prodAId).OrderItemId;
            var itemBId = failedOrder.Items.Single(i => i.ProductId == prodBId).OrderItemId;

            using var resolveConfirmReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/inventory-disposition/resolve");
            resolveConfirmReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{failedOrder.Version}\""));
            resolveConfirmReq.Content = JsonContent.Create(new
            {
                FulfillmentAttemptId = attempt1Id,
                Items = new[]
                {
                    new { OrderItemId = itemAId, Disposition = ReturnDisposition.Restockable, Note = "OK" },
                    new { OrderItemId = itemBId, Disposition = ReturnDisposition.Restockable, Note = "OK" }
                }
            });
            using var resolveConfirmResp = await merchantClient.SendAsync(resolveConfirmReq);
            Assert.Equal(HttpStatusCode.OK, resolveConfirmResp.StatusCode);
            var readyForRetry = await resolveConfirmResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);

            // 4. Retry succeeds -> Prepared attempt (AttemptNumber = null)
            var retryKey = $"retry-{Guid.NewGuid():N}";
            using var retryReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/delivery/retry");
            retryReq.Headers.Add("Idempotency-Key", retryKey);
            retryReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{readyForRetry!.Version}\""));
            retryReq.Content = JsonContent.Create(new { Note = "Second try" });
            using var retryResp = await merchantClient.SendAsync(retryReq);
            Assert.Equal(HttpStatusCode.OK, retryResp.StatusCode);
            var retriedOrder = await retryResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
            Assert.Equal(2, retriedOrder!.FulfillmentAttempts!.Count);
            var attempt2 = retriedOrder.FulfillmentAttempts.Single(a => a.Status == FulfillmentAttemptStatus.Prepared);
            Assert.Null(attempt2.AttemptNumber);

            // 5. Same Idempotency-Key with STALE ETag replays 200 OK; different payload returns 422
            using var replayRetryReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/delivery/retry");
            replayRetryReq.Headers.Add("Idempotency-Key", retryKey);
            replayRetryReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{readyForRetry.Version}\"")); // Stale!
            replayRetryReq.Content = JsonContent.Create(new { Note = "Second try" });
            using var replayRetryResp = await merchantClient.SendAsync(replayRetryReq);
            Assert.Equal(HttpStatusCode.OK, replayRetryResp.StatusCode);

            using var mismatchRetryReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/delivery/retry");
            mismatchRetryReq.Headers.Add("Idempotency-Key", retryKey);
            mismatchRetryReq.Content = JsonContent.Create(new { Note = "Different note" });
            using var mismatchRetryResp = await merchantClient.SendAsync(mismatchRetryReq);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, mismatchRetryResp.StatusCode);

            // 6. Dispatch Attempt 2 (AttemptNumber=2), then Cancel post-dispatch -> ReturnPending!
            using var disp2Resp = await merchantClient.PostAsJsonAsync(
                $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/fulfillment",
                new { NextStatus = FulfillmentStatus.OutForDelivery });
            Assert.Equal(HttpStatusCode.OK, disp2Resp.StatusCode);
            var disp2 = await disp2Resp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
            Assert.Equal(2, disp2!.FulfillmentAttempts!.Single(a => a.IsActive).AttemptNumber);

            // Missing If-Match -> 428; Stale If-Match -> 412
            using var cancelMissingEtag = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/cancel");
            cancelMissingEtag.Content = JsonContent.Create(new { Reason = "Customer cancelled" });
            Assert.Equal(HttpStatusCode.PreconditionRequired, (await merchantClient.SendAsync(cancelMissingEtag)).StatusCode);

            using var cancelStaleEtag = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/cancel");
            cancelStaleEtag.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{readyForRetry.Version}\""));
            cancelStaleEtag.Content = JsonContent.Create(new { Reason = "Customer cancelled" });
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await merchantClient.SendAsync(cancelStaleEtag)).StatusCode);

            using var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/cancel");
            cancelReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{disp2.Version}\""));
            cancelReq.Content = JsonContent.Create(new { Reason = "Customer cancelled" });
            using var cancelResp = await merchantClient.SendAsync(cancelReq);
            Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);
            var cancelled = await cancelResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
            Assert.Equal(OrderStatus.Cancelled, cancelled!.OrderStatus);
            Assert.Equal(OrderReservationSummary.ReturnPending, cancelled.ReservationSummary);

            // Cancel replay with same reason + stale ETag -> 200; different reason -> 409
            using var cancelReplayReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/cancel");
            cancelReplayReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{disp2.Version}\""));
            cancelReplayReq.Content = JsonContent.Create(new { Reason = "Customer cancelled" });
            Assert.Equal(HttpStatusCode.OK, (await merchantClient.SendAsync(cancelReplayReq)).StatusCode);

            using var cancelDiffReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/cancel");
            cancelDiffReq.Content = JsonContent.Create(new { Reason = "Other reason" });
            Assert.Equal(HttpStatusCode.Conflict, (await merchantClient.SendAsync(cancelDiffReq)).StatusCode);

            // 7. Mixed Disposition on Attempt 2: Item A Restockable, Item B Lost (null ReturnedAtUtc + InventoryAdjustment)
            using var resolveMixedReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/inventory-disposition/resolve");
            resolveMixedReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{cancelled.Version}\""));
            resolveMixedReq.Content = JsonContent.Create(new
            {
                FulfillmentAttemptId = attempt2.Id,
                Items = new[]
                {
                    new { OrderItemId = itemAId, Disposition = ReturnDisposition.Restockable, Note = "Returned" },
                    new { OrderItemId = itemBId, Disposition = ReturnDisposition.Lost, Note = "Lost" }
                }
            });
            using var resolveMixedResp = await merchantClient.SendAsync(resolveMixedReq);
            Assert.Equal(HttpStatusCode.OK, resolveMixedResp.StatusCode);
            var settled = await resolveMixedResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
            Assert.Equal(OrderReservationSummary.Settled, settled!.ReservationSummary);

            // Historical Attempt 1 replay does NOT touch Attempt 2 or inventory; duplicate Attempt 2 replay does NOT double write-off
            using var replayAttempt1Req = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/inventory-disposition/resolve");
            replayAttempt1Req.Content = JsonContent.Create(new
            {
                FulfillmentAttemptId = attempt1Id,
                Items = new[]
                {
                    new { OrderItemId = itemAId, Disposition = ReturnDisposition.Restockable, Note = "Old" },
                    new { OrderItemId = itemBId, Disposition = ReturnDisposition.Restockable, Note = "Old" }
                }
            });
            Assert.Equal(HttpStatusCode.OK, (await merchantClient.SendAsync(replayAttempt1Req)).StatusCode);

            await using (var verifyDb = _fixture.CreateDbContext())
            {
                var invA = await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodAId);
                var invB = await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodBId);
                Assert.Equal(10, invA.OnHand);
                Assert.Equal(0, invA.Reserved);
                Assert.Equal(7, invB.OnHand); // 10 - 3 Lost
                Assert.Equal(0, invB.Reserved);

                var adj = await verifyDb.InventoryAdjustments.AsNoTracking().SingleAsync(a => a.OrderId == dispatched.Id);
                Assert.Equal(InventoryAdjustmentOperationType.WriteOffLost, adj.OperationType);
                Assert.Equal(-3, adj.OnHandDelta);
                Assert.Equal(-3, adj.ReservedDelta);
            }
        }
        finally
        {
            await _fixture.DeleteStoresAsync(storeId);
        }
    }

    [Fact]
    public async Task RequiresCorrection_And_Authorization_And_CrossTenantIsolation_Enforced()
    {
        var (storeAId, slugA, prodAId, prodBId, zoneAId, merchA, custA) = await SetupStoreAndClientsAsync(10, 10);
        var (storeBId, slugB, _, _, _, merchB, _) = await SetupStoreAndClientsAsync(10, 10);
        try
        {
            var dispatched = await PlaceAndDispatchOrderAsync(slugA, prodAId, prodBId, zoneAId, merchA, custA, 1, 1);

            using var failResp = await merchA.PostAsJsonAsync(
                $"/api/stores/{slugA}/merchant/orders/{dispatched.Id}/fulfillment",
                new { NextStatus = FulfillmentStatus.Failed, FailureReasonCode = DeliveryFailureReasonCode.IncompleteAddress, FailureNote = "Missing building" });
            var failed = await failResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
            var attempt1Id = failed!.FulfillmentAttempts![0].Id;
            var itemAId = failed.Items.Single(i => i.ProductId == prodAId).OrderItemId;
            var itemBId = failed.Items.Single(i => i.ProductId == prodBId).OrderItemId;

            // Resolve Restockable
            using var resReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slugA}/merchant/orders/{dispatched.Id}/inventory-disposition/resolve");
            resReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{failed.Version}\""));
            resReq.Content = JsonContent.Create(new
            {
                FulfillmentAttemptId = attempt1Id,
                Items = new[] { new { OrderItemId = itemAId, Disposition = ReturnDisposition.Restockable }, new { OrderItemId = itemBId, Disposition = ReturnDisposition.Restockable } }
            });
            var afterReturn = await (await merchA.SendAsync(resReq)).Content.ReadFromJsonAsync<OrderDto>(JsonOptions);

            // Retry blocked until RequiresCorrection resolved
            using var retryBeforeCorr = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slugA}/merchant/orders/{dispatched.Id}/delivery/retry");
            retryBeforeCorr.Headers.Add("Idempotency-Key", "corr-retry-1");
            retryBeforeCorr.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{afterReturn!.Version}\""));
            retryBeforeCorr.Content = JsonContent.Create(new { });
            Assert.Equal(HttpStatusCode.Conflict, (await merchA.SendAsync(retryBeforeCorr)).StatusCode);

            // Correction recorded -> Retry succeeds
            using var corrReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slugA}/merchant/orders/{dispatched.Id}/delivery/correction");
            corrReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{afterReturn.Version}\""));
            corrReq.Content = JsonContent.Create(new { ResolutionNote = "Verified building #5" });
            var afterCorr = await (await merchA.SendAsync(corrReq)).Content.ReadFromJsonAsync<OrderDto>(JsonOptions);

            using var retryAfterCorr = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slugA}/merchant/orders/{dispatched.Id}/delivery/retry");
            retryAfterCorr.Headers.Add("Idempotency-Key", "corr-retry-1");
            retryAfterCorr.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{afterCorr!.Version}\""));
            retryAfterCorr.Content = JsonContent.Create(new { });
            Assert.Equal(HttpStatusCode.OK, (await merchA.SendAsync(retryAfterCorr)).StatusCode);

            // Authorization & Cross-Store checks
            using var anon = _fixture.CreateHttpsClient();
            anon.DefaultRequestHeaders.Add("X-CSRF", "1");
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/stores/{slugA}/merchant/orders/{dispatched.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await custA.GetAsync($"/api/stores/{slugA}/merchant/orders/{dispatched.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await merchB.GetAsync($"/api/stores/{slugA}/merchant/orders/{dispatched.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await merchB.GetAsync($"/api/stores/{slugB}/merchant/orders/{dispatched.Id}")).StatusCode);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(storeAId, storeBId);
        }
    }

    [Fact]
    public async Task Concurrent_ReturnResolution_ProducesSingleWinnerAndConsistentState()
    {
        var (storeId, slug, prodAId, prodBId, zoneId, merch, cust) = await SetupStoreAndClientsAsync(10, 10);
        try
        {
            var dispatched = await PlaceAndDispatchOrderAsync(slug, prodAId, prodBId, zoneId, merch, cust, 2, 2);
            using var failResp = await merch.PostAsJsonAsync(
                $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/fulfillment",
                new { NextStatus = FulfillmentStatus.Failed, FailureReasonCode = DeliveryFailureReasonCode.CustomerUnavailable });
            var failed = await failResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
            var attempt1Id = failed!.FulfillmentAttempts![0].Id;
            var itemAId = failed.Items.Single(i => i.ProductId == prodAId).OrderItemId;
            var itemBId = failed.Items.Single(i => i.ProductId == prodBId).OrderItemId;

            // Cancel post-dispatch -> ReturnPending
            using var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/cancel");
            cancelReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{failed.Version}\""));
            cancelReq.Content = JsonContent.Create(new { Reason = "Cancel" });
            var cancelled = await (await merch.SendAsync(cancelReq)).Content.ReadFromJsonAsync<OrderDto>(JsonOptions);

            // Concurrent conflicting disposition resolution on 2 DbContexts: one Restockable, one Lost
            await using var scope1 = _fixture.CreateAsyncScope();
            await using var scope2 = _fixture.CreateAsyncScope();
            var svc1 = scope1.ServiceProvider.GetRequiredService<IOrderService>();
            var svc2 = scope2.ServiceProvider.GetRequiredService<IOrderService>();

            var t1 = svc1.ResolveInventoryDispositionAsync(
                storeId, Guid.NewGuid(), dispatched.Id, attempt1Id,
                [new(itemAId, ReturnDisposition.Restockable, "OK"), new(itemBId, ReturnDisposition.Damaged, "Dmg")],
                cancelled!.Version, CancellationToken.None);
            var t2 = svc2.ResolveInventoryDispositionAsync(
                storeId, Guid.NewGuid(), dispatched.Id, attempt1Id,
                [new(itemAId, ReturnDisposition.Lost, "Lost"), new(itemBId, ReturnDisposition.Lost, "Lost")],
                cancelled.Version, CancellationToken.None);

            var results = await Task.WhenAll(
                t1.ContinueWith(t => t.Exception?.GetBaseException()),
                t2.ContinueWith(t => t.Exception?.GetBaseException()));

            Assert.True(
                results.Count(ex => ex is null) == 1,
                $"Expected 1 winner, got r0={results[0]?.GetType().Name}:{results[0]?.Message} | r1={results[1]?.GetType().Name}:{results[1]?.Message}");
            var loser = results.Single(ex => ex is not null)!;
            Assert.True(
                loser is OrderConcurrencyConflictException or OrderPreconditionException,
                $"Expected concurrency/precondition conflict, got {loser.GetType().Name}:{loser.Message}");
            Assert.IsType<DbUpdateConcurrencyException>(loser.InnerException);

            await using var verifyDb = _fixture.CreateDbContext();
            var invA = await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodAId);
            var invB = await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodBId);
            Assert.Equal(0, invA.Reserved);
            Assert.Equal(0, invB.Reserved);
            Assert.Equal(8, invB.OnHand); // Damaged or Lost -> decremented exactly once (10 - 2 = 8)
        }
        finally
        {
            await _fixture.DeleteStoresAsync(storeId);
        }
    }

    [Fact]
    public async Task Concurrent_Retry_WithDifferentIdempotencyKeys_ProducesSingleActiveAttempt()
    {
        var (storeId, slug, prodAId, prodBId, zoneId, merch, cust) = await SetupStoreAndClientsAsync(10, 10);
        try
        {
            var dispatched = await PlaceAndDispatchOrderAsync(slug, prodAId, prodBId, zoneId, merch, cust, 2, 1);
            using var failResp = await merch.PostAsJsonAsync(
                $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/fulfillment",
                new { NextStatus = FulfillmentStatus.Failed, FailureReasonCode = DeliveryFailureReasonCode.CustomerUnavailable });
            var failed = await failResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
            var attempt1Id = failed!.FulfillmentAttempts![0].Id;
            var itemAId = failed.Items.Single(i => i.ProductId == prodAId).OrderItemId;
            var itemBId = failed.Items.Single(i => i.ProductId == prodBId).OrderItemId;

            using var resReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/inventory-disposition/resolve");
            resReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{failed.Version}\""));
            resReq.Content = JsonContent.Create(new
            {
                FulfillmentAttemptId = attempt1Id,
                Items = new[] { new { OrderItemId = itemAId, Disposition = ReturnDisposition.Restockable }, new { OrderItemId = itemBId, Disposition = ReturnDisposition.Restockable } }
            });
            var ready = await (await merch.SendAsync(resReq)).Content.ReadFromJsonAsync<OrderDto>(JsonOptions);

            await using var scope1 = _fixture.CreateAsyncScope();
            await using var scope2 = _fixture.CreateAsyncScope();
            var svc1 = scope1.ServiceProvider.GetRequiredService<IOrderService>();
            var svc2 = scope2.ServiceProvider.GetRequiredService<IOrderService>();

            var t1 = svc1.RetryDeliveryAsync(storeId, Guid.NewGuid(), dispatched.Id, $"k1-{Guid.NewGuid():N}", "Try A", ready!.Version, CancellationToken.None);
            var t2 = svc2.RetryDeliveryAsync(storeId, Guid.NewGuid(), dispatched.Id, $"k2-{Guid.NewGuid():N}", "Try B", ready.Version, CancellationToken.None);

            var results = await Task.WhenAll(
                t1.ContinueWith(t => t.Exception?.GetBaseException()),
                t2.ContinueWith(t => t.Exception?.GetBaseException()));

            Assert.Equal(1, results.Count(ex => ex is null));
            var loser = results.Single(ex => ex is not null)!;
            Assert.True(loser is OrderPreconditionException or OrderConcurrencyConflictException or OrderStateConflictException,
                $"Expected 412/409 conflict, got {loser.GetType().Name}:{loser.Message}");

            await using var db = _fixture.CreateDbContext();
            var order = await db.Orders.AsNoTracking().Include(o => o.FulfillmentAttempts).SingleAsync(o => o.Id == dispatched.Id);
            Assert.Single(order.FulfillmentAttempts, a => a.IsActive);
            Assert.Equal(2, order.FulfillmentAttempts.Count);
            Assert.Equal(FulfillmentStatus.Preparing, order.FulfillmentStatus);
            Assert.Equal(ready.Version is not null ? order.Version : order.Version, order.Version);
            Assert.Equal(2, (await db.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodAId)).Reserved);
            Assert.Equal(1, (await db.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodBId)).Reserved);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(storeId);
        }
    }

    [Fact]
    public async Task RetryLimit_AllowsThreePhysicalDispatches_AndRejectsFourth_ViaApi()
    {
        var (storeId, slug, prodAId, prodBId, zoneId, merch, cust) = await SetupStoreAndClientsAsync(10, 10);
        try
        {
            var order = await PlaceAndDispatchOrderAsync(slug, prodAId, prodBId, zoneId, merch, cust, 1, 1);
            Assert.Equal(1, order.FulfillmentAttempts!.Single(a => a.IsActive).AttemptNumber);

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                using var failResp = await merch.PostAsJsonAsync(
                    $"/api/stores/{slug}/merchant/orders/{order.Id}/fulfillment",
                    new { NextStatus = FulfillmentStatus.Failed, FailureReasonCode = DeliveryFailureReasonCode.CustomerUnavailable });
                Assert.Equal(HttpStatusCode.OK, failResp.StatusCode);
                var failed = await failResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
                var failedAttempt = failed!.FulfillmentAttempts!.OrderByDescending(a => a.AttemptNumber ?? 0).First();
                Assert.Equal(attempt, failedAttempt.AttemptNumber);

                var itemAId = failed.Items.Single(i => i.ProductId == prodAId).OrderItemId;
                var itemBId = failed.Items.Single(i => i.ProductId == prodBId).OrderItemId;
                using var resReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{order.Id}/inventory-disposition/resolve");
                resReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{failed.Version}\""));
                resReq.Content = JsonContent.Create(new
                {
                    FulfillmentAttemptId = failedAttempt.Id,
                    Items = new[] { new { OrderItemId = itemAId, Disposition = ReturnDisposition.Restockable }, new { OrderItemId = itemBId, Disposition = ReturnDisposition.Restockable } }
                });
                var ready = await (await merch.SendAsync(resReq)).Content.ReadFromJsonAsync<OrderDto>(JsonOptions);

                using var retryReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{order.Id}/delivery/retry");
                retryReq.Headers.Add("Idempotency-Key", $"limit-retry-{attempt}");
                retryReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{ready!.Version}\""));
                retryReq.Content = JsonContent.Create(new { Note = $"Retry {attempt}" });
                using var retryResp = await merch.SendAsync(retryReq);

                if (attempt < 3)
                {
                    Assert.Equal(HttpStatusCode.OK, retryResp.StatusCode);
                    var retried = await retryResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
                    Assert.Null(retried!.FulfillmentAttempts!.Single(a => a.IsActive).AttemptNumber);

                    using var dispResp = await merch.PostAsJsonAsync(
                        $"/api/stores/{slug}/merchant/orders/{order.Id}/fulfillment",
                        new { NextStatus = FulfillmentStatus.OutForDelivery });
                    Assert.Equal(HttpStatusCode.OK, dispResp.StatusCode);
                    order = (await dispResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions))!;
                    Assert.Equal(attempt + 1, order.FulfillmentAttempts!.Single(a => a.IsActive).AttemptNumber);
                }
                else
                {
                    Assert.Equal(HttpStatusCode.Conflict, retryResp.StatusCode);
                }
            }
        }
        finally
        {
            await _fixture.DeleteStoresAsync(storeId);
        }
    }

    [Fact]
    public async Task CancelByMerchant_PreDispatch_Preparing_AbandonsAttemptAndReleasesInventory()
    {
        var (storeId, slug, prodAId, prodBId, zoneId, merch, cust) = await SetupStoreAndClientsAsync(10, 10);
        try
        {
            await cust.PutAsJsonAsync($"/api/stores/{slug}/cart/items", new UpsertCartItemCommand(prodAId, 2));
            using var coReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/checkout");
            coReq.Headers.Add("Idempotency-Key", $"co-pre-{Guid.NewGuid():N}");
            coReq.Content = JsonContent.Create(new CheckoutCommand(FulfillmentMode.Delivery, zoneId, "Amman"));
            var created = await (await cust.SendAsync(coReq)).Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
            await merch.PostAsync($"/api/stores/{slug}/merchant/orders/{created!.Id}/confirm", null);
            var prepResp = await merch.PostAsJsonAsync($"/api/stores/{slug}/merchant/orders/{created.Id}/fulfillment", new { NextStatus = FulfillmentStatus.Preparing });
            var preparing = await prepResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);

            using var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{created.Id}/cancel");
            cancelReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{preparing!.Version}\""));
            cancelReq.Content = JsonContent.Create(new { Reason = "Out of driver" });
            using var cancelResp = await merch.SendAsync(cancelReq);
            Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);
            var cancelled = await cancelResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);

            Assert.Equal(OrderStatus.Cancelled, cancelled!.OrderStatus);
            Assert.NotEqual(preparing.Version, cancelled.Version);
            Assert.Single(cancelled.FulfillmentAttempts!);
            Assert.Equal(FulfillmentAttemptStatus.Abandoned, cancelled.FulfillmentAttempts![0].Status);
            Assert.False(cancelled.FulfillmentAttempts[0].IsActive);
            Assert.Null(cancelled.FulfillmentAttempts[0].AttemptNumber);

            // Replay does not double-release
            using var replayReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{created.Id}/cancel");
            replayReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{preparing.Version}\""));
            replayReq.Content = JsonContent.Create(new { Reason = "Out of driver" });
            Assert.Equal(HttpStatusCode.OK, (await merch.SendAsync(replayReq)).StatusCode);

            await using var db = _fixture.CreateDbContext();
            var invA = await db.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodAId);
            Assert.Equal(10, invA.OnHand);
            Assert.Equal(0, invA.Reserved);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(storeId);
        }
    }

    [Fact]
    public async Task ResolveInventoryDisposition_Damaged_BatchAtomicity_And_DuplicateWriteOff_Enforced()
    {
        var (storeId, slug, prodAId, prodBId, zoneId, merch, cust) = await SetupStoreAndClientsAsync(10, 10);
        try
        {
            var dispatched = await PlaceAndDispatchOrderAsync(slug, prodAId, prodBId, zoneId, merch, cust, 2, 3);
            using var failResp = await merch.PostAsJsonAsync(
                $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/fulfillment",
                new { NextStatus = FulfillmentStatus.Failed, FailureReasonCode = DeliveryFailureReasonCode.CustomerUnavailable });
            var failed = await failResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
            var attempt1Id = failed!.FulfillmentAttempts![0].Id;
            var itemAId = failed.Items.Single(i => i.ProductId == prodAId).OrderItemId;
            var itemBId = failed.Items.Single(i => i.ProductId == prodBId).OrderItemId;

            using var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/cancel");
            cancelReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{failed.Version}\""));
            cancelReq.Content = JsonContent.Create(new { Reason = "Damaged return" });
            var cancelled = await (await merch.SendAsync(cancelReq)).Content.ReadFromJsonAsync<OrderDto>(JsonOptions);

            // Batch atomicity: valid itemA + invalid random OrderItemId -> 400/409, nothing mutated, Version unchanged
            using var badBatchReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/inventory-disposition/resolve");
            badBatchReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{cancelled!.Version}\""));
            badBatchReq.Content = JsonContent.Create(new
            {
                FulfillmentAttemptId = attempt1Id,
                Items = new[]
                {
                    new { OrderItemId = itemAId, Disposition = ReturnDisposition.Damaged, Note = "Damaged" },
                    new { OrderItemId = Guid.NewGuid(), Disposition = ReturnDisposition.Restockable, Note = "Invalid" }
                }
            });
            using var badBatchResp = await merch.SendAsync(badBatchReq);
            Assert.False(badBatchResp.IsSuccessStatusCode);

            await using (var db = _fixture.CreateDbContext())
            {
                var reloaded = await db.Orders.AsNoTracking().Include(o => o.FulfillmentAttempts).ThenInclude(a => a.Items).SingleAsync(o => o.Id == dispatched.Id);
                Assert.Equal(cancelled.Version, Convert.ToBase64String(reloaded.RowVersion));
                Assert.All(reloaded.FulfillmentAttempts.Single().Items, ai => Assert.Equal(ReturnDisposition.Pending, ai.ReturnDisposition));
                Assert.Empty(await db.InventoryAdjustments.AsNoTracking().Where(a => a.OrderId == dispatched.Id).ToListAsync());
            }

            // Valid Damaged disposition via API
            using var dmgReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/inventory-disposition/resolve");
            dmgReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{cancelled.Version}\""));
            dmgReq.Content = JsonContent.Create(new
            {
                FulfillmentAttemptId = attempt1Id,
                Items = new[]
                {
                    new { OrderItemId = itemAId, Disposition = ReturnDisposition.Damaged, Note = "Crushed" },
                    new { OrderItemId = itemBId, Disposition = ReturnDisposition.Restockable, Note = "Intact" }
                }
            });
            using var dmgResp = await merch.SendAsync(dmgReq);
            Assert.Equal(HttpStatusCode.OK, dmgResp.StatusCode);
            var settled = await dmgResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
            var settledItemA = settled!.FulfillmentAttempts!.Single().Items.Single(i => i.OrderItemId == itemAId);
            Assert.NotNull(settledItemA.ReturnedAtUtc);
            Assert.NotNull(settledItemA.ResolvedByUserId);

            // Attempt to change Damaged -> Lost rejected with 409 without double deduction
            using var dupReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/inventory-disposition/resolve");
            dupReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{settled.Version}\""));
            dupReq.Content = JsonContent.Create(new
            {
                FulfillmentAttemptId = attempt1Id,
                Items = new[] { new { OrderItemId = itemAId, Disposition = ReturnDisposition.Lost, Note = "Dup" } }
            });
            Assert.Equal(HttpStatusCode.Conflict, (await merch.SendAsync(dupReq)).StatusCode);

            await using (var db = _fixture.CreateDbContext())
            {
                var invA = await db.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodAId);
                Assert.Equal(8, invA.OnHand);
                Assert.Equal(0, invA.Reserved);
                var adj = await db.InventoryAdjustments.AsNoTracking().SingleAsync(a => a.OrderId == dispatched.Id);
                Assert.Equal(InventoryAdjustmentOperationType.WriteOffDamaged, adj.OperationType);
                Assert.Equal(-2, adj.OnHandDelta);
                Assert.Equal(-2, adj.ReservedDelta);
            }
        }
        finally
        {
            await _fixture.DeleteStoresAsync(storeId);
        }
    }

    [Fact]
    public async Task ResolveDeliveryCorrection_NegativeAndRepeatedCases_Enforced()
    {
        var (storeId, slug, prodAId, prodBId, zoneId, merch, cust) = await SetupStoreAndClientsAsync(10, 10);
        try
        {
            var dispatched = await PlaceAndDispatchOrderAsync(slug, prodAId, prodBId, zoneId, merch, cust, 1, 1);

            // Fail with Temporary (CustomerUnavailable) -> correction must fail 409
            using var failTemp = await merch.PostAsJsonAsync(
                $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/fulfillment",
                new { NextStatus = FulfillmentStatus.Failed, FailureReasonCode = DeliveryFailureReasonCode.CustomerUnavailable });
            var tempFailed = await failTemp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);

            using var wrongCatReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/delivery/correction");
            wrongCatReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{tempFailed!.Version}\""));
            wrongCatReq.Content = JsonContent.Create(new { ResolutionNote = "Wrong category" });
            Assert.Equal(HttpStatusCode.Conflict, (await merch.SendAsync(wrongCatReq)).StatusCode);

            // Unauthorized customer -> 403
            using var custCorrReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/delivery/correction");
            custCorrReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{tempFailed.Version}\""));
            custCorrReq.Content = JsonContent.Create(new { ResolutionNote = "Hack" });
            Assert.Equal(HttpStatusCode.Forbidden, (await cust.SendAsync(custCorrReq)).StatusCode);

            // Recover & fail with IncompleteAddress (RequiresCorrection)
            var attempt1Id = tempFailed.FulfillmentAttempts![0].Id;
            var itemAId = tempFailed.Items.Single(i => i.ProductId == prodAId).OrderItemId;
            var itemBId = tempFailed.Items.Single(i => i.ProductId == prodBId).OrderItemId;
            using var res1 = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/inventory-disposition/resolve");
            res1.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{tempFailed.Version}\""));
            res1.Content = JsonContent.Create(new { FulfillmentAttemptId = attempt1Id, Items = new[] { new { OrderItemId = itemAId, Disposition = ReturnDisposition.Restockable }, new { OrderItemId = itemBId, Disposition = ReturnDisposition.Restockable } } });
            var r1 = await (await merch.SendAsync(res1)).Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
            using var ret1 = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/delivery/retry");
            ret1.Headers.Add("Idempotency-Key", "corr-neg-1");
            ret1.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{r1!.Version}\""));
            ret1.Content = JsonContent.Create(new { });
            await merch.SendAsync(ret1);
            await merch.PostAsJsonAsync($"/api/stores/{slug}/merchant/orders/{dispatched.Id}/fulfillment", new { NextStatus = FulfillmentStatus.OutForDelivery });
            using var failCorr = await merch.PostAsJsonAsync(
                $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/fulfillment",
                new { NextStatus = FulfillmentStatus.Failed, FailureReasonCode = DeliveryFailureReasonCode.IncompleteAddress });
            var corrFailed = await failCorr.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);

            // Empty ResolutionNote -> 400
            using var emptyNoteReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/delivery/correction");
            emptyNoteReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{corrFailed!.Version}\""));
            emptyNoteReq.Content = JsonContent.Create(new { ResolutionNote = "   " });
            Assert.Equal(HttpStatusCode.BadRequest, (await merch.SendAsync(emptyNoteReq)).StatusCode);

            // Valid correction
            using var okCorrReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/delivery/correction");
            okCorrReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{corrFailed.Version}\""));
            okCorrReq.Content = JsonContent.Create(new { ResolutionNote = "Building 12" });
            var corrected = await (await merch.SendAsync(okCorrReq)).Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
            var origResolvedAt = corrected!.FulfillmentAttempts!.OrderByDescending(a => a.AttemptNumber ?? 0).First().ResolvedAtUtc;

            // Repeated correction with SAME note -> idempotent no-op (preserves ResolvedAtUtc & Version)
            using var sameRepeatReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/delivery/correction");
            sameRepeatReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{corrected.Version}\""));
            sameRepeatReq.Content = JsonContent.Create(new { ResolutionNote = "Building 12" });
            var sameRepeatResp = await merch.SendAsync(sameRepeatReq);
            Assert.Equal(HttpStatusCode.OK, sameRepeatResp.StatusCode);
            var sameRepeated = await sameRepeatResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
            Assert.Equal(corrected.Version, sameRepeated!.Version);
            var repeatedResolvedAt = sameRepeated.FulfillmentAttempts!.OrderByDescending(a => a.AttemptNumber ?? 0).First().ResolvedAtUtc;
            Assert.NotNull(origResolvedAt);
            Assert.NotNull(repeatedResolvedAt);
            Assert.True(Math.Abs((repeatedResolvedAt!.Value - origResolvedAt!.Value).TotalMilliseconds) < 1);

            // Repeated correction with DIFFERENT note -> 409 Conflict (original audit preserved)
            using var diffRepeatReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/merchant/orders/{dispatched.Id}/delivery/correction");
            diffRepeatReq.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{corrected.Version}\""));
            diffRepeatReq.Content = JsonContent.Create(new { ResolutionNote = "Building 99" });
            Assert.Equal(HttpStatusCode.Conflict, (await merch.SendAsync(diffRepeatReq)).StatusCode);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(storeId);
        }
    }

    [Fact]
    public async Task Genuine_CheckConstraint_547_IsNotMaskedAsConcurrencyConflict()
    {
        var (storeId, slug, prodAId, prodBId, zoneId, merch, cust) = await SetupStoreAndClientsAsync(10, 10);
        try
        {
            var dispatched = await PlaceAndDispatchOrderAsync(slug, prodAId, prodBId, zoneId, merch, cust, 1, 1);
            await using var db = _fixture.CreateDbContext();
            var attemptItem = await db.FulfillmentAttemptItems.FirstAsync(ai => ai.OrderId == dispatched.Id);

            // Force genuine invalid (ReturnDisposition=Pending, ReturnedAtUtc != null) -> SQL 547 CK_FulfillmentAttemptItems_ReturnedAtUtc_Semantics
            db.Entry(attemptItem).Property("ReturnedAtUtc").CurrentValue = DateTimeOffset.UtcNow;
            var dbEx = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var sqlEx = Assert.IsType<Microsoft.Data.SqlClient.SqlException>(dbEx.InnerException);
            Assert.Equal(547, sqlEx.Number);

            await using var scope = _fixture.CreateAsyncScope();
            var handler = new RetailFlow.Api.Hosting.RetailFlowExceptionHandler(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<RetailFlow.Api.Hosting.RetailFlowExceptionHandler>.Instance);
            var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider
            };
            ctx.Request.Path = "/api/test";
            await handler.TryHandleAsync(ctx, dbEx, CancellationToken.None);
            Assert.Equal(500, ctx.Response.StatusCode);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(storeId);
        }
    }
}
