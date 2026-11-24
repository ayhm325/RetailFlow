namespace RetailFlow.IdentitySpike.Api.Infrastructure.Identity;

public sealed class IdentityLookupScopeAccessor
    : IIdentityLookupScopeAccessor
{
    private IdentityLookupScope? _current;

    public IdentityLookupScope? Current => _current;

    public void SetCurrent(IdentityLookupScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var existingScope = Interlocked.CompareExchange(
            ref _current,
            scope,
            null);

        if (existingScope is not null)
        {
            throw new InvalidOperationException(
                "Identity lookup scope has already been established.");
        }
    }
}