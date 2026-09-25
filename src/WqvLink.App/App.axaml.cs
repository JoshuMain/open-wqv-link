using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WqvLink.App.ViewModels;
using WqvLink.App.Views;
using WqvLink.Core.Storage;

namespace WqvLink.App;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // A dump.bin or session folder passed on the command line
            var open = desktop.Args?.FirstOrDefault(a => File.Exists(a) || Directory.Exists(a));
            desktop.MainWindow = new MainWindow(new MainViewModel(Settings.Load()), open);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
