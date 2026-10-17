namespace Stockly.Contracts.Admin;

/// <summary>
/// A subscription plan (AD11–AD13). <see cref="Name"/> is in the request's language; <see cref="MonthlyPrice"/> is an
/// EGP money string with 2 decimals.
/// </summary>
public sealed record PlanDto(Guid Id, string Code, string Name, int DailyScanQuota, int MaxMembers, string MonthlyPrice, bool IsActive);

/// <summary>AD12. <see cref="Code"/> matches <c>^[a-z0-9-]{2,32}$</c> and is immutable afterwards.</summary>
public sealed record CreatePlanRequest(string Code, string Name, int DailyScanQuota, int MaxMembers, string MonthlyPrice, bool IsActive);

/// <summary>AD13: every field but the code.</summary>
public sealed record UpdatePlanRequest(string Name, int DailyScanQuota, int MaxMembers, string MonthlyPrice, bool IsActive);
