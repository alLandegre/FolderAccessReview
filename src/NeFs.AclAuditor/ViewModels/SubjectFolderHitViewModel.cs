using NeFs.AclAuditor.Core.Models;

namespace NeFs.AclAuditor.ViewModels;

public sealed class SubjectFolderHitViewModel
{
    public SubjectFolderHitViewModel(FolderNode folder, AceEntry ace)
    {
        Folder = folder;
        Ace = ace;
    }

    public FolderNode Folder { get; }
    public AceEntry Ace { get; }

    public string FullPath => Folder.FullPath;
    public string Name => Folder.Name;
    public string Level => Ace.LevelDisplayName;
    public string Inheritance => Ace.IsInherited ? "Унаследовано" : "Явное";
    public string AceType => Ace.AceType == Core.Models.AceType.Allow ? "Разрешить" : "Запретить";
    public string Summary => $"{Inheritance} · {AceType} · {Level}";
}
