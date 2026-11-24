namespace EverSoft.Core.Util;

/// <summary>Registro de erros em dados_app/erros.log. Nunca lança exceção.</summary>
public static class Log
{
    /// <summary>Pasta onde o erros.log é gravado. O AppConfig define ao carregar.</summary>
    public static string? Pasta { get; set; }

    public static void Erro(string mensagem)
    {
        try
        {
            if (string.IsNullOrEmpty(Pasta)) return;
            Directory.CreateDirectory(Pasta);
            File.AppendAllText(Path.Combine(Pasta, "erros.log"),
                $"[{DateTime.Now:dd/MM/yyyy HH:mm:ss}] {mensagem}{Environment.NewLine}");
        }
        catch
        {
            // O log nunca pode derrubar o app.
        }
    }
}
