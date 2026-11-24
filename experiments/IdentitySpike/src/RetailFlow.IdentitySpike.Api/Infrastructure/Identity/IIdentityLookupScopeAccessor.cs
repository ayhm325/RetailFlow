namespace RetailFlow.IdentitySpike.Api.Infrastructure.Identity;

public interface IIdentityLookupScopeAccessor
{
    IdentityLookupScope? Current { get; }

    void SetCurrent(IdentityLookupScope scope);
}