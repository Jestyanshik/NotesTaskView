using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NotesTaskView.Models;
using NotesTaskView.Services;

namespace NotesTaskView.Views;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const string NotePathDragFormat = "NotesTaskView.NotePath";
    private const string FolderPathDragFormat = "NotesTaskView.FolderPath";
    private const string DragDebugLogPath = @"E:\notes\Notes-overlay\drag-debug.log";
    private const double CardStepWidth = 322;
    private readonly NoteService _noteService;
    private readonly UserSettingsService _userSettingsService;
    private readonly Func<UserSettings, List<string>> _applySettings;
    private readonly Func<UserSettings, List<string>> _validateHotkeys;
    private readonly DispatcherTimer _refreshDebounceTimer;
    private readonly DispatcherTimer _dragBackTimer;
    private readonly DispatcherTimer _dragAutoScrollTimer;
    private readonly DispatcherTimer _enterHoldTimer;
    private readonly DispatcherTimer _globalSearchDebounceTimer;
    private readonly ObservableCollection<TrashItem> _trashItems = [];
    private readonly ObservableCollection<string> _dragDebugEvents = [];
    private readonly ObservableCollection<FolderChoiceItem> _folderChoices = [];
    private readonly ObservableCollection<FolderChoiceItem> _visibleFolderChoices = [];
    private readonly ObservableCollection<PreviewTile> _previewTiles = [];
    private readonly HashSet<string> _selectedPaths = new(StringComparer.OrdinalIgnoreCase);
    private ScrollViewer? _itemsScrollViewer;
    private UserSettings _settings;
    private UserSettings? _settingsSnapshot;
    private bool _isLoadingSettingsUi;
    private bool _isUpdatingColorPicker;
    private bool _colorPickerTargetsAccent;
    private bool _isEditorDirty;
    private bool _isLoadingEditorText;
    private bool _isRenamingTitle;
    private string? _currentFilePath;
    private string _currentFolderPath;
    private string _currentPathDisplay = string.Empty;
    private string _editorText = string.Empty;
    private string _editorTitle = "Редактор";
    private string _editorStateText = "Сохранено";
    private string _overlayTitle;
    private Brush _editorStateBrush;
    private Brush _overlayBackdropBrush;
    private Brush _selectionOutlineBrush;
    private object? _selectedItem;
    private TaskCompletionSource<string?>? _modalCompletion;
    private TaskCompletionSource<string?>? _promptCompletion;
    private TaskCompletionSource<FolderItem?>? _folderPickerCompletion;
    private string? _draggedPath;
    private string? _draggedKind;
    private readonly List<string> _draggedPaths = [];
    private bool _isDragging;
    private bool _isKeyboardDragging;
    private bool _isEnterHoldPending;
    private bool _isCompletingDrag;
    private bool _isChangingMouseCaptureInternally;
    private bool _isCleaningDrag;
    private bool _isDraggingColorWheel;
    private double _dragAutoScrollVelocity;
    private int _currentPlaceholderIndex = -1;
    private string? _lastDragOverPath;
    private int _lastDragOverIndex = -1;
    private DateTime _lastDragOverUpdateUtc = DateTime.MinValue;
    private const int ColorWheelSize = 220;
    private readonly DragPlaceholderItem _dragPlaceholder = new();
    private string _lastSavedContent = string.Empty;
    private string _statusMessage = string.Empty;
    private Visibility _statusVisibility = Visibility.Collapsed;
    private Visibility _listVisibility = Visibility.Visible;
    private Visibility _editorVisibility = Visibility.Collapsed;
    private Visibility _upFolderVisibility = Visibility.Collapsed;
    private int _noteCount;
    private int _folderCount;
    private bool _isDragDebugEnabled;
    private string _dragDebugStatus = "IsDragging: false";
    private string? _selectionAnchorPath;
    private string? _pendingDragPath;
    private Point _pendingDragStartPoint;
    private DateTime _pendingDragStartUtc;
    private ModifierKeys _pendingDragModifiers;
    private bool _suppressOpenAfterDrag;
    private Visibility _previewPopupVisibility = Visibility.Collapsed;
    private string _previewTitle = string.Empty;
    private string _previewMeta = string.Empty;
    private string _previewBody = string.Empty;
    private bool _isFolderPreview;
    private bool _isGlobalSearchOpen;
    private string _globalSearchQuery = string.Empty;
    private CancellationTokenSource? _globalSearchCts;
    private Visibility _fileSearchVisibility = Visibility.Collapsed;
    private string _fileSearchQuery = string.Empty;
    private string _fileSearchStatus = string.Empty;
    private readonly List<(int Index, int Length)> _fileSearchMatches = [];
    private int _fileSearchMatchIndex = -1;
    private int _folderChoiceStartIndex;
    private FolderChoiceItem? _selectedFolderChoice;
    private TextBlock? _onboardingIntroText;
    private ComboBox? _settingsLanguageComboBox;
    private TextBlock? _settingsToggleHotkeyErrorText;
    private TextBlock? _settingsNewNoteHotkeyErrorText;
    private Button? _settingsCheckHotkeysButton;
    private Button? _settingsCloseAppButton;
    private bool _isOnboardingMode;

    public MainWindow(
        NoteService noteService,
        UserSettingsService userSettingsService,
        UserSettings settings,
        Func<UserSettings, List<string>> applySettings,
        Func<UserSettings, List<string>> validateHotkeys)
    {
        _noteService = noteService;
        _userSettingsService = userSettingsService;
        _settings = settings;
        _applySettings = applySettings;
        _validateHotkeys = validateHotkeys;
        _currentFolderPath = _noteService.ResolveFolderOrRoot(settings.NotesDirectory);
        _overlayTitle = settings.OverlayTitle;
        _noteService.NotesChanged += NoteService_OnNotesChanged;

        _refreshDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _refreshDebounceTimer.Tick += RefreshDebounceTimer_OnTick;
        _dragBackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _dragBackTimer.Tick += DragBackTimer_OnTick;
        _dragAutoScrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(24)
        };
        _dragAutoScrollTimer.Tick += DragAutoScrollTimer_OnTick;
        _enterHoldTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(700)
        };
        _enterHoldTimer.Tick += EnterHoldTimer_OnTick;
        _globalSearchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(220)
        };
        _globalSearchDebounceTimer.Tick += GlobalSearchDebounceTimer_OnTick;

        Items = new ObservableCollection<object>();
        _editorStateBrush = GetBrush("SecondaryTextBrush");
        _overlayBackdropBrush = CreateDimBrush(_settings.OverlayDimOpacity);
        UpdateAccentResources(_settings.AccentColor);
        _selectionOutlineBrush = CreateSelectionOutlineBrush(_settings);

        InitializeComponent();
        DataContext = this;
        BuildSettingsOnboardingControls();
        SettingsTrashItemsControl.ItemsSource = _trashItems;
        BuildColorPresets();
        BuildAccentPresets();
        BuildColorWheelBitmap();
        UpdatePathState();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<object> Items { get; }

    public ObservableCollection<string> DragDebugEvents => _dragDebugEvents;

    public ObservableCollection<FolderChoiceItem> VisibleFolderChoices => _visibleFolderChoices;

    public ObservableCollection<PreviewTile> PreviewTiles => _previewTiles;

    public Visibility DragDebugVisibility => _isDragDebugEnabled ? Visibility.Visible : Visibility.Collapsed;

    public string DragDebugStatus
    {
        get => _dragDebugStatus;
        private set
        {
            if (_dragDebugStatus == value)
            {
                return;
            }

            _dragDebugStatus = value;
            OnPropertyChanged();
        }
    }

    public object? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (Equals(_selectedItem, value))
            {
                return;
            }

            SetFocusState(_selectedItem, false);
            _selectedItem = value;
            SetFocusState(_selectedItem, true);
            OnPropertyChanged();
        }
    }

    public string OverlayTitle
    {
        get => _overlayTitle;
        private set
        {
            if (_overlayTitle == value)
            {
                return;
            }

            _overlayTitle = value;
            OnPropertyChanged();
        }
    }

    public string CurrentPathDisplay
    {
        get => _currentPathDisplay;
        private set
        {
            if (_currentPathDisplay == value)
            {
                return;
            }

            _currentPathDisplay = value;
            OnPropertyChanged();
        }
    }

    public Visibility UpFolderVisibility
    {
        get => _upFolderVisibility;
        private set
        {
            if (_upFolderVisibility == value)
            {
                return;
            }

            _upFolderVisibility = value;
            OnPropertyChanged();
        }
    }

    public string TotalItemsText => $"Элементов: {_noteCount + _folderCount}";

    public string FolderCountText => $"Папок: {_folderCount}";

    public string NoteCountText => $"Заметок: {_noteCount}";

    public string SummaryLineText =>
        $"{TotalItemsText} · {FolderCountText} · {NoteCountText}" +
        (_selectedPaths.Count > 1 ? $" · Выбрано: {_selectedPaths.Count}" : string.Empty) +
        " · F5 — обновить · Enter — открыть · Delete — удалить · Right Shift — меню";

    public Visibility PreviewPopupVisibility
    {
        get => _previewPopupVisibility;
        private set
        {
            if (_previewPopupVisibility == value)
            {
                return;
            }

            _previewPopupVisibility = value;
            OnPropertyChanged();
        }
    }

    public string PreviewTitle
    {
        get => _previewTitle;
        private set
        {
            if (_previewTitle == value)
            {
                return;
            }

            _previewTitle = value;
            OnPropertyChanged();
        }
    }

    public string PreviewMeta
    {
        get => _previewMeta;
        private set
        {
            if (_previewMeta == value)
            {
                return;
            }

            _previewMeta = value;
            OnPropertyChanged();
        }
    }

    public string PreviewBody
    {
        get => _previewBody;
        private set
        {
            if (_previewBody == value)
            {
                return;
            }

            _previewBody = value;
            OnPropertyChanged();
        }
    }

    public Visibility FolderPreviewVisibility => _isFolderPreview ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NotePreviewVisibility => _isFolderPreview ? Visibility.Collapsed : Visibility.Visible;

    public Visibility GlobalSearchVisibility => _isGlobalSearchOpen ? Visibility.Visible : Visibility.Collapsed;

    public string GlobalSearchQuery
    {
        get => _globalSearchQuery;
        set
        {
            if (_globalSearchQuery == value)
            {
                return;
            }

            _globalSearchQuery = value;
            OnPropertyChanged();
            RestartGlobalSearchDebounce();
        }
    }

    public Visibility FileSearchVisibility
    {
        get => _fileSearchVisibility;
        private set
        {
            if (_fileSearchVisibility == value)
            {
                return;
            }

            _fileSearchVisibility = value;
            OnPropertyChanged();
        }
    }

    public string FileSearchQuery
    {
        get => _fileSearchQuery;
        set
        {
            if (_fileSearchQuery == value)
            {
                return;
            }

            _fileSearchQuery = value;
            OnPropertyChanged();
            UpdateFileSearchMatches();
        }
    }

    public string FileSearchStatus
    {
        get => _fileSearchStatus;
        private set
        {
            if (_fileSearchStatus == value)
            {
                return;
            }

            _fileSearchStatus = value;
            OnPropertyChanged();
        }
    }

    public Brush OverlayBackdropBrush
    {
        get => _overlayBackdropBrush;
        private set
        {
            if (Equals(_overlayBackdropBrush, value))
            {
                return;
            }

            _overlayBackdropBrush = value;
            OnPropertyChanged();
        }
    }

    public Brush SelectionOutlineBrush
    {
        get => _selectionOutlineBrush;
        private set
        {
            if (Equals(_selectionOutlineBrush, value))
            {
                return;
            }

            _selectionOutlineBrush = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value)
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public Visibility StatusVisibility
    {
        get => _statusVisibility;
        private set
        {
            if (_statusVisibility == value)
            {
                return;
            }

            _statusVisibility = value;
            OnPropertyChanged();
        }
    }

    public Visibility ListVisibility
    {
        get => _listVisibility;
        private set
        {
            if (_listVisibility == value)
            {
                return;
            }

            _listVisibility = value;
            OnPropertyChanged();
        }
    }

    public Visibility EditorVisibility
    {
        get => _editorVisibility;
        private set
        {
            if (_editorVisibility == value)
            {
                return;
            }

            _editorVisibility = value;
            OnPropertyChanged();
        }
    }

    public string EditorTitle
    {
        get => _editorTitle;
        private set
        {
            if (_editorTitle == value)
            {
                return;
            }

            _editorTitle = value;
            OnPropertyChanged();
        }
    }

    public string EditorStateText
    {
        get => _editorStateText;
        private set
        {
            if (_editorStateText == value)
            {
                return;
            }

            _editorStateText = value;
            OnPropertyChanged();
        }
    }

    public Brush EditorStateBrush
    {
        get => _editorStateBrush;
        private set
        {
            if (Equals(_editorStateBrush, value))
            {
                return;
            }

            _editorStateBrush = value;
            OnPropertyChanged();
        }
    }

    public string EditorText
    {
        get => _editorText;
        set
        {
            if (_editorText == value)
            {
                return;
            }

            _editorText = value;
            OnPropertyChanged();
        }
    }

    public async Task ToggleVisibilityAsync()
    {
        if (IsVisible)
        {
            await TryHideOverlayAsync();
            return;
        }

        await ShowOverlayAsync();
    }

    public async Task CreateNoteFromDialogAsync()
    {
        if (!await ShowOverlayAsync())
        {
            return;
        }

        await CreateNoteInCurrentFolderAsync();
    }

    public async Task ShowOverlayErrorAsync(string title, string message)
    {
        await ShowOverlayAsync();
        await ShowErrorAsync(title, message);
    }

    public async Task ShowOnboardingAsync()
    {
        await ShowOverlayForSettingsAsync();
        OpenSettingsPanel(onboarding: true);
    }

    public async Task ShowSettingsWithHotkeyErrorsAsync(IReadOnlyList<string> errors)
    {
        await ShowOverlayForSettingsAsync();
        OpenSettingsPanel(onboarding: false);
        ApplyHotkeyErrors(errors);
    }

    public async Task ShowFromTrayAsync()
    {
        await ShowOverlayAsync();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Maximized;
        }

        Activate();
        Focus();
    }

    public async Task ShowSettingsFromTrayAsync()
    {
        await ShowOverlayForSettingsAsync();
        OpenSettingsPanel();
    }

    public async Task OpenNotesFolderFromTrayAsync()
    {
        if (!Directory.Exists(_settings.NotesDirectory))
        {
            await ShowOverlayAsync();
            return;
        }

        await OpenFolderFromTrayAsync(_settings.NotesDirectory, "NotesFolder", "FolderMissingTitle");
    }

    public async Task OpenConfigFolderFromTrayAsync()
    {
        var settingsDirectory = _userSettingsService.GetSettingsDirectoryPath();
        if (!Directory.Exists(settingsDirectory))
        {
            await ShowOverlayErrorAsync(
                UiText.T(_settings.Language, "ConfigFolder"),
                UiText.T(_settings.Language, "ConfigFolderMissingTitle"));
            return;
        }

        await OpenFolderFromTrayAsync(settingsDirectory, "ConfigFolder", "ConfigFolderMissingTitle");
    }

    public async Task CloseApplicationFromTrayAsync()
    {
        await RequestCloseApplicationAsync();
    }

    private async Task OpenFolderFromTrayAsync(string folderPath, string titleKey, string missingKey)
    {
        try
        {
            if (!Directory.Exists(folderPath))
            {
                await ShowOverlayErrorAsync(UiText.T(_settings.Language, titleKey), UiText.T(_settings.Language, missingKey));
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folderPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await ShowOverlayErrorAsync(
                UiText.T(_settings.Language, "FailedToOpenFolder"),
                ex.Message);
        }
    }

    private async Task ShowOverlayForSettingsAsync()
    {
        WindowState = WindowState.Maximized;
        ShowInTaskbar = false;

        if (!IsVisible)
        {
            Show();
        }

        Activate();
        Focus();
        await Task.CompletedTask;
    }

    private async Task<bool> ShowOverlayAsync()
    {
        WindowState = WindowState.Maximized;
        ShowInTaskbar = false;

        if (!IsVisible)
        {
            Show();
        }

        Activate();
        Focus();

        if (!await EnsureNotesFolderReadyAsync())
        {
            return false;
        }

        await LoadItemsAsync();

        if (EditorVisibility == Visibility.Visible)
        {
            _ = Dispatcher.BeginInvoke(() => EditorTextBox.Focus(), DispatcherPriority.Input);
        }
        else
        {
            FocusItemsHost();
        }

        return true;
    }

    private async Task<bool> EnsureNotesFolderReadyAsync()
    {
        if (Directory.Exists(_settings.NotesDirectory))
        {
            return true;
        }

        while (!Directory.Exists(_settings.NotesDirectory))
        {
            var choice = await ShowChoiceAsync(
                UiText.T(_settings.Language, "FolderMissingTitle"),
                UiText.T(_settings.Language, "FolderMissingMessage"),
                ("choose", $"{UiText.T(_settings.Language, "ChooseFolder")}{Environment.NewLine}{UiText.T(_settings.Language, "EnterFolderPath")}", false),
                ("search", $"{UiText.T(_settings.Language, "FindAutomatically")}{Environment.NewLine}{UiText.T(_settings.Language, "SearchProgress")}", false),
                ("default", $"{UiText.T(_settings.Language, "CreateDefaultFolder")}{Environment.NewLine}Documents\\NotesTaskView", false),
                ("settings", UiText.T(_settings.Language, "OpenSettings"), false),
                ("close-app", UiText.T(_settings.Language, "CloseApp"), true));

            switch (choice)
            {
                case "default":
                    if (TryCreateDefaultNotesFolder(out var defaultPath, out var createError))
                    {
                        ApplyNotesDirectory(defaultPath);
                        return true;
                    }

                    await ShowErrorAsync("Папка заметок", createError);
                    break;

                case "choose":
                    if (await TryChooseExistingNotesFolderAsync())
                    {
                        return true;
                    }
                    break;

                case "search":
                    if (await TryFindExistingNotesFolderAsync())
                    {
                        return true;
                    }
                    break;

                case "settings":
                    OpenSettingsPanel(onboarding: true);
                    return false;

                case "close-app":
                    await RequestCloseApplicationAsync();
                    return false;

                default:
                    Hide();
                    return false;
            }
        }

        return true;
    }

    private bool TryCreateDefaultNotesFolder(out string path, out string error)
    {
        path = Path.Combine(GetDocumentsFolder(), "NotesTaskView");
        error = string.Empty;

        try
        {
            Directory.CreateDirectory(path);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Не удалось создать папку по умолчанию: {ex.Message}";
            return false;
        }
    }

    private async Task<bool> TryChooseExistingNotesFolderAsync()
    {
        var selectedPath = await ShowPromptAsync(
            UiText.T(_settings.Language, "ChooseFolder"),
            UiText.T(_settings.Language, "EnterFolderPath"),
            UiText.T(_settings.Language, "Save"),
            _settings.NotesDirectory);

        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return false;
        }

        var expandedPath = Environment.ExpandEnvironmentVariables(selectedPath.Trim());
        if (!Directory.Exists(expandedPath))
        {
            await ShowErrorAsync(UiText.T(_settings.Language, "NotesFolder"), UiText.T(_settings.Language, "FolderNotFound"));
            return false;
        }

        ApplyNotesDirectory(expandedPath);
        return true;
    }

    private async Task<bool> TryFindExistingNotesFolderAsync()
    {
        ShowStatus(UiText.T(_settings.Language, "SearchProgress"), true);
        var candidates = await FindNotesFolderCandidatesAsync(CancellationToken.None);
        ShowStatus(string.Empty, false);

        if (candidates.Count == 0)
        {
            await ShowErrorAsync(UiText.T(_settings.Language, "FindAutomatically"), UiText.T(_settings.Language, "SearchEmpty"));
            return false;
        }

        var choices = candidates
            .Take(8)
            .Select((candidate, index) => (
                Id: index.ToString(),
                Text: $"{candidate.CandidatePath}{Environment.NewLine}{candidate.TxtCount} txt · order: {(candidate.HasOrderFile ? "yes" : "no")} · {candidate.LastModified:g}",
                Danger: false))
            .Append(("cancel", "Назад", false))
            .ToArray();

        var result = await ShowChoiceAsync(
            UiText.T(_settings.Language, "SearchTitle"),
            UiText.T(_settings.Language, "SearchMessage"),
            choices);

        if (!int.TryParse(result, out var selectedIndex) || selectedIndex < 0 || selectedIndex >= candidates.Count)
        {
            return false;
        }

        ApplyNotesDirectory(candidates[selectedIndex].CandidatePath);
        return true;
    }

    private static Task<IReadOnlyList<NotesFolderCandidate>> FindNotesFolderCandidatesAsync(CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<NotesFolderCandidate>>(() =>
        {
            const int maxDepth = 5;
            const int maxScannedDirectories = 5000;
            var roots = GetSafeSearchRoots();
            var candidates = new Dictionary<string, NotesFolderCandidate>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<(string Path, int Depth)>();

            foreach (var root in roots)
            {
                if (Directory.Exists(root))
                {
                    queue.Enqueue((Path.GetFullPath(root), 0));
                }
            }

            var scanned = 0;
            while (queue.Count > 0 && scanned < maxScannedDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (path, depth) = queue.Dequeue();
                scanned++;

                if (IsNotesFolderCandidate(path, out var candidate))
                {
                    candidates[path] = candidate;
                }

                if (depth >= maxDepth)
                {
                    continue;
                }

                try
                {
                    foreach (var child in Directory.EnumerateDirectories(path))
                    {
                        if (!IsSystemPath(child))
                        {
                            queue.Enqueue((child, depth + 1));
                        }
                    }
                }
                catch
                {
                    // Some user folders can be unavailable; skip them without interrupting the search.
                }
            }

            return candidates.Values
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.LastModified)
                .ToList();
        }, cancellationToken);
    }

    private static bool IsNotesFolderCandidate(string path, out NotesFolderCandidate candidate)
    {
        candidate = new NotesFolderCandidate(path, 0, false, DateTime.MinValue, 0);

        try
        {
            var directory = new DirectoryInfo(path);
            var hasOrderFile = File.Exists(Path.Combine(path, ".notes-order.json"));
            var txtFiles = Directory.EnumerateFiles(path, "*.txt", SearchOption.TopDirectoryOnly)
                .Select(file => new FileInfo(file))
                .Where(file => file.Exists)
                .Take(50)
                .ToList();
            var txtCount = txtFiles.Count;
            var hasSubdirectories = Directory.EnumerateDirectories(path).Any();
            var knownName = directory.Name.Equals("Notes", StringComparison.OrdinalIgnoreCase) ||
                            directory.Name.Equals("NotesTaskView", StringComparison.OrdinalIgnoreCase) ||
                            directory.Name.Equals("Заметки", StringComparison.OrdinalIgnoreCase);

            if (!hasOrderFile && !knownName && txtCount < 2 && !(txtCount > 0 && hasSubdirectories))
            {
                return false;
            }

            var lastModified = txtFiles.Count == 0
                ? directory.LastWriteTime
                : txtFiles.Max(file => file.LastWriteTime);
            var score = (hasOrderFile ? 100 : 0) +
                        (knownName ? 40 : 0) +
                        Math.Min(txtCount, 20) +
                        (hasSubdirectories ? 5 : 0);
            candidate = new NotesFolderCandidate(path, txtCount, hasOrderFile, lastModified, score);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<string> GetSafeSearchRoots()
    {
        var roots = new List<string?>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetEnvironmentVariable("OneDrive")
        };

        return roots
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .Where(path => Directory.Exists(path) && !IsSystemPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsSystemPath(string path)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var systemPaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        return systemPaths
            .Where(systemPath => !string.IsNullOrWhiteSpace(systemPath))
            .Select(systemPath => Path.GetFullPath(systemPath).TrimEnd(Path.DirectorySeparatorChar))
            .Any(systemPath => fullPath.StartsWith(systemPath, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetDocumentsFolder()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return string.IsNullOrWhiteSpace(documents)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents")
            : documents;
    }

    private void ApplyNotesDirectory(string notesDirectory)
    {
        _settings.NotesDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(notesDirectory.Trim()));
        _userSettingsService.Save(_settings);
        _noteService.UpdateNotesFolder(_settings.NotesDirectory);
        _currentFolderPath = _noteService.ResolveFolderOrRoot(_settings.NotesDirectory);
        UpdatePathState();
    }

    private async Task<bool> TryHideOverlayAsync()
    {
        if (!await ConfirmSaveBeforeLeavingEditorAsync())
        {
            return false;
        }

        Hide();
        return true;
    }

    private async Task<bool> TryReturnToListAsync()
    {
        if (EditorVisibility != Visibility.Visible)
        {
            return true;
        }

        if (!await ConfirmSaveBeforeLeavingEditorAsync())
        {
            return false;
        }

        ReturnToList();
        return true;
    }

    private async Task LoadItemsAsync()
    {
        _currentFolderPath = _noteService.ResolveFolderOrRoot(_currentFolderPath);
        var selectedPath = GetItemPath(SelectedItem);
        var items = await _noteService.GetItemsAsync(_currentFolderPath);

        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        _selectedPaths.RemoveWhere(path => FindItemByPath(path) is null);
        foreach (var item in Items)
        {
            SetSelectionState(item, _selectedPaths.Contains(GetItemPath(item) ?? string.Empty));
        }

        _folderCount = items.OfType<FolderItem>().Count();
        _noteCount = items.OfType<NoteItem>().Count();
        OnPropertyChanged(nameof(TotalItemsText));
        OnPropertyChanged(nameof(FolderCountText));
        OnPropertyChanged(nameof(NoteCountText));
        OnPropertyChanged(nameof(SummaryLineText));

        SelectedItem = FindItemByPath(selectedPath) ?? Items.FirstOrDefault();
        if (_selectedPaths.Count == 0 && SelectedItem is not null)
        {
            AddToSelection(SelectedItem);
        }

        UpdatePathState();

        if (EditorVisibility == Visibility.Visible)
        {
            return;
        }

        ShowStatus(items.Count == 0 ? "В этой папке пока пусто." : string.Empty, false);
        LogItemsScrollSizes("LoadItems");
        FocusItemsHost();
    }

    private void UpdatePathState()
    {
        var relativePath = _noteService.GetRelativePath(_currentFolderPath);
        OverlayTitle = string.IsNullOrWhiteSpace(relativePath)
            ? _settings.OverlayTitle
            : Path.GetFileName(_currentFolderPath);
        CurrentPathDisplay = string.IsNullOrWhiteSpace(relativePath)
            ? $"Папка заметок: {_noteService.NotesFolderPath}"
            : $"Папка заметок: {_noteService.NotesFolderPath}  ›  {relativePath}";
        UpFolderVisibility = _noteService.IsRootFolder(_currentFolderPath) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task CreateNoteInCurrentFolderAsync()
    {
        var title = await ShowPromptAsync("Новая заметка", "Введите название заметки. Можно оставить пустым — будет дата.", "Создать");
        if (title is null)
        {
            FocusItemsHost();
            return;
        }

        var result = await _noteService.CreateNoteAsync(title, _currentFolderPath);
        ShowStatus(result.Message, !result.Success);

        if (!result.Success || string.IsNullOrWhiteSpace(result.FilePath))
        {
            return;
        }

        await LoadItemsAsync();
        await OpenNoteAsync(result.FilePath);
    }

    private async Task CreateFolderInCurrentFolderAsync()
    {
        var folderName = await ShowPromptAsync("Новая папка", "Введите название папки", "Создать");
        if (folderName is null)
        {
            FocusItemsHost();
            return;
        }

        var result = await _noteService.CreateFolderAsync(folderName, _currentFolderPath);
        ShowStatus(result.Message, !result.Success);
        await LoadItemsAsync();
    }

    private async Task OpenNoteAsync(string filePath, int? lineNumber = null, int? column = null)
    {
        if (!await ConfirmSaveBeforeLeavingEditorAsync())
        {
            return;
        }

        var result = await _noteService.ReadNoteAsync(filePath);
        if (!result.Success)
        {
            ShowStatus(result.Message, true);
            await LoadItemsAsync();
            return;
        }

        _currentFilePath = filePath;
        _lastSavedContent = result.Content;
        _isLoadingEditorText = true;
        EditorText = result.Content;
        _isLoadingEditorText = false;

        EditorTitle = Path.GetFileNameWithoutExtension(filePath);
        SetEditorDirty(false);
        ListVisibility = Visibility.Collapsed;
        EditorVisibility = Visibility.Visible;
        ShowStatus(string.Empty, false);

        _ = Dispatcher.BeginInvoke(() =>
        {
            EditorTextBox.Focus();
            if (lineNumber is not null)
            {
                MoveEditorCaretToMatch(lineNumber.Value, column ?? 0, Math.Max(0, GlobalSearchQuery.Length));
            }
        }, DispatcherPriority.Input);
    }

    private async Task OpenFolderAsync(string folderPath)
    {
        _currentFolderPath = _noteService.ResolveFolderOrRoot(folderPath);
        await LoadItemsAsync();
    }

    private async Task GoUpFolderAsync()
    {
        var parent = _noteService.GetParentFolder(_currentFolderPath);
        if (parent is null)
        {
            await TryHideOverlayAsync();
            return;
        }

        _currentFolderPath = parent;
        await LoadItemsAsync();
    }

    private async Task<bool> SaveCurrentNoteAsync(bool showSuccessMessage = true)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            return false;
        }

        var result = await _noteService.SaveNoteAsync(_currentFilePath, EditorText);
        if (!result.Success)
        {
            ShowStatus(result.Message, true);
            return false;
        }

        _lastSavedContent = EditorText;
        SetEditorDirty(false);

        if (showSuccessMessage)
        {
            ShowStatus(result.Message, false);
        }

        await LoadItemsAsync();
        return true;
    }

    private async Task<bool> ConfirmSaveBeforeLeavingEditorAsync()
    {
        if (EditorVisibility != Visibility.Visible || !_isEditorDirty)
        {
            return true;
        }

        var confirmation = await ShowChoiceAsync(
            "Несохранённые изменения",
            "Сохранить изменения?",
            ("save", "Сохранить", false),
            ("discard", "Не сохранять", false),
            ("cancel", "Отмена", false));

        if (confirmation == "cancel")
        {
            return false;
        }

        if (confirmation == "save")
        {
            return await SaveCurrentNoteAsync(false);
        }

        return true;
    }

    private void ReturnToList()
    {
        _currentFilePath = null;
        _lastSavedContent = string.Empty;
        _isLoadingEditorText = true;
        EditorText = string.Empty;
        _isLoadingEditorText = false;
        SetEditorDirty(false);
        EditorTitle = "Редактор";
        ListVisibility = Visibility.Visible;
        EditorVisibility = Visibility.Collapsed;
    }

    private void SetEditorDirty(bool isDirty)
    {
        _isEditorDirty = isDirty;
        EditorStateText = isDirty ? "Есть изменения" : "Сохранено";
        EditorStateBrush = GetBrush(isDirty ? "WarningTextBrush" : "SecondaryTextBrush");
    }

    private Brush GetBrush(string key)
    {
        return (Brush)Application.Current.Resources[key];
    }

    private static Brush CreateDimBrush(double opacity)
    {
        var alpha = (byte)Math.Round(Math.Clamp(opacity, 0.00, 1.00) * 255);
        return new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
    }

    private static Brush CreateBrushFromHex(string? hexColor)
    {
        try
        {
            return (Brush)new BrushConverter().ConvertFromString(hexColor ?? "#80FFFFFF")!;
        }
        catch
        {
            return new SolidColorBrush(Color.FromArgb(128, 255, 255, 255));
        }
    }

    private static SolidColorBrush CreateBrush(Color color, byte alpha)
    {
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    private static Color ParseColorOrDefault(string? hexColor, Color fallback)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hexColor ?? string.Empty)!;
        }
        catch
        {
            return fallback;
        }
    }

    private void UpdateAccentResources(string? hexColor)
    {
        var color = ParseColorOrDefault(hexColor, Color.FromRgb(122, 162, 255));
        Application.Current.Resources["AccentColorValue"] = color;
        SetBrushResource("AccentBrush", Color.FromArgb(255, color.R, color.G, color.B));
        var hover = Lighten(color, 0.12);
        SetBrushResource("AccentBrushHover", Color.FromArgb(255, hover.R, hover.G, hover.B));
        SetBrushResource("AccentSoftBrush", Color.FromArgb(128, color.R, color.G, color.B));
        SetBrushResource("AccentVerySoftBrush", Color.FromArgb(42, color.R, color.G, color.B));
        SetBrushResource("AccentGlowBrush", Color.FromArgb(104, color.R, color.G, color.B));
        SetBrushResource("AccentBorderBrush", Color.FromArgb(178, color.R, color.G, color.B));
    }

    private static void SetBrushResource(string key, Color color)
    {
        if (Application.Current.Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }

        Application.Current.Resources[key] = new SolidColorBrush(color);
    }

    private static Color Lighten(Color color, double amount)
    {
        static byte Mix(byte channel, double amount) => (byte)Math.Clamp(channel + ((255 - channel) * amount), 0, 255);
        return Color.FromRgb(Mix(color.R, amount), Mix(color.G, amount), Mix(color.B, amount));
    }

    private Brush CreateSelectionOutlineBrush(UserSettings settings)
    {
        if (!settings.UseAccentForSelectionOutline)
        {
            return CreateBrushFromHex(settings.SelectionOutlineColor);
        }

        var accent = ParseColorOrDefault(settings.AccentColor, Color.FromRgb(122, 162, 255));
        return CreateBrush(accent, 180);
    }

    private void ShowStatus(string message, bool persistent)
    {
        StatusMessage = message;
        StatusVisibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        _ = Dispatcher.BeginInvoke(() =>
        {
            OverlayToast.UpdateLayout();
            Canvas.SetLeft(OverlayToast, Math.Max(24, (RootGrid.ActualWidth - OverlayToast.ActualWidth) / 2));
            Canvas.SetTop(OverlayToast, 34);
        }, DispatcherPriority.Loaded);

        if (!persistent && !string.IsNullOrWhiteSpace(message))
        {
            _ = AutoHideStatusAsync(message);
        }
    }

    private async Task AutoHideStatusAsync(string expectedMessage)
    {
        await Task.Delay(2400);

        if (StatusMessage == expectedMessage)
        {
            StatusMessage = string.Empty;
            StatusVisibility = Visibility.Collapsed;
        }
    }

    private void NoteService_OnNotesChanged(object? sender, EventArgs e)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            _refreshDebounceTimer.Stop();
            _refreshDebounceTimer.Start();
        });
    }

    private async void RefreshDebounceTimer_OnTick(object? sender, EventArgs e)
    {
        _refreshDebounceTimer.Stop();

        if (EditorVisibility == Visibility.Visible)
        {
            if (!string.IsNullOrWhiteSpace(_currentFilePath) && !File.Exists(_currentFilePath))
            {
                ShowStatus("Заметка была удалена или перемещена вне приложения. Возвращаю к списку.", true);
                ReturnToList();
                await LoadItemsAsync();
            }

            return;
        }

        await LoadItemsAsync();
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        await LoadItemsAsync();
    }

    private async void NewButton_OnClick(object sender, RoutedEventArgs e)
    {
        await CreateNoteInCurrentFolderAsync();
    }

    private async void NewFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        await CreateFolderInCurrentFolderAsync();
    }

    private async void UpFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        await GoUpFolderAsync();
    }

    private void SettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSettingsPanel();
    }

    private void FolderChoiceTile_OnClick(object sender, RoutedEventArgs e)
    {
        LogDragDebug("Button clicked: FolderTile", sender);
        if (sender is Button { Tag: FolderChoiceItem choice })
        {
            _selectedFolderChoice = choice;
            RefreshFolderChoiceTiles();
            FolderPickerConfirmButton.Focus();
        }
    }

    private void FolderPickerPrevButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShiftFolderChoicePage(-5);
    }

    private void FolderPickerNextButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShiftFolderChoicePage(5);
    }

    private void FolderPickerCancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        LogDragDebug("Button clicked: FolderPickerCancel", sender);
        CompleteFolderPicker(null);
    }

    private void FolderPickerConfirmButton_OnClick(object sender, RoutedEventArgs e)
    {
        LogDragDebug("Button clicked: MoveToFolder", sender);
        ConfirmFolderPicker();
    }

    private void FolderPickerOverlay_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ShiftFolderChoicePage(e.Delta < 0 ? 5 : -5);
        e.Handled = true;
    }

    private async void BackButton_OnClick(object sender, RoutedEventArgs e)
    {
        LogDragDebug("Button clicked: Back", sender);
        await TryReturnToListAsync();
    }

    private async void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        LogDragDebug("Button clicked: Save", sender);
        await SaveCurrentNoteAsync();
    }

    private async void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        await TryHideOverlayAsync();
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = GetEffectiveKey(e);

        if (key == Key.D && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ToggleDragDebugOverlay();
            e.Handled = true;
            return;
        }

        if (key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            if (EditorVisibility == Visibility.Visible)
            {
                OpenFileSearch();
            }
            else
            {
                OpenGlobalSearch();
            }

            return;
        }

        if (key == Key.Escape)
        {
            LogDragDebug("KeyDown Escape", e.OriginalSource);
        }

        if (_isKeyboardDragging)
        {
            if (key is Key.Left or Key.Right or Key.Up or Key.Down)
            {
                MoveKeyboardDragPlaceholder(key);
                e.Handled = true;
                return;
            }

            if (key == Key.Enter)
            {
                e.Handled = true;
                await CompleteKeyboardDragAsync();
                return;
            }

            if (key == Key.Escape)
            {
                e.Handled = true;
                CancelActiveDrag();
                return;
            }
        }

        if (PromptOverlay.Visibility == Visibility.Visible)
        {
            if (Keyboard.FocusedElement is not TextBox && HandleButtonPanelKey(PromptButtonsPanel, key))
            {
                e.Handled = true;
            }
            else if (key == Key.Escape)
            {
                CompletePrompt(null);
                e.Handled = true;
            }

            return;
        }

        if (ColorPickerOverlay.Visibility == Visibility.Visible)
        {
            if (key == Key.Enter)
            {
                ColorPickerApplyButton_OnClick(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (HandleButtonPanelKey(ColorPickerButtonsPanel, key))
            {
                e.Handled = true;
            }
            else if (key == Key.Escape)
            {
                ColorPickerOverlay.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }

            return;
        }

        if (SettingsOverlay.Visibility == Visibility.Visible)
        {
            if (key == Key.Escape)
            {
                if (SettingsTrashPanel.Visibility == Visibility.Visible)
                {
                    ShowSettingsMainTab();
                }
                else
                {
                    CancelSettings();
                }

                e.Handled = true;
            }

            return;
        }

        if (FolderPickerOverlay.Visibility == Visibility.Visible)
        {
            if (key == Key.Escape)
            {
                CompleteFolderPicker(null);
                e.Handled = true;
            }
            else if (key == Key.Enter)
            {
                ConfirmFolderPicker();
                e.Handled = true;
            }
            else if (key == Key.Left)
            {
                MoveFolderChoiceSelection(-1);
                e.Handled = true;
            }
            else if (key == Key.Right)
            {
                MoveFolderChoiceSelection(1);
                e.Handled = true;
            }

            return;
        }

        if (ModalOverlay.Visibility == Visibility.Visible)
        {
            if (HandleButtonPanelKey(ModalButtonsPanel, key))
            {
                e.Handled = true;
            }
            else if (key == Key.Escape)
            {
                CompleteModal(null);
                e.Handled = true;
            }

            return;
        }

        if (ContextMenuLayer.Visibility == Visibility.Visible &&
            HandleButtonPanelKey(OverlayContextMenuItems, key))
        {
            e.Handled = true;
            return;
        }

        if (ContextMenuLayer.Visibility == Visibility.Visible && key == Key.Escape)
        {
            HideOverlayContextMenu();
            e.Handled = true;
            return;
        }

        if (EditorVisibility != Visibility.Visible && IsAltOnlyKey(key))
        {
            e.Handled = true;
            return;
        }

        if (EditorVisibility != Visibility.Visible && key is Key.RightShift or Key.Apps)
        {
            e.Handled = true;
            ShowContextMenuForSelection();
            return;
        }

        if (EditorVisibility != Visibility.Visible &&
            key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            e.Handled = true;
            MoveSelection(key, Keyboard.Modifiers);
            return;
        }

        if (EditorVisibility != Visibility.Visible &&
            (key is Key.Space or Key.Enter) &&
            SelectedItem is not null &&
            (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)))
        {
            e.Handled = true;
            ToggleSelection(SelectedItem);
            _selectionAnchorPath ??= GetItemPath(SelectedItem);
            OnPropertyChanged(nameof(SummaryLineText));
            return;
        }

        if (EditorVisibility != Visibility.Visible && key == Key.F5)
        {
            e.Handled = true;
            await LoadItemsAsync();
            if (_isGlobalSearchOpen && !string.IsNullOrWhiteSpace(GlobalSearchQuery))
            {
                RestartGlobalSearchDebounce(immediate: true);
            }
            return;
        }

        if (EditorVisibility != Visibility.Visible &&
            key == Key.Enter &&
            SelectedItem is not null)
        {
            if (!_isEnterHoldPending && !e.IsRepeat)
            {
                _isEnterHoldPending = true;
                _enterHoldTimer.Stop();
                _enterHoldTimer.Start();
            }

            e.Handled = true;
            return;
        }

        if (EditorVisibility != Visibility.Visible &&
            key == Key.Delete &&
            SelectedItem is not null)
        {
            e.Handled = true;
            await DeleteSelectedItemAsync();
            return;
        }

        if (EditorVisibility != Visibility.Visible &&
            key == Key.C &&
            Keyboard.Modifiers is ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift) &&
            SelectedItem is NoteItem selectedNote)
        {
            e.Handled = true;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                ShowOperation(_noteService.CopyFileToClipboard(selectedNote.FullPath));
            }
            else
            {
                ShowOperation(await _noteService.CopyContentToClipboardAsync(selectedNote.FullPath));
            }

            return;
        }

        if (key == Key.Escape)
        {
            e.Handled = true;

            if (EditorVisibility == Visibility.Visible && FileSearchVisibility == Visibility.Visible)
            {
                CloseFileSearch();
                return;
            }

            if (EditorVisibility != Visibility.Visible && _isGlobalSearchOpen)
            {
                if (!string.IsNullOrWhiteSpace(GlobalSearchQuery))
                {
                    GlobalSearchQuery = string.Empty;
                }
                else
                {
                    await CloseGlobalSearch();
                }

                return;
            }

            if (PreviewPopupVisibility == Visibility.Visible)
            {
                HidePreviewPopup();
                return;
            }

            if (_selectedPaths.Count > 1)
            {
                ClearMultiSelection();
                return;
            }

            if (EditorVisibility == Visibility.Visible)
            {
                await TryReturnToListAsync();
                return;
            }

            if (!_noteService.IsRootFolder(_currentFolderPath))
            {
                await GoUpFolderAsync();
                return;
            }

            await TryHideOverlayAsync();
            return;
        }

        if (EditorVisibility == Visibility.Visible &&
            FileSearchVisibility == Visibility.Visible &&
            key == Key.Enter)
        {
            e.Handled = true;
            MoveFileSearchMatch(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            return;
        }

        if (EditorVisibility == Visibility.Visible &&
            key == Key.S &&
            Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            await SaveCurrentNoteAsync();
        }
    }

    private async void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (EditorVisibility == Visibility.Visible || e.Key != Key.Enter)
        {
            return;
        }

        if (_isKeyboardDragging)
        {
            e.Handled = true;
            return;
        }

        if (_isEnterHoldPending)
        {
            _enterHoldTimer.Stop();
            _isEnterHoldPending = false;
            e.Handled = true;
            await OpenSelectedItemAsync();
        }
    }

    private async void BackdropBorder_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        HideOverlayContextMenu();
        HidePreviewPopup();

        if (e.OriginalSource != BackdropBorder)
        {
            return;
        }

        if (EditorVisibility == Visibility.Visible)
        {
            await TryReturnToListAsync();
            return;
        }

        SelectedItem = null;
        ClearMultiSelection();
    }

    private void RootGrid_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        LogDragDebug("MouseLeftButtonDown", e.OriginalSource);
        if (IsModalLayerOpen())
        {
            e.Handled = true;
            return;
        }

        if (FindParent<ListBoxItem>(e.OriginalSource) is null && FindParent<Button>(e.OriginalSource) is null)
        {
            HidePreviewPopup();
            if (ListVisibility == Visibility.Visible)
            {
                SelectedItem = null;
                ClearMultiSelection();
            }
        }

        if (ContextMenuLayer.Visibility == Visibility.Visible &&
            FindParent<Button>(e.OriginalSource) is null)
        {
            HideOverlayContextMenu();
        }
    }

    private void RootGrid_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        LogDragDebug("PreviewMouseLeftButtonDown", e.OriginalSource);
        if (IsModalLayerOpen())
        {
            LogDragDebug("Card/root input ignored", e.OriginalSource, "modal open");
            return;
        }
    }

    private async void NoteCard_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not NoteItem note || IsInsideButton(e.OriginalSource))
        {
            return;
        }

        if (IsModalLayerOpen())
        {
            e.Handled = true;
            return;
        }

        if (!_suppressOpenAfterDrag && Keyboard.Modifiers == ModifierKeys.None && _selectedPaths.Count <= 1)
        {
            await OpenNoteAsync(note.FullPath);
        }

        _suppressOpenAfterDrag = false;
    }

    private async void FolderCard_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not FolderItem folder || IsInsideButton(e.OriginalSource))
        {
            return;
        }

        if (IsModalLayerOpen())
        {
            e.Handled = true;
            return;
        }

        if (!_suppressOpenAfterDrag && Keyboard.Modifiers == ModifierKeys.None && _selectedPaths.Count <= 1)
        {
            await OpenFolderAsync(folder.FullPath);
        }

        _suppressOpenAfterDrag = false;
    }

    private void NoteCard_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not NoteItem note || IsInsideButton(e.OriginalSource))
        {
            return;
        }

        if (IsModalLayerOpen())
        {
            e.Handled = true;
            return;
        }

        HidePreviewPopup();
        UpdateSelectionFromClick(note, Keyboard.Modifiers);
        SetPendingDrag(note.FullPath, e.GetPosition(RootGrid), Keyboard.Modifiers);
        e.Handled = false;
    }

    private void FolderCard_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not FolderItem folder || IsInsideButton(e.OriginalSource))
        {
            return;
        }

        if (IsModalLayerOpen())
        {
            e.Handled = true;
            return;
        }

        HidePreviewPopup();
        UpdateSelectionFromClick(folder, Keyboard.Modifiers);
        SetPendingDrag(folder.FullPath, e.GetPosition(RootGrid), Keyboard.Modifiers);
        e.Handled = false;
    }

    private async void NoteCard_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle ||
            sender is not Border border ||
            border.DataContext is not NoteItem note ||
            IsInsideButton(e.OriginalSource))
        {
            return;
        }

        e.Handled = true;
        await ShowNotePreviewAsync(note, e.GetPosition(RootGrid));
    }

    private void FolderCard_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle ||
            sender is not Border border ||
            border.DataContext is not FolderItem folder ||
            IsInsideButton(e.OriginalSource))
        {
            return;
        }

        e.Handled = true;
        ShowFolderPreview(folder, e.GetPosition(RootGrid));
    }

    private void Card_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            HidePreviewPopup();
            e.Handled = true;
        }
    }

    private void NoteCard_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (IsModalLayerOpen())
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed ||
            sender is not Border border ||
            border.DataContext is not NoteItem note ||
            IsInsideButton(e.OriginalSource) ||
            !ShouldStartDrag(note.FullPath, e.GetPosition(RootGrid)))
        {
            return;
        }

        if (_selectedPaths.Count > 1 && !_selectedPaths.Contains(note.FullPath))
        {
            ClearMultiSelection();
            AddToSelection(note);
        }

        BeginDrag(border, note.FullPath, "Заметка", note.Title, NotePathDragFormat);
    }

    private void FolderCard_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (IsModalLayerOpen())
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed ||
            sender is not Border border ||
            border.DataContext is not FolderItem folder ||
            IsInsideButton(e.OriginalSource) ||
            !ShouldStartDrag(folder.FullPath, e.GetPosition(RootGrid)))
        {
            return;
        }

        if (_selectedPaths.Count > 1 && !_selectedPaths.Contains(folder.FullPath))
        {
            ClearMultiSelection();
            AddToSelection(folder);
        }

        BeginDrag(border, folder.FullPath, "Папка", folder.Name, FolderPathDragFormat);
    }

    private void FolderCard_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        UpdateDragPreview(e.GetPosition(RootGrid));
        ClearDropTargets();

        if (sender is Border border && border.DataContext is FolderItem targetFolder)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                UpdateDragPlaceholder(targetFolder.FullPath);
                e.Effects = DragDropEffects.Move;
            }
            else if (e.Data.GetDataPresent(NotePathDragFormat))
            {
                targetFolder.IsDropTarget = true;
                e.Effects = DragDropEffects.Move;
            }
            else if (e.Data.GetDataPresent(FolderPathDragFormat) &&
                     e.Data.GetData(FolderPathDragFormat) is string sourceFolder &&
                     _noteService.CanMoveFolderToFolder(sourceFolder, targetFolder.FullPath))
            {
                targetFolder.IsDropTarget = true;
                e.Effects = DragDropEffects.Move;
            }
        }

        e.Handled = true;
    }

    private async void FolderCard_OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not FolderItem folder)
        {
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            await ReorderDraggedItemAsync(folder.FullPath);
            e.Handled = true;
            return;
        }

        NoteOperationResult result;
        if (e.Data.GetDataPresent(NotePathDragFormat))
        {
            var filePath = e.Data.GetData(NotePathDragFormat) as string;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            result = await _noteService.MoveNoteToFolderAsync(filePath, folder.FullPath);
        }
        else if (e.Data.GetDataPresent(FolderPathDragFormat))
        {
            var folderPath = e.Data.GetData(FolderPathDragFormat) as string;
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            result = await _noteService.MoveFolderToFolderAsync(folderPath, folder.FullPath);
        }
        else
        {
            return;
        }

        ShowStatus(result.Message, !result.Success);
        await LoadItemsAsync();
        HideDragPreview();
        e.Handled = true;
    }

    private void ItemCard_OnDragOver(object sender, DragEventArgs e)
    {
        UpdateDragPreview(e.GetPosition(RootGrid));
        ClearDropTargets();
        if (sender is Border border && border.DataContext is NoteItem note)
        {
            UpdateDragPlaceholder(note.FullPath);
        }
        e.Effects = HasOverlayDragData(e) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private async void NoteCard_OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not NoteItem note)
        {
            return;
        }

        await ReorderDraggedItemAsync(note.FullPath);
        e.Handled = true;
    }

    private void ItemsListBox_OnDragOver(object sender, DragEventArgs e)
    {
        UpdateDragPreview(e.GetPosition(RootGrid));
        ClearDropTargets();
        if (FindParent<ListBoxItem>(e.OriginalSource) is null)
        {
            UpdateDragPlaceholder(null);
        }
        e.Effects = HasOverlayDragData(e) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private async void ItemsListBox_OnDrop(object sender, DragEventArgs e)
    {
        if (FindParent<ListBoxItem>(e.OriginalSource) is not null)
        {
            return;
        }

        await ReorderDraggedItemAsync(null);
        e.Handled = true;
    }

    private void RootGrid_OnDragOver(object sender, DragEventArgs e)
    {
        UpdateDragPreview(e.GetPosition(RootGrid));
        e.Effects = HasOverlayDragData(e) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private async void RootGrid_OnDrop(object sender, DragEventArgs e)
    {
        if (FindParent<ListBoxItem>(e.OriginalSource) is not null)
        {
            return;
        }

        await ReorderDraggedItemAsync(null);
        e.Handled = true;
    }

    private void NoteCard_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not NoteItem note)
        {
            return;
        }

        PrepareContextSelection(note);
        if (ShowGroupContextMenuIfNeeded(e.GetPosition(RootGrid)))
        {
            e.Handled = true;
            return;
        }

        ShowOverlayContextMenu(e.GetPosition(RootGrid),
            ("Открыть", false, async () => await OpenNoteAsync(note.FullPath)),
            ("Переместить в папку", false, async () => await MoveNoteViaDialogAsync(note.FullPath)),
            ("Копировать файл", false, () =>
            {
                ShowOperation(_noteService.CopyFileToClipboard(note.FullPath));
                return Task.CompletedTask;
            }),
            ("Копировать содержимое файла", false, async () => ShowOperation(await _noteService.CopyContentToClipboardAsync(note.FullPath))),
            ("Открыть в проводнике", false, async () => await OpenInExplorerAndHideAsync(note.FullPath, true)),
            ("Удалить", true, async () => await MoveNoteToTrashWithConfirmationAsync(note.FullPath)));
        e.Handled = true;
    }

    private void FolderCard_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not FolderItem folder)
        {
            return;
        }

        PrepareContextSelection(folder);
        if (ShowGroupContextMenuIfNeeded(e.GetPosition(RootGrid)))
        {
            e.Handled = true;
            return;
        }

        ShowOverlayContextMenu(e.GetPosition(RootGrid),
            ("Открыть", false, async () => await OpenFolderAsync(folder.FullPath)),
            ("Переместить в папку", false, async () => await MoveFolderViaDialogAsync(folder)),
            ("Переименовать", false, async () => await RenameFolderAsync(folder)),
            ("Открыть в проводнике", false, async () => await OpenInExplorerAndHideAsync(folder.FullPath, false)),
            ("Удалить", true, async () => await DeleteFolderAsync(folder)));
        e.Handled = true;
    }

    private void ItemsSurface_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindParent<ListBoxItem>(e.OriginalSource) is not null)
        {
            return;
        }

        ShowOverlayContextMenu(e.GetPosition(RootGrid),
            ("Создать заметку", false, async () => await CreateNoteInCurrentFolderAsync()),
            ("Создать папку", false, async () => await CreateFolderInCurrentFolderAsync()),
            ("Обновить", false, async () => await LoadItemsAsync()));
        e.Handled = true;
    }

    private async Task MoveNoteViaDialogAsync(string filePath)
    {
        var selectedFolder = await ShowFolderChoiceAsync("Переместить заметку", await _noteService.GetFoldersAsync());
        if (selectedFolder is null)
        {
            FocusItemsHost();
            return;
        }

        var result = await _noteService.MoveNoteToFolderAsync(filePath, selectedFolder.FullPath);
        ShowStatus(result.Message, !result.Success);
        await LoadItemsAsync();
    }

    private async Task MoveFolderViaDialogAsync(FolderItem folder)
    {
        var folders = (await _noteService.GetFoldersAsync())
            .Where(candidate => _noteService.CanMoveFolderToFolder(folder.FullPath, candidate.FullPath))
            .ToList();
        var selectedFolder = await ShowFolderChoiceAsync("Переместить папку", folders);
        if (selectedFolder is null)
        {
            FocusItemsHost();
            return;
        }

        var result = await _noteService.MoveFolderToFolderAsync(folder.FullPath, selectedFolder.FullPath);
        ShowStatus(result.Message, !result.Success);
        await LoadItemsAsync();
    }

    private async Task OpenInExplorerAndHideAsync(string path, bool selectFile)
    {
        try
        {
            var exists = selectFile ? File.Exists(path) : Directory.Exists(path);
            if (!exists)
            {
                ShowStatus(selectFile ? "Файл не найден." : "Папка не найдена.", true);
                await LoadItemsAsync();
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = selectFile ? $"/select,\"{path}\"" : $"\"{path}\"",
                UseShellExecute = true
            });

            await TryHideOverlayAsync();
        }
        catch (Exception ex)
        {
            ShowStatus($"Не удалось открыть проводник: {ex.Message}", true);
        }
    }

    private async Task RenameFolderAsync(FolderItem folder)
    {
        var newName = await ShowPromptAsync("Переименовать папку", "Введите новое название папки", "Сохранить", folder.Name);
        if (newName is null)
        {
            FocusItemsHost();
            return;
        }

        var result = await _noteService.RenameFolderAsync(folder.FullPath, newName);
        ShowStatus(result.Message, !result.Success);
        await LoadItemsAsync();
    }

    private async Task DeleteFolderAsync(FolderItem folder)
    {
        bool hasItems;
        try
        {
            if (!Directory.Exists(folder.FullPath))
            {
                ShowStatus("Папка не найдена.", true);
                await LoadItemsAsync();
                return;
            }

            hasItems = Directory.EnumerateFileSystemEntries(folder.FullPath).Any();
        }
        catch (Exception ex)
        {
            ShowStatus($"Не удалось проверить папку: {ex.Message}", true);
            await LoadItemsAsync();
            return;
        }

        var confirmed = await ShowConfirmAsync(
            "Подтверждение удаления",
            hasItems ? "Папка не пустая. Переместить папку в мусорку?" : "Удалить папку?",
            "Удалить",
            "Отмена",
            true);

        if (!confirmed)
        {
            return;
        }

        var result = await _noteService.MoveFolderToTrashAsync(folder.FullPath);
        ShowStatus(result.Message, !result.Success);
        await LoadItemsAsync();
        FocusItemsHost();
    }

    private async void DeleteFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button button || button.Tag is not FolderItem folder)
        {
            return;
        }

        await DeleteFolderAsync(folder);
    }

    private async void DeleteButton_OnClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button button || button.Tag is not string filePath)
        {
            return;
        }

        await MoveNoteToTrashWithConfirmationAsync(filePath);
    }

    private async Task MoveNoteToTrashWithConfirmationAsync(string filePath)
    {
        var confirmed = await ShowConfirmAsync(
            "Подтверждение удаления",
            "Удалить заметку?",
            "Удалить",
            "Отмена",
            true);

        if (!confirmed)
        {
            return;
        }

        var result = await _noteService.MoveNoteToTrashAsync(filePath);
        ShowStatus(result.Message, !result.Success);
        await LoadItemsAsync();
    }

    private async void DeleteCurrentButton_OnClick(object sender, RoutedEventArgs e)
    {
        LogDragDebug("Button clicked: Delete", sender);
        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            return;
        }

        var confirmed = await ShowConfirmAsync(
            "Подтверждение удаления",
            "Удалить заметку?",
            "Удалить",
            "Отмена",
            true);

        if (!confirmed)
        {
            return;
        }

        var result = await _noteService.MoveNoteToTrashAsync(_currentFilePath);
        ShowStatus(result.Message, !result.Success);

        if (result.Success)
        {
            ReturnToList();
        }

        await LoadItemsAsync();
    }

    private void EditorTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingEditorText || EditorVisibility != Visibility.Visible)
        {
            return;
        }

        SetEditorDirty(EditorText != _lastSavedContent);
        if (FileSearchVisibility == Visibility.Visible && !string.IsNullOrWhiteSpace(FileSearchQuery))
        {
            UpdateFileSearchMatches();
        }
        else if (FileSearchVisibility == Visibility.Visible)
        {
            RefreshFileSearchHighlight();
        }
    }

    private void BeginDrag(Border source, string path, string kind, string title, string dataFormat)
    {
        if (IsModalLayerOpen() || _isGlobalSearchOpen)
        {
            LogDragDebug("StartDrag ignored", source, "modal/search active");
            return;
        }

        HidePreviewPopup();
        LogDragDebug("StartDrag requested", source, $"{kind}: {title} | {path}");
        if (_isDragging || _isKeyboardDragging)
        {
            LogDragDebug("StartDrag ignored", source, "already dragging");
            return;
        }

        _draggedPath = path;
        _draggedKind = kind;
        _draggedPaths.Clear();
        if (_selectedPaths.Count > 1 && _selectedPaths.Contains(path))
        {
            _draggedPaths.AddRange(_selectedPaths);
            DragPreviewTypeText.Text = $"{_draggedPaths.Count} элементов";
            DragPreviewTitleText.Text = "Групповое перемещение";
        }
        else
        {
            _draggedPaths.Add(path);
            DragPreviewTypeText.Text = kind;
            DragPreviewTitleText.Text = title;
        }

        _isDragging = true;
        SetDraggedItemState(true);
        DragPreviewFolderIcon.Visibility = kind == "Папка" ? Visibility.Visible : Visibility.Collapsed;
        DragPreviewNoteIcon.Visibility = kind == "Заметка" ? Visibility.Visible : Visibility.Collapsed;
        DragPreviewLayer.Visibility = Visibility.Visible;
        _isChangingMouseCaptureInternally = true;
        Mouse.Capture(RootGrid, CaptureMode.SubTree);
        _ = Dispatcher.BeginInvoke(() =>
        {
            _isChangingMouseCaptureInternally = false;
            LogDragDebug("Internal capture transfer ended", RootGrid);
        }, DispatcherPriority.Input);

        var position = Mouse.GetPosition(RootGrid);
        UpdateDragPreview(position);
        UpdateManualDragTarget(position, null);
        LogDragDebug("StartDrag", source, $"captured={Mouse.Captured?.GetType().Name ?? "null"}");
    }

    private async void RootGrid_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        LogDragDebug("PreviewMouseMove", e.OriginalSource);
        if (!_isDragging || _isKeyboardDragging)
        {
            return;
        }

        if (Mouse.LeftButton != MouseButtonState.Pressed)
        {
            LogDragDebug("PreviewMouseMove LeftButtonReleasedDuringDrag", e.OriginalSource, "calling CompletePointerDragAsync");
            e.Handled = true;
            await CompletePointerDragAsync(e.OriginalSource);
            return;
        }

        var position = e.GetPosition(RootGrid);
        UpdateDragPreview(position);
        UpdateManualDragTarget(position, e.OriginalSource);
        UpdateDragAutoScroll(e.GetPosition(ItemsListBox));
        e.Handled = true;
    }

    private void RootGrid_OnMouseMove(object sender, MouseEventArgs e)
    {
        LogDragDebug("MouseMove", e.OriginalSource);
    }

    private async void Window_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        LogDragDebug("PreviewMouseLeftButtonUp(Window)", e.OriginalSource, _isDragging ? "will call EndDrag" : "not dragging");
        if (!_isDragging || _isKeyboardDragging)
        {
            return;
        }

        e.Handled = true;
        await CompletePointerDragAsync(e.OriginalSource);
    }

    private void Window_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            HidePreviewPopup();
            e.Handled = true;
        }
    }

    private async void RootGrid_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        LogDragDebug("PreviewMouseLeftButtonUp(Root)", e.OriginalSource, _isDragging ? "will call EndDrag" : "not dragging");
        if (!_isDragging || _isKeyboardDragging)
        {
            return;
        }

        e.Handled = true;
        await CompletePointerDragAsync(e.OriginalSource);
    }

    private async void RootGrid_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        LogDragDebug("MouseLeftButtonUp", e.OriginalSource, _isDragging ? "will call EndDrag" : "not dragging");
        if (!_isDragging || _isKeyboardDragging)
        {
            return;
        }

        e.Handled = true;
        await CompletePointerDragAsync(e.OriginalSource);
    }

    private void RootGrid_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        LogDragDebug("LostMouseCapture", e.OriginalSource);
        if (_isChangingMouseCaptureInternally)
        {
            LogDragDebug("LostMouseCapture ignored", e.OriginalSource, "internal capture transfer");
            return;
        }

        if (_isDragging && !_isKeyboardDragging && !_isCompletingDrag)
        {
            if (Mouse.LeftButton == MouseButtonState.Pressed)
            {
                LogDragDebug("LostMouseCapture recover", e.OriginalSource, "left still pressed; recapturing RootGrid");
                _isChangingMouseCaptureInternally = true;
                Mouse.Capture(RootGrid, CaptureMode.SubTree);
                _ = Dispatcher.BeginInvoke(() => _isChangingMouseCaptureInternally = false, DispatcherPriority.Input);
                return;
            }

            ForceCleanupDragState("LostMouseCapture without pressed left button");
        }
    }

    private async void RootGrid_OnMouseLeave(object sender, MouseEventArgs e)
    {
        LogDragDebug("MouseLeave", e.OriginalSource);
        if (_isDragging && !_isKeyboardDragging && Mouse.LeftButton != MouseButtonState.Pressed)
        {
            await CompletePointerDragAsync(e.OriginalSource);
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        LogDragDebug("Window.Deactivated", this);
        if (_isDragging || _isKeyboardDragging || _isEnterHoldPending)
        {
            CompleteDragOrCancel();
        }
    }

    private void UpdateDragPreview(Point position)
    {
        if (DragPreviewLayer.Visibility != Visibility.Visible)
        {
            return;
        }

        Canvas.SetLeft(DragPreviewCard, position.X + 18);
        Canvas.SetTop(DragPreviewCard, position.Y + 18);
    }

    private void HideDragPreview()
    {
        DragPreviewLayer.Visibility = Visibility.Collapsed;
    }

    private void UpdateManualDragTarget(Point rootPosition, object? originalSource)
    {
        if (IsPointInsideElement(ListHeaderRegion, rootPosition))
        {
            ClearDropTargets();
            StartDragBackHover();
            return;
        }

        StopDragBackHover();

        var targetItem = GetStableItemFromDragSource(originalSource);

        if (targetItem is FolderItem folder &&
            !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) &&
            CanDropIntoFolder(folder) &&
            !ShouldReorderAtFolderEdge(originalSource))
        {
            SetOnlyDropTarget(folder);
            ClearDragPlaceholder();
            return;
        }

        ClearDropTargets();
        if (targetItem is not null)
        {
            UpdateDragPlaceholder(GetItemPath(targetItem));
            return;
        }

        if (IsPointInsideElement(ItemsListBox, rootPosition))
        {
            if (originalSource is not DependencyObject dependencyObject ||
                FindParent<ListBoxItem>(dependencyObject) is null)
            {
                UpdateDragPlaceholder(null);
            }
            else
            {
                LogDragDebug("ReorderIndex ignored", originalSource, "item container without valid data context");
            }
        }
    }

    private async Task CompletePointerDragAsync(object originalSource)
    {
        LogDragDebug("EndDrag requested", originalSource);
        if (!_isDragging)
        {
            LogDragDebug("EndDrag ignored", originalSource, "not dragging");
            return;
        }

        if (_isCompletingDrag)
        {
            LogDragDebug("EndDrag already completing", originalSource, "forcing cleanup safety path");
            if (Mouse.LeftButton != MouseButtonState.Pressed)
            {
                ForceCleanupDragState("EndDrag requested while already completing and left released");
            }

            return;
        }

        _isCompletingDrag = true;
        try
        {
            var targetItem = GetStableItemFromDragSource(originalSource);

            if (targetItem is FolderItem folder &&
                !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) &&
                CanDropIntoFolder(folder) &&
                !ShouldReorderAtFolderEdge(originalSource))
            {
                LogDragDebug("Drop path", originalSource, $"move into folder: {folder.FullPath}");
                await MoveDraggedItemIntoFolderAsync(folder.FullPath);
            }
            else
            {
                LogDragDebug("Drop/Reorder path", originalSource, $"target={GetItemPath(targetItem) ?? "<empty>"}");
                await ReorderDraggedItemAsync(GetItemPath(targetItem));
            }
        }
        finally
        {
            CompleteDragOrCancel();
        }
    }

    private async Task CompleteKeyboardDragAsync()
    {
        LogDragDebug("EndDrag requested", Keyboard.FocusedElement, "keyboard drag");
        if (!_isDragging)
        {
            LogDragDebug("EndDrag ignored", Keyboard.FocusedElement, "not dragging");
            return;
        }

        if (_isCompletingDrag)
        {
            LogDragDebug("EndDrag already completing", Keyboard.FocusedElement, "keyboard drag cleanup safety path");
            ForceCleanupDragState("Keyboard EndDrag while already completing");
            return;
        }

        _isCompletingDrag = true;
        try
        {
            await ReorderDraggedItemAsync(null);
        }
        finally
        {
            CompleteDragOrCancel();
        }
    }

    private async Task MoveDraggedItemIntoFolderAsync(string targetFolder)
    {
        if (string.IsNullOrWhiteSpace(_draggedPath))
        {
            return;
        }

        if (_draggedPaths.Count > 1)
        {
            foreach (var path in _draggedPaths.ToList())
            {
                var moveResult = File.Exists(path)
                    ? await _noteService.MoveNoteToFolderAsync(path, targetFolder)
                    : Directory.Exists(path)
                        ? await _noteService.MoveFolderToFolderAsync(path, targetFolder)
                        : new NoteOperationResult(false, "Элемент уже удалён или недоступен.");
                if (!moveResult.Success)
                {
                    ShowStatus(moveResult.Message, true);
                }
            }

            await LoadItemsAsync();
            return;
        }

        NoteOperationResult result;
        if (File.Exists(_draggedPath))
        {
            result = await _noteService.MoveNoteToFolderAsync(_draggedPath, targetFolder);
        }
        else if (Directory.Exists(_draggedPath))
        {
            result = await _noteService.MoveFolderToFolderAsync(_draggedPath, targetFolder);
        }
        else
        {
            ShowStatus("Элемент уже удалён или недоступен.", true);
            await LoadItemsAsync();
            return;
        }

        ShowStatus(result.Message, !result.Success);
        await LoadItemsAsync();
    }

    private bool CanDropIntoFolder(FolderItem folder)
    {
        if (string.IsNullOrWhiteSpace(_draggedPath) ||
            string.Equals(_draggedPath, folder.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return File.Exists(_draggedPath) ||
               (Directory.Exists(_draggedPath) && _noteService.CanMoveFolderToFolder(_draggedPath, folder.FullPath));
    }

    private void CancelActiveDrag()
    {
        CompleteDragOrCancel();
    }

    private void CompleteDragOrCancel()
    {
        ForceCleanupDragState("CompleteDragOrCancel");
    }

    private void ForceCleanupDragState(string reason)
    {
        LogDragDebug("CleanupDragState requested", this);
        if (_isCleaningDrag)
        {
            LogDragDebug("CleanupDragState ignored", this, $"already cleaning | {reason}");
            return;
        }

        if (!_isDragging && !_isKeyboardDragging && !_isEnterHoldPending && !_isCompletingDrag)
        {
            LogDragDebug("CleanupDragState ignored", this, $"nothing active | {reason}");
            return;
        }

        _isCleaningDrag = true;
        _enterHoldTimer.Stop();
        _isEnterHoldPending = false;
        _isKeyboardDragging = false;
        _isDragging = false;
        _isCompletingDrag = false;
        _currentPlaceholderIndex = -1;
        _lastDragOverPath = null;
        _lastDragOverIndex = -1;
        StopDragBackHover();
        StopDragAutoScroll();
        HideDragPreview();
        ClearDragPlaceholder();
        ClearDropTargets();
        SetDraggedItemState(false);
        _draggedPath = null;
        _draggedKind = null;
        _draggedPaths.Clear();
        Mouse.Capture(null);
        _isChangingMouseCaptureInternally = false;
        _isCleaningDrag = false;

        var placeholderExists = Items.Contains(_dragPlaceholder);
        var placeholderCount = Items.Count(item => item is DragPlaceholderItem);
        var emptyPathCount = Items.Count(item => item is not DragPlaceholderItem && string.IsNullOrWhiteSpace(GetItemPath(item)));
        var realItemsCount = Items.Count(item => item is not DragPlaceholderItem);
        var orderIds = string.Join(" | ", Items
            .Where(item => item is not DragPlaceholderItem)
            .Take(10)
            .Select(item => GetItemPath(item) ?? item.GetType().Name));
        LogDragDebug("CleanupDragState", this, $"reason={reason}; ghost={DragPreviewLayer.Visibility}; placeholderExists={placeholderExists}; placeholderCount={placeholderCount}; emptyPathCount={emptyPathCount}; visualCount={Items.Count}; realCount={realItemsCount}; order10={orderIds}; captured={Mouse.Captured?.GetType().Name ?? "null"}");
        FocusItemsHost();
    }

    private void UpdateDragPlaceholder(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(_draggedPath))
        {
            return;
        }

        RemoveDuplicatePlaceholders();

        var normalizedTargetPath = targetPath ?? string.Empty;
        var draggedPaths = _draggedPaths.Count > 0 ? _draggedPaths : [_draggedPath];
        var orderedItems = Items
            .Where(item => !ReferenceEquals(item, _dragPlaceholder))
            .Where(item => !draggedPaths.Contains(GetItemPath(item) ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var targetIndex = orderedItems.Count;
        if (!string.IsNullOrWhiteSpace(targetPath))
        {
            if (draggedPaths.Contains(targetPath, StringComparer.OrdinalIgnoreCase))
            {
                LogDragDebug("ReorderIndex ignored", this, "target is dragged item");
                return;
            }

            var target = orderedItems.FirstOrDefault(item => string.Equals(GetItemPath(item), targetPath, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                LogDragDebug("ReorderIndex ignored", this, $"target not found: {targetPath}");
                return;
            }

            targetIndex = orderedItems.IndexOf(target);
        }

        var currentPlaceholderIndex = Items.IndexOf(_dragPlaceholder);
        targetIndex = Math.Clamp(targetIndex, 0, orderedItems.Count);
        if (string.Equals(_lastDragOverPath, normalizedTargetPath, StringComparison.OrdinalIgnoreCase) &&
            _lastDragOverIndex == targetIndex &&
            _currentPlaceholderIndex == targetIndex)
        {
            LogDragDebug("ReorderIndex unchanged", this, $"index={targetIndex}");
            return;
        }

        var now = DateTime.UtcNow;
        if ((now - _lastDragOverUpdateUtc).TotalMilliseconds < 16 &&
            _lastDragOverIndex == targetIndex)
        {
            LogDragDebug("ReorderIndex unchanged", this, $"throttled index={targetIndex}");
            return;
        }

        if (_currentPlaceholderIndex == targetIndex)
        {
            _currentPlaceholderIndex = targetIndex;
            _lastDragOverPath = normalizedTargetPath;
            _lastDragOverIndex = targetIndex;
            _lastDragOverUpdateUtc = now;
            LogDragDebug("ReorderIndex unchanged", this, $"placeholder already at visual index={targetIndex}");
            return;
        }

        if (currentPlaceholderIndex >= 0)
        {
            Items.RemoveAt(currentPlaceholderIndex);
        }

        var visualIndex = GetVisualInsertIndexForStableIndex(targetIndex);
        Items.Insert(visualIndex, _dragPlaceholder);
        LogDragDebug("ReorderIndex changed", this, $"old={_currentPlaceholderIndex} new={targetIndex} visual={visualIndex} reason={(string.IsNullOrWhiteSpace(targetPath) ? "empty-space-end" : "item-under-cursor")}");
        _currentPlaceholderIndex = targetIndex;
        _lastDragOverPath = normalizedTargetPath;
        _lastDragOverIndex = targetIndex;
        _lastDragOverUpdateUtc = now;
    }

    private void ClearDragPlaceholder()
    {
        var index = Items.IndexOf(_dragPlaceholder);
        if (index >= 0)
        {
            Items.RemoveAt(index);
        }

        _currentPlaceholderIndex = -1;
        _lastDragOverPath = null;
        _lastDragOverIndex = -1;
    }

    private void SetDraggedItemState(bool isDragging)
    {
        var paths = _draggedPaths.Count > 0 ? _draggedPaths : string.IsNullOrWhiteSpace(_draggedPath) ? [] : [_draggedPath];
        foreach (var path in paths)
        {
            var item = FindItemByPath(path);
            switch (item)
            {
                case NoteItem note:
                    note.IsDragging = isDragging;
                    break;
                case FolderItem folder:
                    folder.IsDragging = isDragging;
                    break;
            }
        }
    }

    private void UpdateSelectionFromClick(object item, ModifierKeys modifiers)
    {
        var path = GetItemPath(item);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            SelectRangeTo(path, modifiers.HasFlag(ModifierKeys.Control) || modifiers.HasFlag(ModifierKeys.Alt));
        }
        else if (modifiers.HasFlag(ModifierKeys.Control) || modifiers.HasFlag(ModifierKeys.Alt))
        {
            ToggleSelection(item);
            _selectionAnchorPath ??= path;
        }
        else
        {
            if (!(_selectedPaths.Count > 1 && _selectedPaths.Contains(path)))
            {
                ClearMultiSelection();
                AddToSelection(item);
            }

            _selectionAnchorPath = path;
        }

        SelectedItem = item;
        OnPropertyChanged(nameof(SummaryLineText));
    }

    private void SetPendingDrag(string path, Point position, ModifierKeys modifiers)
    {
        if (modifiers.HasFlag(ModifierKeys.Control) ||
            modifiers.HasFlag(ModifierKeys.Shift) ||
            modifiers.HasFlag(ModifierKeys.Alt))
        {
            _pendingDragPath = null;
            _suppressOpenAfterDrag = true;
            return;
        }

        _pendingDragPath = path;
        _pendingDragStartPoint = position;
        _pendingDragStartUtc = DateTime.UtcNow;
        _pendingDragModifiers = modifiers;
        _suppressOpenAfterDrag = false;
    }

    private bool ShouldStartDrag(string path, Point position)
    {
        if (!string.Equals(_pendingDragPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_pendingDragModifiers.HasFlag(ModifierKeys.Control) ||
            _pendingDragModifiers.HasFlag(ModifierKeys.Shift) ||
            _pendingDragModifiers.HasFlag(ModifierKeys.Alt))
        {
            return false;
        }

        if ((DateTime.UtcNow - _pendingDragStartUtc).TotalMilliseconds < 50)
        {
            return false;
        }

        var minX = SystemParameters.MinimumHorizontalDragDistance * 1.5;
        var minY = SystemParameters.MinimumVerticalDragDistance * 1.5;
        if (Math.Abs(position.X - _pendingDragStartPoint.X) < minX &&
            Math.Abs(position.Y - _pendingDragStartPoint.Y) < minY)
        {
            return false;
        }

        _suppressOpenAfterDrag = true;
        return true;
    }

    private void AddToSelection(object item)
    {
        var path = GetItemPath(item);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _selectedPaths.Add(path);
        SetSelectionState(item, true);
    }

    private void ToggleSelection(object item)
    {
        var path = GetItemPath(item);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (_selectedPaths.Remove(path))
        {
            SetSelectionState(item, false);
        }
        else
        {
            _selectedPaths.Add(path);
            SetSelectionState(item, true);
        }
    }

    private void SelectRangeTo(string targetPath, bool addToExisting)
    {
        if (!addToExisting)
        {
            ClearMultiSelection();
        }

        var anchor = _selectionAnchorPath;
        if (string.IsNullOrWhiteSpace(anchor) || FindItemByPath(anchor) is null)
        {
            anchor = GetItemPath(SelectedItem) ?? targetPath;
        }

        var start = Items.ToList().FindIndex(item => string.Equals(GetItemPath(item), anchor, StringComparison.OrdinalIgnoreCase));
        var end = Items.ToList().FindIndex(item => string.Equals(GetItemPath(item), targetPath, StringComparison.OrdinalIgnoreCase));
        if (start < 0 || end < 0)
        {
            return;
        }

        var min = Math.Min(start, end);
        var max = Math.Max(start, end);
        for (var index = min; index <= max; index++)
        {
            if (Items[index] is DragPlaceholderItem)
            {
                continue;
            }

            AddToSelection(Items[index]);
        }

        _selectionAnchorPath = anchor;
    }

    private void ClearMultiSelection()
    {
        foreach (var path in _selectedPaths.ToList())
        {
            SetSelectionState(FindItemByPath(path), false);
        }

        _selectedPaths.Clear();
        _selectionAnchorPath = null;
        OnPropertyChanged(nameof(SummaryLineText));
    }

    private IReadOnlyList<object> GetSelectedRealItems()
    {
        return Items
            .Where(item => item is not DragPlaceholderItem)
            .Where(item => _selectedPaths.Contains(GetItemPath(item) ?? string.Empty))
            .ToList();
    }

    private void ClearDropTargets()
    {
        foreach (var folder in Items.OfType<FolderItem>())
        {
            folder.IsDropTarget = false;
        }
    }

    private void SetOnlyDropTarget(FolderItem targetFolder)
    {
        foreach (var folder in Items.OfType<FolderItem>())
        {
            folder.IsDropTarget = ReferenceEquals(folder, targetFolder);
        }
    }

    private async Task ShowNotePreviewAsync(NoteItem note, Point position)
    {
        _isFolderPreview = false;
        _previewTiles.Clear();
        PreviewTitle = note.Title;
        PreviewMeta = $"Заметка · {note.FullPath}{Environment.NewLine}Изменено: {note.DisplayModified} · Размер: {note.DisplaySize}";
        PreviewBody = await BuildNotePreviewBodyAsync(note.FullPath);
        OnPropertyChanged(nameof(FolderPreviewVisibility));
        OnPropertyChanged(nameof(NotePreviewVisibility));
        ShowPreviewAt(position);
    }

    private void ShowFolderPreview(FolderItem folder, Point position)
    {
        _isFolderPreview = true;
        _previewTiles.Clear();
        foreach (var tile in BuildFolderPreviewTiles(folder.FullPath))
        {
            _previewTiles.Add(tile);
        }

        PreviewTitle = folder.Name;
        PreviewMeta = $"{folder.FullPath}{Environment.NewLine}Изменено: {folder.DisplayModified} · {folder.DisplayItemCount}";
        PreviewBody = string.Empty;
        OnPropertyChanged(nameof(FolderPreviewVisibility));
        OnPropertyChanged(nameof(NotePreviewVisibility));
        ShowPreviewAt(position);
    }

    private void ShowPreviewAt(Point position)
    {
        PreviewPopupVisibility = Visibility.Visible;
        PreviewPopup.UpdateLayout();
        const double padding = 24;
        var popupWidth = PreviewPopup.ActualWidth > 0 ? PreviewPopup.ActualWidth : PreviewPopup.Width;
        var popupHeight = PreviewPopup.ActualHeight > 0 ? PreviewPopup.ActualHeight : PreviewPopup.MaxHeight;
        var maxX = Math.Max(padding, RootGrid.ActualWidth - popupWidth - padding);
        var maxY = Math.Max(padding, RootGrid.ActualHeight - popupHeight - padding);
        var x = Math.Clamp(position.X + 18, padding, maxX);
        var y = Math.Clamp(position.Y + 18, padding, maxY);
        Canvas.SetLeft(PreviewPopup, Math.Round(x));
        Canvas.SetTop(PreviewPopup, Math.Round(y));
    }

    private void HidePreviewPopup()
    {
        PreviewPopupVisibility = Visibility.Collapsed;
    }

    private static async Task<string> BuildNotePreviewBodyAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return "Файл не найден.";
            }

            var lines = new List<string>();
            using var reader = new StreamReader(path);
            while (lines.Count < 20 && await reader.ReadLineAsync() is { } line)
            {
                lines.Add(line);
            }

            return lines.Count == 0 ? "Заметка пустая." : string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            return $"Не удалось прочитать preview: {ex.Message}";
        }
    }

    private static IReadOnlyList<PreviewTile> BuildFolderPreviewTiles(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return [new PreviewTile("!", "Папка не найдена", path)];
            }

            var entries = Directory.EnumerateFileSystemEntries(path)
                .Where(entry => !Path.GetFileName(entry).StartsWith(".", StringComparison.Ordinal))
                .Take(6)
                .ToList();
            if (entries.Count == 0)
            {
                return [new PreviewTile("EMPTY", "Папка пустая", "Нет элементов")];
            }

            return entries.Select(entry =>
            {
                var isDirectory = Directory.Exists(entry);
                var name = Path.GetFileName(entry);
                var subtitle = isDirectory ? "Папка" : FormatBytes(new FileInfo(entry).Length);
                return new PreviewTile(isDirectory ? "DIR" : "TXT", name, subtitle);
            }).ToList();
        }
        catch (Exception ex)
        {
            return [new PreviewTile("ERR", "Ошибка preview", ex.Message)];
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["Б", "КБ", "МБ", "ГБ"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }

    private void RemoveDuplicatePlaceholders()
    {
        var found = false;
        for (var index = Items.Count - 1; index >= 0; index--)
        {
            if (!ReferenceEquals(Items[index], _dragPlaceholder))
            {
                continue;
            }

            if (!found)
            {
                found = true;
                continue;
            }

            Items.RemoveAt(index);
        }
    }

    private static bool HasOverlayDragData(DragEventArgs e)
    {
        return e.Data.GetDataPresent(NotePathDragFormat) || e.Data.GetDataPresent(FolderPathDragFormat);
    }

    private async Task ReorderDraggedItemAsync(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(_draggedPath))
        {
            return;
        }

        if (_draggedPaths.Count > 1)
        {
            await ReorderDraggedGroupAsync();
            return;
        }

        var dragged = FindItemByPath(_draggedPath);
        if (dragged is null)
        {
            NoteOperationResult moveResult;
            if (File.Exists(_draggedPath))
            {
                moveResult = await _noteService.MoveNoteToFolderAsync(_draggedPath, _currentFolderPath);
            }
            else if (Directory.Exists(_draggedPath))
            {
                moveResult = await _noteService.MoveFolderToFolderAsync(_draggedPath, _currentFolderPath);
            }
            else
            {
                return;
            }

            if (!moveResult.Success)
            {
                ShowStatus(moveResult.Message, true);
            }

            await LoadItemsAsync();
            return;
        }

        var newIndex = _currentPlaceholderIndex;
        if (!string.IsNullOrWhiteSpace(targetPath))
        {
            var target = FindItemByPath(targetPath);
            if (target is null || ReferenceEquals(target, dragged))
            {
                return;
            }

            var stableTargetList = Items
                .Where(item => !ReferenceEquals(item, _dragPlaceholder))
                .Where(item => !ReferenceEquals(item, dragged))
                .ToList();
            newIndex = stableTargetList.IndexOf(target);
        }

        var oldOrder = Items.Where(item => item is not DragPlaceholderItem).ToList();
        var oldIndex = oldOrder.IndexOf(dragged);
        var placeholderIndexForLog = _currentPlaceholderIndex;
        ClearDragPlaceholder();
        if (oldIndex < 0)
        {
            return;
        }

        var stableItems = Items.Where(item => !ReferenceEquals(item, dragged)).ToList();
        newIndex = newIndex < 0 ? stableItems.Count : Math.Clamp(newIndex, 0, stableItems.Count);
        stableItems.Insert(newIndex, dragged);
        Items.Clear();
        foreach (var item in stableItems)
        {
            Items.Add(item);
        }

        SelectedItem = dragged;
        LogReorderResult("single", [dragged], [oldIndex], newIndex, placeholderIndexForLog);

        EnsureNoPlaceholderItems("single reorder before save");
        var result = await _noteService.SaveFolderOrderAsync(_currentFolderPath, GetRealOrderPaths());
        if (!result.Success)
        {
            ShowStatus(result.Message, true);
        }
        FocusItemsHost();
    }

    private async Task ReorderDraggedGroupAsync()
    {
        var group = Items
            .Where(item => _draggedPaths.Contains(GetItemPath(item) ?? string.Empty))
            .ToList();
        if (group.Count == 0)
        {
            return;
        }

        var placeholderIndex = _currentPlaceholderIndex;
        var oldOrder = Items.Where(item => item is not DragPlaceholderItem).ToList();
        var oldIndices = group.Select(item => oldOrder.IndexOf(item)).ToList();
        ClearDragPlaceholder();

        var stableItems = Items.Where(item => !group.Contains(item)).ToList();
        var insertIndex = placeholderIndex < 0 ? stableItems.Count : Math.Clamp(placeholderIndex, 0, stableItems.Count);
        Items.Clear();
        foreach (var item in stableItems)
        {
            Items.Add(item);
        }

        for (var index = 0; index < group.Count; index++)
        {
            Items.Insert(Math.Min(insertIndex + index, Items.Count), group[index]);
        }

        foreach (var item in group)
        {
            SetSelectionState(item, true);
        }

        LogReorderResult("group", group, oldIndices, insertIndex, placeholderIndex);

        EnsureNoPlaceholderItems("group reorder before save");
        var result = await _noteService.SaveFolderOrderAsync(_currentFolderPath, GetRealOrderPaths());
        if (!result.Success)
        {
            ShowStatus(result.Message, true);
        }

        SelectedItem = group.FirstOrDefault();
        FocusItemsHost();
    }

    private void LogReorderResult(string mode, IReadOnlyList<object> draggedItems, IReadOnlyList<int> oldIndices, int targetIndex, int placeholderIndex)
    {
        var draggedNames = string.Join(", ", draggedItems.Select(item => item switch
        {
            NoteItem note => note.Title,
            FolderItem folder => folder.Name,
            _ => GetItemPath(item) ?? "?"
        }));
        var finalOrder = string.Join(" | ", Items
            .Where(item => item is not DragPlaceholderItem)
            .Take(10)
            .Select(item => item switch
            {
                NoteItem note => note.Title,
                FolderItem folder => folder.Name,
                SearchResultItem result => result.Title,
                _ => GetItemPath(item) ?? "?"
            }));
        var placeholderCount = Items.Count(item => item is DragPlaceholderItem);
        var emptyPathCount = Items.Count(item => item is not DragPlaceholderItem && string.IsNullOrWhiteSpace(GetItemPath(item)));
        LogDragDebug("ReorderCommit", this, $"mode={mode}; dragged={draggedNames}; old=[{string.Join(",", oldIndices)}]; placeholder={placeholderIndex}; placeholderExists={placeholderCount > 0}; placeholderCount={placeholderCount}; emptyPathCount={emptyPathCount}; targetStable={targetIndex}; visualCount={Items.Count}; realCount={Items.Count(item => item is not DragPlaceholderItem)}; final10={finalOrder}");
    }

    private IReadOnlyList<string> GetRealOrderPaths()
    {
        return Items
            .Where(item => item is NoteItem or FolderItem)
            .Select(GetItemPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToList();
    }

    private void EnsureNoPlaceholderItems(string reason)
    {
        var placeholderCount = Items.Count(item => item is DragPlaceholderItem);
        if (placeholderCount == 0)
        {
            return;
        }

        RemoveDuplicatePlaceholders();
        LogDragDebug("Placeholder removed before save", this, $"{reason}; removed={placeholderCount}");
    }

    private void MoveSelection(Key key, ModifierKeys modifiers = ModifierKeys.None)
    {
        if (Items.Count == 0)
        {
            SelectedItem = null;
            return;
        }

        var currentIndex = SelectedItem is null ? -1 : Items.IndexOf(SelectedItem);
        if (currentIndex < 0)
        {
            if (modifiers.HasFlag(ModifierKeys.Control) || modifiers.HasFlag(ModifierKeys.Alt))
            {
                FocusItemAt(0);
            }
            else
            {
                SelectItemAt(0);
            }

            return;
        }

        var columns = Math.Max(1, (int)Math.Floor(Math.Max(ItemsListBox.ActualWidth, CardStepWidth) / CardStepWidth));
        var targetIndex = key switch
        {
            Key.Left => Math.Max(0, currentIndex - 1),
            Key.Right => Math.Min(Items.Count - 1, currentIndex + 1),
            Key.Up => currentIndex - columns >= 0 ? currentIndex - columns : currentIndex,
            Key.Down => currentIndex + columns < Items.Count ? currentIndex + columns : currentIndex,
            _ => currentIndex
        };

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            var targetPath = GetItemPath(Items[targetIndex]);
            if (!string.IsNullOrWhiteSpace(targetPath))
            {
                _selectionAnchorPath ??= GetItemPath(SelectedItem) ?? targetPath;
                SelectRangeTo(targetPath, modifiers.HasFlag(ModifierKeys.Control) || modifiers.HasFlag(ModifierKeys.Alt));
                SelectedItem = Items[targetIndex];
                ItemsListBox.ScrollIntoView(SelectedItem);
                FocusItemsHost();
            }

            return;
        }

        if (modifiers.HasFlag(ModifierKeys.Control) || modifiers.HasFlag(ModifierKeys.Alt))
        {
            SelectedItem = Items[targetIndex];
            ItemsListBox.ScrollIntoView(SelectedItem);
            FocusItemsHost();
            return;
        }

        ClearMultiSelection();
        SelectItemAt(targetIndex);
    }

    private void FocusItemAt(int index)
    {
        if (index < 0 || index >= Items.Count)
        {
            return;
        }

        SelectedItem = Items[index];
        ItemsListBox.ScrollIntoView(SelectedItem);
        FocusItemsHost();
    }

    private void SelectItemAt(int index)
    {
        if (index < 0 || index >= Items.Count)
        {
            return;
        }

        ClearMultiSelection();
        AddToSelection(Items[index]);
        SelectedItem = Items[index];
        _selectionAnchorPath = GetItemPath(Items[index]);
        ItemsListBox.ScrollIntoView(SelectedItem);
        FocusItemsHost();
    }

    private void MoveKeyboardDragPlaceholder(Key key)
    {
        var placeholderIndex = Items.IndexOf(_dragPlaceholder);
        if (placeholderIndex < 0)
        {
            UpdateDragPlaceholder(GetItemPath(SelectedItem));
            placeholderIndex = Items.IndexOf(_dragPlaceholder);
        }

        if (placeholderIndex < 0)
        {
            return;
        }

        var columns = Math.Max(1, (int)Math.Floor(Math.Max(ItemsListBox.ActualWidth, CardStepWidth) / CardStepWidth));
        var targetIndex = key switch
        {
            Key.Left => Math.Max(0, placeholderIndex - 1),
            Key.Right => Math.Min(Items.Count - 1, placeholderIndex + 1),
            Key.Up => Math.Max(0, placeholderIndex - columns),
            Key.Down => Math.Min(Items.Count - 1, placeholderIndex + columns),
            _ => placeholderIndex
        };

        if (targetIndex != placeholderIndex)
        {
            Items.Move(placeholderIndex, targetIndex);
            ItemsListBox.ScrollIntoView(_dragPlaceholder);
        }
    }

    private void EnterHoldTimer_OnTick(object? sender, EventArgs e)
    {
        _enterHoldTimer.Stop();
        if (!_isEnterHoldPending || SelectedItem is null || EditorVisibility == Visibility.Visible)
        {
            return;
        }

        _isEnterHoldPending = false;
        _isKeyboardDragging = true;
        _isDragging = true;
        _draggedPath = GetItemPath(SelectedItem);
        _draggedKind = SelectedItem is FolderItem ? "Папка" : "Заметка";
        _draggedPaths.Clear();
        if (!string.IsNullOrWhiteSpace(_draggedPath))
        {
            if (_selectedPaths.Count > 1 && _selectedPaths.Contains(_draggedPath))
            {
                _draggedPaths.AddRange(_selectedPaths);
            }
            else
            {
                _draggedPaths.Add(_draggedPath);
            }
        }

        SetDraggedItemState(true);
        UpdateDragPlaceholder(_draggedPath);
        ShowStatus("Перемещение: стрелки — выбрать место, Enter — отпустить, Esc — отмена.", false);
    }

    private void UpdateDragAutoScroll(Point position)
    {
        var scrollViewer = GetItemsScrollViewer();
        if (!_isDragging || scrollViewer is null || scrollViewer.ActualHeight <= 0)
        {
            StopDragAutoScroll();
            return;
        }

        const double edge = 80;
        _dragAutoScrollVelocity = position.Y switch
        {
            < 0 => -24,
            var y when y < edge => -Math.Max(3, (edge - y) / edge * 22),
            var y when y > scrollViewer.ActualHeight => 24,
            var y when y > scrollViewer.ActualHeight - edge => Math.Max(3, (y - (scrollViewer.ActualHeight - edge)) / edge * 22),
            _ => 0
        };

        if (Math.Abs(_dragAutoScrollVelocity) > 0.1)
        {
            if (!_dragAutoScrollTimer.IsEnabled)
            {
                _dragAutoScrollTimer.Start();
            }
        }
        else
        {
            StopDragAutoScroll();
        }
    }

    private void DragAutoScrollTimer_OnTick(object? sender, EventArgs e)
    {
        var scrollViewer = GetItemsScrollViewer();
        if (!_isDragging || scrollViewer is null || Math.Abs(_dragAutoScrollVelocity) < 0.1)
        {
            StopDragAutoScroll();
            return;
        }

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + _dragAutoScrollVelocity);
    }

    private void StopDragAutoScroll()
    {
        _dragAutoScrollVelocity = 0;
        _dragAutoScrollTimer.Stop();
    }

    private void FocusItemsHost()
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            ItemsListBox.Focus();
            if (SelectedItem is not null)
            {
                ItemsListBox.ScrollIntoView(SelectedItem);
            }
        }, DispatcherPriority.Input);
    }

    private object? FindItemByPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Items.FirstOrDefault(item => string.Equals(GetItemPath(item), path, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetItemPath(object? item)
    {
        return item switch
        {
            NoteItem note => note.FullPath,
            FolderItem folder => folder.FullPath,
            SearchResultItem result => result.FullPath,
            _ => null
        };
    }

    private static void SetSelectionState(object? item, bool isSelected)
    {
        switch (item)
        {
            case NoteItem note:
                note.IsSelected = isSelected;
                break;
            case FolderItem folder:
                folder.IsSelected = isSelected;
                break;
            case SearchResultItem result:
                result.IsSelected = isSelected;
                break;
        }
    }

    private static void SetFocusState(object? item, bool isFocused)
    {
        switch (item)
        {
            case NoteItem note:
                note.IsFocused = isFocused;
                break;
            case FolderItem folder:
                folder.IsFocused = isFocused;
                break;
            case SearchResultItem result:
                result.IsFocused = isFocused;
                break;
        }
    }

    private async Task OpenSelectedItemAsync()
    {
        switch (SelectedItem)
        {
            case NoteItem note:
                await OpenNoteAsync(note.FullPath);
                break;
            case FolderItem folder:
                await OpenFolderAsync(folder.FullPath);
                break;
            case SearchResultItem result when result.IsFolder:
                await OpenFolderAsync(result.FullPath);
                break;
            case SearchResultItem result:
                await OpenNoteAsync(result.FullPath, result.MatchLineNumber, result.MatchColumn);
                break;
        }
    }

    private async void SearchResultCard_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { DataContext: SearchResultItem result } || IsInsideButton(e.OriginalSource))
        {
            return;
        }

        SelectedItem = result;
        await OpenSelectedItemAsync();
        e.Handled = true;
    }

    private void OpenGlobalSearch()
    {
        _isGlobalSearchOpen = true;
        OnPropertyChanged(nameof(GlobalSearchVisibility));
        _ = Dispatcher.BeginInvoke(() =>
        {
            GlobalSearchTextBox.Focus();
            GlobalSearchTextBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private async void GlobalSearchCloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        LogDragDebug("Button clicked: SearchClose", sender);
        await CloseGlobalSearch();
    }

    private void FileSearchCloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        LogDragDebug("Button clicked: SearchClose", sender);
        CloseFileSearch();
    }

    private void FileSearchTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = GetEffectiveKey(e);
        if (key == Key.Enter)
        {
            MoveFileSearchMatch(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            e.Handled = true;
        }
        else if (key == Key.Escape)
        {
            CloseFileSearch();
            e.Handled = true;
        }
    }

    private async Task CloseGlobalSearch()
    {
        _globalSearchCts?.Cancel();
        _globalSearchDebounceTimer.Stop();
        _isGlobalSearchOpen = false;
        _globalSearchQuery = string.Empty;
        OnPropertyChanged(nameof(GlobalSearchQuery));
        OnPropertyChanged(nameof(GlobalSearchVisibility));
        await LoadItemsAsync();
    }

    private void RestartGlobalSearchDebounce(bool immediate = false)
    {
        if (!_isGlobalSearchOpen)
        {
            return;
        }

        _globalSearchDebounceTimer.Stop();
        if (immediate)
        {
            _ = RunGlobalSearchAsync(GlobalSearchQuery);
            return;
        }

        _globalSearchDebounceTimer.Start();
    }

    private async void GlobalSearchDebounceTimer_OnTick(object? sender, EventArgs e)
    {
        _globalSearchDebounceTimer.Stop();
        await RunGlobalSearchAsync(GlobalSearchQuery);
    }

    private async Task RunGlobalSearchAsync(string query)
    {
        _globalSearchCts?.Cancel();
        if (string.IsNullOrWhiteSpace(query))
        {
            await LoadItemsAsync();
            return;
        }

        var cts = new CancellationTokenSource();
        _globalSearchCts = cts;
        var root = _noteService.NotesFolderPath;
        var results = await Task.Run(() => SearchNotes(root, query.Trim(), cts.Token), cts.Token)
            .ContinueWith(task => task.IsCompletedSuccessfully ? task.Result : [], TaskScheduler.Default);
        if (cts.IsCancellationRequested || !ReferenceEquals(_globalSearchCts, cts))
        {
            return;
        }

        Items.Clear();
        ClearMultiSelection();
        foreach (var result in results)
        {
            Items.Add(result);
        }

        _folderCount = results.Count(item => item.IsFolder);
        _noteCount = results.Count(item => !item.IsFolder);
        SelectedItem = Items.FirstOrDefault();
        if (SelectedItem is not null)
        {
            AddToSelection(SelectedItem);
        }

        OnPropertyChanged(nameof(TotalItemsText));
        OnPropertyChanged(nameof(FolderCountText));
        OnPropertyChanged(nameof(NoteCountText));
        OnPropertyChanged(nameof(SummaryLineText));
        ShowStatus(results.Count == 0 ? "Ничего не найдено." : string.Empty, false);
    }

    private List<SearchResultItem> SearchNotes(string root, string query, CancellationToken token)
    {
        var results = new List<SearchResultItem>();
        if (!Directory.Exists(root))
        {
            return results;
        }

        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Prepend(root))
        {
            token.ThrowIfCancellationRequested();
            if (Path.GetFileName(directory).StartsWith(".", StringComparison.Ordinal))
            {
                continue;
            }

            var name = string.Equals(directory, root, StringComparison.OrdinalIgnoreCase)
                ? "Корень заметок"
                : Path.GetFileName(directory);
            if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new SearchResultItem
                {
                    Title = name,
                    FullPath = directory,
                    IsFolder = true,
                    RelativeFolder = _noteService.GetRelativePath(directory),
                    Snippet = "Совпадение в названии папки"
                });
            }
        }

        foreach (var file in Directory.EnumerateFiles(root, "*.txt", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var title = Path.GetFileNameWithoutExtension(file);
            var relativeFolder = _noteService.GetRelativePath(Path.GetDirectoryName(file) ?? root);
            if (title.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new SearchResultItem
                {
                    Title = title,
                    FullPath = file,
                    IsFolder = false,
                    RelativeFolder = string.IsNullOrWhiteSpace(relativeFolder) ? "Корень заметок" : relativeFolder,
                    Snippet = "Совпадение в названии"
                });
                continue;
            }

            var contentMatch = FindContentMatch(file, query, token);
            if (contentMatch is not null)
            {
                results.Add(new SearchResultItem
                {
                    Title = title,
                    FullPath = file,
                    IsFolder = false,
                    RelativeFolder = string.IsNullOrWhiteSpace(relativeFolder) ? "Корень заметок" : relativeFolder,
                    Snippet = contentMatch.Value.Snippet,
                    MatchLineNumber = contentMatch.Value.Line,
                    MatchColumn = contentMatch.Value.Column
                });
            }
        }

        return results.Take(200).ToList();
    }

    private static (int Line, int Column, string Snippet)? FindContentMatch(string file, string query, CancellationToken token)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var lineNumber = 0;
            long readChars = 0;
            while (!reader.EndOfStream && readChars < 512 * 1024)
            {
                token.ThrowIfCancellationRequested();
                var line = reader.ReadLine() ?? string.Empty;
                lineNumber++;
                readChars += line.Length;
                var column = line.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                if (column >= 0)
                {
                    var start = Math.Max(0, column - 48);
                    var length = Math.Min(line.Length - start, query.Length + 96);
                    return (lineNumber, column, line.Substring(start, length).Trim());
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private void OpenFileSearch()
    {
        FileSearchVisibility = Visibility.Visible;
        FileSearchHighlightBox.Visibility = Visibility.Visible;
        if (string.IsNullOrWhiteSpace(FileSearchQuery))
        {
            FileSearchStatus = "0 / 0";
        }

        RefreshFileSearchHighlight();
        _ = Dispatcher.BeginInvoke(() =>
        {
            FileSearchTextBox.Focus();
            FileSearchTextBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void CloseFileSearch()
    {
        FileSearchVisibility = Visibility.Collapsed;
        FileSearchHighlightBox.Visibility = Visibility.Collapsed;
        FileSearchQuery = string.Empty;
        FileSearchStatus = string.Empty;
        _fileSearchMatches.Clear();
        _fileSearchMatchIndex = -1;
        FileSearchHighlightBox.Document.Blocks.Clear();
        EditorTextBox.Focus();
    }

    private void UpdateFileSearchMatches()
    {
        var keepSearchFocus = FileSearchTextBox?.IsKeyboardFocusWithin == true;
        _fileSearchMatches.Clear();
        _fileSearchMatchIndex = -1;
        if (string.IsNullOrWhiteSpace(FileSearchQuery))
        {
            FileSearchStatus = "0 / 0";
            RefreshFileSearchHighlight();
            RestoreFileSearchFocusIfNeeded(keepSearchFocus);
            return;
        }

        var index = 0;
        while (index < EditorText.Length)
        {
            var found = EditorText.IndexOf(FileSearchQuery, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                break;
            }

            _fileSearchMatches.Add((found, FileSearchQuery.Length));
            index = found + Math.Max(1, FileSearchQuery.Length);
        }

        FileSearchStatus = _fileSearchMatches.Count == 0 ? "0 совпадений" : $"0 / {_fileSearchMatches.Count}";
        RefreshFileSearchHighlight();
        RestoreFileSearchFocusIfNeeded(keepSearchFocus);
    }

    private void MoveFileSearchMatch(int direction)
    {
        if (_fileSearchMatches.Count == 0)
        {
            return;
        }

        _fileSearchMatchIndex = (_fileSearchMatchIndex + direction + _fileSearchMatches.Count) % _fileSearchMatches.Count;
        var match = _fileSearchMatches[_fileSearchMatchIndex];
        EditorTextBox.Focus();
        EditorTextBox.Select(match.Index, match.Length);
        EditorTextBox.ScrollToLine(EditorTextBox.GetLineIndexFromCharacterIndex(match.Index));
        FileSearchStatus = $"{_fileSearchMatchIndex + 1} / {_fileSearchMatches.Count}";
        RefreshFileSearchHighlight();
        _ = Dispatcher.BeginInvoke(() =>
        {
            FileSearchTextBox.Focus();
            FileSearchTextBox.Select(FileSearchTextBox.Text.Length, 0);
        }, DispatcherPriority.Input);
    }

    private void RestoreFileSearchFocusIfNeeded(bool shouldRestore)
    {
        if (!shouldRestore)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(() =>
        {
            FileSearchTextBox.Focus();
            FileSearchTextBox.Select(FileSearchTextBox.Text.Length, 0);
        }, DispatcherPriority.Input);
    }

    private void RefreshFileSearchHighlight()
    {
        if (FileSearchHighlightBox is null)
        {
            return;
        }

        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = EditorTextBox.FontFamily,
            FontSize = EditorTextBox.FontSize,
            Foreground = EditorTextBox.Foreground
        };
        var paragraph = new Paragraph { Margin = new Thickness(0) };

        if (_fileSearchMatches.Count == 0)
        {
            paragraph.Inlines.Add(new Run(EditorText));
        }
        else
        {
            var cursor = 0;
            for (var index = 0; index < _fileSearchMatches.Count; index++)
            {
                var match = _fileSearchMatches[index];
                if (match.Index > cursor)
                {
                    paragraph.Inlines.Add(new Run(EditorText[cursor..match.Index]));
                }

                var run = new Run(EditorText.Substring(match.Index, match.Length))
                {
                    Background = index == _fileSearchMatchIndex
                        ? Brushes.LightGreen
                        : Brushes.Gold,
                    Foreground = Brushes.Black,
                    FontWeight = index == _fileSearchMatchIndex ? FontWeights.SemiBold : FontWeights.Normal
                };
                paragraph.Inlines.Add(run);
                cursor = match.Index + match.Length;
            }

            if (cursor < EditorText.Length)
            {
                paragraph.Inlines.Add(new Run(EditorText[cursor..]));
            }
        }

        document.Blocks.Add(paragraph);
        FileSearchHighlightBox.Document = document;
        FileSearchHighlightBox.ScrollToVerticalOffset(EditorTextBox.VerticalOffset);
    }

    private void MoveEditorCaretToMatch(int lineNumber, int column, int length)
    {
        var lineIndex = Math.Clamp(lineNumber - 1, 0, Math.Max(0, EditorTextBox.LineCount - 1));
        var charIndex = EditorTextBox.GetCharacterIndexFromLineIndex(lineIndex) + Math.Max(0, column);
        charIndex = Math.Clamp(charIndex, 0, EditorTextBox.Text.Length);
        EditorTextBox.Select(charIndex, Math.Min(Math.Max(0, length), EditorTextBox.Text.Length - charIndex));
        EditorTextBox.ScrollToLine(lineIndex);
    }

    private async Task DeleteSelectedItemAsync()
    {
        switch (SelectedItem)
        {
            case NoteItem note:
                await MoveNoteToTrashWithConfirmationAsync(note.FullPath);
                break;
            case FolderItem folder:
                await DeleteFolderAsync(folder);
                break;
        }
    }

    private Task<string?> ShowPromptAsync(string title, string hint, string confirmText, string initialValue = "")
    {
        HideOverlayContextMenu();
        _promptCompletion = new TaskCompletionSource<string?>();
        PromptTitleText.Text = title;
        PromptHintText.Text = hint;
        PromptConfirmButton.Content = confirmText;
        PromptTextBox.Text = initialValue;
        PromptOverlay.Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(() =>
        {
            PromptTextBox.Focus();
            PromptTextBox.SelectAll();
        }, DispatcherPriority.Input);
        return _promptCompletion.Task;
    }

    private void PromptConfirmButton_OnClick(object sender, RoutedEventArgs e)
    {
        CompletePrompt(PromptTextBox.Text);
    }

    private void PromptCancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        CompletePrompt(null);
    }

    private void PromptTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CompletePrompt(PromptTextBox.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CompletePrompt(null);
            e.Handled = true;
        }
    }

    private void CompletePrompt(string? value)
    {
        if (_promptCompletion is null)
        {
            return;
        }

        PromptOverlay.Visibility = Visibility.Collapsed;
        _promptCompletion.TrySetResult(value);
        _promptCompletion = null;
        FocusItemsHost();
    }

    private async Task<FolderItem?> ShowFolderChoiceAsync(string title, IReadOnlyList<FolderItem> folders)
    {
        if (folders.Count == 0)
        {
            await ShowErrorAsync(title, "Нет доступных папок для перемещения.");
            return null;
        }

        HideOverlayContextMenu();
        HidePreviewPopup();
        _folderChoices.Clear();
        foreach (var folder in folders)
        {
            _folderChoices.Add(new FolderChoiceItem(folder));
        }

        _folderChoiceStartIndex = 0;
        _selectedFolderChoice = _folderChoices.FirstOrDefault();
        RefreshFolderChoiceTiles();

        _folderPickerCompletion = new TaskCompletionSource<FolderItem?>();
        FolderPickerOverlay.Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(() => FolderPickerConfirmButton.Focus(), DispatcherPriority.Input);
        return await _folderPickerCompletion.Task;
    }

    private void RefreshFolderChoiceTiles()
    {
        _visibleFolderChoices.Clear();
        _folderChoiceStartIndex = Math.Clamp(_folderChoiceStartIndex, 0, Math.Max(0, _folderChoices.Count - 1));

        foreach (var choice in _folderChoices.Skip(_folderChoiceStartIndex).Take(5))
        {
            choice.IsSelected = ReferenceEquals(choice, _selectedFolderChoice);
            choice.SelectionBrush = SelectionOutlineBrush;
            _visibleFolderChoices.Add(choice);
        }

        foreach (var choice in _folderChoices.Except(_visibleFolderChoices))
        {
            choice.IsSelected = ReferenceEquals(choice, _selectedFolderChoice);
            choice.SelectionBrush = SelectionOutlineBrush;
        }

        FolderPickerPrevButton.Visibility = _folderChoices.Count > 5 ? Visibility.Visible : Visibility.Hidden;
        FolderPickerNextButton.Visibility = _folderChoices.Count > 5 ? Visibility.Visible : Visibility.Hidden;
        FolderPickerPrevButton.IsEnabled = _folderChoiceStartIndex > 0;
        FolderPickerNextButton.IsEnabled = _folderChoiceStartIndex + 5 < _folderChoices.Count;
        FolderPickerConfirmButton.IsEnabled = _selectedFolderChoice is not null;
    }

    private void MoveFolderChoiceSelection(int delta)
    {
        if (_folderChoices.Count == 0)
        {
            return;
        }

        var index = _selectedFolderChoice is null ? 0 : _folderChoices.IndexOf(_selectedFolderChoice);
        index = Math.Clamp(index + delta, 0, _folderChoices.Count - 1);
        _selectedFolderChoice = _folderChoices[index];
        if (index < _folderChoiceStartIndex)
        {
            _folderChoiceStartIndex = index;
        }
        else if (index >= _folderChoiceStartIndex + 5)
        {
            _folderChoiceStartIndex = Math.Max(0, index - 4);
        }

        RefreshFolderChoiceTiles();
    }

    private void ShiftFolderChoicePage(int delta)
    {
        if (_folderChoices.Count <= 5)
        {
            return;
        }

        _folderChoiceStartIndex = Math.Clamp(_folderChoiceStartIndex + delta, 0, Math.Max(0, _folderChoices.Count - 5));
        _selectedFolderChoice = _folderChoices[Math.Clamp(_folderChoiceStartIndex, 0, _folderChoices.Count - 1)];
        RefreshFolderChoiceTiles();
    }

    private void CompleteFolderPicker(FolderItem? folder)
    {
        if (_folderPickerCompletion is null)
        {
            return;
        }

        FolderPickerOverlay.Visibility = Visibility.Collapsed;
        _folderPickerCompletion.TrySetResult(folder);
        _folderPickerCompletion = null;
        FocusItemsHost();
    }

    private void ConfirmFolderPicker()
    {
        CompleteFolderPicker(_selectedFolderChoice?.Folder);
    }

    private void BuildSettingsOnboardingControls()
    {
        if (SettingsMainPanel.Content is not StackPanel panel)
        {
            return;
        }

        _onboardingIntroText = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 16),
            Foreground = GetBrush("SecondaryTextBrush"),
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };

        _settingsLanguageComboBox = new ComboBox
        {
            Margin = new Thickness(0, 8, 0, 16),
            Padding = new Thickness(10, 7, 10, 7),
            Background = new SolidColorBrush(Color.FromRgb(32, 32, 40)),
            Foreground = GetBrush("PrimaryTextBrush")
        };
        _settingsLanguageComboBox.Items.Add(new ComboBoxItem { Content = "Русский", Tag = UiText.Ru });
        _settingsLanguageComboBox.Items.Add(new ComboBoxItem { Content = "English", Tag = UiText.En });
        _settingsLanguageComboBox.SelectionChanged += SettingsLanguageComboBox_OnSelectionChanged;

        var folderButtons = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        folderButtons.Children.Add(CreateSettingsActionButton(UiText.T(_settings.Language, "ChooseFolder"), SettingsChooseFolderButton_OnClick, 150));
        folderButtons.Children.Add(CreateSettingsActionButton(UiText.T(_settings.Language, "CreateDefaultFolder"), SettingsCreateDefaultFolderButton_OnClick, 220));
        folderButtons.Children.Add(CreateSettingsActionButton(UiText.T(_settings.Language, "FindAutomatically"), SettingsFindFolderButton_OnClick, 180));

        _settingsToggleHotkeyErrorText = CreateSettingsErrorText();
        _settingsNewNoteHotkeyErrorText = CreateSettingsErrorText();
        _settingsCheckHotkeysButton = CreateSettingsActionButton(UiText.T(_settings.Language, "CheckHotkeys"), SettingsCheckHotkeysButton_OnClick, 210);
        _settingsCheckHotkeysButton.Margin = new Thickness(0, 0, 0, 18);
        _settingsCloseAppButton = CreateSettingsActionButton(UiText.T(_settings.Language, "CloseApp"), CloseAppButton_OnClick, 180);
        _settingsCloseAppButton.Margin = new Thickness(0, 0, 0, 18);
        _settingsCloseAppButton.Style = (Style)Application.Current.Resources["DangerButtonStyle"];

        panel.Children.Insert(0, _onboardingIntroText);
        panel.Children.Insert(1, CreateSettingsLabel(() => UiText.T(_settings.Language, "Language")));
        panel.Children.Insert(2, _settingsLanguageComboBox);
        panel.Children.Insert(3, _settingsCloseAppButton);

        var notesDirectoryIndex = panel.Children.IndexOf(SettingsNotesDirectoryTextBox);
        if (notesDirectoryIndex >= 0)
        {
            panel.Children.Insert(notesDirectoryIndex + 1, folderButtons);
        }

        var toggleHotkeyIndex = panel.Children.IndexOf(SettingsToggleHotkeyTextBox);
        if (toggleHotkeyIndex >= 0)
        {
            panel.Children.Insert(toggleHotkeyIndex + 1, _settingsToggleHotkeyErrorText);
        }

        var newNoteHotkeyIndex = panel.Children.IndexOf(SettingsNewNoteHotkeyTextBox);
        if (newNoteHotkeyIndex >= 0)
        {
            panel.Children.Insert(newNoteHotkeyIndex + 1, _settingsNewNoteHotkeyErrorText);
            panel.Children.Insert(newNoteHotkeyIndex + 2, _settingsCheckHotkeysButton);
        }
    }

    private TextBlock CreateSettingsLabel(Func<string> textFactory)
    {
        return new TextBlock
        {
            Text = textFactory(),
            Foreground = GetBrush("SecondaryTextBrush"),
            FontSize = 13
        };
    }

    private TextBlock CreateSettingsErrorText()
    {
        return new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 12),
            Foreground = GetBrush("WarningTextBrush"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
    }

    private Button CreateSettingsActionButton(string content, RoutedEventHandler handler, double minWidth)
    {
        var button = new Button
        {
            Content = content,
            MinWidth = minWidth,
            Margin = new Thickness(0, 0, 10, 8),
            Style = (Style)Application.Current.Resources["ActionButtonStyle"]
        };
        button.Click += handler;
        return button;
    }

    private void OpenSettingsPanel(bool onboarding = false)
    {
        _isOnboardingMode = onboarding;
        _settingsSnapshot = CloneSettings(_settings);
        _isLoadingSettingsUi = true;
        SettingsTitleTextBox.Text = _settings.OverlayTitle;
        SettingsNotesDirectoryTextBox.Text = _settings.NotesDirectory;
        SettingsToggleHotkeyTextBox.Text = _settings.ToggleOverlayHotkey;
        SettingsNewNoteHotkeyTextBox.Text = _settings.NewNoteHotkey;
        SelectLanguageComboItem(_settings.Language);
        SettingsDimSlider.Value = _settings.OverlayDimOpacity;
        SettingsAccentColorTextBox.Text = _settings.AccentColor;
        UseAccentForSelectionCheckBox.IsChecked = _settings.UseAccentForSelectionOutline;
        SettingsSelectionColorTextBox.Text = _settings.SelectionOutlineColor;
        SettingsAccentPreviewButton.Foreground = (Brush)Application.Current.Resources["AccentBrush"];
        SettingsStatusText.Text = string.Empty;
        ClearHotkeyErrors();
        ApplySettingsLanguage();
        SettingsOverlay.Visibility = Visibility.Visible;
        ShowSettingsMainTab();
        _isLoadingSettingsUi = false;
    }

    private void SelectLanguageComboItem(string language)
    {
        if (_settingsLanguageComboBox is null)
        {
            return;
        }

        var normalized = UiText.NormalizeLanguage(language);
        foreach (ComboBoxItem item in _settingsLanguageComboBox.Items)
        {
            if (string.Equals(item.Tag?.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                _settingsLanguageComboBox.SelectedItem = item;
                return;
            }
        }
    }

    private void ApplySettingsLanguage()
    {
        var language = _settings.Language;
        if (_onboardingIntroText is not null)
        {
            _onboardingIntroText.Text = UiText.T(language, "OnboardingIntro");
            _onboardingIntroText.Visibility = _isOnboardingMode ? Visibility.Visible : Visibility.Collapsed;
        }

        SettingsStatusText.Text = string.Empty;
        if (_settingsCheckHotkeysButton is not null)
        {
            _settingsCheckHotkeysButton.Content = UiText.T(language, "CheckHotkeys");
        }

        if (_settingsCloseAppButton is not null)
        {
            _settingsCloseAppButton.Content = UiText.T(language, "CloseApp");
        }

        SetLabelBefore(SettingsTitleTextBox, UiText.T(language, "OverlayTitle"));
        SetLabelBefore(SettingsNotesDirectoryTextBox, UiText.T(language, "NotesFolder"));
        SetLabelBefore(SettingsToggleHotkeyTextBox, UiText.T(language, "OverlayHotkey"));
        SetLabelBefore(SettingsNewNoteHotkeyTextBox, UiText.T(language, "NewNoteHotkey"));
        SetLabelBefore(SettingsDimSlider, UiText.T(language, "DimOpacity"));
        SetLabelBefore(SettingsAccentColorTextBox, UiText.T(language, "AccentColor"));
        SetLabelBefore(SettingsSelectionColorTextBox, UiText.T(language, "SelectionColor"));
        UpdateNotesFolderActionButtons(language);
        UseAccentForSelectionCheckBox.Content = UiText.T(language, "UseAccentForSelection");
        SettingsAccentPickerButton.Content = UiText.T(language, "Palette");
    }

    private void UpdateNotesFolderActionButtons(string language)
    {
        if (SettingsMainPanel.Content is not StackPanel panel)
        {
            return;
        }

        var index = panel.Children.IndexOf(SettingsNotesDirectoryTextBox);
        if (index < 0 || index + 1 >= panel.Children.Count || panel.Children[index + 1] is not WrapPanel buttons)
        {
            return;
        }

        var labels = new[]
        {
            UiText.T(language, "ChooseFolder"),
            UiText.T(language, "CreateDefaultFolder"),
            UiText.T(language, "FindAutomatically")
        };

        for (var i = 0; i < labels.Length && i < buttons.Children.Count; i++)
        {
            if (buttons.Children[i] is Button button)
            {
                button.Content = labels[i];
            }
        }
    }

    private void SetLabelBefore(FrameworkElement element, string text)
    {
        if (SettingsMainPanel.Content is not StackPanel panel)
        {
            return;
        }

        var index = panel.Children.IndexOf(element);
        if (index > 0 && panel.Children[index - 1] is TextBlock label)
        {
            label.Text = text;
        }
    }

    private void SettingsLanguageComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettingsUi || _settingsLanguageComboBox?.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        _settings.Language = UiText.NormalizeLanguage(item.Tag?.ToString());
        ApplySettingsLanguage();
    }

    private async void SettingsChooseFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        var oldValue = _settings.NotesDirectory;
        _settings.NotesDirectory = SettingsNotesDirectoryTextBox.Text;
        if (await TryChooseExistingNotesFolderAsync())
        {
            SettingsNotesDirectoryTextBox.Text = _settings.NotesDirectory;
        }
        else
        {
            _settings.NotesDirectory = oldValue;
        }
    }

    private void SettingsCreateDefaultFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryCreateDefaultNotesFolder(out var path, out var error))
        {
            SettingsNotesDirectoryTextBox.Text = path;
            _settings.NotesDirectory = path;
            SettingsStatusText.Text = string.Empty;
        }
        else
        {
            SettingsStatusText.Text = error;
        }
    }

    private async void SettingsFindFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        var oldValue = _settings.NotesDirectory;
        if (await TryFindExistingNotesFolderAsync())
        {
            SettingsNotesDirectoryTextBox.Text = _settings.NotesDirectory;
        }
        else
        {
            _settings.NotesDirectory = oldValue;
        }
    }

    private void SettingsCheckHotkeysButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ValidateHotkeyTextBoxes())
        {
            return;
        }

        var candidate = CloneSettings(_settings);
        candidate.ToggleOverlayHotkey = SettingsToggleHotkeyTextBox.Text.Trim();
        candidate.NewNoteHotkey = SettingsNewNoteHotkeyTextBox.Text.Trim();
        candidate.Language = _settings.Language;
        var errors = _validateHotkeys(candidate);
        if (errors.Count > 0)
        {
            ApplyHotkeyErrors(errors);
            return;
        }

        _settings = candidate;
        SettingsStatusText.Text = UiText.T(_settings.Language, "HotkeyOk");
        ClearHotkeyErrors();
    }

    private void ClearHotkeyErrors()
    {
        if (_settingsToggleHotkeyErrorText is not null)
        {
            _settingsToggleHotkeyErrorText.Text = string.Empty;
        }

        if (_settingsNewNoteHotkeyErrorText is not null)
        {
            _settingsNewNoteHotkeyErrorText.Text = string.Empty;
        }
    }

    private void ApplyHotkeyErrors(IReadOnlyList<string> errors)
    {
        ClearHotkeyErrors();
        var language = _settings.Language;
        var fallback = UiText.T(language, "HotkeyBusy");
        foreach (var error in errors)
        {
            var parts = error.Split('|', 3);
            var field = parts.Length > 0 ? parts[0] : string.Empty;
            var message = parts.Length > 1 && parts[1] == "busy"
                ? fallback
                : UiText.T(language, "InvalidHotkey");

            if (field == "overlay" && _settingsToggleHotkeyErrorText is not null)
            {
                _settingsToggleHotkeyErrorText.Text = message;
            }
            else if (field == "new-note" && _settingsNewNoteHotkeyErrorText is not null)
            {
                _settingsNewNoteHotkeyErrorText.Text = message;
            }
            else
            {
                SettingsStatusText.Text = message;
            }
        }

        SettingsOverlay.Visibility = Visibility.Visible;
    }

    private bool ValidateHotkeyTextBoxes()
    {
        ClearHotkeyErrors();
        var isValid = true;

        if (!HotkeyGesture.TryParse(SettingsToggleHotkeyTextBox.Text, out _, out _))
        {
            if (_settingsToggleHotkeyErrorText is not null)
            {
                _settingsToggleHotkeyErrorText.Text = UiText.T(_settings.Language, "InvalidHotkey");
            }

            isValid = false;
        }

        if (!HotkeyGesture.TryParse(SettingsNewNoteHotkeyTextBox.Text, out _, out _))
        {
            if (_settingsNewNoteHotkeyErrorText is not null)
            {
                _settingsNewNoteHotkeyErrorText.Text = UiText.T(_settings.Language, "InvalidHotkey");
            }

            isValid = false;
        }

        return isValid;
    }

    private void ShowSettingsMainTab()
    {
        SettingsMainPanel.Visibility = Visibility.Visible;
        SettingsTrashPanel.Visibility = Visibility.Collapsed;
    }

    private async void SettingsTrashTabButton_OnClick(object sender, RoutedEventArgs e)
    {
        SettingsMainPanel.Visibility = Visibility.Collapsed;
        SettingsTrashPanel.Visibility = Visibility.Visible;
        await RefreshSettingsTrashAsync();
    }

    private void SettingsMainTabButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowSettingsMainTab();
    }

    private async void SettingsSaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ValidateHotkeyTextBoxes())
        {
            return;
        }

        if (!TryBuildSettingsFromUi(out var updatedSettings))
        {
            return;
        }

        var errors = _applySettings(updatedSettings);
        if (errors.Count > 0)
        {
            ApplyHotkeyErrors(errors);
            return;
        }

        _settings = updatedSettings;
        UpdateAccentResources(_settings.AccentColor);
        OverlayBackdropBrush = CreateDimBrush(_settings.OverlayDimOpacity);
        SelectionOutlineBrush = CreateSelectionOutlineBrush(_settings);
        _currentFolderPath = _noteService.ResolveFolderOrRoot(_settings.NotesDirectory);
        SettingsOverlay.Visibility = Visibility.Collapsed;
        await LoadItemsAsync();
        ShowStatus("Настройки сохранены.", false);
    }

    private void SettingsCancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        CancelSettings();
    }

    private async void CloseAppButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RequestCloseApplicationAsync();
    }

    private async Task RequestCloseApplicationAsync()
    {
        if (HasUnsavedSettingsChanges())
        {
            var shouldClose = await ShowConfirmAsync(
                UiText.T(_settings.Language, "CloseApp"),
                UiText.T(_settings.Language, "CloseWithoutSaving"),
                UiText.T(_settings.Language, "Close"),
                UiText.T(_settings.Language, "Cancel"),
                danger: true);

            if (!shouldClose)
            {
                return;
            }
        }

        _modalCompletion?.TrySetResult(null);
        _promptCompletion?.TrySetResult(null);
        _folderPickerCompletion?.TrySetResult(null);
        Application.Current.Shutdown(0);
    }

    private bool HasUnsavedSettingsChanges()
    {
        if (SettingsOverlay.Visibility != Visibility.Visible || _settingsSnapshot is null)
        {
            return false;
        }

        var snapshot = _settingsSnapshot;
        return !StringEquals(SettingsTitleTextBox.Text, snapshot.OverlayTitle) ||
               !StringEquals(SettingsNotesDirectoryTextBox.Text, snapshot.NotesDirectory) ||
               !StringEquals(SettingsToggleHotkeyTextBox.Text, snapshot.ToggleOverlayHotkey) ||
               !StringEquals(SettingsNewNoteHotkeyTextBox.Text, snapshot.NewNoteHotkey) ||
               !StringEquals(_settings.Language, snapshot.Language) ||
               !StringEquals(SettingsAccentColorTextBox.Text, snapshot.AccentColor) ||
               !StringEquals(SettingsSelectionColorTextBox.Text, snapshot.SelectionOutlineColor) ||
               UseAccentForSelectionCheckBox.IsChecked != snapshot.UseAccentForSelectionOutline ||
               Math.Abs(SettingsDimSlider.Value - snapshot.OverlayDimOpacity) > 0.0001;
    }

    private static bool StringEquals(string? left, string? right)
    {
        return string.Equals(left?.Trim(), right?.Trim(), StringComparison.Ordinal);
    }

    private void CancelSettings()
    {
        if (_settingsSnapshot is not null)
        {
            _settings = CloneSettings(_settingsSnapshot);
            ApplySettingsPreview(_settings);
        }

        SettingsOverlay.Visibility = Visibility.Collapsed;
        _settingsSnapshot = null;
        FocusItemsHost();
    }

    private void SettingsTitleTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingSettingsUi)
        {
            return;
        }

        _settings.OverlayTitle = string.IsNullOrWhiteSpace(SettingsTitleTextBox.Text) ? "Мои заметки" : SettingsTitleTextBox.Text.Trim();
        UpdatePathState();
    }

    private void SettingsDimSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoadingSettingsUi)
        {
            return;
        }

        _settings.OverlayDimOpacity = e.NewValue;
        OverlayBackdropBrush = CreateDimBrush(_settings.OverlayDimOpacity);
    }

    private void SettingsSelectionColorTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingSettingsUi)
        {
            return;
        }

        if (!IsHexColor(SettingsSelectionColorTextBox.Text))
        {
            SettingsColorErrorText.Text = "Неверный Hex";
            return;
        }

        SettingsColorErrorText.Text = string.Empty;
        _settings.SelectionOutlineColor = SettingsSelectionColorTextBox.Text.Trim();
        SelectionOutlineBrush = CreateSelectionOutlineBrush(_settings);
        SettingsColorPreviewButton.Foreground = SelectionOutlineBrush;
    }

    private void SettingsAccentColorTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingSettingsUi)
        {
            return;
        }

        if (!IsHexColor(SettingsAccentColorTextBox.Text))
        {
            SettingsAccentErrorText.Text = "Неверный Hex";
            return;
        }

        SettingsAccentErrorText.Text = string.Empty;
        _settings.AccentColor = SettingsAccentColorTextBox.Text.Trim();
        UpdateAccentResources(_settings.AccentColor);
        SelectionOutlineBrush = CreateSelectionOutlineBrush(_settings);
        SettingsAccentPreviewButton.Foreground = (Brush)Application.Current.Resources["AccentBrush"];
    }

    private void UseAccentForSelectionCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettingsUi)
        {
            return;
        }

        _settings.UseAccentForSelectionOutline = UseAccentForSelectionCheckBox.IsChecked == true;
        SelectionOutlineBrush = CreateSelectionOutlineBrush(_settings);
    }

    private void SettingsColorPreviewButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenColorPicker(false);
    }

    private void SettingsAccentPickerButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenColorPicker(true);
    }

    private void OpenColorPicker(bool targetsAccent)
    {
        _colorPickerTargetsAccent = targetsAccent;
        ColorPickerTitleText.Text = targetsAccent ? "Акцентный цвет" : "Цвет рамки";
        SetColorPickerValue(targetsAccent ? SettingsAccentColorTextBox.Text : SettingsSelectionColorTextBox.Text);
        ColorPickerErrorText.Text = string.Empty;
        ColorPickerOverlay.Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(() => ColorPickerHexTextBox.Focus(), DispatcherPriority.Input);
    }

    private void BuildColorPresets()
    {
        BuildColorWheelPalette();

        string[] colors =
        [
            "#80FFFFFF",
            "#FF7AA2FF",
            "#FFFF6B6B",
            "#FF8AFF80",
            "#FFFFD166",
            "#FFC084FC",
            "#FF00D1FF"
        ];

        foreach (var color in colors)
        {
            var button = new Button
            {
                Width = 34,
                Height = 34,
                Margin = new Thickness(0, 0, 10, 10),
                Content = "●",
                FontSize = 22,
                Foreground = CreateBrushFromHex(color),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Tag = color,
                Cursor = Cursors.Hand
            };
            button.Click += (_, _) =>
            {
                SetColorPickerValue((string)button.Tag);
                ApplyPickerColor((string)button.Tag);
            };
            ColorPresetPanel.Children.Add(button);
        }
    }

    private void BuildColorWheelPalette()
    {
        string[] colors =
        [
            "#FFFF3B30", "#FFFF9500", "#FFFFCC00", "#FF34C759",
            "#FF00C7BE", "#FF32ADE6", "#FF007AFF", "#FF5856D6",
            "#FFAF52DE", "#FFFF2D55", "#FFFFFFFF", "#FF8E8E93",
            "#FF636366", "#FF3A3A3C", "#FF1C1C1E", "#FF000000"
        ];

        foreach (var color in colors)
        {
            var button = CreateColorPresetButton(color);
            button.Width = 38;
            button.Height = 38;
            button.Margin = new Thickness(0, 0, 9, 9);
            button.Click += (_, _) =>
            {
                SetColorPickerValue((string)button.Tag);
                ApplyPickerColor((string)button.Tag);
            };
            ColorWheelPalettePanel.Children.Add(button);
        }
    }

    private void BuildColorWheelBitmap()
    {
        var pixels = new byte[ColorWheelSize * ColorWheelSize * 4];
        var center = (ColorWheelSize - 1) / 2.0;
        var radius = center;

        for (var y = 0; y < ColorWheelSize; y++)
        {
            for (var x = 0; x < ColorWheelSize; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var distance = Math.Sqrt((dx * dx) + (dy * dy));
                var offset = ((y * ColorWheelSize) + x) * 4;

                if (distance > radius)
                {
                    pixels[offset + 3] = 0;
                    continue;
                }

                var hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                var saturation = Math.Clamp(distance / radius, 0, 1);
                var color = ColorFromHsv(hue, saturation, 1);
                pixels[offset] = color.B;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R;
                pixels[offset + 3] = 255;
            }
        }

        ColorWheelImage.Source = BitmapSource.Create(
            ColorWheelSize,
            ColorWheelSize,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            ColorWheelSize * 4);
    }

    private void BuildAccentPresets()
    {
        string[] colors =
        [
            "#FF6E8CFF",
            "#FFFF5BC8",
            "#FFB56CFF",
            "#FF00D1FF",
            "#FF8AFF80",
            "#FFFFD166",
            "#FFFF6B6B"
        ];

        foreach (var color in colors)
        {
            var button = CreateColorPresetButton(color);
            button.Click += (_, _) =>
            {
                SettingsAccentColorTextBox.Text = (string)button.Tag;
                _settings.AccentColor = (string)button.Tag;
                UpdateAccentResources(_settings.AccentColor);
                SelectionOutlineBrush = CreateSelectionOutlineBrush(_settings);
                SettingsAccentPreviewButton.Foreground = (Brush)Application.Current.Resources["AccentBrush"];
            };
            AccentPresetPanel.Children.Add(button);
        }
    }

    private Button CreateColorPresetButton(string color)
    {
        return new Button
        {
            Width = 30,
            Height = 30,
            Margin = new Thickness(0, 0, 10, 10),
            Content = "●",
            FontSize = 21,
            Foreground = CreateBrushFromHex(color),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Tag = color,
            Cursor = Cursors.Hand
        };
    }

    private void ColorPickerHexTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingColorPicker)
        {
            return;
        }

        if (!IsHexColor(ColorPickerHexTextBox.Text))
        {
            ColorPickerErrorText.Text = "Неверный Hex";
            return;
        }

        ColorPickerErrorText.Text = string.Empty;
        SyncRgbSlidersFromHex(ColorPickerHexTextBox.Text.Trim());
        UpdateColorPickerPreviewAndMarker(ColorPickerHexTextBox.Text.Trim());
        ApplyPickerColor(ColorPickerHexTextBox.Text.Trim());
    }

    private void ColorPickerRgbSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingColorPicker || ColorPickerHexTextBox is null)
        {
            return;
        }

        var color = Color.FromArgb(
            255,
            (byte)Math.Round(ColorPickerRSlider.Value),
            (byte)Math.Round(ColorPickerGSlider.Value),
            (byte)Math.Round(ColorPickerBSlider.Value));
        SetColorPickerValue(color.ToString());
        ApplyPickerColor(color.ToString());
    }

    private void ColorWheel_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingColorWheel = true;
        ColorWheelImage.CaptureMouse();
        PickColorFromWheel(e.GetPosition(ColorWheelImage));
        e.Handled = true;
    }

    private void ColorWheel_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingColorWheel || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        PickColorFromWheel(e.GetPosition(ColorWheelImage));
        e.Handled = true;
    }

    private void ColorWheel_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingColorWheel = false;
        ColorWheelImage.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void PickColorFromWheel(Point point)
    {
        var center = ColorWheelSize / 2.0;
        var dx = point.X - center;
        var dy = point.Y - center;
        var radius = ColorWheelSize / 2.0;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));

        if (distance > radius)
        {
            dx *= radius / distance;
            dy *= radius / distance;
            distance = radius;
        }

        var hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
        var saturation = Math.Clamp(distance / radius, 0, 1);
        var color = ColorFromHsv(hue, saturation, 1).ToString();
        SetColorPickerValue(color);
        ApplyPickerColor(color);
    }

    private void ColorPickerApplyButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!IsHexColor(ColorPickerHexTextBox.Text))
        {
            ColorPickerErrorText.Text = "Неверный Hex";
            return;
        }

        ApplyPickerColor(ColorPickerHexTextBox.Text.Trim());
        ColorPickerOverlay.Visibility = Visibility.Collapsed;
    }

    private void ColorPickerCancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        ColorPickerOverlay.Visibility = Visibility.Collapsed;
    }

    private void ApplySelectionColor(string color)
    {
        SettingsSelectionColorTextBox.Text = color;
        _settings.SelectionOutlineColor = color;
        SelectionOutlineBrush = CreateSelectionOutlineBrush(_settings);
        SettingsColorPreviewButton.Foreground = SelectionOutlineBrush;
    }

    private void ApplyAccentColor(string color)
    {
        SettingsAccentColorTextBox.Text = color;
        _settings.AccentColor = color;
        UpdateAccentResources(_settings.AccentColor);
        SelectionOutlineBrush = CreateSelectionOutlineBrush(_settings);
        SettingsAccentPreviewButton.Foreground = (Brush)Application.Current.Resources["AccentBrush"];
    }

    private void ApplyPickerColor(string color)
    {
        if (_colorPickerTargetsAccent)
        {
            ApplyAccentColor(color);
        }
        else
        {
            ApplySelectionColor(color);
        }
    }

    private void SetColorPickerValue(string color)
    {
        _isUpdatingColorPicker = true;
        ColorPickerHexTextBox.Text = color;
        SyncRgbSlidersFromHex(color);
        UpdateColorPickerPreviewAndMarker(color);
        _isUpdatingColorPicker = false;
    }

    private void SyncRgbSlidersFromHex(string color)
    {
        var parsed = ParseColorOrDefault(color, Color.FromArgb(255, 128, 128, 128));
        ColorPickerRSlider.Value = parsed.R;
        ColorPickerGSlider.Value = parsed.G;
        ColorPickerBSlider.Value = parsed.B;
    }

    private void UpdateColorPickerPreviewAndMarker(string color)
    {
        var parsed = ParseColorOrDefault(color, Color.FromRgb(255, 255, 255));
        ColorPickerPreview.Background = new SolidColorBrush(parsed);

        var (hue, saturation) = HsvFromColor(parsed);
        var radius = (ColorWheelSize / 2.0) * saturation;
        var angle = hue * Math.PI / 180;
        var markerX = (ColorWheelSize / 2.0) + (Math.Cos(angle) * radius);
        var markerY = (ColorWheelSize / 2.0) + (Math.Sin(angle) * radius);
        ColorWheelMarker.Margin = new Thickness(markerX - (ColorWheelMarker.Width / 2) + 6, markerY - (ColorWheelMarker.Height / 2) + 6, 0, 0);
    }

    private static Color ColorFromHsv(double hue, double saturation, double value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs((hue / 60 % 2) - 1));
        var m = value - c;
        (double r, double g, double b) = hue switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x)
        };

        return Color.FromArgb(
            255,
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private static (double Hue, double Saturation) HsvFromColor(Color color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        var hue = delta == 0
            ? 0
            : max == r
                ? 60 * (((g - b) / delta) % 6)
                : max == g
                    ? 60 * (((b - r) / delta) + 2)
                    : 60 * (((r - g) / delta) + 4);
        if (hue < 0)
        {
            hue += 360;
        }

        var saturation = max == 0 ? 0 : delta / max;
        return (hue, saturation);
    }

    private async Task RefreshSettingsTrashAsync()
    {
        _trashItems.Clear();
        foreach (var item in await _noteService.GetTrashItemsAsync())
        {
            _trashItems.Add(item);
        }
    }

    private async void SettingsRestoreTrashButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not TrashItem item)
        {
            return;
        }

        var result = await _noteService.RestoreTrashItemAsync(item);
        ShowStatus(result.Message, !result.Success);
        await RefreshSettingsTrashAsync();
        await LoadItemsAsync();
    }

    private async void SettingsDeleteTrashButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not TrashItem item)
        {
            return;
        }

        if (!await ShowConfirmAsync("Мусорка", "Удалить заметку навсегда?", "Удалить", "Отмена", true))
        {
            return;
        }

        var result = await _noteService.DeleteTrashItemPermanentlyAsync(item);
        ShowStatus(result.Message, !result.Success);
        await RefreshSettingsTrashAsync();
    }

    private async void SettingsEmptyTrashButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!await ShowConfirmAsync("Мусорка", "Очистить мусорку?", "Очистить", "Отмена", true))
        {
            return;
        }

        var result = await _noteService.EmptyTrashAsync();
        ShowStatus(result.Message, !result.Success);
        await RefreshSettingsTrashAsync();
    }

    private bool TryBuildSettingsFromUi(out UserSettings settings)
    {
        settings = CloneSettings(_settings);
        if (!HotkeyGesture.TryParse(SettingsToggleHotkeyTextBox.Text, out _, out var toggleError))
        {
            SettingsStatusText.Text = $"Не удалось распознать горячую клавишу overlay: {toggleError}";
            return false;
        }

        if (!HotkeyGesture.TryParse(SettingsNewNoteHotkeyTextBox.Text, out _, out var newNoteError))
        {
            SettingsStatusText.Text = $"Не удалось распознать горячую клавишу новой заметки: {newNoteError}";
            return false;
        }

        if (!IsHexColor(SettingsSelectionColorTextBox.Text))
        {
            SettingsStatusText.Text = "Цвет рамки выделения должен быть в формате #RRGGBB или #AARRGGBB.";
            return false;
        }

        if (!IsHexColor(SettingsAccentColorTextBox.Text))
        {
            SettingsStatusText.Text = "Акцентный цвет должен быть в формате #RRGGBB или #AARRGGBB.";
            return false;
        }

        settings.OverlayTitle = string.IsNullOrWhiteSpace(SettingsTitleTextBox.Text) ? "Мои заметки" : SettingsTitleTextBox.Text.Trim();
        var notesDirectory = Environment.ExpandEnvironmentVariables(SettingsNotesDirectoryTextBox.Text.Trim());
        if (!Directory.Exists(notesDirectory))
        {
            SettingsStatusText.Text = UiText.T(_settings.Language, "FolderNotFound");
            return false;
        }

        settings.NotesDirectory = notesDirectory;
        settings.ToggleOverlayHotkey = SettingsToggleHotkeyTextBox.Text;
        settings.NewNoteHotkey = SettingsNewNoteHotkeyTextBox.Text;
        settings.Language = _settings.Language;
        settings.IsOnboardingComplete = true;
        settings.OverlayDimOpacity = SettingsDimSlider.Value;
        settings.SelectionOutlineColor = SettingsSelectionColorTextBox.Text.Trim();
        settings.AccentColor = SettingsAccentColorTextBox.Text.Trim();
        settings.UseAccentForSelectionOutline = UseAccentForSelectionCheckBox.IsChecked == true;
        return true;
    }

    private void ApplySettingsPreview(UserSettings settings)
    {
        UpdateAccentResources(settings.AccentColor);
        OverlayBackdropBrush = CreateDimBrush(settings.OverlayDimOpacity);
        SelectionOutlineBrush = CreateSelectionOutlineBrush(settings);
        UpdatePathState();
    }

    private static UserSettings CloneSettings(UserSettings settings)
    {
        return new UserSettings
        {
            OverlayTitle = settings.OverlayTitle,
            NotesDirectory = settings.NotesDirectory,
            ToggleOverlayHotkey = settings.ToggleOverlayHotkey,
            NewNoteHotkey = settings.NewNoteHotkey,
            Language = settings.Language,
            IsOnboardingComplete = settings.IsOnboardingComplete,
            OverlayDimOpacity = settings.OverlayDimOpacity,
            SelectionOutlineColor = settings.SelectionOutlineColor,
            AccentColor = settings.AccentColor,
            UseAccentForSelectionOutline = settings.UseAccentForSelectionOutline
        };
    }

    private static bool IsHexColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var color = value.Trim();
        return (color.Length == 7 || color.Length == 9) &&
               color[0] == '#' &&
               color[1..].All(Uri.IsHexDigit);
    }

    private void OverlayTitleTextBlock_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
        {
            return;
        }

        _isRenamingTitle = true;
        OverlayTitleTextBlock.Visibility = Visibility.Collapsed;
        OverlayTitleEditBox.Text = OverlayTitle;
        OverlayTitleEditBox.Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(() =>
        {
            OverlayTitleEditBox.Focus();
            OverlayTitleEditBox.SelectAll();
        }, DispatcherPriority.Input);
        e.Handled = true;
    }

    private async void OverlayTitleEditBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await CommitTitleRenameAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelTitleRename();
            e.Handled = true;
        }
    }

    private async void OverlayTitleEditBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_isRenamingTitle)
        {
            await CommitTitleRenameAsync();
        }
    }

    private async Task CommitTitleRenameAsync()
    {
        if (!_isRenamingTitle)
        {
            return;
        }

        var value = OverlayTitleEditBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            await ShowErrorAsync("Переименование", "Название не может быть пустым.");
            return;
        }

        if (_noteService.IsRootFolder(_currentFolderPath))
        {
            _settings.OverlayTitle = value;
            _userSettingsService.Save(_settings);
            OverlayTitle = value;
        }
        else
        {
            var oldPath = _currentFolderPath;
            var result = await _noteService.RenameFolderAsync(_currentFolderPath, value);
            if (!result.Success || string.IsNullOrWhiteSpace(result.FilePath))
            {
                await ShowErrorAsync("Переименование", result.Message);
                return;
            }

            _currentFolderPath = result.FilePath;
            if (string.Equals(_currentFilePath, oldPath, StringComparison.OrdinalIgnoreCase))
            {
                _currentFilePath = result.FilePath;
            }

            await LoadItemsAsync();
        }

        _isRenamingTitle = false;
        OverlayTitleEditBox.Visibility = Visibility.Collapsed;
        OverlayTitleTextBlock.Visibility = Visibility.Visible;
        UpdatePathState();
    }

    private void CancelTitleRename()
    {
        _isRenamingTitle = false;
        OverlayTitleEditBox.Visibility = Visibility.Collapsed;
        OverlayTitleTextBlock.Visibility = Visibility.Visible;
        FocusItemsHost();
    }

    private void HeaderDropBackZone_OnDragEnter(object sender, DragEventArgs e)
    {
        if (_noteService.IsRootFolder(_currentFolderPath) || !HasOverlayDragData(e))
        {
            return;
        }

        HeaderDropBackZone.Opacity = 0.65;
        _dragBackTimer.Stop();
        _dragBackTimer.Start();
    }

    private void HeaderDropBackZone_OnDragLeave(object sender, DragEventArgs e)
    {
        HeaderDropBackZone.Opacity = 1;
        _dragBackTimer.Stop();
    }

    private void HeaderDropBackZone_OnDragOver(object sender, DragEventArgs e)
    {
        UpdateDragPreview(e.GetPosition(RootGrid));
        e.Effects = HasOverlayDragData(e) && !_noteService.IsRootFolder(_currentFolderPath)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void DragBackTimer_OnTick(object? sender, EventArgs e)
    {
        LogDragDebug("DragToParentTimer tick", this);
        _dragBackTimer.Stop();
        HeaderDropBackZone.Opacity = 1;
        if (!_isDragging ||
            (!_isKeyboardDragging && Mouse.LeftButton != MouseButtonState.Pressed) ||
            _noteService.IsRootFolder(_currentFolderPath))
        {
            LogDragDebug("NavigateToParentDuringDrag fail", this, "not dragging, left released, or already root");
            return;
        }

        var parent = _noteService.GetParentFolder(_currentFolderPath);
        if (parent is null)
        {
            LogDragDebug("NavigateToParentDuringDrag fail", this, "no parent");
            return;
        }

        _currentFolderPath = parent;
        await LoadItemsAsync();
        UpdateDragPlaceholder(null);
        ShowStatus("Родительская папка", false);
        LogDragDebug("NavigateToParentDuringDrag success", this, parent);
    }

    private void StartDragBackHover()
    {
        if (_noteService.IsRootFolder(_currentFolderPath))
        {
            return;
        }

        HeaderDropBackZone.Opacity = 0.65;
        if (!_dragBackTimer.IsEnabled)
        {
            LogDragDebug("DragToParentTimer started", this, "delay=0.5s");
            _dragBackTimer.Start();
        }
    }

    private void StopDragBackHover()
    {
        HeaderDropBackZone.Opacity = 1;
        if (_dragBackTimer.IsEnabled)
        {
            LogDragDebug("DragToParentTimer cancelled", this);
            _dragBackTimer.Stop();
        }
    }

    private static bool IsInsideButton(object source)
    {
        if (source is not DependencyObject current)
        {
            return false;
        }

        while (current is not null)
        {
            if (current is Button)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static bool IsInteractiveElement(DependencyObject source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is ButtonBase or TextBoxBase or PasswordBox or ScrollViewer or Slider or ComboBox or ListBoxItem)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private bool IsModalLayerOpen()
    {
        return PromptOverlay.Visibility == Visibility.Visible ||
               ModalOverlay.Visibility == Visibility.Visible ||
               FolderPickerOverlay.Visibility == Visibility.Visible ||
               ColorPickerOverlay.Visibility == Visibility.Visible ||
               SettingsOverlay.Visibility == Visibility.Visible;
    }

    private static T? FindParent<T>(object source)
        where T : DependencyObject
    {
        if (source is not DependencyObject current)
        {
            return null;
        }

        while (current is not null)
        {
            if (current is T parent)
            {
                return parent;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static object? GetItemFromSource(object? source)
    {
        if (source is not DependencyObject dependencyObject)
        {
            return null;
        }

        var listBoxItem = FindParent<ListBoxItem>(dependencyObject);
        return listBoxItem?.DataContext is DragPlaceholderItem ? null : listBoxItem?.DataContext;
    }

    private static object? GetItemFromHitTest(Visual visual, Point position)
    {
        var result = VisualTreeHelper.HitTest(visual, position);
        return result is null ? null : GetItemFromSource(result.VisualHit);
    }

    private object? GetStableItemFromDragSource(object? source)
    {
        var item = GetItemFromSource(source);
        if (item is NoteItem or FolderItem)
        {
            return item;
        }

        var hitItem = GetItemFromHitTest(ItemsListBox, Mouse.GetPosition(ItemsListBox));
        if (hitItem is NoteItem or FolderItem)
        {
            return hitItem;
        }

        if (item is DragPlaceholderItem || hitItem is DragPlaceholderItem)
        {
            LogDragDebug("ReorderIndex ignored", source, "placeholder target");
        }
        else
        {
            LogDragDebug("ReorderIndex ignored", source, "no stable item under cursor");
        }

        return null;
    }

    private bool ShouldReorderAtFolderEdge(object? source)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            return true;
        }

        if (source is not DependencyObject dependencyObject)
        {
            return false;
        }

        var container = FindParent<ListBoxItem>(dependencyObject);
        if (container?.DataContext is not FolderItem)
        {
            return false;
        }

        var point = Mouse.GetPosition(container);
        return point.Y <= container.ActualHeight * 0.45;
    }

    private int GetVisualInsertIndexForStableIndex(int stableIndex)
    {
        var draggedPaths = _draggedPaths.Count > 0 ? _draggedPaths : string.IsNullOrWhiteSpace(_draggedPath) ? [] : [_draggedPath];
        var seen = 0;
        for (var index = 0; index < Items.Count; index++)
        {
            var item = Items[index];
            if (ReferenceEquals(item, _dragPlaceholder) ||
                draggedPaths.Contains(GetItemPath(item) ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (seen == stableIndex)
            {
                return index;
            }

            seen++;
        }

        return Items.Count;
    }

    private bool IsPointInsideElement(FrameworkElement element, Point rootPosition)
    {
        if (!element.IsVisible)
        {
            return false;
        }

        var topLeft = element.TranslatePoint(new Point(0, 0), RootGrid);
        var bounds = new Rect(topLeft, new Size(element.ActualWidth, element.ActualHeight));
        return bounds.Contains(rootPosition);
    }

    private void ItemsScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scrollViewer = GetItemsScrollViewer();
        if (scrollViewer is null)
        {
            return;
        }

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private void ItemsListBox_OnLoaded(object sender, RoutedEventArgs e)
    {
        _itemsScrollViewer = FindVisualChild<ScrollViewer>(ItemsListBox);
        LogItemsScrollSizes("ItemsListBox loaded");
    }

    private ScrollViewer? GetItemsScrollViewer()
    {
        return _itemsScrollViewer ??= FindVisualChild<ScrollViewer>(ItemsListBox);
    }

    private void LogItemsScrollSizes(string reason)
    {
        var scrollViewer = GetItemsScrollViewer();
        var scrollHeight = scrollViewer?.ActualHeight.ToString("0.0") ?? "null";
        var viewport = scrollViewer?.ViewportHeight.ToString("0.0") ?? "null";
        var extent = scrollViewer?.ExtentHeight.ToString("0.0") ?? "null";
        LogDragDebug(
            "ScrollLayout",
            ItemsListBox,
            $"{reason}; ItemsScrollViewer.ActualHeight={scrollHeight}; ItemsListBox.ActualHeight={ItemsListBox.ActualHeight:0.0}; SearchResultsScrollViewer.ActualHeight=n/a(same ItemsListBox); Window.ActualHeight={ActualHeight:0.0}; Viewport={viewport}; Extent={extent}",
            force: true);
    }

    private void SettingsDimSlider_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider)
        {
            return;
        }

        if (FindParent<Thumb>(e.OriginalSource) is not null)
        {
            return;
        }

        var point = e.GetPosition(slider);
        var ratio = slider.ActualWidth <= 0 ? 0 : Math.Clamp(point.X / slider.ActualWidth, 0, 1);
        slider.Value = slider.Minimum + ((slider.Maximum - slider.Minimum) * ratio);
        e.Handled = true;
    }

    private void ShowOverlayContextMenu(Point position, params (string Header, bool IsDanger, Func<Task> Action)[] items)
    {
        HidePreviewPopup();
        OverlayContextMenuItems.Children.Clear();

        foreach (var item in items)
        {
            var button = new Button
            {
                Content = item.Header,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                MinHeight = 38,
                MinWidth = 284,
                Margin = new Thickness(0, 0, 0, 2),
                Padding = new Thickness(12, 8, 12, 8),
                Foreground = item.IsDanger ? GetBrush("DangerBrushHover") : GetBrush("PrimaryTextBrush"),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                Cursor = Cursors.Hand,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };
            void SetMenuButtonActive(bool active)
            {
                button.Background = active
                    ? item.IsDanger
                        ? new SolidColorBrush(Color.FromRgb(72, 32, 36))
                        : new SolidColorBrush(Color.FromRgb(48, 48, 58))
                    : Brushes.Transparent;
                button.BorderBrush = active ? GetBrush("AccentBorderBrush") : Brushes.Transparent;
            }

            button.Click += async (_, _) =>
            {
                HideOverlayContextMenu();
                await item.Action();
            };
            button.MouseEnter += (_, _) => SetMenuButtonActive(true);
            button.MouseLeave += (_, _) => SetMenuButtonActive(button.IsKeyboardFocusWithin);
            button.GotKeyboardFocus += (_, _) => SetMenuButtonActive(true);
            button.LostKeyboardFocus += (_, _) => SetMenuButtonActive(false);
            OverlayContextMenuItems.Children.Add(button);
        }

        ContextMenuLayer.Visibility = Visibility.Visible;
        OverlayContextMenu.UpdateLayout();

        var x = Math.Min(position.X, Math.Max(0, RootGrid.ActualWidth - OverlayContextMenu.ActualWidth - 12));
        var y = Math.Min(position.Y, Math.Max(0, RootGrid.ActualHeight - OverlayContextMenu.ActualHeight - 12));
        Canvas.SetLeft(OverlayContextMenu, Math.Round(Math.Max(12, x)));
        Canvas.SetTop(OverlayContextMenu, Math.Round(Math.Max(12, y)));

        if (OverlayContextMenuItems.Children.OfType<Button>().FirstOrDefault() is { } firstButton)
        {
            firstButton.Focus();
        }
    }

    private void ShowContextMenuForSelection()
    {
        var position = GetSelectedItemMenuPosition();
        if (ShowGroupContextMenuIfNeeded(position))
        {
            return;
        }

        switch (SelectedItem)
        {
            case NoteItem note:
                ShowOverlayContextMenu(position,
                    ("Открыть", false, async () => await OpenNoteAsync(note.FullPath)),
                    ("Переместить в папку", false, async () => await MoveNoteViaDialogAsync(note.FullPath)),
                    ("Копировать файл", false, () =>
                    {
                        ShowOperation(_noteService.CopyFileToClipboard(note.FullPath));
                        return Task.CompletedTask;
                    }),
                    ("Копировать содержимое файла", false, async () => ShowOperation(await _noteService.CopyContentToClipboardAsync(note.FullPath))),
                    ("Открыть в проводнике", false, async () => await OpenInExplorerAndHideAsync(note.FullPath, true)),
                    ("Удалить", true, async () => await MoveNoteToTrashWithConfirmationAsync(note.FullPath)));
                break;
            case FolderItem folder:
                ShowOverlayContextMenu(position,
                    ("Открыть", false, async () => await OpenFolderAsync(folder.FullPath)),
                    ("Переместить в папку", false, async () => await MoveFolderViaDialogAsync(folder)),
                    ("Переименовать", false, async () => await RenameFolderAsync(folder)),
                    ("Открыть в проводнике", false, async () => await OpenInExplorerAndHideAsync(folder.FullPath, false)),
                    ("Удалить", true, async () => await DeleteFolderAsync(folder)));
                break;
            default:
                ShowOverlayContextMenu(position,
                    ("Новая заметка", false, async () => await CreateNoteInCurrentFolderAsync()),
                    ("Новая папка", false, async () => await CreateFolderInCurrentFolderAsync()),
                    ("Обновить", false, async () => await LoadItemsAsync()));
                break;
        }
    }

    private void PrepareContextSelection(object item)
    {
        var path = GetItemPath(item);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (_selectedPaths.Count > 1 && _selectedPaths.Contains(path))
        {
            SelectedItem = item;
            return;
        }

        ClearMultiSelection();
        AddToSelection(item);
        _selectionAnchorPath = path;
        SelectedItem = item;
    }

    private bool ShowGroupContextMenuIfNeeded(Point position)
    {
        var selected = GetSelectedRealItems();
        if (selected.Count <= 1)
        {
            return false;
        }

        ShowOverlayContextMenu(position,
            ($"Открыть первый ({selected.Count})", false, async () => await OpenFirstSelectedAsync(selected)),
            ("Переместить в папку", false, async () => await MoveSelectedViaDialogAsync(selected)),
            ("Копировать файлы", false, () =>
            {
                CopySelectedPaths(selected);
                return Task.CompletedTask;
            }),
            ("Копировать содержимое", false, async () => await CopySelectedContentsAsync(selected)),
            ("Открыть в проводнике", false, async () => await OpenFirstSelectedInExplorerAsync(selected)),
            ("Удалить выбранные", true, async () => await DeleteSelectedItemsAsync(selected)));
        return true;
    }

    private async Task OpenFirstSelectedAsync(IReadOnlyList<object> selected)
    {
        switch (selected.FirstOrDefault())
        {
            case NoteItem note:
                await OpenNoteAsync(note.FullPath);
                break;
            case FolderItem folder:
                await OpenFolderAsync(folder.FullPath);
                break;
        }
    }

    private async Task OpenFirstSelectedInExplorerAsync(IReadOnlyList<object> selected)
    {
        switch (selected.FirstOrDefault())
        {
            case NoteItem note:
                await OpenInExplorerAndHideAsync(note.FullPath, true);
                break;
            case FolderItem folder:
                await OpenInExplorerAndHideAsync(folder.FullPath, false);
                break;
        }
    }

    private async Task MoveSelectedViaDialogAsync(IReadOnlyList<object> selected)
    {
        var selectedFolder = await ShowFolderChoiceAsync("Переместить выбранное", await _noteService.GetFoldersAsync());
        if (selectedFolder is null)
        {
            FocusItemsHost();
            return;
        }

        foreach (var item in selected)
        {
            var result = item switch
            {
                NoteItem note => await _noteService.MoveNoteToFolderAsync(note.FullPath, selectedFolder.FullPath),
                FolderItem folder => await _noteService.MoveFolderToFolderAsync(folder.FullPath, selectedFolder.FullPath),
                _ => new NoteOperationResult(false, "Неизвестный элемент.")
            };
            if (!result.Success)
            {
                ShowStatus(result.Message, true);
            }
        }

        ClearMultiSelection();
        await LoadItemsAsync();
    }

    private static void CopySelectedPaths(IReadOnlyList<object> selected)
    {
        Clipboard.SetText(string.Join(Environment.NewLine, selected.Select(GetItemPath).Where(path => !string.IsNullOrWhiteSpace(path))));
    }

    private async Task CopySelectedContentsAsync(IReadOnlyList<object> selected)
    {
        var chunks = new List<string>();
        foreach (var note in selected.OfType<NoteItem>())
        {
            try
            {
                chunks.Add($"--- {note.Title} ---");
                chunks.Add(await File.ReadAllTextAsync(note.FullPath));
            }
            catch (Exception ex)
            {
                chunks.Add($"--- {note.Title}: не удалось прочитать ({ex.Message}) ---");
            }
        }

        Clipboard.SetText(chunks.Count == 0 ? string.Join(Environment.NewLine, selected.Select(GetItemPath)) : string.Join(Environment.NewLine, chunks));
    }

    private async Task DeleteSelectedItemsAsync(IReadOnlyList<object> selected)
    {
        var confirmed = await ShowConfirmAsync(
            "Удалить выбранные элементы?",
            $"Количество: {selected.Count}",
            "Удалить",
            "Отмена",
            true);
        if (!confirmed)
        {
            return;
        }

        foreach (var item in selected)
        {
            var result = item switch
            {
                NoteItem note => await _noteService.MoveNoteToTrashAsync(note.FullPath),
                FolderItem folder => await _noteService.MoveFolderToTrashAsync(folder.FullPath),
                _ => new NoteOperationResult(false, "Неизвестный элемент.")
            };
            if (!result.Success)
            {
                ShowStatus(result.Message, true);
            }
        }

        ClearMultiSelection();
        await LoadItemsAsync();
    }

    private Point GetSelectedItemMenuPosition()
    {
        if (SelectedItem is not null &&
            ItemsListBox.ItemContainerGenerator.ContainerFromItem(SelectedItem) is FrameworkElement container)
        {
            var point = container.TranslatePoint(new Point(container.ActualWidth / 2, container.ActualHeight / 2), RootGrid);
            return point;
        }

        return new Point(Math.Max(32, RootGrid.ActualWidth / 2 - 140), Math.Max(32, RootGrid.ActualHeight / 2 - 40));
    }

    private static bool HandleButtonPanelKey(Panel panel, Key key)
    {
        var buttons = panel.Children.OfType<Button>().Where(button => button.IsEnabled).ToList();
        if (buttons.Count == 0)
        {
            return false;
        }

        var currentIndex = buttons.FindIndex(button => button.IsKeyboardFocusWithin || button.IsFocused);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        if (key is Key.Left or Key.Up)
        {
            buttons[(currentIndex - 1 + buttons.Count) % buttons.Count].Focus();
            return true;
        }

        if (key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            buttons[(currentIndex - 1 + buttons.Count) % buttons.Count].Focus();
            return true;
        }

        if (key is Key.Right or Key.Down or Key.Tab)
        {
            buttons[(currentIndex + 1) % buttons.Count].Focus();
            return true;
        }

        if (key == Key.Enter)
        {
            buttons[currentIndex].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return true;
        }

        return false;
    }

    private static Key GetEffectiveKey(KeyEventArgs e)
    {
        return e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
    }

    private static bool IsAltOnlyKey(Key key)
    {
        return key is Key.LeftAlt or Key.RightAlt or Key.System &&
               Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
    }

    private void HideOverlayContextMenu()
    {
        ContextMenuLayer.Visibility = Visibility.Collapsed;
        OverlayContextMenuItems.Children.Clear();
    }

    private async Task<bool> ShowConfirmAsync(string title, string message, string confirmText, string cancelText, bool danger)
    {
        var result = await ShowChoiceAsync(
            title,
            message,
            ("confirm", confirmText, danger),
            ("cancel", cancelText, false));
        return result == "confirm";
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        await ShowChoiceAsync(title, message, ("ok", "ОК", false));
    }

    private Task<string?> ShowChoiceAsync(string title, string message, params (string Id, string Text, bool Danger)[] choices)
    {
        HideOverlayContextMenu();
        _modalCompletion = new TaskCompletionSource<string?>();
        ModalTitleText.Text = title;
        ModalMessageText.Text = message;
        ModalButtonsPanel.Children.Clear();
        var isChoiceList = choices.Any(choice => choice.Text.Contains(Environment.NewLine, StringComparison.Ordinal));
        ModalButtonsPanel.Orientation = isChoiceList ? Orientation.Vertical : Orientation.Horizontal;
        ModalButtonsPanel.HorizontalAlignment = isChoiceList ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;

        foreach (var choice in choices)
        {
            var button = new Button
            {
                Content = isChoiceList && choice.Id != "cancel"
                    ? BuildChoiceRowContent(choice.Text)
                    : choice.Text,
                MinWidth = isChoiceList ? 520 : 110,
                MinHeight = isChoiceList && choice.Id != "cancel" ? 64 : 0,
                Margin = isChoiceList ? new Thickness(0, 0, 0, 8) : new Thickness(8, 0, 0, 0),
                HorizontalContentAlignment = isChoiceList ? HorizontalAlignment.Left : HorizontalAlignment.Center,
                Style = (Style)Application.Current.Resources[choice.Danger ? "DangerButtonStyle" : "ActionButtonStyle"],
                Tag = choice.Id
            };
            button.Click += (_, _) =>
            {
                LogDragDebug($"Button clicked: {choice.Text}", button);
                CompleteModal((string)button.Tag);
            };
            ModalButtonsPanel.Children.Add(button);
        }

        ModalOverlay.Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(() =>
        {
            var defaultButton = ModalButtonsPanel.Children
                .OfType<Button>()
                .FirstOrDefault(button => button.Tag is "cancel") ??
                ModalButtonsPanel.Children.OfType<Button>().FirstOrDefault();
            if (defaultButton is not null)
            {
                defaultButton.Focus();
            }
        }, DispatcherPriority.Input);
        return _modalCompletion.Task;
    }

    private static StackPanel BuildChoiceRowContent(string text)
    {
        var parts = text.Split(Environment.NewLine, 2, StringSplitOptions.None);
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        panel.Children.Add(new TextBlock
        {
            Text = parts[0],
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap
        });

        if (parts.Length > 1)
        {
            panel.Children.Add(new TextBlock
            {
                Text = parts[1],
                Margin = new Thickness(0, 4, 0, 0),
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                TextWrapping = TextWrapping.Wrap
            });
        }

        return panel;
    }

    private void CompleteModal(string? result)
    {
        if (_modalCompletion is null)
        {
            return;
        }

        ModalOverlay.Visibility = Visibility.Collapsed;
        ModalButtonsPanel.Orientation = Orientation.Horizontal;
        ModalButtonsPanel.HorizontalAlignment = HorizontalAlignment.Right;
        ModalButtonsPanel.Children.Clear();
        _modalCompletion.TrySetResult(result);
        _modalCompletion = null;
        FocusItemsHost();
    }

    private void ShowOperation(NoteOperationResult result)
    {
        ShowStatus(result.Message, !result.Success);
    }

    private void ToggleDragDebugOverlay()
    {
        _isDragDebugEnabled = !_isDragDebugEnabled;
        OnPropertyChanged(nameof(DragDebugVisibility));
        LogDragDebug("DebugOverlay toggled", this, _isDragDebugEnabled ? "enabled" : "disabled", force: true);
    }

    private void LogDragDebug(string eventName, object? source = null, string? detail = null, bool force = false)
    {
        if (!_isDragDebugEnabled && !force)
        {
            return;
        }

        var now = DateTime.Now.ToString("HH:mm:ss.fff");
        var captured = Mouse.Captured;
        var capturedName = captured is FrameworkElement capturedElement
            ? $"{captured.GetType().Name}:{capturedElement.Name}"
            : captured?.GetType().Name ?? "null";
        var rootHasCapture = RootGrid?.IsMouseCaptured == true || RootGrid?.IsMouseCaptureWithin == true;
        var cardHasCapture = source is DependencyObject dependencyObject &&
                             FindParent<Border>(dependencyObject) is { IsMouseCaptureWithin: true };
        var position = GetDebugMousePosition();
        var dragged = DescribeDraggedItem();
        var target = DescribeTarget(source);
        var dropTarget = Items.OfType<FolderItem>().FirstOrDefault(folder => folder.IsDropTarget)?.FullPath ?? "<none>";
        var line =
            $"{now} | {eventName} | dragging={_isDragging} keyboard={_isKeyboardDragging} left={Mouse.LeftButton} " +
            $"dragged={dragged} placeholder={_currentPlaceholderIndex} target={target} dropTarget={dropTarget} " +
            $"pos={position} captured={capturedName} rootCapture={rootHasCapture} cardCapture={cardHasCapture}" +
            (string.IsNullOrWhiteSpace(detail) ? string.Empty : $" | {detail}");

        DragDebugStatus =
            $"IsDragging: {_isDragging} | LeftButton: {Mouse.LeftButton} | Captured: {capturedName} | " +
            $"PlaceholderIndex: {_currentPlaceholderIndex} | Dragged: {dragged}";

        _dragDebugEvents.Add(line);
        while (_dragDebugEvents.Count > 40)
        {
            _dragDebugEvents.RemoveAt(0);
        }

        try
        {
            File.AppendAllText(DragDebugLogPath, line + Environment.NewLine);
        }
        catch
        {
            // Debug logging must never affect the app under investigation.
        }
    }

    private string GetDebugMousePosition()
    {
        try
        {
            if (RootGrid is null)
            {
                return "<no-root>";
            }

            var point = Mouse.GetPosition(RootGrid);
            return $"{point.X:0.0},{point.Y:0.0}";
        }
        catch
        {
            return "<unknown>";
        }
    }

    private string DescribeDraggedItem()
    {
        if (string.IsNullOrWhiteSpace(_draggedPath))
        {
            return "<none>";
        }

        var displayName = Path.GetFileNameWithoutExtension(_draggedPath);
        return $"{_draggedKind ?? "?"}:{displayName}|{_draggedPath}";
    }

    private string DescribeTarget(object? source)
    {
        var item = GetItemFromSource(source);
        return item switch
        {
            NoteItem note => $"note:{note.Title}|{note.FullPath}",
            FolderItem folder => $"folder:{folder.Name}|{folder.FullPath}",
            DragPlaceholderItem => "placeholder",
            _ => source is FrameworkElement element
                ? $"{source.GetType().Name}:{element.Name}"
                : source?.GetType().Name ?? "<null>"
        };
    }

    public sealed class PreviewTile(string icon, string name, string subtitle)
    {
        public string Icon { get; } = icon;

        public string Name { get; } = name;

        public string Subtitle { get; } = subtitle;

        public Visibility FolderIconVisibility => Icon == "DIR" ? Visibility.Visible : Visibility.Collapsed;

        public Visibility NoteIconVisibility => Icon == "TXT" ? Visibility.Visible : Visibility.Collapsed;

        public Visibility FallbackIconVisibility => Icon is "DIR" or "TXT" ? Visibility.Collapsed : Visibility.Visible;
    }

    private sealed class NotesFolderCandidate(
        string candidatePath,
        int txtCount,
        bool hasOrderFile,
        DateTime lastModified,
        int score)
    {
        public string CandidatePath { get; } = candidatePath;

        public int TxtCount { get; } = txtCount;

        public bool HasOrderFile { get; } = hasOrderFile;

        public DateTime LastModified { get; } = lastModified;

        public int Score { get; } = score;
    }

    public sealed class FolderChoiceItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private Brush _selectionBrush = Brushes.Transparent;

        public FolderChoiceItem(FolderItem folder)
        {
            Folder = folder;
            Name = string.IsNullOrWhiteSpace(folder.RelativePath) ? "Корень заметок" : folder.Name;
            Subtitle = folder.ItemCount == 0 ? "Папка пустая" : folder.DisplayItemCount;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public FolderItem Folder { get; }

        public string Name { get; }

        public string Subtitle { get; }

        public Brush BorderBrush => IsSelected ? SelectionBrush : Brushes.Transparent;

        public Brush BackgroundBrush => IsSelected
            ? new SolidColorBrush(Color.FromArgb(255, 48, 48, 62))
            : new SolidColorBrush(Color.FromArgb(240, 36, 36, 43));

        public Brush SelectionBrush
        {
            get => _selectionBrush;
            set
            {
                _selectionBrush = value;
                OnPropertyChanged(nameof(BorderBrush));
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
                OnPropertyChanged(nameof(BorderBrush));
                OnPropertyChanged(nameof(BackgroundBrush));
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
