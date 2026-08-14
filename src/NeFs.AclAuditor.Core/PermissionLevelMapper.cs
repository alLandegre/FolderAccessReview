using System.Security.AccessControl;
using NeFs.AclAuditor.Core.Models;

namespace NeFs.AclAuditor.Core;

public static class PermissionLevelMapper
{
    public static string ToDisplayName(PermissionLevel level) => level switch
    {
        PermissionLevel.FullControl => "Полный доступ",
        PermissionLevel.Modify => "Изменение",
        PermissionLevel.ReadAndExecute => "Чтение и выполнение",
        PermissionLevel.ListFolderContents => "Список содержимого папки",
        PermissionLevel.Read => "Чтение",
        PermissionLevel.Write => "Запись",
        _ => "Особые"
    };

    public static (PermissionLevel Level, string DisplayName) Map(FileSystemRights rights)
    {
        // Match Windows Explorer named levels (folder ACE semantics).
        if (HasExact(rights, FileSystemRights.FullControl))
            return (PermissionLevel.FullControl, ToDisplayName(PermissionLevel.FullControl));

        if (HasExact(rights, FileSystemRights.Modify))
            return (PermissionLevel.Modify, ToDisplayName(PermissionLevel.Modify));

        if (HasExact(rights, FileSystemRights.ReadAndExecute))
            return (PermissionLevel.ReadAndExecute, ToDisplayName(PermissionLevel.ReadAndExecute));

        if (HasExact(rights, FileSystemRights.ListDirectory))
            return (PermissionLevel.ListFolderContents, ToDisplayName(PermissionLevel.ListFolderContents));

        if (HasExact(rights, FileSystemRights.Read))
            return (PermissionLevel.Read, ToDisplayName(PermissionLevel.Read));

        if (HasExact(rights, FileSystemRights.Write))
            return (PermissionLevel.Write, ToDisplayName(PermissionLevel.Write));

        return (PermissionLevel.Special, ToDisplayName(PermissionLevel.Special));
    }

    private static bool HasExact(FileSystemRights actual, FileSystemRights expected)
    {
        // Explorer levels are exact masks; Synchronize is often present alongside.
        var normalized = actual & ~FileSystemRights.Synchronize;
        var expectedNormalized = expected & ~FileSystemRights.Synchronize;
        return (normalized & expectedNormalized) == expectedNormalized
               && (normalized & ~expectedNormalized) == 0;
    }
}
