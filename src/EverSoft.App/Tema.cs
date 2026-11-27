using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using EverSoft.Core.Util;

namespace EverSoft.App;

/// <summary>
/// Tema claro / escuro. A escolha fica guardada neste computador (%APPDATA%\EverSoft\tema.txt).
/// </summary>
public static class Tema
{
    private static string Arquivo =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EverSoft", "tema.txt");

    public static event Action? Mudou;

    public static bool Escuro => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    /// <summary>Aplica o tema salvo (claro, se nunca foi escolhido).</summary>
    public static void AplicarSalvo()
    {
        string salvo = "";
        try { if (File.Exists(Arquivo)) salvo = File.ReadAllText(Arquivo).Trim(); } catch { }
        Aplicar(salvo == "escuro");
    }

    public static void Alternar() => Aplicar(!Escuro, salvar: true);

    public static void Aplicar(bool escuro, bool salvar = false)
    {
        if (Application.Current is null) return;
        Application.Current.RequestedThemeVariant = escuro ? ThemeVariant.Dark : ThemeVariant.Light;
        if (salvar)
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Arquivo)!);
                File.WriteAllText(Arquivo, escuro ? "escuro" : "claro");
            }
            catch (Exception e) { Log.Erro("tema: " + e.Message); }
        Mudou?.Invoke();
    }

    /// <summary>Pincel do tema atual (para cores montadas no código).</summary>
    public static IBrush Pincel(string chave, string reserva = "#808080")
    {
        var app = Application.Current;
        if (app is not null && app.TryGetResource(chave, app.ActualThemeVariant, out var r) && r is IBrush b) return b;
        return Brush.Parse(reserva);
    }
}
