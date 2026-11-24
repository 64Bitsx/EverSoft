using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EverSoft.App.Views;
using EverSoft.Core.Util;

namespace EverSoft.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Abre com a tela de carregamento (logo) enquanto lê as planilhas;
            // a janela principal só aparece quando tudo estiver pronto.
            var splash = new SplashWindow("Carregando planilhas...");
            desktop.MainWindow = splash;
            splash.Opened += async (_, _) =>
            {
                var janela = new MainWindow { IniciarAoAbrir = false };
                try { await janela.ViewModel.IniciarAsync(); }
                catch (Exception e) { Log.Erro("abertura: " + e); }
                desktop.MainWindow = janela;
                janela.Show();
                splash.Close();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
