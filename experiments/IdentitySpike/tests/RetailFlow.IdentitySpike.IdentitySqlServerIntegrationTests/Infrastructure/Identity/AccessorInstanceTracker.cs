using System.Collections.Concurrent;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;

namespace RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests.Infrastructure.Identity;

public sealed class AccessorInstanceTracker
{
    private readonly ConcurrentQueue<IIdentityLookupScopeAccessor>
        _instances = new();

    public void Record(IIdentityLookupScopeAccessor accessor)
    {
        _instances.Enqueue(accessor);
    }

    public IReadOnlyList<IIdentityLookupScopeAccessor> Instances =>
        _instances.ToArray();
}
