namespace RetailFlow.Application.Stores;

public interface IStoreRequestContextAccessor
{
    StoreRequestContext? Current { get; }

    void SetCurrent(StoreRequestContext context);
}
