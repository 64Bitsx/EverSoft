using ClosedXML.Excel;
using EverSoft.Core.Util;

namespace EverSoft.Core.Planilhas;

/// <summary>
/// Grava nas planilhas .xlsx da rede, como as macros Status, Bloqueio, Coments e NotificaReposicao faziam.
/// Antes de gravar: confere se alguém está com o arquivo aberto e faz um backup.
/// Depois de gravar: atualiza o JSON espelho, para o app ver a mudança na hora.
/// </summary>
public class GravadorPlanilha
{
    private readonly string _pastaBackups;
    private readonly string _pastaJson;
    private readonly bool _fazerBackup;

    /// <summary>Quantos backups manter por arquivo.</summary>
    public const int BackupsPorArquivo = 20;

    public GravadorPlanilha(string pastaBackups, string pastaJson, bool fazerBackup)
    {
        _pastaBackups = pastaBackups;
        _pastaJson = pastaJson;
        _fazerBackup = fazerBackup;
    }

    /// <summary>
    /// Insere uma linha na posição 2, preenche as colunas e remove repetidos pelo nome (coluna A),
    /// mantendo o registro mais novo (o que acabou de entrar no topo).
    /// </summary>
    public void GravarNoTopo(string caminho, string aba, IReadOnlyDictionary<int, object?> valores, int nColunas)
    {
        using var wb = AbrirParaGravar(caminho);
        var ws = EscolherAba(wb, aba) ?? wb.Worksheet(1);
        ws.Row(2).InsertRowsAbove(1);
        for (int c = 1; c <= nColunas; c++) ws.Cell(2, c).Clear(XLClearOptions.Contents);
        foreach (var (c, v) in valores)
        {
            var cel = ws.Cell(2, c);
            if (v is null || (v is string s && s == "")) continue;
            cel.Value = v switch
            {
                string s2 => s2,
                double d => d,
                int i => i,
                bool b => b,
                DateTime dt => dt,
                _ => v.ToString(),
            };
        }
        RemoverDuplicatas(ws);
        Salvar(wb, caminho);
    }

    /// <summary>Apaga todas as linhas cuja coluna A é o nome do aluno. Devolve quantas apagou.</summary>
    public int RemoverLinhasPorNome(string caminho, IEnumerable<string> abasCandidatas, string nome)
    {
        using var wb = AbrirParaGravar(caminho);
        var lista = abasCandidatas.ToList();
        var ws = lista.Select(a => EscolherAba(wb, a)).FirstOrDefault(w => w is not null)
                 ?? throw new AbaNaoEncontradaException($"Nenhuma das abas [{string.Join(", ", lista)}] existe em {Path.GetFileName(caminho)}");
        string alvo = Texto.Norm(nome);
        int ultima = ws.LastRowUsed()?.RowNumber() ?? 0;
        int apagadas = 0;
        for (int r = ultima; r >= 1; r--)
        {
            if (Texto.Norm(LeitorPlanilha.ValorClosedXml(ws.Cell(r, 1))) == alvo)
            {
                ws.Row(r).Delete();
                apagadas++;
            }
        }
        if (apagadas > 0) Salvar(wb, caminho);
        return apagadas;
    }

    // ------------------------------------------------------------------ apoio
    private static XLWorkbook AbrirParaGravar(string caminho)
    {
        if (!File.Exists(caminho)) throw new FileNotFoundException($"Arquivo não encontrado: {caminho}", caminho);
        ChecarSeEstaAberto(caminho);
        try
        {
            return new XLWorkbook(caminho);
        }
        catch (IOException e)
        {
            throw new ArquivoEmUsoException($"O arquivo '{Path.GetFileName(caminho)}' está em uso. Feche-o e tente novamente.", e);
        }
    }

    /// <summary>O Excel cria um arquivo "~$Nome.xlsx" ao lado enquanto a planilha está aberta.</summary>
    public static void ChecarSeEstaAberto(string caminho)
    {
        string trava = Path.Combine(Path.GetDirectoryName(caminho) ?? "", "~$" + Path.GetFileName(caminho));
        if (File.Exists(trava))
            throw new ArquivoEmUsoException(
                $"O arquivo '{Path.GetFileName(caminho)}' parece estar aberto no Excel por alguém.\nFeche-o e tente novamente.");
    }

    private static IXLWorksheet? EscolherAba(XLWorkbook wb, string aba) =>
        wb.Worksheets.FirstOrDefault(w => string.Equals(w.Name, aba, StringComparison.OrdinalIgnoreCase));

    /// <summary>Remove linhas repetidas pela coluna A (a partir da linha 2), mantendo a primeira.</summary>
    internal static int RemoverDuplicatas(IXLWorksheet ws)
    {
        var vistos = new HashSet<string>();
        var apagar = new List<int>();
        int ultima = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (int r = 2; r <= ultima; r++)
        {
            string k = Texto.Norm(LeitorPlanilha.ValorClosedXml(ws.Cell(r, 1)));
            if (k == "") continue;
            if (!vistos.Add(k)) apagar.Add(r);
        }
        for (int i = apagar.Count - 1; i >= 0; i--) ws.Row(apagar[i]).Delete();
        return apagar.Count;
    }

    private void Salvar(XLWorkbook wb, string caminho)
    {
        FazerBackup(caminho);
        string pasta = Path.GetDirectoryName(caminho) ?? "";
        string tmp = Path.Combine(pasta, "." + Path.GetFileNameWithoutExtension(caminho) + ".eversoft-tmp" + Path.GetExtension(caminho));
        try
        {
            wb.SaveAs(tmp);
            File.Move(tmp, caminho, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignora */ }
            throw new ArquivoEmUsoException($"Sem permissão para gravar '{Path.GetFileName(caminho)}' (arquivo aberto?).", e);
        }

        // Atualiza o JSON espelho com o arquivo que acabamos de gravar.
        try
        {
            var abas = LeitorPlanilha.LerTodas(caminho);
            FonteJson.Gravar(FonteJson.CaminhoEspelho(_pastaJson, caminho), caminho, abas);
        }
        catch (Exception e)
        {
            Log.Erro($"não foi possível atualizar o JSON de {caminho}: {e.Message}");
        }
    }

    private void FazerBackup(string caminho)
    {
        if (!_fazerBackup) return;
        try
        {
            Directory.CreateDirectory(_pastaBackups);
            string nome = Path.GetFileNameWithoutExtension(caminho);
            string ext = Path.GetExtension(caminho);
            File.Copy(caminho, Path.Combine(_pastaBackups, $"{nome}.{DateTime.Now:yyyyMMdd_HHmmss}{ext}"), overwrite: true);
            var antigos = new DirectoryInfo(_pastaBackups).GetFiles(nome + ".*")
                .OrderByDescending(f => f.LastWriteTimeUtc).Skip(BackupsPorArquivo);
            foreach (var f in antigos) f.Delete();
        }
        catch (Exception e)
        {
            Log.Erro($"backup falhou: {e.Message}");
        }
    }
}
