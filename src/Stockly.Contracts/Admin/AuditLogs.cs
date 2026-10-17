using System.Text.Json;

namespace Stockly.Contracts.Admin;

/// <summary>AD20: one admin write. <see cref="Before"/> and <see cref="After"/> are JSON objects of the changed fields.</summary>
public sealed record AuditLogEntry(
    Guid Id,
    Guid AdminUserId,
    string AdminEmail,
    string Action,
    string TargetType,
    Guid TargetId,
    string? Reason,
    JsonElement? Before,
    JsonElement? After,
    DateTime CreatedAt);
