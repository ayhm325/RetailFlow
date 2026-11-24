namespace RetailFlow.SessionTransportSpike.Api.Contracts;

public sealed record CustomerLoginRequest(
    string Email,
    string Password);
