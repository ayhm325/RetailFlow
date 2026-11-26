using Microsoft.EntityFrameworkCore;

using RetailFlow.Application.Orders;
using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Orders;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Infrastructure.Orders;

public sealed class CartService : ICartService
{
    private readonly RetailFlowDbContext _dbContext;

    public CartService(RetailFlowDbContext dbContext)
    {
        _dbContext = dbContext;
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

        if (cart is null || cart.Items.Count == 0)
        {
            return new CartDto(storeId, customerId, currencyCode, 0m, Array.Empty<CartItemDto>());
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

        return new CartDto(storeId, customerId, currencyCode, subtotal, items);
    }

    public async Task<CartDto> UpsertItemAsync(
        Guid storeId,
        Guid customerId,
        UpsertCartItemCommand command,
        CancellationToken cancellationToken)
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

        var now = DateTimeOffset.UtcNow;
        var cart = await _dbContext.Carts
            .Include(c => c.Items)
            .SingleOrDefaultAsync(c => c.StoreId == storeId && c.CustomerId == customerId, cancellationToken);

        if (cart is null)
        {
            cart = Cart.Create(Guid.NewGuid(), storeId, customerId, now);
            _dbContext.Carts.Add(cart);
        }
        else
        {
            cart.Touch(now);
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

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetCartAsync(storeId, customerId, cancellationToken);
    }

    public async Task<CartDto> RemoveItemAsync(
        Guid storeId,
        Guid customerId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var cart = await _dbContext.Carts
            .Include(c => c.Items)
            .SingleOrDefaultAsync(c => c.StoreId == storeId && c.CustomerId == customerId, cancellationToken);

        if (cart is not null)
        {
            var item = cart.Items.SingleOrDefault(i => i.ProductId == productId);
            if (item is not null)
            {
                _dbContext.CartItems.Remove(item);
                cart.Touch(DateTimeOffset.UtcNow);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        return await GetCartAsync(storeId, customerId, cancellationToken);
    }

    public async Task ClearCartAsync(Guid storeId, Guid customerId, CancellationToken cancellationToken)
    {
        var cart = await _dbContext.Carts
            .Include(c => c.Items)
            .SingleOrDefaultAsync(c => c.StoreId == storeId && c.CustomerId == customerId, cancellationToken);

        if (cart is not null && cart.Items.Count > 0)
        {
            _dbContext.CartItems.RemoveRange(cart.Items);
            cart.Touch(DateTimeOffset.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
