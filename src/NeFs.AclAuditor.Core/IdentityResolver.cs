using System.Collections.Concurrent;
using System.DirectoryServices.AccountManagement;
using System.Security.Principal;
using NeFs.AclAuditor.Core.Models;

namespace NeFs.AclAuditor.Core;

public interface IIdentityResolver
{
    (string DisplayName, IdentityKind Kind, string? Note) Resolve(IdentityReference identity);
}

public sealed class IdentityResolver : IIdentityResolver
{
    private readonly ConcurrentDictionary<string, (string DisplayName, IdentityKind Kind, string? Note)> _cache = new();

    public (string DisplayName, IdentityKind Kind, string? Note) Resolve(IdentityReference identity)
    {
        var key = identity.Value;
        return _cache.GetOrAdd(key, _ => ResolveCore(identity));
    }

    private static (string DisplayName, IdentityKind Kind, string? Note) ResolveCore(IdentityReference identity)
    {
        string? sid = null;
        try
        {
            if (identity is SecurityIdentifier securityIdentifier)
                sid = securityIdentifier.Value;
            else if (identity is NTAccount)
            {
                try
                {
                    sid = identity.Translate(typeof(SecurityIdentifier)).Value;
                }
                catch
                {
                    // keep null
                }
            }

            NTAccount account;
            try
            {
                account = (NTAccount)identity.Translate(typeof(NTAccount));
            }
            catch (IdentityNotMappedException)
            {
                return (sid ?? identity.Value, IdentityKind.Unknown, "SID не сопоставлен с именем");
            }
            catch (SystemException)
            {
                return (sid ?? identity.Value, IdentityKind.Unknown, "Не удалось разрешить SID");
            }

            var display = account.Value;
            var kind = DetectKind(display, sid);
            return (display, kind, null);
        }
        catch (Exception ex)
        {
            return (sid ?? identity.Value, IdentityKind.Unknown, ex.Message);
        }
    }

    private static IdentityKind DetectKind(string accountName, string? sid)
    {
        try
        {
            if (!string.IsNullOrEmpty(sid))
            {
                var securityIdentifier = new SecurityIdentifier(sid);
                // Well-known groups / built-in often appear as groups.
                if (IsWellKnownGroup(securityIdentifier))
                    return IdentityKind.Group;
            }

            var parts = accountName.Split('\\', 2);
            if (parts.Length == 2)
            {
                var domain = parts[0];
                var name = parts[1];

                // Machine-local accounts
                if (string.Equals(domain, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
                    || domain is "." or "BUILTIN" or "NT AUTHORITY")
                {
                    using var context = new PrincipalContext(ContextType.Machine);
                    var principal = Principal.FindByIdentity(context, IdentityType.SamAccountName, name);
                    return principal switch
                    {
                        GroupPrincipal => IdentityKind.Group,
                        UserPrincipal => IdentityKind.User,
                        _ => IdentityKind.Unknown
                    };
                }

                using var domainContext = new PrincipalContext(ContextType.Domain, domain);
                var domainPrincipal = Principal.FindByIdentity(domainContext, IdentityType.SamAccountName, name);
                return domainPrincipal switch
                {
                    GroupPrincipal => IdentityKind.Group,
                    UserPrincipal => IdentityKind.User,
                    _ => IdentityKind.Unknown
                };
            }
        }
        catch
        {
            // Fall through — SID prefix heuristics
        }

        // SID S-1-5-32-* = Builtin groups; S-1-5-21-...-512/513 etc. often groups — leave Unknown if unsure
        if (!string.IsNullOrEmpty(sid) && sid.StartsWith("S-1-5-32-", StringComparison.Ordinal))
            return IdentityKind.Group;

        return IdentityKind.Unknown;
    }

    private static bool IsWellKnownGroup(SecurityIdentifier sid)
    {
        try
        {
            return sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)
                   || sid.IsWellKnown(WellKnownSidType.BuiltinUsersSid)
                   || sid.IsWellKnown(WellKnownSidType.BuiltinGuestsSid)
                   || sid.IsWellKnown(WellKnownSidType.CreatorOwnerSid)
                   || sid.IsWellKnown(WellKnownSidType.WorldSid)
                   || sid.IsWellKnown(WellKnownSidType.AuthenticatedUserSid)
                   || sid.IsWellKnown(WellKnownSidType.LocalSystemSid)
                   || sid.IsWellKnown(WellKnownSidType.NetworkServiceSid)
                   || sid.IsWellKnown(WellKnownSidType.LocalServiceSid);
        }
        catch
        {
            return false;
        }
    }
}
