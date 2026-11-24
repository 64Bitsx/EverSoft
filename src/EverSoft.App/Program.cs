using Avalonia;

namespace EverSoft.App;

internal static class Program
{
    // Ponto de entrada do programa (como o "if __name__ == '__main__': main()" do Python).
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
