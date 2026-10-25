using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using SectorRemovalUpdater.Gui.ViewModels;

namespace SectorRemovalUpdater.Gui.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        Opened += OnOpened;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel != null)
            _viewModel.LogUpdated -= ViewModel_LogUpdated;

        base.OnDataContextChanged(e);

        _viewModel = DataContext as MainWindowViewModel;

        if (_viewModel != null)
            _viewModel.LogUpdated += ViewModel_LogUpdated;
    }

    private void ViewModel_LogUpdated(object? sender, EventArgs e)
    {
        if (_viewModel is not { AutoScroll: true } || _viewModel.LogEntries.Count == 0)
            return;

        LogList.ScrollIntoView(_viewModel.LogEntries.Count - 1);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_viewModel is not { IsBusy: true, CloseConfirmed: false })
            return;

        e.Cancel = true;
        _viewModel.RequestCloseConfirmation();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        ThemeToggle.IsChecked = ActualThemeVariant == ThemeVariant.Dark;
    }

    private void ThemeToggle_Changed(object? sender, RoutedEventArgs e)
    {
        if (Application.Current == null)
            return;

        Application.Current.RequestedThemeVariant =
            ThemeToggle.IsChecked == true ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private async void BrowseInput_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null)
            return;

        var options = new FilePickerOpenOptions
        {
            Title = "Select an ArchiveXL file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("ArchiveXL files")
                {
                    Patterns = new[] { "*.xl", "*.xl.json", "*.json", "*.yaml", "*.yml" }
                },
                FilePickerFileTypes.All
            }
        };

        var startFolder = await TryGetFolderAsync(Path.GetDirectoryName(_viewModel.InputPath));
        if (startFolder != null)
            options.SuggestedStartLocation = startFolder;

        var files = await StorageProvider.OpenFilePickerAsync(options);
        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;

        if (!string.IsNullOrWhiteSpace(path))
            _viewModel.SetInputFile(path);
    }

    private async void BrowseOutput_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null)
            return;

        var options = new FilePickerSaveOptions
        {
            Title = "Save updated ArchiveXL file",
            SuggestedFileName = string.IsNullOrWhiteSpace(_viewModel.OutputPath)
                ? "updated.xl.json"
                : Path.GetFileName(_viewModel.OutputPath),
            DefaultExtension = GetArchiveXlExtension(_viewModel.InputPath),
            ShowOverwritePrompt = true,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("ArchiveXL files")
                {
                    Patterns = new[] { "*.xl", "*.xl.json", "*.json", "*.yaml", "*.yml" }
                },
                FilePickerFileTypes.All
            }
        };

        var startFolder = await TryGetFolderAsync(Path.GetDirectoryName(_viewModel.InputPath));
        if (startFolder != null)
            options.SuggestedStartLocation = startFolder;

        var file = await StorageProvider.SaveFilePickerAsync(options);
        var path = file?.TryGetLocalPath();

        if (!string.IsNullOrWhiteSpace(path))
            _viewModel.OutputPath = path;
    }

    private async void BrowseGameFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null)
            return;

        var folder = await PickFolderAsync(
            "Select the Cyberpunk 2077 installation folder",
            _viewModel.GamePath);

        if (!string.IsNullOrWhiteSpace(folder))
            _viewModel.GamePath = folder;
    }

    private async void BrowseDatabaseFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null)
            return;

        var folder = await PickFolderAsync(
            "Select the hash database folder",
            _viewModel.DatabasePath);

        if (!string.IsNullOrWhiteSpace(folder))
            _viewModel.DatabasePath = folder;
    }

    private async void LoadDatabase_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null)
            return;

        var options = new FilePickerOpenOptions
        {
            Title = "Load hash database",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Sector Removal Updater database")
                {
                    Patterns = new[] { "*.bin" }
                },
                FilePickerFileTypes.All
            }
        };

        var startFolder = await TryGetFolderAsync(_viewModel.DatabasePath);
        if (startFolder != null)
            options.SuggestedStartLocation = startFolder;

        var files = await StorageProvider.OpenFilePickerAsync(options);
        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;

        if (!string.IsNullOrWhiteSpace(path))
            await _viewModel.LoadDatabaseFromFileAsync(path);
    }

    private async void SaveDatabase_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null)
            return;

        if (string.IsNullOrWhiteSpace(_viewModel.SelectedDatabaseVersion))
        {
            _viewModel.ReportError("Select a database version to save first.");
            return;
        }

        var options = new FilePickerSaveOptions
        {
            Title = "Save hash database version",
            SuggestedFileName = $"{_viewModel.SelectedDatabaseVersion}.bin",
            DefaultExtension = "bin",
            ShowOverwritePrompt = true,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Sector Removal Updater database")
                {
                    Patterns = new[] { "*.bin" }
                },
                FilePickerFileTypes.All
            }
        };

        var startFolder = await TryGetFolderAsync(_viewModel.DatabasePath);
        if (startFolder != null)
            options.SuggestedStartLocation = startFolder;

        var file = await StorageProvider.SaveFilePickerAsync(options);
        var path = file?.TryGetLocalPath();

        if (!string.IsNullOrWhiteSpace(path))
            await _viewModel.SaveDatabaseToFileAsync(path);
    }

    private async void CopyLog_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null)
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null)
        {
            _viewModel.ReportError("The clipboard is not available on this platform.");
            return;
        }

        await clipboard.SetTextAsync(_viewModel.GetLogText());
    }

    private async void SaveLog_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null)
            return;

        var options = new FilePickerSaveOptions
        {
            Title = "Save activity log",
            SuggestedFileName = $"sru-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            DefaultExtension = "txt",
            ShowOverwritePrompt = true,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Text files")
                {
                    Patterns = new[] { "*.txt", "*.log" }
                },
                FilePickerFileTypes.All
            }
        };

        var file = await StorageProvider.SaveFilePickerAsync(options);
        var path = file?.TryGetLocalPath();

        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            await File.WriteAllTextAsync(path, _viewModel.GetLogText());
            _viewModel.ReportSuccess($"Activity log saved to {path}.");
        }
        catch (Exception ex)
        {
            _viewModel.ReportError($"Could not save the activity log: {ex.Message}");
        }
    }

    private void CloseAnyway_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.CloseConfirmed = true;

        Close();
    }

    private async Task<IStorageFolder?> TryGetFolderAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return null;

        try
        {
            return await StorageProvider.TryGetFolderFromPathAsync(path);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> PickFolderAsync(string title, string? currentPath)
    {
        var options = new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        };

        var startFolder = await TryGetFolderAsync(currentPath);
        if (startFolder != null)
            options.SuggestedStartLocation = startFolder;

        var folders = await StorageProvider.OpenFolderPickerAsync(options);
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private static string GetArchiveXlExtension(string inputPath)
    {
        var extension = Path.GetExtension(inputPath);
        return string.IsNullOrWhiteSpace(extension) ? "json" : extension.TrimStart('.');
    }
}
