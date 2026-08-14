using Microsoft.Data.Sqlite;
using NeFs.AclAuditor.Core.Models;

namespace NeFs.AclAuditor.Core.Storage;

public interface IUserAccessStore
{
    string DatabasePath { get; }
    void EnsureCreated();
    ScanPersistResult PersistExplicitUserGrants(string scanRoot, FolderNode root, DateTimeOffset scannedAt);
    IReadOnlyList<StoredUser> GetUsers(string? search = null, bool onlyWithActiveGrants = true);
    IReadOnlyList<StoredUserGrant> GetGrantsForUser(string sidOrName, bool includeInactive = false);
}

public sealed class SqliteUserAccessStore : IUserAccessStore
{
    private readonly string _dbPath;

    public SqliteUserAccessStore(string? dbPath = null)
    {
        _dbPath = dbPath ?? GetDefaultDbPath();
    }

    public string DatabasePath => _dbPath;

    public static string GetDefaultDbPath()
        => Path.Combine(AppSettings.GetDefaultDbFolder(), "user-access.db");

    public static SqliteUserAccessStore FromFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        return new SqliteUserAccessStore(Path.Combine(folder.TrimEnd('\\', '/'), "user-access.db"));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS users (
                sid TEXT NOT NULL PRIMARY KEY,
                display_name TEXT NOT NULL,
                last_seen_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS grants (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_sid TEXT NOT NULL,
                folder_path TEXT NOT NULL,
                level_display TEXT NOT NULL,
                ace_type TEXT NOT NULL,
                rights_raw TEXT NOT NULL,
                scan_root TEXT NOT NULL,
                first_seen_at TEXT NOT NULL,
                last_seen_at TEXT NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 1,
                UNIQUE(user_sid, folder_path),
                FOREIGN KEY(user_sid) REFERENCES users(sid)
            );

            CREATE INDEX IF NOT EXISTS ix_grants_user ON grants(user_sid);
            CREATE INDEX IF NOT EXISTS ix_grants_active ON grants(is_active);
            CREATE INDEX IF NOT EXISTS ix_users_name ON users(display_name COLLATE NOCASE);
            """;
        cmd.ExecuteNonQuery();
    }

    public ScanPersistResult PersistExplicitUserGrants(string scanRoot, FolderNode root, DateTimeOffset scannedAt)
    {
        EnsureCreated();
        var normalizedRoot = NormalizePath(scanRoot);
        var found = new List<(string Sid, string Name, string Folder, AceEntry Ace)>();
        Collect(root, found);

        var now = scannedAt.ToString("O");
        var usersTouched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var upserted = 0;

        using var connection = Open();
        using var tx = connection.BeginTransaction();

        foreach (var item in found)
        {
            var sid = string.IsNullOrWhiteSpace(item.Sid) ? "NAME:" + item.Name : item.Sid!;
            usersTouched.Add(sid);

            using (var upsertUser = connection.CreateCommand())
            {
                upsertUser.Transaction = tx;
                upsertUser.CommandText =
                    """
                    INSERT INTO users(sid, display_name, last_seen_at)
                    VALUES ($sid, $name, $seen)
                    ON CONFLICT(sid) DO UPDATE SET
                        display_name = excluded.display_name,
                        last_seen_at = excluded.last_seen_at;
                    """;
                upsertUser.Parameters.AddWithValue("$sid", sid);
                upsertUser.Parameters.AddWithValue("$name", item.Name);
                upsertUser.Parameters.AddWithValue("$seen", now);
                upsertUser.ExecuteNonQuery();
            }

            using (var upsertGrant = connection.CreateCommand())
            {
                upsertGrant.Transaction = tx;
                upsertGrant.CommandText =
                    """
                    INSERT INTO grants(user_sid, folder_path, level_display, ace_type, rights_raw, scan_root, first_seen_at, last_seen_at, is_active)
                    VALUES ($sid, $path, $level, $type, $rights, $root, $seen, $seen, 1)
                    ON CONFLICT(user_sid, folder_path) DO UPDATE SET
                        level_display = excluded.level_display,
                        ace_type = excluded.ace_type,
                        rights_raw = excluded.rights_raw,
                        scan_root = excluded.scan_root,
                        last_seen_at = excluded.last_seen_at,
                        is_active = 1;
                    """;
                upsertGrant.Parameters.AddWithValue("$sid", sid);
                upsertGrant.Parameters.AddWithValue("$path", NormalizePath(item.Folder));
                upsertGrant.Parameters.AddWithValue("$level", item.Ace.LevelDisplayName);
                upsertGrant.Parameters.AddWithValue("$type", item.Ace.AceType == Models.AceType.Allow ? "Allow" : "Deny");
                upsertGrant.Parameters.AddWithValue("$rights", item.Ace.RightsRaw);
                upsertGrant.Parameters.AddWithValue("$root", normalizedRoot);
                upsertGrant.Parameters.AddWithValue("$seen", now);
                upsertGrant.ExecuteNonQuery();
                upserted++;
            }
        }

        // Mark grants under this scan root as inactive if not seen in this pass.
        var activeKeys = new HashSet<string>(
            found.Select(f =>
            {
                var sid = string.IsNullOrWhiteSpace(f.Sid) ? "NAME:" + f.Name : f.Sid!;
                return sid + "|" + NormalizePath(f.Folder);
            }),
            StringComparer.OrdinalIgnoreCase);

        var markedInactive = 0;
        using (var select = connection.CreateCommand())
        {
            select.Transaction = tx;
            select.CommandText =
                """
                SELECT id, user_sid, folder_path FROM grants
                WHERE is_active = 1 AND (
                    folder_path = $root OR folder_path LIKE $rootLike
                );
                """;
            select.Parameters.AddWithValue("$root", normalizedRoot);
            select.Parameters.AddWithValue("$rootLike", normalizedRoot.TrimEnd('\\') + @"\%");

            using var reader = select.ExecuteReader();
            var toDeactivate = new List<long>();
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                var sid = reader.GetString(1);
                var path = reader.GetString(2);
                var key = sid + "|" + path;
                if (!activeKeys.Contains(key))
                    toDeactivate.Add(id);
            }

            reader.Close();

            foreach (var id in toDeactivate)
            {
                using var upd = connection.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = "UPDATE grants SET is_active = 0 WHERE id = $id;";
                upd.Parameters.AddWithValue("$id", id);
                upd.ExecuteNonQuery();
                markedInactive++;
            }
        }

        tx.Commit();
        return new ScanPersistResult
        {
            UsersTouched = usersTouched.Count,
            GrantsUpserted = upserted,
            GrantsMarkedInactive = markedInactive
        };
    }

    public IReadOnlyList<StoredUser> GetUsers(string? search = null, bool onlyWithActiveGrants = true)
    {
        EnsureCreated();
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            onlyWithActiveGrants
                ? """
                  SELECT u.sid, u.display_name, u.last_seen_at,
                         (SELECT COUNT(*) FROM grants g WHERE g.user_sid = u.sid AND g.is_active = 1) AS cnt
                  FROM users u
                  WHERE ($q IS NULL OR u.display_name LIKE $like OR u.sid LIKE $like)
                    AND EXISTS (SELECT 1 FROM grants g WHERE g.user_sid = u.sid AND g.is_active = 1)
                  ORDER BY u.display_name COLLATE NOCASE;
                  """
                : """
                  SELECT u.sid, u.display_name, u.last_seen_at,
                         (SELECT COUNT(*) FROM grants g WHERE g.user_sid = u.sid AND g.is_active = 1) AS cnt
                  FROM users u
                  WHERE ($q IS NULL OR u.display_name LIKE $like OR u.sid LIKE $like)
                  ORDER BY u.display_name COLLATE NOCASE;
                  """;

        var q = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        cmd.Parameters.AddWithValue("$q", (object?)q ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$like", q is null ? DBNull.Value : "%" + q + "%");

        var list = new List<StoredUser>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new StoredUser
            {
                Sid = reader.GetString(0),
                DisplayName = reader.GetString(1),
                LastSeenAt = DateTimeOffset.Parse(reader.GetString(2)),
                ActiveGrantCount = reader.GetInt32(3)
            });
        }

        return list;
    }

    public IReadOnlyList<StoredUserGrant> GetGrantsForUser(string sidOrName, bool includeInactive = false)
    {
        EnsureCreated();
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT g.folder_path, g.level_display, g.ace_type, g.rights_raw, g.is_active,
                   g.first_seen_at, g.last_seen_at, g.scan_root
            FROM grants g
            INNER JOIN users u ON u.sid = g.user_sid
            WHERE (g.user_sid = $key OR u.display_name = $key COLLATE NOCASE)
              AND ($inc = 1 OR g.is_active = 1)
            ORDER BY g.is_active DESC, g.folder_path COLLATE NOCASE;
            """;
        cmd.Parameters.AddWithValue("$key", sidOrName);
        cmd.Parameters.AddWithValue("$inc", includeInactive ? 1 : 0);

        var list = new List<StoredUserGrant>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new StoredUserGrant
            {
                FolderPath = reader.GetString(0),
                LevelDisplayName = reader.GetString(1),
                AceType = reader.GetString(2),
                RightsRaw = reader.GetString(3),
                IsActive = reader.GetInt64(4) == 1,
                FirstSeenAt = DateTimeOffset.Parse(reader.GetString(5)),
                LastSeenAt = DateTimeOffset.Parse(reader.GetString(6)),
                ScanRoot = reader.GetString(7)
            });
        }

        return list;
    }

    private static void Collect(FolderNode node, List<(string Sid, string Name, string Folder, AceEntry Ace)> sink)
    {
        foreach (var ace in node.Aces)
        {
            if (ace.IsInherited)
                continue;
            if (ace.IdentityKind != IdentityKind.User)
                continue;

            sink.Add((ace.Sid ?? string.Empty, ace.IdentityDisplayName, node.FullPath, ace));
        }

        foreach (var child in node.Children)
            Collect(child, sink);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());
        connection.Open();
        return connection;
    }

    private static string NormalizePath(string path)
        => path.TrimEnd('\\', '/');
}
