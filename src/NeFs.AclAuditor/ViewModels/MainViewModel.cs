using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using NeFs.AclAuditor.Core;
using NeFs.AclAuditor.Core.Models;
using NeFs.AclAuditor.Core.Storage;

namespace NeFs.AclAuditor.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IFolderScanner _scanner;
    private readonly ExportService _exportService;
    private readonly IGroupMembersResolver _groupMembersResolver;
    private readonly AppSettings _settings;
    private IUserAccessStore _userAccessStore;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _membersCts;

    private string _rootPath = string.Empty;
    private int _depth = 2;
    private string _statusText = "Укажите путь и нажмите «Сканировать».";
    private bool _isScanning;
    private int _folderCount;
    private int _errorCount;
    private FolderNodeViewModel? _selectedNode;
    private FolderNode? _scanRoot;
    private SubjectFilter _subjectFilter = SubjectFilter.All;
    private AceTypeFilter _aceTypeFilter = AceTypeFilter.All;
    private InheritanceFilter _inheritanceFilter = InheritanceFilter.All;
    private bool _onlyFoldersWithExplicit;
    private string _searchText = string.Empty;
    private string _selectedPathDisplay = string.Empty;
    private string _aclSummaryText = string.Empty;
    private AceRowViewModel? _selectedAce;
    private SubjectFolderHitViewModel? _selectedSubjectHit;
    private string _membersHeader = "Участники группы";
    private string _membersStatus = "Выберите группу в таблице ACL, чтобы увидеть участников.";
    private bool _hasGroupMembersPanel;
    private string? _focusedSubjectSid;
    private string? _focusedSubjectName;
    private string _subjectFoldersHeader = "Каталоги субъекта";
    private string _subjectFoldersStatus = "Выберите пользователя или группу в таблице — покажем все каталоги скана с этим ACE.";
    private bool _suppressAceReselect;
    private string _userSearchText = string.Empty;
    private bool _showInactiveGrants;
    private StoredUserViewModel? _selectedStoredUser;
    private string _usersStatus = "База наполняется после сканирования (только прямые ACE пользователей).";
    private string _dbFolderPath = string.Empty;

    public MainViewModel()
        : this(
            new FolderScanner(new AclReader(new IdentityResolver())),
            new ExportService(),
            new GroupMembersResolver(),
            AppSettings.Load())
    {
    }

    public MainViewModel(
        IFolderScanner scanner,
        ExportService exportService,
        IGroupMembersResolver groupMembersResolver,
        AppSettings settings)
    {
        _scanner = scanner;
        _exportService = exportService;
        _groupMembersResolver = groupMembersResolver;
        _settings = settings;
        _dbFolderPath = _settings.ResolveDbFolder();
        _userAccessStore = SqliteUserAccessStore.FromFolder(_dbFolderPath);
        _userAccessStore.EnsureCreated();

        BrowseCommand = new RelayCommand(Browse, () => !IsScanning);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsScanning && !string.IsNullOrWhiteSpace(RootPath));
        CancelCommand = new RelayCommand(Cancel, () => IsScanning);
        ExportCommand = new RelayCommand(Export, () => !IsScanning && _scanRoot is not null);
        RefreshUsersCommand = new RelayCommand(RefreshUsers);
        BrowseDbFolderCommand = new RelayCommand(BrowseDbFolder, () => !IsScanning);
        UseSharedDbFolderCommand = new RelayCommand(UseSharedDbFolder, () => !IsScanning);
        ResetDbFolderCommand = new RelayCommand(ResetDbFolder, () => !IsScanning);
        TreeNodes = [];
        FilteredAces = [];
        GroupMemberNodes = [];
        SubjectFolderHits = [];
        StoredUsers = [];
        StoredGrants = [];
        NavigateToSubjectFolderCommand = new RelayCommand(p => NavigateToSubjectFolder(p as SubjectFolderHitViewModel), p => p is SubjectFolderHitViewModel);
        ClearSubjectFocusCommand = new RelayCommand(ClearSubjectFocus, () => _focusedSubjectName is not null);
        SubjectFilterOptions = Enum.GetValues<SubjectFilter>();
        AceTypeFilterOptions = Enum.GetValues<AceTypeFilter>();
        InheritanceFilterOptions = Enum.GetValues<InheritanceFilter>();
        RefreshUsers();
    }

    public ObservableCollection<FolderNodeViewModel> TreeNodes { get; }
    public ObservableCollection<AceRowViewModel> FilteredAces { get; }
    public ObservableCollection<GroupMemberNodeViewModel> GroupMemberNodes { get; }
    public ObservableCollection<SubjectFolderHitViewModel> SubjectFolderHits { get; }
    public ObservableCollection<StoredUserViewModel> StoredUsers { get; }
    public ObservableCollection<StoredGrantViewModel> StoredGrants { get; }

    public ICommand NavigateToSubjectFolderCommand { get; }
    public ICommand ClearSubjectFocusCommand { get; }
    public ICommand RefreshUsersCommand { get; }
    public ICommand BrowseDbFolderCommand { get; }
    public ICommand UseSharedDbFolderCommand { get; }
    public ICommand ResetDbFolderCommand { get; }

    public SubjectFilter[] SubjectFilterOptions { get; }
    public AceTypeFilter[] AceTypeFilterOptions { get; }
    public InheritanceFilter[] InheritanceFilterOptions { get; }

    public ICommand BrowseCommand { get; }
    public ICommand ScanCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ExportCommand { get; }

    public string RootPath
    {
        get => _rootPath;
        set
        {
            if (SetProperty(ref _rootPath, value))
                (ScanCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public int Depth
    {
        get => _depth;
        set => SetProperty(ref _depth, Math.Clamp(value, 0, 10));
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (SetProperty(ref _isScanning, value))
            {
                (BrowseCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ScanCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ExportCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (BrowseDbFolderCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (UseSharedDbFolderCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ResetDbFolderCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public int FolderCount
    {
        get => _folderCount;
        set => SetProperty(ref _folderCount, value);
    }

    public int ErrorCount
    {
        get => _errorCount;
        set => SetProperty(ref _errorCount, value);
    }

    public string SelectedPathDisplay
    {
        get => _selectedPathDisplay;
        set => SetProperty(ref _selectedPathDisplay, value);
    }

    public FolderNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (SetProperty(ref _selectedNode, value))
            {
                SelectedPathDisplay = value?.FullPath ?? string.Empty;
                UpdateAclSummary();
                RefreshFilteredAces();
            }
        }
    }

    public SubjectFilter SubjectFilter
    {
        get => _subjectFilter;
        set
        {
            if (SetProperty(ref _subjectFilter, value))
                RefreshFilteredAces();
        }
    }

    public AceTypeFilter AceTypeFilter
    {
        get => _aceTypeFilter;
        set
        {
            if (SetProperty(ref _aceTypeFilter, value))
                RefreshFilteredAces();
        }
    }

    public InheritanceFilter InheritanceFilter
    {
        get => _inheritanceFilter;
        set
        {
            if (SetProperty(ref _inheritanceFilter, value))
                RefreshFilteredAces();
        }
    }

    public bool OnlyFoldersWithExplicit
    {
        get => _onlyFoldersWithExplicit;
        set
        {
            if (SetProperty(ref _onlyFoldersWithExplicit, value))
                RebuildTree();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                RefreshFilteredAces();
        }
    }

    public string AclSummaryText
    {
        get => _aclSummaryText;
        set => SetProperty(ref _aclSummaryText, value);
    }

    public AceRowViewModel? SelectedAce
    {
        get => _selectedAce;
        set
        {
            if (!SetProperty(ref _selectedAce, value))
                return;

            if (!_suppressAceReselect && value is not null)
                FocusSubject(value.Entry.Sid, value.Entry.IdentityDisplayName);

            _ = LoadGroupMembersAsync(value);
        }
    }

    public SubjectFolderHitViewModel? SelectedSubjectHit
    {
        get => _selectedSubjectHit;
        set
        {
            if (SetProperty(ref _selectedSubjectHit, value) && value is not null)
                NavigateToSubjectFolder(value);
        }
    }

    public string MembersHeader
    {
        get => _membersHeader;
        set => SetProperty(ref _membersHeader, value);
    }

    public string MembersStatus
    {
        get => _membersStatus;
        set => SetProperty(ref _membersStatus, value);
    }

    public bool HasGroupMembersPanel
    {
        get => _hasGroupMembersPanel;
        set => SetProperty(ref _hasGroupMembersPanel, value);
    }

    public string SubjectFoldersHeader
    {
        get => _subjectFoldersHeader;
        set => SetProperty(ref _subjectFoldersHeader, value);
    }

    public string SubjectFoldersStatus
    {
        get => _subjectFoldersStatus;
        set => SetProperty(ref _subjectFoldersStatus, value);
    }

    public bool HasSubjectFocus => _focusedSubjectName is not null;

    public string UserSearchText
    {
        get => _userSearchText;
        set
        {
            if (SetProperty(ref _userSearchText, value))
                RefreshUsers();
        }
    }

    public bool ShowInactiveGrants
    {
        get => _showInactiveGrants;
        set
        {
            if (SetProperty(ref _showInactiveGrants, value))
            {
                RefreshUsers();
                ReloadSelectedUserGrants();
            }
        }
    }

    public StoredUserViewModel? SelectedStoredUser
    {
        get => _selectedStoredUser;
        set
        {
            if (SetProperty(ref _selectedStoredUser, value))
                ReloadSelectedUserGrants();
        }
    }

    public string UsersStatus
    {
        get => _usersStatus;
        set => SetProperty(ref _usersStatus, value);
    }

    public string DbFolderPath
    {
        get => _dbFolderPath;
        private set
        {
            if (SetProperty(ref _dbFolderPath, value))
                OnPropertyChanged(nameof(DbPathDisplay));
        }
    }

    public string DbPathDisplay => _userAccessStore.DatabasePath;

    public string RecommendedSharedDbFolder => AppSettings.GetRecommendedSharedDbFolder();

    private void Browse()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите корневую папку для аудита ACL"
        };

        if (!string.IsNullOrWhiteSpace(RootPath) && Directory.Exists(RootPath))
            dialog.InitialDirectory = RootPath;

        if (dialog.ShowDialog() == true)
            RootPath = dialog.FolderName;
    }

    private async Task ScanAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsScanning = true;
        FolderCount = 0;
        ErrorCount = 0;
        TreeNodes.Clear();
        FilteredAces.Clear();
        ClearGroupMembers("Выберите группу в таблице ACL, чтобы увидеть участников.");
        ClearSubjectFocus();
        SelectedNode = null;
        _scanRoot = null;
        StatusText = "Сканирование…";

        var progress = new Progress<ScanProgress>(p =>
        {
            FolderCount = p.FoldersProcessed;
            ErrorCount = p.ErrorCount;
            StatusText = $"Сканирование… {p.FoldersProcessed} папок. {p.CurrentPath}";
        });

        try
        {
            var result = await _scanner.ScanAsync(RootPath, Depth, progress, _cts.Token);
            _scanRoot = result.Root;
            FolderCount = result.FolderCount;
            ErrorCount = result.ErrorCount;
            RebuildTree();
            if (TreeNodes.Count > 0)
            {
                TreeNodes[0].IsExpanded = true;
                SelectedNode = TreeNodes[0];
                TreeNodes[0].IsSelected = true;
            }

            var persist = await Task.Run(() =>
                _userAccessStore.PersistExplicitUserGrants(RootPath, result.Root, DateTimeOffset.Now));
            RefreshUsers();

            StatusText = ErrorCount == 0
                ? $"Готово. Папок: {FolderCount}. В базу: пользователей {persist.UsersTouched}, прямых ACE {persist.GrantsUpserted}" +
                  (persist.GrantsMarkedInactive > 0 ? $", снято пометок {persist.GrantsMarkedInactive}." : ".")
                : $"Готово. Папок: {FolderCount}, ошибок: {ErrorCount}. База обновлена: {persist.GrantsUpserted} ACE.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Сканирование отменено.";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
            MessageBox.Show(ex.Message, "Folder Access Review", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void Cancel() => _cts?.Cancel();

    private void RebuildTree()
    {
        TreeNodes.Clear();
        if (_scanRoot is null)
            return;

        var rootVm = new FolderNodeViewModel(_scanRoot);
        if (OnlyFoldersWithExplicit)
            FilterTree(rootVm);

        TreeNodes.Add(rootVm);
        if (_focusedSubjectName is not null)
            RebuildSubjectFolderHits();
    }

    private static void FilterTree(FolderNodeViewModel node)
    {
        for (var i = node.Children.Count - 1; i >= 0; i--)
        {
            var child = node.Children[i];
            if (!child.MatchesOnlyExplicitFilter(true))
            {
                node.Children.RemoveAt(i);
                continue;
            }

            FilterTree(child);
        }
    }

    private void RefreshFilteredAces()
    {
        FilteredAces.Clear();
        if (SelectedNode is null)
        {
            _suppressAceReselect = true;
            SelectedAce = null;
            _suppressAceReselect = false;
            return;
        }

        var filtered = AceFilters.Apply(
            SelectedNode.Model.Aces,
            SubjectFilter,
            AceTypeFilter,
            InheritanceFilter,
            SearchText);

        foreach (var ace in filtered)
            FilteredAces.Add(new AceRowViewModel(ace));

        // Keep focus on the same subject when switching folders.
        _suppressAceReselect = true;
        try
        {
            if (_focusedSubjectName is not null)
            {
                SelectedAce = FilteredAces.FirstOrDefault(a =>
                    SubjectFolderFinder.Matches(a.Entry, _focusedSubjectSid, _focusedSubjectName));
            }
            else
            {
                SelectedAce = null;
            }
        }
        finally
        {
            _suppressAceReselect = false;
        }
    }

    private void FocusSubject(string? sid, string displayName)
    {
        _focusedSubjectSid = sid;
        _focusedSubjectName = displayName;
        (ClearSubjectFocusCommand as RelayCommand)?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(HasSubjectFocus));
        RebuildSubjectFolderHits();
    }

    private void ClearSubjectFocus()
    {
        _focusedSubjectSid = null;
        _focusedSubjectName = null;
        SubjectFolderHits.Clear();
        ClearSubjectHighlights();
        SubjectFoldersHeader = "Каталоги субъекта";
        SubjectFoldersStatus = "Выберите пользователя или группу в таблице — покажем все каталоги скана с этим ACE.";
        _suppressAceReselect = true;
        SelectedAce = null;
        _suppressAceReselect = false;
        (ClearSubjectFocusCommand as RelayCommand)?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(HasSubjectFocus));
    }

    private void RebuildSubjectFolderHits()
    {
        SubjectFolderHits.Clear();
        ClearSubjectHighlights();

        if (_scanRoot is null || string.IsNullOrWhiteSpace(_focusedSubjectName))
            return;

        var hits = SubjectFolderFinder.Find(_scanRoot, _focusedSubjectSid, _focusedSubjectName);
        foreach (var hit in hits)
            SubjectFolderHits.Add(new SubjectFolderHitViewModel(hit.Folder, hit.Ace));

        var paths = new HashSet<string>(hits.Select(h => h.Folder.FullPath), StringComparer.OrdinalIgnoreCase);
        MarkSubjectHits(TreeNodes, paths);

        SubjectFoldersHeader = $"Каталоги с {_focusedSubjectName}";
        SubjectFoldersStatus = SubjectFolderHits.Count == 0
            ? "В текущем скане этот субъект не найден в ACL (прямое назначение)."
            : $"Найдено каталогов: {SubjectFolderHits.Count}. Клик по строке — перейти к папке. ◆ в дереве = совпадение.";
    }

    private static void MarkSubjectHits(IEnumerable<FolderNodeViewModel> nodes, HashSet<string> paths)
    {
        foreach (var node in nodes)
        {
            node.IsSubjectHit = paths.Contains(node.FullPath);
            MarkSubjectHits(node.Children, paths);
        }
    }

    private void ClearSubjectHighlights()
    {
        MarkSubjectHits(TreeNodes, []);
    }

    private void NavigateToSubjectFolder(SubjectFolderHitViewModel? hit)
    {
        if (hit is null || TreeNodes.Count == 0)
            return;

        var vm = FindNodeByPath(TreeNodes, hit.FullPath);
        if (vm is null)
            return;

        // Expand ancestors
        for (var p = vm.Parent; p is not null; p = p.Parent)
            p.IsExpanded = true;

        // Deselect previous
        ClearTreeSelection(TreeNodes);
        vm.IsSelected = true;
        SelectedNode = vm;
    }

    private static FolderNodeViewModel? FindNodeByPath(IEnumerable<FolderNodeViewModel> nodes, string fullPath)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
                return node;
            var child = FindNodeByPath(node.Children, fullPath);
            if (child is not null)
                return child;
        }

        return null;
    }

    private static void ClearTreeSelection(IEnumerable<FolderNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            node.IsSelected = false;
            ClearTreeSelection(node.Children);
        }
    }

    private async Task LoadGroupMembersAsync(AceRowViewModel? ace)
    {
        _membersCts?.Cancel();
        GroupMemberNodes.Clear();

        if (ace is null)
        {
            ClearGroupMembers("Выберите группу в таблице ACL, чтобы увидеть участников.");
            return;
        }

        if (!ace.IsGroup)
        {
            ClearGroupMembers($"«{ace.Subject}» — пользователь. Участники есть только у групп; слева — каталоги, где он встречается в ACL.");
            return;
        }

        HasGroupMembersPanel = true;
        MembersHeader = $"Участники: {ace.Subject}";
        MembersStatus = "Загрузка из Active Directory…";

        _membersCts = new CancellationTokenSource();
        var token = _membersCts.Token;
        try
        {
            var members = await _groupMembersResolver.GetDirectMembersAsync(
                ace.Entry.Sid,
                ace.Entry.IdentityDisplayName,
                token);

            if (token.IsCancellationRequested)
                return;

            GroupMemberNodes.Clear();
            foreach (var m in members)
            {
                var node = new GroupMemberNodeViewModel(
                    _groupMembersResolver,
                    m.DisplayName,
                    m.Kind,
                    m.Sid);
                GroupMemberNodes.Add(node);
            }

            MembersStatus = GroupMemberNodes.Count == 0
                ? "Участников не найдено."
                : $"Прямых участников: {GroupMemberNodes.Count}. Вложенную группу можно раскрыть стрелкой.";
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        catch (Exception ex)
        {
            MembersStatus = ex.Message;
        }
    }

    private void ClearGroupMembers(string status)
    {
        HasGroupMembersPanel = false;
        MembersHeader = "Участники группы";
        MembersStatus = status;
        GroupMemberNodes.Clear();
    }

    private void RefreshUsers()
    {
        var previousSid = SelectedStoredUser?.Sid;
        StoredUsers.Clear();
        foreach (var u in _userAccessStore.GetUsers(UserSearchText, onlyWithActiveGrants: !ShowInactiveGrants))
            StoredUsers.Add(new StoredUserViewModel(u));

        UsersStatus = StoredUsers.Count == 0
            ? "Пока нет прямых ACE пользователей. Выполните сканирование."
            : $"Пользователей в базе: {StoredUsers.Count}. Только прямые (не через группы).";

        if (previousSid is not null)
            SelectedStoredUser = StoredUsers.FirstOrDefault(u => u.Sid == previousSid);
    }

    private void ReloadSelectedUserGrants()
    {
        StoredGrants.Clear();
        if (SelectedStoredUser is null)
            return;

        foreach (var g in _userAccessStore.GetGrantsForUser(SelectedStoredUser.Sid, ShowInactiveGrants))
            StoredGrants.Add(new StoredGrantViewModel(g));
    }

    private void BrowseDbFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку для базы пользователей (user-access.db)"
        };
        if (Directory.Exists(DbFolderPath))
            dialog.InitialDirectory = DbFolderPath;

        if (dialog.ShowDialog() == true)
            ApplyDbFolder(dialog.FolderName, saveExplicit: true);
    }

    private void UseSharedDbFolder()
    {
        var folder = AppSettings.GetRecommendedSharedDbFolder();
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось создать папку:\n{folder}\n\n{ex.Message}\n\nЗапустите один раз от администратора или выберите другую папку.",
                "Folder Access Review",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ApplyDbFolder(folder, saveExplicit: true);
        MessageBox.Show(
            $"База переключена на общую папку:\n{folder}\n\nУкажите тот же путь в программе под другой УЗ (кнопка «Обзор…» или снова «ProgramData»).",
            "Folder Access Review",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ResetDbFolder()
    {
        _settings.DbFolder = null;
        _settings.Save();
        ApplyDbFolder(AppSettings.GetDefaultDbFolder(), saveExplicit: false);
    }

    private void ApplyDbFolder(string folder, bool saveExplicit)
    {
        try
        {
            Directory.CreateDirectory(folder);
            if (saveExplicit)
            {
                _settings.DbFolder = folder;
                _settings.Save();
            }

            _userAccessStore = SqliteUserAccessStore.FromFolder(folder);
            _userAccessStore.EnsureCreated();
            DbFolderPath = folder;
            SelectedStoredUser = null;
            StoredGrants.Clear();
            RefreshUsers();
            StatusText = $"База пользователей: {DbPathDisplay}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Folder Access Review", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateAclSummary()
    {
        if (SelectedNode is null)
        {
            AclSummaryText = string.Empty;
            return;
        }

        var m = SelectedNode.Model;
        if (!string.IsNullOrEmpty(m.Error))
        {
            AclSummaryText = $"Ошибка чтения ACL: {m.Error}";
            return;
        }

        AclSummaryText =
            $"Явных: {m.ExplicitAceCount}, унаследованных: {m.InheritedAceCount}. " +
            "Клик по субъекту — каталоги по всему скану (◆). Клик по группе — ещё и участники. ● = явные ACE.";
    }

    private void Export()
    {
        if (_scanRoot is null)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Экспорт ACL",
            Filter = "Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv",
            FileName = $"acl-export-{DateTime.Now:yyyyMMdd-HHmmss}"
        };

        if (dialog.ShowDialog() != true)
            return;

        var scope = MessageBox.Show(
            "Экспортировать весь скан?\n\nДа — весь скан\nНет — только выбранную папку",
            "Область экспорта",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (scope == MessageBoxResult.Cancel)
            return;

        var exportScope = scope == MessageBoxResult.Yes
            ? ExportScope.EntireScan
            : ExportScope.SelectedFolderOnly;

        try
        {
            if (dialog.FilterIndex == 1 || dialog.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                _exportService.ExportXlsx(dialog.FileName, _scanRoot, exportScope, SelectedNode?.Model);
            else
                _exportService.ExportCsv(dialog.FileName, _scanRoot, exportScope, SelectedNode?.Model);

            StatusText = $"Экспорт сохранён: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Экспорт", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
