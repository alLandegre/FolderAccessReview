using NeFs.AclAuditor.Core.Models;

namespace NeFs.AclAuditor.Core;

public sealed class SubjectFolderHit
{
    public required FolderNode Folder { get; init; }
    public required AceEntry Ace { get; init; }
}

public static class SubjectFolderFinder
{
    public static IReadOnlyList<SubjectFolderHit> Find(FolderNode root, string? sid, string displayName)
    {
        var hits = new List<SubjectFolderHit>();
        Walk(root, sid, displayName, hits);
        return hits
            .OrderBy(h => h.Folder.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void Walk(FolderNode node, string? sid, string displayName, List<SubjectFolderHit> hits)
    {
        foreach (var ace in node.Aces)
        {
            if (Matches(ace, sid, displayName))
            {
                hits.Add(new SubjectFolderHit { Folder = node, Ace = ace });
                break; // one row per folder is enough for navigation list
            }
        }

        foreach (var child in node.Children)
            Walk(child, sid, displayName, hits);
    }

    public static bool Matches(AceEntry ace, string? sid, string displayName)
    {
        if (!string.IsNullOrWhiteSpace(sid)
            && !string.IsNullOrWhiteSpace(ace.Sid)
            && string.Equals(ace.Sid, sid, StringComparison.OrdinalIgnoreCase))
            return true;

        return !string.IsNullOrWhiteSpace(displayName)
               && ace.IdentityDisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// All matching ACEs on a folder (explicit + inherited) for detail lines.
    /// </summary>
    public static IReadOnlyList<AceEntry> FindAcesOnFolder(FolderNode folder, string? sid, string displayName)
        => folder.Aces.Where(a => Matches(a, sid, displayName)).ToList();
}
