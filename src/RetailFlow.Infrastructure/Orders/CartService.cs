using System.Data;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using RetailFlow.Application.Orders;
using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Orders;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Infrastructure.Orders;

public sealed class CartService : ICartService
{
    public const string EmptyCartVersion = "empty";
    private const int MaxConcurrencyRetries = 6;

    private readonly RetailFlowDbContext _dbContext;
    private readonly ILogger<CartService> _logger;

    public CartService(RetailFlowDbContext dbContext, ILogger<CartService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<CartDto> GetCartAsync(Guid storeId, Guid customerId, CancellationToken cancellationToken)
    {
        var currencyCode = await _dbContext.StoreSettings
            .AsNoTracking()
            .Where(s => s.StoreId == storeId)
            .Select(s => s.CurrencyCode)
            .SingleOrDefaultAsync(cancellationToken) ?? "USD";

        var cart = await _dbContext.Carts
            .AsNoTracking()
            .Include(c => c.Items)
            .SingleOrDefaultAsync(c => c.StoreId == storeId && c.CustomerId == customerId, cancellationToken);

        var version = EncodeVersion(cart?.RowVersion);

        if (cart is null || cart.Items.Count == 0)
        {
            return new CartDto(storeId, customerId, currencyCode, 0m, Array.Empty<CartItemDto>(), version);
        }

        var productIds = cart.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _dbContext.Products
            .AsNoTracking()
            .Include(p => p.InventoryItem)
            .Where(p => p.StoreId == storeId && productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var items = new List<CartItemDto>(cart.Items.Count);
        decimal subtotal = 0m;
        foreach (var ci in cart.Items)
        {
            if (!products.TryGetValue(ci.ProductId, out var product))
            {
                continue;
            }

            var lineTotal = product.Price * ci.Quantity;
            subtotal += lineTotal;
            var available = product.Status == ProductStatus.Published &&
                            product.InventoryItem is not null &&
                            product.InventoryItem.Available >= ci.Quantity;

            items.Add(new CartItemDto(
                product.Id,
                product.Name,
                product.Sku,
                product.Price,
                ci.Quantity,
                lineTotal,
                available));
        }

        return new CartDto(storeId, customerId, currencyCode, subtotal, items, version);
    }

    public async Task<CartDto> UpsertItemAsync(
        Guid storeId,
        Guid customerId,
        UpsertCartItemCommand command,
        CancellationToken cancellationToken,
        string? expectedVersion = null)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Quantity must be positive.");
        }

        var productValid = await _dbContext.Products
            .AsNoTracking()
            .AnyAsync(
                p => p.Id == command.ProductId &&
                     p.StoreId == storeId &&
                     p.Status == ProductStatus.Published &&
                     p.Category != null &&
                     p.Category.IsActive,
                cancellationToken);

        if (!productValid)
        {
            throw new InvalidOperationException("Product is unavailable or does not belong to this store.");
        }

        var hasExpectedVersion = !string.IsNullOrWhiteSpace(expectedVersion) &&
                                 !string.Equals(expectedVersion.Trim(), "*", StringComparison.Ordinal);

        for (var attempt = 1; attempt <= MaxConcurrencyRetries; attempt++)
        {
            var now = DateTimeOffset.UtcNow;
            var cart = await _dbContext.Carts
                .Include(c => c.Items)
                .SingleOrDefaultAsync(c => c.StoreId == storeId && c.CustomerId == customerId, cancellationToken);

            if (hasExpectedVersion)
            {
                EnsureVersionMatches(storeId, cart, expectedVersion!);
            }

            if (cart is null)
            {
                cart = Cart.Create(Guid.NewGuid(), storeId, customerId, now);
                _dbContext.Carts.Add(cart);
            }
            else
            {
                TouchAggregate(cart, now);
            }

            var existingItem = cart.Items.SingleOrDefault(i => i.ProductId == command.ProductId);
            if (existingItem is null)
            {
                var newItem = CartItem.Create(Guid.NewGuid(), storeId, cart.Id, command.ProductId, command.Quantity);
                _dbContext.CartItems.Add(newItem);
            }
            else
            {
                existingItem.SetQuantity(command.Quantity);
            }

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return await GetCartAsync(storeId, customerId, cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _dbContext.ChangeTracker.Clear();

                if (hasExpectedVersion)
                {
                    _logger.LogWarning(
                        "CartStaleVersionRejected StoreId={StoreId} CartId={CartId} Operation=UpsertItem",
                        storeId,
                        cart.Id);
                    throw new CartConcurrencyConflictException(
                        "The cart was modified concurrently. Refresh the cart and retry.",
                        isPreconditionFailed: true,
                        ex);
                }

                _logger.LogWarning(
                    "CartConcurrencyConflict StoreId={StoreId} CartId={CartId} Operation=UpsertItem Attempt={Attempt}",
                    storeId,
                    cart.Id,
                    attempt);

                if (attempt == MaxConcurrencyRetries)
                {
                    _logger.LogError(
                        "CartRetryExhausted StoreId={StoreId} CartId={CartId} Operation=UpsertItem",
                        storeId,
                        cart.Id);
                    throw new CartConcurrencyConflictException(
                        "The cart could not be updated due to concurrent modifications.",
                        isPreconditionFailed: false,
                        ex);
                }
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                _dbContext.ChangeTracker.Clear();

                if (hasExpectedVersion)
                {
                    _logger.LogWarning(
                        "CartStaleVersionRejected StoreId={StoreId} Operation=UpsertItem Reason=UniqueConstraint",
                        storeId);
                    throw new CartConcurrencyConflictException(
                        "The cart was modified concurrently. Refresh the cart and retry.",
                        isPreconditionFailed: true,
                        ex);
                }

                _logger.LogInformation(
                    "CartUniqueConstraintRetry StoreId={StoreId} Operation=UpsertItem Attempt={Attempt}",
                    storeId,
                    attempt);

                if (attempt == MaxConcurrencyRetries)
                {
                    _logger.LogError(
                        "CartRetryExhausted StoreId={StoreId} Operation=UpsertItem Reason=UniqueConstraint",
                        storeId);
                    throw new CartConcurrencyConflictException(
                        "The cart could not be updated due to concurrent modifications.",
                        isPreconditionFailed: false,
                        ex);
                }
            }
        }

        throw new CartConcurrencyConflictException(
            "The cart could not be updated due to concurrent modifications.",
            isPreconditionFailed: false);
    }

