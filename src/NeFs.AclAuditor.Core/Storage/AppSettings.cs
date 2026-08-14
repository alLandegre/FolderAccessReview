using System.Text.Json;

namespace NeFs.AclAuditor.Core.Storage;

public sealed class AppSettings
{
    public string? DbFolder { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string GetSettingsPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Folder Access Review");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    public static string GetDefaultDbFolder()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Folder Access Review");

    public static string GetRecommendedSharedDbFolder()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Folder Access Review");

    public static AppSettings Load()
    {
        var path = GetSettingsPath();
        if (!File.Exists(path))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        var path = GetSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public string ResolveDbFolder()
    {
        if (!string.IsNullOrWhiteSpace(DbFolder))
            return DbFolder.Trim().TrimEnd('\\', '/');
        return GetDefaultDbFolder();
    }

    public string ResolveDbFilePath()
        => Path.Combine(ResolveDbFolder(), "user-access.db");
}
