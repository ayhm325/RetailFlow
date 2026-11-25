namespace RetailFlow.Application.Stores;

public interface IStoreDirectoryResolver
{
    Task<StoreRequestContext?> ResolveActiveStoreAsync(
        string slug,
        CancellationToken cancellationToken);
}
