using System.Security.AccessControl;
using System.Security.Principal;
using NeFs.AclAuditor.Core.Models;

namespace NeFs.AclAuditor.Core;

public interface IAclReader
{
    IReadOnlyList<AceEntry> ReadAces(string folderPath);
}

/// <summary>
/// Read-only ACL reader. Must never call SetAccessControl or modify ACL APIs.
/// </summary>
public sealed class AclReader : IAclReader
{
    private readonly IIdentityResolver _identityResolver;

    public AclReader(IIdentityResolver identityResolver)
    {
        _identityResolver = identityResolver;
    }

    public IReadOnlyList<AceEntry> ReadAces(string folderPath)
    {
        var info = new DirectoryInfo(folderPath);
        var security = info.GetAccessControl(AccessControlSections.Access);

        // Read both explicit and inherited, then classify via IsInherited.
        // (Some share/ACL edge cases are clearer than GetAccessRules(..., includeInherited: false).)
        var rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: true,
            targetType: typeof(SecurityIdentifier));

        var result = new List<AceEntry>();
        foreach (FileSystemAccessRule rule in rules)
        {
            var (display, kind, note) = _identityResolver.Resolve(rule.IdentityReference);
            var (level, levelName) = PermissionLevelMapper.Map(rule.FileSystemRights);
            var aceType = rule.AccessControlType == AccessControlType.Allow
                ? Models.AceType.Allow
                : Models.AceType.Deny;

            var notes = new List<string>();
            if (!string.IsNullOrWhiteSpace(note))
                notes.Add(note);
            if (level == PermissionLevel.Special)
                notes.Add(rule.FileSystemRights.ToString());
            if (rule.InheritanceFlags != InheritanceFlags.None || rule.PropagationFlags != PropagationFlags.None)
                notes.Add($"Флаги: {rule.InheritanceFlags}; {rule.PropagationFlags}");

            result.Add(new AceEntry
            {
                IdentityDisplayName = display,
                Sid = rule.IdentityReference is SecurityIdentifier sid ? sid.Value : rule.IdentityReference.Value,
                IdentityKind = kind,
                AceType = aceType,
                Level = level,
                LevelDisplayName = levelName,
                RightsRaw = rule.FileSystemRights.ToString(),
                IsInherited = rule.IsInherited,
                Note = notes.Count == 0 ? null : string.Join("; ", notes)
            });
        }

        return result
            .OrderBy(a => a.IsInherited) // explicit first
            .ThenBy(a => a.AceType)
            .ThenBy(a => a.IdentityDisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
