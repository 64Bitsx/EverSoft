using System.Diagnostics;
using Avalonia.Platform;
using EverSoft.Core.Servicos;
using EverSoft.Core.Util;

namespace EverSoft.App;

/// <summary>
/// Regras de instalação e atualização do EverSoft.exe (só valem para o .exe publicado no Windows):
/// 1. Aberto de qualquer lugar (ex.: da rede), ele se copia para C:\EverSoft e abre de lá.
/// 2. Aberto de C:\EverSoft, se o EverSoft.exe da rede for mais novo, copia o da rede e reabre.
/// 3. Com o programa aberto, confere a cada minuto e pergunta se quer atualizar.
/// </summary>
public static class Atualizacao
{
    public static string PastaInstalacao => Atualizador.PastaInstalacaoPadrao;
    public static string ExeInstalado => Path.Combine(PastaInstalacao, "EverSoft.exe");
    public static string? ExeAtual => Environment.ProcessPath;

    /// <summary>
    /// Só o .exe de arquivo único (o publicado) se instala e se atualiza. Rodando pelo Visual Studio,
    /// nada disso acontece.
    /// </summary>
    public static bool Ativa =>
        OperatingSystem.IsWindows()
        && string.IsNullOrEmpty(typeof(Atualizacao).Assembly.Location)
        && ExeAtual is not null
        && Path.GetFileName(ExeAtual).Equals("EverSoft.exe", StringComparison.OrdinalIgnoreCase);

    public static bool RodandoDaInstalacao =>
        ExeAtual is not null && Atualizador.MesmaPasta(Path.GetDirectoryName(ExeAtual)!, PastaInstalacao);

    /// <summary>Abre o EverSoft de C:\EverSoft (este processo deve fechar em seguida).</summary>
    public static void AbrirInstalado()
    {
        Process.Start(new ProcessStartInfo(ExeInstalado) { UseShellExecute = true, WorkingDirectory = PastaInstalacao });
    }

    // ------------------------------------------------------------------ novidades
    private static string ArquivoVersaoVista =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EverSoft", "versao_vista.txt");

    /// <summary>Texto "o que há de novo" desta versão, se ainda não foi mostrado neste computador.</summary>
    public static string? NovidadesNaoVistas(string versao)
    {
        try
        {
            string vista = File.Exists(ArquivoVersaoVista) ? File.ReadAllText(ArquivoVersaoVista).Trim() : "";
            if (vista == versao) return null;
            using var s = AssetLoader.Open(new Uri("avares://EverSoft/Assets/novidades.txt"));
            string texto = new StreamReader(s).ReadToEnd();
            string n = Atualizador.Novidades(texto, versao);
            return n == "" ? null : n;
        }
        catch (Exception e) { Log.Erro($"novidades: {e.Message}"); return null; }
    }

    public static void MarcarNovidadesVistas(string versao)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ArquivoVersaoVista)!);
            File.WriteAllText(ArquivoVersaoVista, versao);
        }
        catch (Exception e) { Log.Erro($"novidades: {e.Message}"); }
    }
}
