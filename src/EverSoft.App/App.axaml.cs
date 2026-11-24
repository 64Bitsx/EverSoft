using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EverSoft.App.ViewModels;
using EverSoft.App.Views;
using EverSoft.Core.Configuracao;
using EverSoft.Core.Servicos;
using EverSoft.Core.Util;

namespace EverSoft.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Abre com a tela de carregamento (logo) enquanto confere a versão e lê as planilhas;
            // a janela principal só aparece quando tudo estiver pronto.
            var splash = new SplashWindow("Abrindo...");
            desktop.MainWindow = splash;
            splash.Opened += async (_, _) =>
            {
                var cfg = await Task.Run(() => AppConfig.Carregar());
                if (await InstalarOuAtualizar(splash, cfg))
                {
                    desktop.Shutdown();          // a cópia de C:\EverSoft já foi aberta
                    return;
                }

                splash.Mensagem = "Carregando planilhas...";
                var janela = new MainWindow(new Nucleo(cfg)) { IniciarAoAbrir = false };
                try { await janela.ViewModel.IniciarAsync(); }
                catch (Exception e) { Log.Erro("abertura: " + e); }
                desktop.MainWindow = janela;
                janela.Show();
                splash.Close();

                if (Atualizacao.NovidadesNaoVistas(MainViewModel.Versao) is { } novidades)
                {
                    await janela.Mensagem($"Novidades da EverSoft {MainViewModel.Versao}:\n\n{novidades}");
                    Atualizacao.MarcarNovidadesVistas(MainViewModel.Versao);
                }
                if (Atualizacao.Ativa && Atualizacao.RodandoDaInstalacao)
                    janela.VerificarAtualizacoesACadaMinuto();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Devolve true quando abriu o EverSoft de C:\EverSoft e este processo deve fechar.
    /// </summary>
    private static async Task<bool> InstalarOuAtualizar(SplashWindow splash, AppConfig cfg)
    {
        if (!Atualizacao.Ativa) return false;
        string atual = Atualizacao.ExeAtual!;
        string instalado = Atualizacao.ExeInstalado;
        try
        {
            if (!Atualizacao.RodandoDaInstalacao)
            {
                // Aberto pela rede (ou outro lugar): garante a cópia em C:\EverSoft e abre ela.
                if (!File.Exists(instalado) || Atualizador.TemVersaoNova(instalado, atual))
                {
                    splash.Mensagem = $"Instalando o EverSoft em {Atualizacao.PastaInstalacao}...";
                    await Task.Run(() => Atualizador.Instalar(atual, instalado));
                }
                // A rede pode ter uma versão ainda mais nova que este .exe.
                if (await Task.Run(() => Atualizador.TemVersaoNova(instalado, cfg.ExecutavelAtualizacao)))
                {
                    splash.Mensagem = "Atualização: baixando a versão mais nova do EverSoft...";
                    await Task.Run(() => Atualizador.Instalar(cfg.ExecutavelAtualizacao, instalado));
                }
                Atualizacao.AbrirInstalado();
                return true;
            }

            Atualizador.LimparSobras(Atualizacao.PastaInstalacao);
            if (await Task.Run(() => Atualizador.TemVersaoNova(instalado, cfg.ExecutavelAtualizacao)))
            {
                splash.Mensagem = "Atualização: baixando a versão mais nova do EverSoft...";
                await Task.Run(() => Atualizador.Instalar(cfg.ExecutavelAtualizacao, instalado));
                Atualizacao.AbrirInstalado();
                return true;
            }
        }
        catch (Exception e)
        {
            // Sem rede, sem permissão em C:\... : abre a versão atual mesmo.
            Log.Erro("instalação/atualização: " + e);
        }
        return false;
    }
}
