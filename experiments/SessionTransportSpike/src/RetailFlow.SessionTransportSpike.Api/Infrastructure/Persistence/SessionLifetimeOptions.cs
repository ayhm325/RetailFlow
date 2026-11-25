namespace RetailFlow.SessionTransportSpike.Api.Infrastructure.Persistence;

public sealed class SessionLifetimeOptions
{
    public const string SectionName = "Authentication:Session";

    public static readonly TimeSpan DefaultIdleTimeout =
        TimeSpan.FromMinutes(15);

    public static readonly TimeSpan DefaultAbsoluteLifetime =
        TimeSpan.FromHours(8);

    public TimeSpan IdleTimeout { get; set; } = DefaultIdleTimeout;

    public TimeSpan AbsoluteLifetime { get; set; } =
        DefaultAbsoluteLifetime;
}
