using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using RetailFlow.Application.Identity;
using RetailFlow.Application.Orders;
using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Currencies;
using RetailFlow.Domain.Identity;
using RetailFlow.Domain.Inventory;
using RetailFlow.Domain.Orders;
using RetailFlow.Domain.Stores;
using RetailFlow.Infrastructure.Identity;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Api.IntegrationTests;

public sealed class AuditPhasesIntegrationTests : IClassFixture<CatalogSqlTestFixture>
{
    private static readonly Currency Jod = Currency.Create("JOD", "400", 3);
    private readonly CatalogSqlTestFixture _fixture;

    public AuditPhasesIntegrationTests(CatalogSqlTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static string CreateLegacy310kHash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, 310_000, HashAlgorithmName.SHA256, 32);
        return $"v1.310000.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(derived)}";
    }

    [Fact]
    public async Task Phase1_LegacyHashMigration_UpgradesStoreUserAndPlatformAdmin_AndHandlesConcurrency()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var store = Store.Create(Guid.NewGuid(), "Hash Shop", $"hash-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, DateTimeOffset.UtcNow);
        var settings = StoreSettings.Create(Guid.NewGuid(), store, Jod, "Asia/Amman", true, false);

        var storeUserId = Guid.NewGuid();
        var storeUser = new RetailFlowIdentityUser
        {
            Id = storeUserId,
            UserName = storeUserId.ToString("N"),
            NormalizedUserName = storeUserId.ToString("N").ToUpperInvariant(),
            Email = $"legacy-{suffix}@example.com",
            NormalizedEmail = $"LEGACY-{suffix}@EXAMPLE.COM",
            DisplayName = "Legacy User",
            StoreId = store.Id,
            PasswordHash = CreateLegacy310kHash("Leg123"),
            SecurityStamp = Guid.NewGuid().ToString("N").ToUpperInvariant(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var adminUserId = Guid.NewGuid();
        var adminUser = new RetailFlowIdentityUser
        {
            Id = adminUserId,
            UserName = adminUserId.ToString("N"),
            NormalizedUserName = adminUserId.ToString("N").ToUpperInvariant(),
            Email = $"legadmin-{suffix}@example.com",
            NormalizedEmail = $"LEGADMIN-{suffix}@EXAMPLE.COM",
            DisplayName = "Legacy Admin",
            StoreId = null,
            PasswordHash = CreateLegacy310kHash("Adm123"),
            SecurityStamp = Guid.NewGuid().ToString("N").ToUpperInvariant(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(store);
            db.StoreSettings.Add(settings);
            if (!await db.Roles.AnyAsync(r => r.Id == RetailFlowIdentityRole.CustomerRoleId))
            {
                db.Roles.Add(new RetailFlowIdentityRole
                {
                    Id = RetailFlowIdentityRole.CustomerRoleId,
                    Name = IdentityRoleNames.Customer,
                    NormalizedName = IdentityRoleNames.Customer.ToUpperInvariant(),
                    ConcurrencyStamp = Guid.NewGuid().ToString()
                });
            }

            if (!await db.Roles.AnyAsync(r => r.Id == RetailFlowIdentityRole.PlatformAdminRoleId))
            {
                db.Roles.Add(new RetailFlowIdentityRole
                {
                    Id = RetailFlowIdentityRole.PlatformAdminRoleId,
                    Name = IdentityRoleNames.PlatformAdmin,
                    NormalizedName = IdentityRoleNames.PlatformAdmin.ToUpperInvariant(),
                    ConcurrencyStamp = Guid.NewGuid().ToString()
                });
            }

            db.Users.AddRange(storeUser, adminUser);
            db.UserRoles.AddRange(
                new IdentityUserRole<Guid> { UserId = storeUser.Id, RoleId = RetailFlowIdentityRole.CustomerRoleId },
                new IdentityUserRole<Guid> { UserId = adminUser.Id, RoleId = RetailFlowIdentityRole.PlatformAdminRoleId });
            await db.SaveChangesAsync();
        }

        try
        {
            await using var scope1 = _fixture.CreateAsyncScope();
            await using var scope2 = _fixture.CreateAsyncScope();
            var auth1 = scope1.ServiceProvider.GetRequiredService<IAuthService>();
            var auth2 = scope2.ServiceProvider.GetRequiredService<IAuthService>();

            // Failed login on PlatformAdmin does NOT upgrade hash
            var failed = await auth1.SignInPlatformAdminAsync(new LoginCommand(adminUser.Email!, "WrongPass"), CancellationToken.None);
            Assert.Equal(AuthSignInOutcome.Failed, failed.Outcome);
            await using (var db = _fixture.CreateDbContext())
            {
                var a = await db.Users.AsNoTracking().SingleAsync(x => x.Id == adminUser.Id);
                Assert.StartsWith("v1.310000.", a.PasswordHash, StringComparison.Ordinal);
            }

            // Concurrent successful logins for StoreUser both succeed and upgrade hash to Identity V3 (AQAAAA)
            var results = await Task.WhenAll(
                auth1.SignInStoreUserAsync(store.Id, new LoginCommand(storeUser.Email!, "Leg123"), CancellationToken.None),
                auth2.SignInStoreUserAsync(store.Id, new LoginCommand(storeUser.Email!, "Leg123"), CancellationToken.None));
            Assert.All(results, r => Assert.Equal(AuthSignInOutcome.Succeeded, r.Outcome));

            // PlatformAdmin login also upgrades hash to Identity V3 (AQAAAA)
            var adminResult = await auth1.SignInPlatformAdminAsync(new LoginCommand(adminUser.Email!, "Adm123"), CancellationToken.None);
            Assert.Equal(AuthSignInOutcome.Succeeded, adminResult.Outcome);

            await using (var db = _fixture.CreateDbContext())
            {
                var upgradedUser = await db.Users.AsNoTracking().SingleAsync(x => x.Id == storeUser.Id);
                var upgradedAdmin = await db.Users.AsNoTracking().SingleAsync(x => x.Id == adminUser.Id);
                Assert.StartsWith("AQAAAA", upgradedUser.PasswordHash, StringComparison.Ordinal);
                Assert.StartsWith("AQAAAA", upgradedAdmin.PasswordHash, StringComparison.Ordinal);
            }
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
            await using var db = _fixture.CreateDbContext();
            await db.Users.Where(u => u.Id == adminUser.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Phase2And3_CheckoutLeaseRecovery_ConcurrentClaims_AndExpired3StepLifecycle_AllVerified()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var now = DateTimeOffset.UtcNow;
        var store = Store.Create(Guid.NewGuid(), "Recov Shop", $"recov-{suffix}", StoreType.Physical, StoreStatus.Active, now);
        var settings = StoreSettings.Create(Guid.NewGuid(), store, Jod, "Asia/Amman", true, true);
        var cat = Category.Create(Guid.NewGuid(), store.Id, "Cat", "cat", true, 1);
        var prod = Product.Create(Guid.NewGuid(), store.Id, cat.Id, "Beans", 10.000m, settings, Jod, ProductStatus.Published, now);
        var inv = InventoryItem.Create(Guid.NewGuid(), store.Id, prod.Id, onHand: 10, reserved: 0);
        var customerId = Guid.NewGuid();

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(store);
            db.StoreSettings.Add(settings);
            db.Categories.Add(cat);
            db.Products.Add(prod);
            db.InventoryItems.Add(inv);
            await db.SaveChangesAsync();
        }

        try
        {
            await using var scope1 = _fixture.CreateAsyncScope();
            await using var scope2 = _fixture.CreateAsyncScope();
            var cartSvc = scope1.ServiceProvider.GetRequiredService<ICartService>();
            var checkout1 = scope1.ServiceProvider.GetRequiredService<ICheckoutService>();
            var checkout2 = scope2.ServiceProvider.GetRequiredService<ICheckoutService>();

            // A. Phase 3: Expired CheckoutRequest 3-step lifecycle (First clears, Second creates 1 Order, Third replays)
            await cartSvc.UpsertItemAsync(store.Id, customerId, new UpsertCartItemCommand(prod.Id, 2), CancellationToken.None);
            var expiredKey = $"exp-3step-{suffix}";
            var expiredClaim = CheckoutRequest.Claim(Guid.NewGuid(), store.Id, customerId, expiredKey, new string('C', 64), now.AddHours(-25));
            await using (var db = _fixture.CreateDbContext())
            {
                db.CheckoutRequests.Add(expiredClaim);
                await db.SaveChangesAsync();
            }

            var cmd = new CheckoutCommand(FulfillmentMode.Pickup, null, null);
            var firstAttempt = await checkout1.CheckoutAsync(store.Id, customerId, expiredKey, cmd, CancellationToken.None);
            Assert.Equal(CheckoutOutcomeStatus.ValidationFailed, firstAttempt.Status);

            var secondAttempt = await checkout1.CheckoutAsync(store.Id, customerId, expiredKey, cmd, CancellationToken.None);
            Assert.Equal(CheckoutOutcomeStatus.Created, secondAttempt.Status);
            Assert.NotNull(secondAttempt.Order);

            var thirdAttempt = await checkout1.CheckoutAsync(store.Id, customerId, expiredKey, cmd, CancellationToken.None);
            Assert.Equal(CheckoutOutcomeStatus.Replayed, thirdAttempt.Status);
            Assert.Equal(secondAttempt.Order!.Id, thirdAttempt.Order!.Id);

            // B. Phase 2: Stale InProgress lease (crashed worker) is atomically reclaimed and creates 1 Order
            await cartSvc.UpsertItemAsync(store.Id, customerId, new UpsertCartItemCommand(prod.Id, 3), CancellationToken.None);
            var staleKey = $"stale-{suffix}";
            var staleCartHash = await ComputeTestHashAsync(store.Id, customerId, cmd, $"{prod.Id}:3");
            var staleClaim = CheckoutRequest.Claim(Guid.NewGuid(), store.Id, customerId, staleKey, staleCartHash, now.AddMinutes(-5));
            await using (var db = _fixture.CreateDbContext())
            {
                db.CheckoutRequests.Add(staleClaim);
                await db.SaveChangesAsync();
            }

            // Concurrent recovery attempts on the same expired lease -> at most 1 Created
            var raceResults = await Task.WhenAll(
                checkout1.CheckoutAsync(store.Id, customerId, staleKey, cmd, CancellationToken.None),
                checkout2.CheckoutAsync(store.Id, customerId, staleKey, cmd, CancellationToken.None));

            Assert.Equal(1, raceResults.Count(r => r.Status == CheckoutOutcomeStatus.Created));
            Assert.All(raceResults, r => Assert.True(r.Status is CheckoutOutcomeStatus.Created or CheckoutOutcomeStatus.ConcurrentInFlight or CheckoutOutcomeStatus.Replayed));

            await using (var db = _fixture.CreateDbContext())
            {
                var ordersCount = await db.Orders.CountAsync(o => o.StoreId == store.Id);
                var dbInv = await db.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prod.Id);
                var dbCartItems = await db.Carts
                    .Where(c => c.StoreId == store.Id && c.CustomerId == customerId)
                    .SelectMany(c => c.Items)
                    .CountAsync();
                Assert.Equal(2, ordersCount);
                Assert.Equal(5, dbInv.Reserved); // 2 + 3
                Assert.Equal(0, dbCartItems);
            }
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task Phase5_ConcurrentPlatformAdminBootstrap_CreatesAtMostOneAdmin()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var verifyDb0 = _fixture.CreateDbContext();
        var existingAdmins = await (
            from ur in verifyDb0.UserRoles.AsNoTracking()
            join r in verifyDb0.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where r.NormalizedName == IdentityRoleNames.PlatformAdmin.ToUpperInvariant()
            select ur.UserId)
            .ToListAsync();

        if (existingAdmins.Count > 0)
        {
            // Skip modifying pre-existing admins if another test left one; ensure idempotency returns null
            await using var scope = _fixture.CreateAsyncScope();
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
            var skipped = await auth.EnsurePlatformAdminAsync($"b-{suffix}@test.local", "Admin", "Str0ngPass!1", CancellationToken.None);
            Assert.Null(skipped);
            return;
        }

        Guid? createdId = null;
        try
        {
            await using var scope1 = _fixture.CreateAsyncScope();
            await using var scope2 = _fixture.CreateAsyncScope();
            var auth1 = scope1.ServiceProvider.GetRequiredService<IAuthService>();
            var auth2 = scope2.ServiceProvider.GetRequiredService<IAuthService>();

            var ids = await Task.WhenAll(
                auth1.EnsurePlatformAdminAsync($"boot1-{suffix}@test.local", "Admin 1", "Str0ngPass!1", CancellationToken.None),
                auth2.EnsurePlatformAdminAsync($"boot2-{suffix}@test.local", "Admin 2", "Str0ngPass!1", CancellationToken.None));

            var created = ids.Where(id => id.HasValue).Select(id => id!.Value).ToList();
            Assert.True(created.Count <= 1);
            if (created.Count == 1)
            {
                createdId = created[0];
            }

            await using var verifyDb = _fixture.CreateDbContext();
            var hasAdmin = await (
                from ur in verifyDb.UserRoles.AsNoTracking()
                join r in verifyDb.Roles.AsNoTracking() on ur.RoleId equals r.Id
                where r.NormalizedName == IdentityRoleNames.PlatformAdmin.ToUpperInvariant()
                select ur.UserId)
                .AnyAsync();
            Assert.True(hasAdmin);
        }
        finally
        {
            if (createdId.HasValue)
            {
                await using var db = _fixture.CreateDbContext();
                await db.Users.Where(u => u.Id == createdId.Value).ExecuteDeleteAsync();
            }
        }
    }

    [Fact]
    public async Task Phase6_DataProtection_EncryptedKeysWithCertificate_ShareAcrossProviders()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=RetailFlowTestDP", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        var services1 = new ServiceCollection();
        services1.AddDbContext<RetailFlowDbContext>(o => o.UseSqlServer(_fixture.ConnectionString));
        services1.AddDataProtection()
            .SetApplicationName("RetailFlow")
            .PersistKeysToDbContext<RetailFlowDbContext>()
            .ProtectKeysWithCertificate(cert);

        var services2 = new ServiceCollection();
        services2.AddDbContext<RetailFlowDbContext>(o => o.UseSqlServer(_fixture.ConnectionString));
        services2.AddDataProtection()
            .SetApplicationName("RetailFlow")
            .PersistKeysToDbContext<RetailFlowDbContext>()
            .ProtectKeysWithCertificate(cert);

        await using var sp1 = services1.BuildServiceProvider();
        await using var sp2 = services2.BuildServiceProvider();

        // Force creation of a fresh key protected by the X.509 certificate
        var keyManager = sp1.GetRequiredService<Microsoft.AspNetCore.DataProtection.KeyManagement.IKeyManager>();
        var createdKey = keyManager.CreateNewKey(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(30));

        try
        {
            var protector1 = sp1.GetRequiredService<IDataProtectionProvider>().CreateProtector("TestPurpose");
            var protector2 = sp2.GetRequiredService<IDataProtectionProvider>().CreateProtector("TestPurpose");

            var protectedPayload = protector1.Protect("RetailFlow-Secret-Ticket");
            var unprotectedPayload = protector2.Unprotect(protectedPayload);
            Assert.Equal("RetailFlow-Secret-Ticket", unprotectedPayload);

            await using var db = _fixture.CreateDbContext();
            var keyIdText = createdKey.KeyId.ToString();
            var encryptedXmlCount = await db.DataProtectionKeys
                .AsNoTracking()
                .CountAsync(k => k.FriendlyName != null && k.FriendlyName.Contains(keyIdText) && k.Xml != null && k.Xml.Contains("encryptedKey"));
            Assert.Equal(1, encryptedXmlCount);
        }
        finally
        {
            await using var db = _fixture.CreateDbContext();
            var keyIdText = createdKey.KeyId.ToString();
            await db.DataProtectionKeys
                .Where(k => k.FriendlyName != null && k.FriendlyName.Contains(keyIdText))
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Phase7_OrderPagination_HandlesBoundariesOverflowAndEqualTimestamps()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sameTime = DateTimeOffset.UtcNow;
        var store = Store.Create(Guid.NewGuid(), "Page Shop", $"page-{suffix}", StoreType.Physical, StoreStatus.Active, sameTime);
        var settings = StoreSettings.Create(Guid.NewGuid(), store, Jod, "Asia/Amman", true, true);
        var customerId = Guid.NewGuid();
        var prodId = Guid.NewGuid();

        var orders = Enumerable.Range(1, 3).Select(idx =>
        {
            var oid = Guid.NewGuid();
            return Order.Create(
                oid,
                store,
                settings,
                Jod,
                customerId,
                $"ORD-P-{idx}-{suffix}",
                FulfillmentMode.Pickup,
                [OrderItem.Create(Guid.NewGuid(), store.Id, oid, prodId, "Item", null, 2.000m, 1, Jod)],
                0m,
                null,
                null,
                null,
                sameTime);
        }).ToList();

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(store);
            db.StoreSettings.Add(settings);
            db.Orders.AddRange(orders);
            await db.SaveChangesAsync();
        }

        try
        {
            await using var scope = _fixture.CreateAsyncScope();
            var orderSvc = scope.ServiceProvider.GetRequiredService<IOrderService>();

            var p0 = await orderSvc.GetMerchantOrdersAsync(store.Id, page: 0, pageSize: 0);
            Assert.Equal(1, p0.Page);
            Assert.Equal(1, p0.PageSize);
            Assert.Equal(3, p0.TotalCount);
            Assert.Single(p0.Items);

            var pNeg = await orderSvc.GetCustomerOrdersAsync(store.Id, customerId, page: -5, pageSize: 500);
            Assert.Equal(1, pNeg.Page);
            Assert.Equal(RetailFlow.Infrastructure.Orders.OrderService.MaxPageSize, pNeg.PageSize);
            Assert.Equal(3, pNeg.Items.Count);

            var pOverflow = await orderSvc.GetMerchantOrdersAsync(store.Id, page: int.MaxValue, pageSize: 20);
            Assert.Equal(int.MaxValue, pOverflow.Page);
            Assert.Empty(pOverflow.Items);
            Assert.Equal(3, pOverflow.TotalCount);

            // Deterministic secondary ordering by SQL Server uniqueidentifier (SqlGuid) when CreatedAtUtc is identical
            var expectedOrder = orders.OrderByDescending(o => new System.Data.SqlTypes.SqlGuid(o.Id)).Select(o => o.Id).ToList();
            Assert.Equal(expectedOrder, pNeg.Items.Select(i => i.Id).ToList());
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    private static Task<string> ComputeTestHashAsync(Guid storeId, Guid customerId, CheckoutCommand command, string cartSignature)
    {
        var canonical = $"{storeId:N}|{customerId:N}|{(int)command.FulfillmentMode}|{command.DeliveryZoneId:N}|{command.DeliveryAddress?.Trim()}|{cartSignature}";
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical));
        return Task.FromResult(Convert.ToHexString(bytes));
    }
}