    public async Task<CartDto> RemoveItemAsync(
        Guid storeId,
        Guid customerId,
        Guid productId,
        CancellationToken cancellationToken,
        string? expectedVersion = null)
    {
        var hasExpectedVersion = !string.IsNullOrWhiteSpace(expectedVersion) &&
                                 !string.Equals(expectedVersion.Trim(), "*", StringComparison.Ordinal);

        for (var attempt = 1; attempt <= MaxConcurrencyRetries; attempt++)
        {
            var cart = await _dbContext.Carts
                .Include(c => c.Items)
                .SingleOrDefaultAsync(c => c.StoreId == storeId && c.CustomerId == customerId, cancellationToken);

            if (hasExpectedVersion)
            {
                EnsureVersionMatches(storeId, cart, expectedVersion!);
            }

            if (cart is null)
            {
                return await GetCartAsync(storeId, customerId, cancellationToken);
            }

            var item = cart.Items.SingleOrDefault(i => i.ProductId == productId);
            if (item is null)
            {
                return await GetCartAsync(storeId, customerId, cancellationToken);
            }

            _dbContext.CartItems.Remove(item);
            TouchAggregate(cart, DateTimeOffset.UtcNow);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return await GetCartAsync(storeId, customerId, cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _dbContext.ChangeTracker.Clear();

                if (hasExpectedVersion)
                {
                    _logger.LogWarning(
                        "CartStaleVersionRejected StoreId={StoreId} CartId={CartId} Operation=RemoveItem",
                        storeId,
                        cart.Id);
                    throw new CartConcurrencyConflictException(
                        "The cart was modified concurrently. Refresh the cart and retry.",
                        isPreconditionFailed: true,
                        ex);
                }

                _logger.LogWarning(
                    "CartConcurrencyConflict StoreId={StoreId} CartId={CartId} Operation=RemoveItem Attempt={Attempt}",
                    storeId,
                    cart.Id,
                    attempt);

                if (attempt == MaxConcurrencyRetries)
                {
                    throw new CartConcurrencyConflictException(
                        "The cart could not be updated due to concurrent modifications.",
                        isPreconditionFailed: false,
                        ex);
                }
            }
        }

        throw new CartConcurrencyConflictException(
            "The cart could not be updated due to concurrent modifications.",
            isPreconditionFailed: false);
    }

