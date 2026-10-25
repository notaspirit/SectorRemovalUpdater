using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.FontAwesome;
using SectorRemovalUpdater.Gui.Services;
using SectorRemovalUpdater.Gui.ViewModels;
using SectorRemovalUpdater.Gui.Views;

namespace SectorRemovalUpdater.Gui;

public partial class App : Application
{
    public override void Initialize()
    {
        IconProvider.Current.Register<FontAwesomeIconProvider>();
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Redirect Console output from the shared services into the GUI log view.
            UiLog.Install();

            var viewModel = new MainWindowViewModel();
            viewModel.Initialize();

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };

            desktop.Exit += (_, _) => viewModel.Shutdown();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
