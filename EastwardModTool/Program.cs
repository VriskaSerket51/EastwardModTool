using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using EastwardModTool.Views;

namespace EastwardModTool;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var lifetime = new ClassicDesktopStyleApplicationLifetime { Args = args, ShutdownMode = ShutdownMode.OnLastWindowClose };
        AppBuilder.Configure<App>().UsePlatformDetect().SetupWithLifetime(lifetime);
        lifetime.MainWindow = new MainWindow(args);
        lifetime.Start(args);
    }
}

public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://EastwardModTool/"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml")
        });
    }
}
