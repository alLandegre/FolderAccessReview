using NeFs.AclAuditor.Core.Models;

namespace NeFs.AclAuditor.Core;

public static class AceFilters
{
    public static IEnumerable<AceEntry> Apply(
        IEnumerable<AceEntry> source,
        SubjectFilter subjectFilter,
        AceTypeFilter aceTypeFilter,
        InheritanceFilter inheritanceFilter,
        string? searchText)
    {
        IEnumerable<AceEntry> query = source;

        query = subjectFilter switch
        {
            SubjectFilter.Users => query.Where(a => a.IdentityKind == IdentityKind.User),
            SubjectFilter.Groups => query.Where(a => a.IdentityKind == IdentityKind.Group),
            _ => query
        };

        query = aceTypeFilter switch
        {
            AceTypeFilter.Allow => query.Where(a => a.AceType == Models.AceType.Allow),
            AceTypeFilter.Deny => query.Where(a => a.AceType == Models.AceType.Deny),
            _ => query
        };

        query = inheritanceFilter switch
        {
            InheritanceFilter.ExplicitOnly => query.Where(a => !a.IsInherited),
            InheritanceFilter.InheritedOnly => query.Where(a => a.IsInherited),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var term = searchText.Trim();
            query = query.Where(a =>
                a.IdentityDisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (a.Sid?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return query;
    }
}
