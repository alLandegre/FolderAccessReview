using NeFs.AclAuditor.Core.Models;
using NeFs.AclAuditor.Core.Storage;

namespace NeFs.AclAuditor.Core.Tests;

public class SqliteUserAccessStoreTests
{
    [Fact]
    public void Persist_StoresOnlyExplicitUserAces_AndMarksMissingInactive()
    {
        var db = Path.Combine(Path.GetTempPath(), "nefs-user-db-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var store = new SqliteUserAccessStore(db);
            var rootPath = @"\\fileserver\Share\Projects";

            var root1 = new FolderNode { FullPath = rootPath, Name = "Projects", Depth = 0 };
            root1.Aces.Add(MakeAce(@"CORP\j.smith", "S-1-5-21-1", IdentityKind.User, inherited: false));
            root1.Aces.Add(MakeAce(@"CORP\Project-Readers", "S-1-5-21-2", IdentityKind.Group, inherited: false));
            root1.Aces.Add(MakeAce(@"CORP\a.jones", "S-1-5-21-3", IdentityKind.User, inherited: true));

            store.PersistExplicitUserGrants(rootPath, root1, DateTimeOffset.Parse("2026-07-30T10:00:00Z"));

            var users = store.GetUsers();
            Assert.Single(users);
            Assert.Equal(@"CORP\j.smith", users[0].DisplayName);

            var grants = store.GetGrantsForUser("S-1-5-21-1");
            Assert.Single(grants);
            Assert.Equal(rootPath, grants[0].FolderPath);
            Assert.True(grants[0].IsActive);

            // Second scan: user gone from this root → inactive
            var root2 = new FolderNode { FullPath = rootPath, Name = "Projects", Depth = 0 };
            store.PersistExplicitUserGrants(rootPath, root2, DateTimeOffset.Parse("2026-07-30T11:00:00Z"));

            var after = store.GetGrantsForUser("S-1-5-21-1", includeInactive: true);
            Assert.Single(after);
            Assert.False(after[0].IsActive);
            Assert.Empty(store.GetUsers(onlyWithActiveGrants: true));
        }
        finally
        {
            try { File.Delete(db); } catch { /* ignore */ }
        }
    }

    private static AceEntry MakeAce(string name, string sid, IdentityKind kind, bool inherited) => new()
    {
        IdentityDisplayName = name,
        Sid = sid,
        IdentityKind = kind,
        AceType = Models.AceType.Allow,
        Level = PermissionLevel.Read,
        LevelDisplayName = "Чтение",
        RightsRaw = "Read",
        IsInherited = inherited,
        Note = null
    };
}
