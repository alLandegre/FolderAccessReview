namespace NeFs.AclAuditor.Core.Storage;

public sealed class StoredUser
{
    public required string Sid { get; init; }
    public required string DisplayName { get; init; }
    public int ActiveGrantCount { get; init; }
    public DateTimeOffset? LastSeenAt { get; init; }
}

public sealed class StoredUserGrant
{
    public required string FolderPath { get; init; }
    public required string LevelDisplayName { get; init; }
    public required string AceType { get; init; }
    public required string RightsRaw { get; init; }
    public required bool IsActive { get; init; }
    public required DateTimeOffset FirstSeenAt { get; init; }
    public required DateTimeOffset LastSeenAt { get; init; }
    public required string ScanRoot { get; init; }
}

public sealed class ScanPersistResult
{
    public int UsersTouched { get; init; }
    public int GrantsUpserted { get; init; }
    public int GrantsMarkedInactive { get; init; }
}
