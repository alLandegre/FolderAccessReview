using NeFs.AclAuditor.Core.Storage;

namespace NeFs.AclAuditor.Core.Tests;

public class AppSettingsTests
{
    [Fact]
    public void ResolveDbFolder_UsesExplicitOrDefault()
    {
        var settings = new AppSettings { DbFolder = @"C:\ProgramData\Folder Access Review\" };
        Assert.Equal(@"C:\ProgramData\Folder Access Review", settings.ResolveDbFolder());
        Assert.Equal(
            Path.Combine(@"C:\ProgramData\Folder Access Review", "user-access.db"),
            settings.ResolveDbFilePath());

        var def = new AppSettings();
        Assert.Equal(AppSettings.GetDefaultDbFolder(), def.ResolveDbFolder());
    }
}
