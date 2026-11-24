using EverSoft.Core.Util;

namespace EverSoft.Core.Servicos;

/// <summary>
/// Instalação em C:\EverSoft e atualização pelo EverSoft.exe da rede.
/// A versão mais nova é a que tem a data de modificação mais recente.
///
/// Truque do Windows: um .exe aberto não pode ser apagado nem sobrescrito, mas pode ser
/// renomeado. Então o atual vira EverSoft.exe.old, o novo é copiado no lugar e o .old
/// é apagado na próxima abertura.
/// </summary>
public static class Atualizador
{
    public const string PastaInstalacaoPadrao = @"C:\EverSoft";

    /// <summary>Diferença mínima para considerar "mais novo" (cópias de rede arredondam os segundos).</summary>
    private static readonly TimeSpan Folga = TimeSpan.FromSeconds(2);

    public static bool MesmaPasta(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>true se o executável da rede existe e é mais novo que o local.</summary>
    public static bool TemVersaoNova(string exeLocal, string exeRede)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(exeRede) || !File.Exists(exeRede)) return false;
            if (!CopiaTerminada(exeRede)) return false;     // alguém ainda está copiando o .exe para a rede
            if (!File.Exists(exeLocal)) return true;
            return File.GetLastWriteTimeUtc(exeRede) > File.GetLastWriteTimeUtc(exeLocal) + Folga;
        }
        catch (Exception e) { Log.Erro($"verificar atualização: {e.Message}"); return false; }
    }

    /// <summary>O arquivo não está sendo gravado: dá para abrir só para leitura sem ninguém escrevendo.</summary>
    public static bool CopiaTerminada(string caminho)
    {
        try
        {
            using var f = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.Read);
            return f.Length > 0;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static DateTime? DataDoExecutavel(string exe)
    {
        try { return File.Exists(exe) ? File.GetLastWriteTime(exe) : null; } catch { return null; }
    }

    /// <summary>
    /// Copia <paramref name="origem"/> para <paramref name="destinoExe"/>, mesmo que o destino esteja aberto,
    /// mantendo a data de modificação da origem. Se algo der errado, o executável antigo volta.
    /// </summary>
    public static void Instalar(string origem, string destinoExe)
    {
        if (!File.Exists(origem)) throw new FileNotFoundException("Executável não encontrado: " + origem);
        string pasta = Path.GetDirectoryName(destinoExe)!;
        Directory.CreateDirectory(pasta);
        string temporario = destinoExe + ".novo";
        string velho = destinoExe + ".old";

        File.Copy(origem, temporario, true);                 // a parte demorada (rede) vem primeiro
        if (new FileInfo(temporario).Length != new FileInfo(origem).Length)
        {
            try { File.Delete(temporario); } catch { }
            throw new IOException("A cópia do EverSoft.exe veio incompleta. Tente de novo em instantes.");
        }
        File.SetLastWriteTimeUtc(temporario, File.GetLastWriteTimeUtc(origem));
        try { if (File.Exists(velho)) File.Delete(velho); } catch { velho = destinoExe + $".{DateTime.Now:yyyyMMddHHmmss}.old"; }

        bool renomeou = false;
        try
        {
            if (File.Exists(destinoExe)) { File.Move(destinoExe, velho); renomeou = true; }
            File.Move(temporario, destinoExe);
        }
        catch
        {
            if (renomeou && !File.Exists(destinoExe)) File.Move(velho, destinoExe);
            try { File.Delete(temporario); } catch { }
            throw;
        }
    }

    /// <summary>Apaga os EverSoft.exe.old que sobraram de atualizações anteriores.</summary>
    public static void LimparSobras(string pasta)
    {
        try
        {
            if (!Directory.Exists(pasta)) return;
            foreach (var f in Directory.GetFiles(pasta, "EverSoft.exe*.old").Concat(Directory.GetFiles(pasta, "EverSoft.exe.novo")))
                try { File.Delete(f); } catch { /* ainda aberto: fica para a próxima */ }
        }
        catch (Exception e) { Log.Erro($"limpar atualização: {e.Message}"); }
    }

    /// <summary>
    /// Trecho das novidades de uma versão. O texto tem seções "## V2.2"; devolve o que está
    /// abaixo do título da versão pedida até o próximo título.
    /// </summary>
    public static string Novidades(string texto, string versao)
    {
        var linhas = texto.Replace("\r\n", "\n").Split('\n');
        var saida = new List<string>();
        bool dentro = false;
        foreach (var l in linhas)
        {
            if (l.StartsWith("## "))
            {
                if (dentro) break;
                dentro = string.Equals(l[3..].Trim(), versao, StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (dentro) saida.Add(l);
        }
        return string.Join("\n", saida).Trim();
    }
}
