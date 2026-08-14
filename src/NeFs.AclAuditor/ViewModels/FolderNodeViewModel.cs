using System.Collections.ObjectModel;
using NeFs.AclAuditor.Core.Models;

namespace NeFs.AclAuditor.ViewModels;

public sealed class FolderNodeViewModel : ObservableObject
{
    private bool _isExpanded;
    private bool _isSelected;
    private bool _isSubjectHit;

    public FolderNodeViewModel(FolderNode model, FolderNodeViewModel? parent = null)
    {
        Model = model;
        Parent = parent;
        Children = new ObservableCollection<FolderNodeViewModel>(
            model.Children.Select(c => new FolderNodeViewModel(c, this)));
    }

    public FolderNode Model { get; }
    public FolderNodeViewModel? Parent { get; }
    public ObservableCollection<FolderNodeViewModel> Children { get; }

    public string Name => Model.Name;
    public string FullPath => Model.FullPath;
    public bool HasExplicitAces => Model.HasExplicitAces;
    public bool HasError => !string.IsNullOrEmpty(Model.Error);
    public string? Error => Model.Error;
    public string DisplayLabel
    {
        get
        {
            var mark = HasExplicitAces ? " ●" : string.Empty;
            var hit = IsSubjectHit ? " ◆" : string.Empty;
            return Name + mark + hit;
        }
    }

    public bool IsSubjectHit
    {
        get => _isSubjectHit;
        set
        {
            if (SetProperty(ref _isSubjectHit, value))
                OnPropertyChanged(nameof(DisplayLabel));
        }
    }
    public string ToolTip
    {
        get
        {
            if (HasError)
                return $"{FullPath}\nОшибка: {Error}";

            return $"{FullPath}\nЯвных: {Model.ExplicitAceCount}, унаследованных: {Model.InheritedAceCount}";
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool MatchesOnlyExplicitFilter(bool onlyWithExplicit)
    {
        if (!onlyWithExplicit)
            return true;
        if (HasExplicitAces)
            return true;
        return Children.Any(c => c.MatchesOnlyExplicitFilter(true));
    }
}
