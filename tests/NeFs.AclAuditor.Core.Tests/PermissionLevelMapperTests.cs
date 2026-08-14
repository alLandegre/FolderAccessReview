using System.Security.AccessControl;
using NeFs.AclAuditor.Core;
using NeFs.AclAuditor.Core.Models;

namespace NeFs.AclAuditor.Core.Tests;

public class PermissionLevelMapperTests
{
    [Theory]
    [InlineData(FileSystemRights.FullControl, PermissionLevel.FullControl, "Полный доступ")]
    [InlineData(FileSystemRights.Modify, PermissionLevel.Modify, "Изменение")]
    [InlineData(FileSystemRights.ReadAndExecute, PermissionLevel.ReadAndExecute, "Чтение и выполнение")]
    [InlineData(FileSystemRights.ListDirectory, PermissionLevel.ListFolderContents, "Список содержимого папки")]
    [InlineData(FileSystemRights.Read, PermissionLevel.Read, "Чтение")]
    [InlineData(FileSystemRights.Write, PermissionLevel.Write, "Запись")]
    public void Map_NamedLevels(FileSystemRights rights, PermissionLevel expected, string display)
    {
        var (level, name) = PermissionLevelMapper.Map(rights);
        Assert.Equal(expected, level);
        Assert.Equal(display, name);
    }

    [Fact]
    public void Map_IgnoresSynchronizeBit()
    {
        var rights = FileSystemRights.Read | FileSystemRights.Synchronize;
        var (level, name) = PermissionLevelMapper.Map(rights);
        Assert.Equal(PermissionLevel.Read, level);
        Assert.Equal("Чтение", name);
    }

    [Fact]
    public void Map_UnusualMask_IsSpecial()
    {
        var (level, name) = PermissionLevelMapper.Map(FileSystemRights.Delete);
        Assert.Equal(PermissionLevel.Special, level);
        Assert.Equal("Особые", name);
    }
}

public class AceFiltersTests
{
    private static AceEntry Ace(string name, IdentityKind kind, Models.AceType type, bool inherited = false) => new()
    {
        IdentityDisplayName = name,
        Sid = "S-1-5-21-1",
        IdentityKind = kind,
        AceType = type,
        Level = PermissionLevel.Read,
        LevelDisplayName = "Чтение",
        RightsRaw = "Read",
        IsInherited = inherited,
        Note = null
    };

    [Fact]
    public void Apply_FiltersBySubjectAndTypeAndSearch()
    {
        var source = new[]
        {
            Ace(@"CORP\alice", IdentityKind.User, Models.AceType.Allow),
            Ace(@"CORP\Project-Readers", IdentityKind.Group, Models.AceType.Allow),
            Ace(@"CORP\bob", IdentityKind.User, Models.AceType.Deny)
        };

        var result = AceFilters.Apply(source, SubjectFilter.Users, AceTypeFilter.Allow, InheritanceFilter.All, "ali").ToList();
        Assert.Single(result);
        Assert.Equal(@"CORP\alice", result[0].IdentityDisplayName);
    }

    [Fact]
    public void Apply_FiltersInherited()
    {
        var source = new[]
        {
            Ace(@"CORP\alice", IdentityKind.User, Models.AceType.Allow, inherited: false),
            Ace(@"CORP\Readers", IdentityKind.Group, Models.AceType.Allow, inherited: true)
        };

        var explicitOnly = AceFilters.Apply(source, SubjectFilter.All, AceTypeFilter.All, InheritanceFilter.ExplicitOnly, null).ToList();
        Assert.Single(explicitOnly);
        Assert.Equal(@"CORP\alice", explicitOnly[0].IdentityDisplayName);

        var inheritedOnly = AceFilters.Apply(source, SubjectFilter.All, AceTypeFilter.All, InheritanceFilter.InheritedOnly, null).ToList();
        Assert.Single(inheritedOnly);
        Assert.Equal(@"CORP\Readers", inheritedOnly[0].IdentityDisplayName);
    }
}

public class FolderScannerDepthTests
{
    [Fact]
    public async Task Scan_RespectsMaxDepth_AndReadsOnlyFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "nefs-acl-test-" + Guid.NewGuid().ToString("N"));
        var child = Path.Combine(root, "level1");
        var grand = Path.Combine(child, "level2");
        Directory.CreateDirectory(grand);

        try
        {
            var scanner = new FolderScanner(new AclReader(new IdentityResolver()));
            var result = await scanner.ScanAsync(root, maxDepth: 1, progress: null, CancellationToken.None);

            Assert.Equal(root, result.Root.FullPath);
            Assert.Single(result.Root.Children);
            Assert.Equal("level1", result.Root.Children[0].Name);
            Assert.Empty(result.Root.Children[0].Children);
            Assert.True(result.FolderCount >= 2);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }
}

public class SubjectFolderFinderTests
{
    [Fact]
    public void Find_ReturnsFoldersWhereSubjectAppears()
    {
        var root = new FolderNode { FullPath = @"\\s\share", Name = "share", Depth = 0 };
        var a = new FolderNode { FullPath = @"\\s\share\A", Name = "A", Depth = 1 };
        var b = new FolderNode { FullPath = @"\\s\share\B", Name = "B", Depth = 1 };
        root.Children.Add(a);
        root.Children.Add(b);

        a.Aces.Add(new AceEntry
        {
            IdentityDisplayName = @"CORP\j.smith",
            Sid = "S-1-5-21-100",
            IdentityKind = IdentityKind.User,
            AceType = Models.AceType.Allow,
            Level = PermissionLevel.ReadAndExecute,
            LevelDisplayName = "Чтение и выполнение",
            RightsRaw = "ReadAndExecute",
            IsInherited = false,
            Note = null
        });
        b.Aces.Add(new AceEntry
        {
            IdentityDisplayName = @"CORP\other",
            Sid = "S-1-5-21-200",
            IdentityKind = IdentityKind.User,
            AceType = Models.AceType.Allow,
            Level = PermissionLevel.Read,
            LevelDisplayName = "Чтение",
            RightsRaw = "Read",
            IsInherited = true,
            Note = null
        });

        var hits = SubjectFolderFinder.Find(root, "S-1-5-21-100", @"CORP\j.smith");
        Assert.Single(hits);
        Assert.Equal(@"\\s\share\A", hits[0].Folder.FullPath);
    }
}

