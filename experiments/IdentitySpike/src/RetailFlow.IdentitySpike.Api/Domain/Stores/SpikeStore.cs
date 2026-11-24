namespace RetailFlow.IdentitySpike.Api.Domain.Stores;

public sealed class SpikeStore
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}