    public async Task ClearCartAsync(
        Guid storeId,
        Guid customerId,
        CancellationToken cancellationToken,
        string? expectedVersion = null)
    {
        var hasExpectedVersion = !string.IsNullOrWhiteSpace(expectedVersion) &&
                                 !string.Equals(expectedVersion.Trim(), "*", StringComparison.Ordinal);

        for (var attempt = 1; attempt <= MaxConcurrencyRetries; attempt++)
        {
            var cart = await _dbContext.Carts
                .Include(c => c.Items)
                .SingleOrDefaultAsync(c => c.StoreId == storeId && c.CustomerId == customerId, cancellationToken);

            if (hasExpectedVersion)
            {
                EnsureVersionMatches(storeId, cart, expectedVersion!);
            }

            if (cart is null || cart.Items.Count == 0)
            {
                return;
            }

            _dbContext.CartItems.RemoveRange(cart.Items);
            TouchAggregate(cart, DateTimeOffset.UtcNow);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _dbContext.ChangeTracker.Clear();

                if (hasExpectedVersion)
                {
                    _logger.LogWarning(
                        "CartStaleVersionRejected StoreId={StoreId} CartId={CartId} Operation=ClearCart",
                        storeId,
                        cart.Id);
                    throw new CartConcurrencyConflictException(
                        "The cart was modified concurrently. Refresh the cart and retry.",
                        isPreconditionFailed: true,
                        ex);
                }

                _logger.LogWarning(
                    "CartConcurrencyConflict StoreId={StoreId} CartId={CartId} Operation=ClearCart Attempt={Attempt}",
                    storeId,
                    cart.Id,
                    attempt);

                if (attempt == MaxConcurrencyRetries)
                {
                    throw new CartConcurrencyConflictException(
                        "The cart could not be updated due to concurrent modifications.",
                        isPreconditionFailed: false,
                        ex);
                }
            }
        }

        throw new CartConcurrencyConflictException(
            "The cart could not be updated due to concurrent modifications.",
            isPreconditionFailed: false);
    }

    private void TouchAggregate(Cart cart, DateTimeOffset now)
    {
        cart.Touch(now);
        // Force UpdatedAtUtc modification so SQL Server always updates the Cart row and advances its rowversion
        // even when child CartItem rows are added/updated/deleted within the same millisecond.
        _dbContext.Entry(cart).Property(c => c.UpdatedAtUtc).IsModified = true;
    }

    private void EnsureVersionMatches(Guid storeId, Cart? cart, string expectedVersion)
    {
        var currentVersion = EncodeVersion(cart?.RowVersion);
        if (!string.Equals(currentVersion, expectedVersion, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "CartStaleVersionRejected StoreId={StoreId} CartId={CartId}",
                storeId,
                cart?.Id);
            throw new CartConcurrencyConflictException(
                "The cart version does not match the current server state. Refresh the cart and retry.",
                isPreconditionFailed: true);
        }
    }

    private static string EncodeVersion(byte[]? rowVersion) =>
        rowVersion is { Length: > 0 }
            ? Convert.ToBase64String(rowVersion)
            : EmptyCartVersion;

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx &&
        sqlEx.Number is 2601 or 2627;
}
