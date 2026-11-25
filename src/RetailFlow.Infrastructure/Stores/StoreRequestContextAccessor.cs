using RetailFlow.Application.Stores;

namespace RetailFlow.Infrastructure.Stores;

public sealed class StoreRequestContextAccessor : IStoreRequestContextAccessor
{
    private StoreRequestContext? _current;

    public StoreRequestContext? Current => Volatile.Read(ref _current);

    public void SetCurrent(StoreRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Interlocked.CompareExchange(ref _current, context, null) is not null)
        {
            throw new InvalidOperationException(
                "StoreRequestContext has already been established for this request.");
        }
    }
}
