using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SectorRemovalUpdater.Gui.Services;
using SectorRemovalUpdater.Services;

namespace SectorRemovalUpdater.Gui.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private const int MaxLogEntries = 3000;
    private const int MaxLogLinesPerFlush = 750;

    private readonly SettingsService _settings;
    private readonly DatabaseService _database;
    private readonly GitHubReleaseService _releaseService = new();
    private readonly ConcurrentQueue<LogEntry> _pendingLogs = new();
    private readonly DispatcherTimer _logTimer;

    public MainWindowViewModel()
    {
        _settings = SettingsService.Instance;
        _database = DatabaseService.Instance;

        _logTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _logTimer.Tick += (_, _) => FlushPendingLogs();
        _logTimer.Start();

        UiLog.LineWritten += OnConsoleLine;
        Versions.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasVersions));
            OnPropertyChanged(nameof(HasNoVersions));
        };
    }

    public ObservableCollection<string> Versions { get; } = new();

    public ObservableCollection<ReleaseAsset> ReleaseAssets { get; } = new();

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    /// <summary>Raised after a batch of log lines was added, removed or cleared.</summary>
    public event EventHandler? LogUpdated;

    /// <summary>Set by the view when the user accepted closing during a running operation.</summary>
    public bool CloseConfirmed { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateFileCommand))]
    private string inputPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateFileCommand))]
    private string outputPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(SwapVersionsCommand))]
    private string? selectedSourceVersion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(SwapVersionsCommand))]
    private string? selectedTargetVersion;

    [ObservableProperty]
    private string? selectedDatabaseVersion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadReleaseAssetCommand))]
    private ReleaseAsset? selectedReleaseAsset;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshReleaseAssetsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadReleaseAssetCommand))]
    private bool isReleaseListLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshReleaseAssetsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadReleaseAssetCommand))]
    private bool isReleaseDownloading;

    [ObservableProperty]
    private double releaseDownloadProgress;

    [ObservableProperty]
    private string releaseStatus = "Loading the list of precompiled databases from GitHub...";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildHashesCommand))]
    private string gamePath = string.Empty;

    [ObservableProperty]
    private string databasePath = string.Empty;

    [ObservableProperty]
    private bool enableMods;

    [ObservableProperty]
    private decimal? maxSectorDepth = 2m;

    [ObservableProperty]
    private decimal? minimumActorHashMatchRate = 0.9m;

    [ObservableProperty]
    private bool verboseLogging;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(UpdateFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildHashesCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveSettingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadVersionsCommand))]
    [NotifyCanExecuteChangedFor(nameof(SwapVersionsCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshReleaseAssetsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadReleaseAssetCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = "Ready.";

    [ObservableProperty]
    private bool isConfirmCloseVisible;

    [ObservableProperty]
    private bool autoScroll = true;

    [ObservableProperty]
    private string databaseSummary = string.Empty;

    [ObservableProperty]
    private string versionStats = string.Empty;

    [ObservableProperty]
    private string gamePathStatus = string.Empty;

    [ObservableProperty]
    private string platformText = string.Empty;

    [ObservableProperty]
    private string appVersion = string.Empty;

    [ObservableProperty]
    private string selectedVersionDisplay = "No version selected";

    public bool IsNotBusy => !IsBusy;

    public bool HasVersions => Versions.Count > 0;

    public bool HasNoVersions => !HasVersions;

    public bool HasLogEntries => LogEntries.Count > 0;

    public string LogCountText => LogEntries.Count == 0 ? "empty" : $"{LogEntries.Count} lines";

    public string GamePathDisplay => string.IsNullOrWhiteSpace(GamePath) ? "Not configured" : GamePath;

    public void Initialize()
    {
        AppVersion = $"GUI {GetAssemblyVersion(GetType().Assembly)} • core {GetAssemblyVersion(typeof(SettingsService).Assembly)}";
        PlatformText = $"{RuntimeInformation.OSDescription} • {RuntimeInformation.ProcessArchitecture} • .NET {Environment.Version}";

        LoadSettingsFromService();

        AddLog("Sector Removal Updater started.", LogLevel.Success);
        AddLog($"Settings file: {_settings.SettingsFilePath}");
        AddLog($"Platform: {PlatformText}");

        EnsureDatabase();
        RefreshVersionList();
        UpdateGamePathStatus();

        StatusMessage = HasVersions
            ? "Ready."
            : "No hash database loaded. Build or load one to get started.";

        _ = LoadReleaseAssetsAsync(logErrors: false);
    }

    public void Shutdown()
    {
        _logTimer.Stop();
        UiLog.LineWritten -= OnConsoleLine;
        _database.Shutdown();
        UiLog.Uninstall();
    }

    public void SetInputFile(string path)
    {
        var previousSuggestion = string.IsNullOrWhiteSpace(InputPath)
            ? null
            : SuggestOutputPath(InputPath);

        InputPath = path;

        var suggestion = SuggestOutputPath(path);
        if (string.IsNullOrWhiteSpace(OutputPath) || PathsEqual(OutputPath, previousSuggestion))
            OutputPath = suggestion;
    }

    public async Task LoadDatabaseFromFileAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        SyncSettings();
        if (!EnsureDatabase())
        {
            Fail("The hash database is not available.");
            return;
        }

        string? loadedVersion = null;
        await RunBusyAsync(
            $"Loading database from {Path.GetFileName(path)}...",
            () => Task.Run(() => loadedVersion = _database.LoadDatabaseFromFile(path)),
            "Database loaded.");

        RefreshVersionList();
        SelectLoadedVersion(loadedVersion);
    }

    public async Task SaveDatabaseToFileAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (string.IsNullOrWhiteSpace(SelectedDatabaseVersion))
        {
            Fail("Select a database version to save first.");
            return;
        }

        SyncSettings();
        if (!EnsureDatabase())
        {
            Fail("The hash database is not available.");
            return;
        }

        var version = SelectedDatabaseVersion;
        await RunBusyAsync(
            $"Saving database version {version}...",
            () => Task.Run(() => _database.WriteDataBaseToFile(version, path)),
            $"Database saved to {Path.GetFileName(path)}.");
    }

    [RelayCommand(CanExecute = nameof(CanRefreshReleaseAssets))]
    private Task RefreshReleaseAssetsAsync() => LoadReleaseAssetsAsync(logErrors: true);

    [RelayCommand(CanExecute = nameof(CanDownloadReleaseAsset))]
    private async Task DownloadReleaseAssetAsync()
    {
        var asset = SelectedReleaseAsset;
        if (asset is null)
        {
            Fail("Select a precompiled hash database to download first.");
            return;
        }

        SyncSettings();
        if (!EnsureDatabase())
        {
            Fail("The hash database is not available.");
            return;
        }

        var succeeded = false;
        var temporaryFileDeleted = false;
        string? loadedVersion = null;

        IsReleaseDownloading = true;
        ReleaseDownloadProgress = 0;
        ReleaseStatus = $"Downloading {asset.Name}...";

        try
        {
            succeeded = await RunBusyAsync(
                $"Downloading {asset.Name} from GitHub...",
                async () =>
                {
                    var progress = new Progress<DownloadProgress>(value =>
                    {
                        if (!IsReleaseDownloading)
                            return;

                        if (value.Total is > 0 && value.Received >= value.Total)
                        {
                            ReleaseDownloadProgress = 100;
                            ReleaseStatus = $"Importing {asset.Name} into the local hash database...";
                            StatusMessage = ReleaseStatus;
                            return;
                        }

                        ReleaseDownloadProgress = value.Total is > 0
                            ? value.Received * 100d / value.Total.Value
                            : 0d;

                        var totalText = value.Total is > 0
                            ? $" of {ReleaseAsset.FormatSize(value.Total.Value)}"
                            : string.Empty;

                        ReleaseStatus =
                            $"Downloading {asset.Name}: {ReleaseAsset.FormatSize(value.Received)}{totalText} " +
                            $"({ReleaseDownloadProgress:0.0}%)";
                        StatusMessage = ReleaseStatus;
                    });

                    var result = await _releaseService.DownloadAndLoadDatabaseAsync(_database, asset, progress);
                    loadedVersion = result.GameVersion;
                    temporaryFileDeleted = result.TemporaryFileDeleted;

                    AddLog(
                        $"Downloaded and imported {asset.Name} ({ReleaseAsset.FormatSize(asset.Size)}).",
                        LogLevel.Success);
                },
                $"Downloaded and imported {asset.Name}.");

            if (succeeded)
            {
                RefreshVersionList();
                SelectLoadedVersion(loadedVersion);
            }
        }
        finally
        {
            IsReleaseDownloading = false;
            ReleaseDownloadProgress = 0;
        }

        if (succeeded)
        {
            var loadedText = string.IsNullOrWhiteSpace(loadedVersion)
                ? $"Imported {asset.Name}."
                : $"Loaded version {loadedVersion} from {asset.Name}.";

            ReleaseStatus = temporaryFileDeleted
                ? $"{loadedText} The downloaded file was deleted."
                : $"{loadedText} The temporary download could not be deleted.";
        }
        else
        {
            ReleaseStatus = $"Could not download or import {asset.Name}: {StatusMessage}";
        }
    }

    public string GetLogText()
    {
        while (!_pendingLogs.IsEmpty)
            FlushPendingLogs();

        return string.Join(Environment.NewLine, LogEntries.Select(entry => entry.Display));
    }

    public void RequestCloseConfirmation()
    {
        IsConfirmCloseVisible = true;
    }

    public void ReportError(string message)
    {
        Fail(message);
    }

    public void ReportSuccess(string message)
    {
        StatusMessage = message;
        AddLog(message, LogLevel.Success);
    }

    [RelayCommand(CanExecute = nameof(CanUpdateFile))]
    private async Task UpdateFileAsync()
    {
        if (!ValidateUpdateRequest(out var error))
        {
            Fail(error);
            return;
        }

        SyncSettings();
        if (!EnsureDatabase())
        {
            Fail("The hash database is not available.");
            return;
        }

        var input = InputPath;
        var output = OutputPath;
        var sourceVersion = SelectedSourceVersion!;
        var targetVersion = SelectedTargetVersion!;

        await RunBusyAsync(
            "Updating ArchiveXL file...",
            () => Task.Run(() =>
            {
                var processingService = new XlProcessingService();
                processingService.Process(input, output, sourceVersion, targetVersion);
            }),
            $"Updated file saved to {Path.GetFileName(output)}.");

        AddLog($"Updated ArchiveXL file written to {output}", LogLevel.Success);
    }

    [RelayCommand(CanExecute = nameof(CanSwapVersions))]
    private void SwapVersions()
    {
        (SelectedSourceVersion, SelectedTargetVersion) = (SelectedTargetVersion, SelectedSourceVersion);
    }

    [RelayCommand(CanExecute = nameof(CanBuildHashes))]
    private async Task BuildHashesAsync()
    {
        var executablePath = Path.Combine(GamePath, "bin", "x64", "Cyberpunk2077.exe");
        if (!File.Exists(executablePath))
        {
            Fail("Cyberpunk2077.exe was not found. Set the correct game folder in Settings.");
            return;
        }

        SyncSettings();
        if (!EnsureDatabase())
        {
            Fail("The hash database is not available.");
            return;
        }

        await RunBusyAsync(
            "Building hash database from game archives. This can take several minutes...",
            () => Task.Run(async () =>
            {
                if (WolvenKitWrapper.IsInitialized &&
                    !PathsEqual(WolvenKitWrapper.Instance.GameExePath, executablePath))
                {
                    throw new InvalidOperationException(
                        "The archive manager is already loaded for a different game folder. Restart the app and try again.");
                }

                if (!WolvenKitWrapper.Initialize(executablePath, EnableMods))
                {
                    throw new InvalidOperationException(
                        "Could not open the game archives. Check the game folder and the activity log.");
                }

                var dataBuilder = new DataBuilder();
                dataBuilder.Initialize();
                await dataBuilder.BuildDataSet();
            }),
            "Hash build finished.");

        RefreshVersionList();
    }

    [RelayCommand(CanExecute = nameof(CanSaveSettings))]
    private void SaveSettings()
    {
        try
        {
            SyncSettings();
            _settings.SaveSettings();
            EnsureDatabase();
            RefreshVersionList();
            StatusMessage = "Settings saved.";
            AddLog("Settings saved.", LogLevel.Success);
        }
        catch (Exception e)
        {
            Fail($"Could not save settings: {e.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanReloadVersions))]
    private void ReloadVersions()
    {
        if (!EnsureDatabase())
            return;

        RefreshVersionList();
        StatusMessage = $"Found {Versions.Count} database version(s).";
    }

    [RelayCommand]
    private void ClearLog()
    {
        while (_pendingLogs.TryDequeue(out _))
        {
        }

        LogEntries.Clear();
        OnPropertyChanged(nameof(HasLogEntries));
        OnPropertyChanged(nameof(LogCountText));
        LogUpdated?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        var directory = Path.GetDirectoryName(_settings.SettingsFilePath);
        OpenFolder(directory, "settings");
    }

    [RelayCommand]
    private void OpenDatabaseFolder()
    {
        var directory = _database.IsInitialized ? _database.EnvironmentPath : DatabasePath;
        OpenFolder(directory, "database");
    }

    [RelayCommand]
    private void CancelCloseConfirmation()
    {
        IsConfirmCloseVisible = false;
    }

    partial void OnGamePathChanged(string value)
    {
        OnPropertyChanged(nameof(GamePathDisplay));
        UpdateGamePathStatus();
    }

    partial void OnSelectedDatabaseVersionChanged(string? value)
    {
        SelectedVersionDisplay = string.IsNullOrWhiteSpace(value) ? "No version selected" : value;
        UpdateVersionStats();
    }

    private bool CanUpdateFile()
    {
        return !IsBusy
               && !string.IsNullOrWhiteSpace(InputPath)
               && !string.IsNullOrWhiteSpace(OutputPath)
               && !string.IsNullOrWhiteSpace(SelectedSourceVersion)
               && !string.IsNullOrWhiteSpace(SelectedTargetVersion);
    }

    private bool CanSwapVersions()
    {
        return !IsBusy
               && !string.IsNullOrWhiteSpace(SelectedSourceVersion)
               && !string.IsNullOrWhiteSpace(SelectedTargetVersion);
    }

    private bool CanBuildHashes() => !IsBusy && !string.IsNullOrWhiteSpace(GamePath);

    private bool CanSaveSettings() => !IsBusy;

    private bool CanReloadVersions() => !IsBusy;

    private bool CanRefreshReleaseAssets()
    {
        return !IsBusy && !IsReleaseListLoading && !IsReleaseDownloading;
    }

    private bool CanDownloadReleaseAsset()
    {
        return !IsBusy
               && !IsReleaseListLoading
               && !IsReleaseDownloading
               && SelectedReleaseAsset != null;
    }

    private void LoadSettingsFromService()
    {
        GamePath = _settings.GamePath ?? string.Empty;
        DatabasePath = _settings.DatabasePath ?? string.Empty;
        EnableMods = _settings.EnableMods;
        MaxSectorDepth = Clamp((decimal)_settings.MaxSectorDepth, 0m, 10m, 2m);
        MinimumActorHashMatchRate = Clamp((decimal)_settings.MinimumActorHashMatchRate, 0m, 1m, 0.9m);
        VerboseLogging = _settings.VerboseLogging;
    }

    private void SyncSettings()
    {
        _settings.GamePath = (GamePath ?? string.Empty).Trim();

        var databasePath = (DatabasePath ?? string.Empty).Trim();
        if (databasePath.Length > 0)
        {
            try
            {
                databasePath = Path.GetFullPath(databasePath);
            }
            catch
            {
                // Keep the user-entered value; Initialize will report the problem.
            }
        }

        _settings.DatabasePath = databasePath;
        DatabasePath = databasePath;

        _settings.EnableMods = EnableMods;
        _settings.MaxSectorDepth = (int)Clamp(MaxSectorDepth ?? 2m, 0m, 10m, 2m);
        _settings.MinimumActorHashMatchRate = (double)Clamp(MinimumActorHashMatchRate ?? 0.9m, 0m, 1m, 0.9m);
        _settings.VerboseLogging = VerboseLogging;
    }

    private async Task LoadReleaseAssetsAsync(bool logErrors)
    {
        if (IsReleaseListLoading || IsReleaseDownloading)
            return;

        var previousSelection = SelectedReleaseAsset?.Name;

        IsReleaseListLoading = true;
        ReleaseStatus = "Loading the list of precompiled databases from GitHub...";

        try
        {
            var assets = await _releaseService.GetAssetsAsync();

            ReleaseAssets.Clear();
            foreach (var asset in assets)
                ReleaseAssets.Add(asset);

            if (ReleaseAssets.Count == 0)
            {
                SelectedReleaseAsset = null;
                ReleaseStatus = "The PrecompiledBinaries release does not contain any .bin files.";
                return;
            }

            SelectedReleaseAsset = ReleaseAssets.FirstOrDefault(asset =>
                string.Equals(asset.Name, previousSelection, StringComparison.OrdinalIgnoreCase))
                ?? ReleaseAssets[^1];

            ReleaseStatus = $"Found {ReleaseAssets.Count} precompiled hash database(s) on GitHub.";
            AddLog(ReleaseStatus, LogLevel.Success);
        }
        catch (Exception e)
        {
            ReleaseStatus = $"Could not load the precompiled database list: {e.Message}";
            if (logErrors)
                AddLog(ReleaseStatus, LogLevel.Warning);
        }
        finally
        {
            IsReleaseListLoading = false;
        }
    }

    private void SelectLoadedVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version) || !Versions.Contains(version))
            return;

        SelectedDatabaseVersion = version;
    }

    private bool EnsureDatabase()
    {
        var path = (DatabasePath ?? string.Empty).Trim();
        if (path.Length == 0)
        {
            AddLog("Database folder is not configured.", LogLevel.Error);
            DatabaseSummary = "Database folder is not configured.";
            return false;
        }

        try
        {
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(path);

            if (_database.IsInitialized && PathsEqual(_database.EnvironmentPath, path))
            {
                UpdateDatabaseSummary();
                return true;
            }

            if (_database.IsInitialized)
                _database.Shutdown();

            _database.Initialize(path);
            if (!_database.IsInitialized)
            {
                DatabaseSummary = "Failed to open the hash database. See the log for details.";
                AddLog("Failed to open the hash database.", LogLevel.Error);
                return false;
            }

            DatabasePath = path;
            UpdateDatabaseSummary();
            AddLog($"Hash database opened at {path}", LogLevel.Success);
            return true;
        }
        catch (Exception e)
        {
            AddLog($"Failed to open the hash database: {e.Message}", LogLevel.Error);
            DatabaseSummary = "Failed to open the hash database.";
            return false;
        }
    }

    private void RefreshVersionList()
    {
        if (!_database.IsInitialized && !EnsureDatabase())
            return;

        var previousSource = SelectedSourceVersion;
        var previousTarget = SelectedTargetVersion;
        var previousDatabase = SelectedDatabaseVersion;

        Versions.Clear();
        if (_database.IsInitialized)
        {
            foreach (var version in SortVersions(_database.GetDatabaseNames()))
                Versions.Add(version);
        }

        SelectedSourceVersion = ResolveVersion(previousSource) ?? Versions.FirstOrDefault();
        SelectedTargetVersion = ResolveVersion(previousTarget) ?? Versions.LastOrDefault();
        SelectedDatabaseVersion = ResolveVersion(previousDatabase) ?? Versions.LastOrDefault();

        if (Versions.Count == 1)
            SelectedTargetVersion = Versions[0];

        UpdateDatabaseSummary();
        UpdateVersionStats();
        OnPropertyChanged(nameof(HasVersions));
        OnPropertyChanged(nameof(HasNoVersions));
        NotifyCanExecuteChanged();
    }

    private string? ResolveVersion(string? version)
    {
        return !string.IsNullOrWhiteSpace(version) && Versions.Contains(version) ? version : null;
    }

    private static IEnumerable<string> SortVersions(IEnumerable<string> versions)
    {
        return versions
            .OrderBy(version =>
            {
                Version.TryParse(version, out var parsed);
                return parsed ?? new Version(0, 0);
            })
            .ThenBy(version => version, StringComparer.OrdinalIgnoreCase);
    }

    private void UpdateDatabaseSummary()
    {
        var path = _database.IsInitialized ? _database.EnvironmentPath ?? DatabasePath : DatabasePath;
        var count = Versions.Count;
        var versionsText = count == 1 ? "1 game version" : $"{count} game versions";
        DatabaseSummary = $"{versionsText} • {path}";
    }

    private void UpdateVersionStats()
    {
        if (!_database.IsInitialized || string.IsNullOrWhiteSpace(SelectedDatabaseVersion))
        {
            VersionStats = "Select a version to see its statistics.";
            return;
        }

        try
        {
            var (pageSize, entries) = _database.GetStats(SelectedDatabaseVersion);
            VersionStats = $"{entries:N0} sector entries • LMDB page size {pageSize:N0} bytes";
        }
        catch (Exception e)
        {
            VersionStats = $"Could not read statistics: {e.Message}";
        }
    }

    private void UpdateGamePathStatus()
    {
        if (string.IsNullOrWhiteSpace(GamePath))
        {
            GamePathStatus = "No game folder selected.";
            return;
        }

        var executablePath = Path.Combine(GamePath, "bin", "x64", "Cyberpunk2077.exe");
        if (!File.Exists(executablePath))
        {
            GamePathStatus = "Cyberpunk2077.exe was not found under bin/x64 in this folder.";
            return;
        }

        try
        {
            var version = FileVersionInfo.GetVersionInfo(executablePath).ProductVersion;
            GamePathStatus = string.IsNullOrWhiteSpace(version)
                ? "Game executable found."
                : $"Game executable found (version {version}).";
        }
        catch
        {
            GamePathStatus = "Game executable found.";
        }
    }

    private bool ValidateUpdateRequest(out string error)
    {
        if (!_database.IsInitialized && !EnsureDatabase())
        {
            error = "The hash database is not available. Load or build a database first.";
            return false;
        }

        if (!File.Exists(InputPath))
        {
            error = "Select an existing ArchiveXL file to update.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(SelectedSourceVersion) || string.IsNullOrWhiteSpace(SelectedTargetVersion))
        {
            error = "Select both a source and a target game version.";
            return false;
        }

        if (!Versions.Contains(SelectedSourceVersion) || !Versions.Contains(SelectedTargetVersion))
        {
            error = "The selected game version is not loaded in the database.";
            return false;
        }

        if (PathsEqual(InputPath, OutputPath))
        {
            error = "The output path must be different from the input path.";
            return false;
        }

        try
        {
            _ = Path.GetFullPath(OutputPath);
        }
        catch (Exception e)
        {
            error = $"The output path is not valid: {e.Message}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private async Task<bool> RunBusyAsync(string busyMessage, Func<Task> action, string? successMessage = null)
    {
        if (IsBusy)
            return false;

        IsBusy = true;
        StatusMessage = busyMessage;
        var succeeded = false;

        try
        {
            await action();
            succeeded = true;

            if (!string.IsNullOrWhiteSpace(successMessage))
                StatusMessage = successMessage;
        }
        catch (Exception e)
        {
            StatusMessage = e.Message;
            AddLog($"ERROR: {e.Message}", LogLevel.Error);
            if (e.InnerException != null)
                AddLog($"  {e.InnerException.Message}", LogLevel.Error);
        }
        finally
        {
            IsBusy = false;
            NotifyCanExecuteChanged();
            FlushPendingLogs();
        }

        return succeeded;
    }

    private void OnConsoleLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        _pendingLogs.Enqueue(new LogEntry(DateTime.Now, ClassifyConsoleLine(line), line.TrimEnd()));
    }

    private static LogLevel ClassifyConsoleLine(string line)
    {
        if (line.Contains("error", StringComparison.OrdinalIgnoreCase)
            || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || line.Contains("exception", StringComparison.OrdinalIgnoreCase))
        {
            return LogLevel.Error;
        }

        if (line.Contains("warning", StringComparison.OrdinalIgnoreCase))
            return LogLevel.Warning;

        if (line.Contains("finished", StringComparison.OrdinalIgnoreCase)
            || line.Contains("saved", StringComparison.OrdinalIgnoreCase)
            || line.Contains("loaded", StringComparison.OrdinalIgnoreCase))
        {
            return LogLevel.Success;
        }

        return LogLevel.Info;
    }

    private void AddLog(string message, LogLevel level = LogLevel.Info)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var lines = message
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        foreach (var line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line))
                _pendingLogs.Enqueue(new LogEntry(DateTime.Now, level, line.TrimEnd()));
        }
    }

    private void FlushPendingLogs()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(FlushPendingLogs);
            return;
        }

        var added = false;
        var budget = MaxLogLinesPerFlush;

        while (budget-- > 0 && _pendingLogs.TryDequeue(out var entry))
        {
            LogEntries.Add(entry);
            added = true;
        }

        if (!added)
            return;

        var overflow = LogEntries.Count - MaxLogEntries;
        for (var i = 0; i < overflow; i++)
            LogEntries.RemoveAt(0);

        OnPropertyChanged(nameof(HasLogEntries));
        OnPropertyChanged(nameof(LogCountText));
        LogUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void Fail(string message)
    {
        StatusMessage = message;
        AddLog(message, LogLevel.Error);
    }

    private void NotifyCanExecuteChanged()
    {
        UpdateFileCommand.NotifyCanExecuteChanged();
        SwapVersionsCommand.NotifyCanExecuteChanged();
        BuildHashesCommand.NotifyCanExecuteChanged();
        SaveSettingsCommand.NotifyCanExecuteChanged();
        ReloadVersionsCommand.NotifyCanExecuteChanged();
        RefreshReleaseAssetsCommand.NotifyCanExecuteChanged();
        DownloadReleaseAssetCommand.NotifyCanExecuteChanged();
    }

    private void OpenFolder(string? directory, string description)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;

        try
        {
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
        }
        catch (Exception e)
        {
            Fail($"Could not open the {description} folder: {e.Message}");
        }
    }

    private static string SuggestOutputPath(string inputPath)
    {
        var directory = Path.GetDirectoryName(inputPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(inputPath);
        var extension = Path.GetExtension(inputPath);

        if (string.IsNullOrWhiteSpace(name))
            name = "updated";

        if (string.IsNullOrWhiteSpace(extension))
            extension = ".xl.json";

        return Path.Combine(directory, $"{name}.updated{extension}");
    }

    private static string GetAssemblyVersion(System.Reflection.Assembly assembly)
    {
        var version = assembly.GetName().Version;
        return version == null ? "dev" : version.ToString(3);
    }

    private static decimal Clamp(decimal value, decimal minimum, decimal maximum, decimal fallback)
    {
        if (value < minimum || value > maximum)
            return fallback;

        return value;
    }

    private static bool PathsEqual(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            return false;

        try
        {
            var left = Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var right = Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return string.Equals(left, right, comparison);
        }
        catch
        {
            return false;
        }
    }
}
