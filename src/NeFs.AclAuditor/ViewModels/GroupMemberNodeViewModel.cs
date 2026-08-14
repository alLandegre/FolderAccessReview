using System.Collections.ObjectModel;
using NeFs.AclAuditor.Core;
using NeFs.AclAuditor.Core.Models;

namespace NeFs.AclAuditor.ViewModels;

public sealed class GroupMemberNodeViewModel : ObservableObject
{
    private readonly IGroupMembersResolver _resolver;
    private readonly string? _sid;
    private bool _isExpanded;
    private bool _isLoading;
    private bool _loaded;
    private string? _status;

    public GroupMemberNodeViewModel(
        IGroupMembersResolver resolver,
        string displayName,
        IdentityKind kind,
        string? sid)
    {
        _resolver = resolver;
        _sid = sid;
        DisplayName = displayName;
        Kind = kind;
        Children = [];
        if (IsGroup)
            Children.Add(CreatePlaceholder());
    }

    public string DisplayName { get; }
    public IdentityKind Kind { get; }
    public bool IsGroup => Kind == IdentityKind.Group;
    public bool IsPlaceholder { get; private init; }

    public string KindDisplay => IsPlaceholder
        ? string.Empty
        : Kind switch
        {
            IdentityKind.User => "Пользователь",
            IdentityKind.Group => "Группа",
            _ => "Неизвестно"
        };

    public ObservableCollection<GroupMemberNodeViewModel> Children { get; }

    public string? Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetProperty(ref _isExpanded, value))
                return;
            if (value && IsGroup)
                _ = EnsureMembersAsync();
        }
    }

    public async Task EnsureMembersAsync()
    {
        if (_loaded || IsLoading || !IsGroup || IsPlaceholder)
            return;

        IsLoading = true;
        Status = "Загрузка участников…";
        try
        {
            var members = await _resolver.GetDirectMembersAsync(_sid, DisplayName);
            Children.Clear();
            if (members.Count == 0)
            {
                Status = "Участников нет (или группа недоступна для чтения).";
                Children.Add(new GroupMemberNodeViewModel(_resolver, Status, IdentityKind.Unknown, null)
                {
                    IsPlaceholder = false
                });
            }
            else
            {
                Status = $"Участников: {members.Count}";
                foreach (var m in members)
                    Children.Add(new GroupMemberNodeViewModel(_resolver, m.DisplayName, m.Kind, m.Sid));
            }

            _loaded = true;
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            Children.Clear();
            Children.Add(new GroupMemberNodeViewModel(_resolver, ex.Message, IdentityKind.Unknown, null));
        }
        finally
        {
            IsLoading = false;
        }
    }

    private GroupMemberNodeViewModel CreatePlaceholder() =>
        new(_resolver, "…", IdentityKind.Unknown, null) { IsPlaceholder = true };
}

public sealed class AceRowViewModel : ObservableObject
{
    public AceRowViewModel(AceEntry entry)
    {
        Entry = entry;
    }

    public AceEntry Entry { get; }

    public string Subject => Entry.IdentityDisplayName;
    public string IdentityKind => Entry.IdentityKind switch
    {
        Core.Models.IdentityKind.User => "Пользователь",
        Core.Models.IdentityKind.Group => "Группа",
        _ => "Неизвестно"
    };
    public bool IsGroup => Entry.IdentityKind == Core.Models.IdentityKind.Group;
    public string AceType => Entry.AceType == Core.Models.AceType.Allow ? "Разрешить" : "Запретить";
    public string Inheritance => Entry.IsInherited ? "Унаследовано" : "Явное";
    public string Level => Entry.LevelDisplayName;
    public string? Note => Entry.Note;
}
