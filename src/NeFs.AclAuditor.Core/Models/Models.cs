namespace NeFs.AclAuditor.Core.Models;

public enum AceType
{
    Allow,
    Deny
}

public enum IdentityKind
{
    User,
    Group,
    Unknown
}

public enum PermissionLevel
{
    FullControl,
    Modify,
    ReadAndExecute,
    ListFolderContents,
    Read,
    Write,
    Special
}

public sealed class AceEntry
{
    public required string IdentityDisplayName { get; init; }
    public required string? Sid { get; init; }
    public required IdentityKind IdentityKind { get; init; }
    public required AceType AceType { get; init; }
    public required PermissionLevel Level { get; init; }
    public required string LevelDisplayName { get; init; }
    public required string RightsRaw { get; init; }
    public required bool IsInherited { get; init; }
    public required string? Note { get; init; }
}

public sealed class FolderNode
{
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public required int Depth { get; init; }
    public List<FolderNode> Children { get; } = [];
    public List<AceEntry> Aces { get; set; } = [];
    public string? Error { get; set; }

    public IEnumerable<AceEntry> ExplicitAces => Aces.Where(a => !a.IsInherited);
    public IEnumerable<AceEntry> InheritedAces => Aces.Where(a => a.IsInherited);
    public bool HasExplicitAces => Aces.Any(a => !a.IsInherited);
    public bool HasAnyAces => Aces.Count > 0;
    public int ExplicitAceCount => Aces.Count(a => !a.IsInherited);
    public int InheritedAceCount => Aces.Count(a => a.IsInherited);
}

public sealed class ScanProgress
{
    public int FoldersProcessed { get; init; }
    public int ErrorCount { get; init; }
    public string? CurrentPath { get; init; }
}

public sealed class ScanResult
{
    public required FolderNode Root { get; init; }
    public int FolderCount { get; init; }
    public int ErrorCount { get; init; }
}

public enum SubjectFilter
{
    All,
    Users,
    Groups
}

public enum AceTypeFilter
{
    All,
    Allow,
    Deny
}

public enum InheritanceFilter
{
    All,
    ExplicitOnly,
    InheritedOnly
}
