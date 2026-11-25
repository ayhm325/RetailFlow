namespace RetailFlow.SessionTransportSpike.Api.Contracts;

public sealed record CustomerChangePasswordRequest(
    string CurrentPassword,
    string NewPassword);
