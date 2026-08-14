using NeFs.AclAuditor.Core.Storage;

namespace NeFs.AclAuditor.ViewModels;

public sealed class StoredUserViewModel
{
    public StoredUserViewModel(StoredUser user)
    {
        User = user;
    }

    public StoredUser User { get; }
    public string DisplayName => User.DisplayName;
    public string Sid => User.Sid;
    public int ActiveGrantCount => User.ActiveGrantCount;
    public string Summary => $"{DisplayName}  ({ActiveGrantCount})";
}

public sealed class StoredGrantViewModel
{
    public StoredGrantViewModel(StoredUserGrant grant)
    {
        Grant = grant;
    }

    public StoredUserGrant Grant { get; }
    public string FolderPath => Grant.FolderPath;
    public string Level => Grant.LevelDisplayName;
    public string AceType => Grant.AceType == "Allow" ? "Разрешить" : "Запретить";
    public string Status => Grant.IsActive ? "Активно" : "Не найдено в последнем скане";
    public string LastSeen => Grant.LastSeenAt.ToLocalTime().ToString("g");
    public string FirstSeen => Grant.FirstSeenAt.ToLocalTime().ToString("g");
}
