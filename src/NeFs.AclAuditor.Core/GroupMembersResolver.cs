using System.Collections.Concurrent;
using System.DirectoryServices.AccountManagement;
using System.Security.Principal;
using NeFs.AclAuditor.Core.Models;

namespace NeFs.AclAuditor.Core;

public sealed class GroupMemberInfo
{
    public required string DisplayName { get; init; }
    public required IdentityKind Kind { get; init; }
    public required string? Sid { get; init; }
}

public interface IGroupMembersResolver
{
    Task<IReadOnlyList<GroupMemberInfo>> GetDirectMembersAsync(
        string? sid,
        string displayName,
        CancellationToken cancellationToken = default);
}

public sealed class GroupMembersResolver : IGroupMembersResolver
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<GroupMemberInfo>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<GroupMemberInfo>> GetDirectMembersAsync(
        string? sid,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = !string.IsNullOrWhiteSpace(sid) ? "SID:" + sid : "NAME:" + displayName;
        if (_cache.TryGetValue(cacheKey, out var cached))
            return Task.FromResult(cached);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var members = ResolveMembers(sid, displayName);
            _cache[cacheKey] = members;
            return members;
        }, cancellationToken);
    }

    private static IReadOnlyList<GroupMemberInfo> ResolveMembers(string? sid, string displayName)
    {
        try
        {
            foreach (var contextFactory in BuildContextFactories(sid, displayName))
            {
                using var context = contextFactory();
                GroupPrincipal? group = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(sid))
                        group = GroupPrincipal.FindByIdentity(context, IdentityType.Sid, sid);

                    if (group is null && !string.IsNullOrWhiteSpace(displayName))
                    {
                        var sam = displayName.Contains('\\')
                            ? displayName.Split('\\', 2)[1]
                            : displayName;
                        group = GroupPrincipal.FindByIdentity(context, IdentityType.SamAccountName, sam);
                    }

                    if (group is null)
                        continue;

                    using (group)
                    {
                        return ReadMembers(group);
                    }
                }
                catch
                {
                    group?.Dispose();
                }
            }

            return
            [
                new GroupMemberInfo
                {
                    DisplayName = "Группа не найдена в AD / на компьютере",
                    Kind = IdentityKind.Unknown,
                    Sid = null
                }
            ];
        }
        catch (Exception ex)
        {
            return
            [
                new GroupMemberInfo
                {
                    DisplayName = $"Не удалось получить участников: {ex.Message}",
                    Kind = IdentityKind.Unknown,
                    Sid = null
                }
            ];
        }
    }

    private static IReadOnlyList<GroupMemberInfo> ReadMembers(GroupPrincipal group)
    {
        var list = new List<GroupMemberInfo>();
        using var principals = group.GetMembers(recursive: false);
        foreach (var principal in principals)
        {
            using (principal)
            {
                var kind = principal switch
                {
                    GroupPrincipal => IdentityKind.Group,
                    UserPrincipal => IdentityKind.User,
                    _ => IdentityKind.Unknown
                };

                string? memberSid = null;
                try { memberSid = principal.Sid?.Value; } catch { /* ignore */ }

                list.Add(new GroupMemberInfo
                {
                    DisplayName = FormatName(principal),
                    Kind = kind,
                    Sid = memberSid
                });
            }
        }

        return list
            .OrderBy(m => m.Kind == IdentityKind.Group ? 0 : 1)
            .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<Func<PrincipalContext>> BuildContextFactories(string? sid, string displayName)
    {
        var domainHint = TryGetDomainHint(displayName);

        if (!string.IsNullOrWhiteSpace(domainHint)
            && !IsLocalAuthority(domainHint))
        {
            var d = domainHint;
            yield return () => new PrincipalContext(ContextType.Domain, d);
        }

        yield return () => new PrincipalContext(ContextType.Domain);
        yield return () => new PrincipalContext(ContextType.Machine);

        // sid-only builtin often lives on machine
        if (!string.IsNullOrWhiteSpace(sid)
            && sid.StartsWith("S-1-5-32-", StringComparison.Ordinal))
        {
            yield return () => new PrincipalContext(ContextType.Machine);
        }
    }

    private static string? TryGetDomainHint(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return null;
        var parts = displayName.Split('\\', 2);
        return parts.Length == 2 ? parts[0] : null;
    }

    private static bool IsLocalAuthority(string domain) =>
        string.Equals(domain, "BUILTIN", StringComparison.OrdinalIgnoreCase)
        || string.Equals(domain, "NT AUTHORITY", StringComparison.OrdinalIgnoreCase)
        || string.Equals(domain, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
        || domain == ".";

    private static string FormatName(Principal principal)
    {
        try
        {
            if (principal.Sid is not null)
            {
                try
                {
                    return ((NTAccount)principal.Sid.Translate(typeof(NTAccount))).Value;
                }
                catch
                {
                    // fall through
                }
            }

            return principal.SamAccountName ?? principal.Name ?? principal.DisplayName ?? "(без имени)";
        }
        catch
        {
            return principal.SamAccountName ?? principal.Name ?? "(без имени)";
        }
    }
}
