namespace Stockly.Contracts.Admin;

/// <summary>
/// <c>POST /api/admin/hangfire/session</c>: <see cref="Url"/> is <c>/hangfire?code=…</c>, valid 60 seconds and single use
/// (contract §11.3, not part of the endpoint index).
/// </summary>
public sealed record HangfireSessionResponse(string Url);
