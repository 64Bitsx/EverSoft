using EverSoft.Core.Configuracao;
using EverSoft.Core.Dados;
using EverSoft.Core.Planilhas;
using EverSoft.Core.Util;

namespace EverSoft.Core.Servicos;

/// <summary>
/// Gera os JSONs espelho a partir das planilhas da rede.
/// "Automação TEC" cuida das 4 planilhas do sistema; "Sincronia" cuida de todas as outras.
/// </summary>
public static class Sincronizacao
{
    /// <summary>Uma planilha a converter: caminho, abas desejadas e se limpa espaços repetidos.</summary>
    public record Entrada(string Caminho, string?[] Abas, bool LimparTexto);

    /// <summary>As 4 planilhas da Automação TEC.</summary>
    public static List<Entrada> EntradasTec(AppConfig cfg) => new()
    {
        new(cfg.P("grade_presencial"), new[] { cfg.Grade.Aba }, true),
        new(cfg.P("acompanhar_alunos"), DataHub.FontePorChave("acomp").Abas, true),
        new(cfg.P("mat_ativas"), DataHub.FontePorChave("matativas").Abas, true),
        new(cfg.P("grade_ead"), new[] { cfg.Grade.Aba }, true),
    };

    /// <summary>
    /// Fontes que já foram convertidas uma única vez e não são mais reconvertidas
    /// (a Base Prepara): o app só lê o JSON delas.
    /// </summary>
    public static readonly HashSet<string> SoConvertidasUmaVez = new() { "prepara" };

    /// <summary>As demais planilhas (Sincronia), incluindo o Boletim.</summary>
    public static List<Entrada> EntradasSincronia(AppConfig cfg)
    {
        var porCaminho = new Dictionary<string, List<string?>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in DataHub.Fontes)
        {
            string c = cfg.P(f.ChaveCaminho);
            if (SoConvertidasUmaVez.Contains(f.Chave)) continue;
            if (c == "" || DataHub.ArquivosAutomacaoTec.Contains(Path.GetFileName(c.Replace('\\', '/')))) continue;
            if (!porCaminho.TryGetValue(c, out var l)) porCaminho[c] = l = new();
            l.AddRange(f.Abas);
        }
        string boletim = cfg.P("boletim_xlsm");
        if (boletim != "")
        {
            var b = cfg.Boletim;
            if (!porCaminho.TryGetValue(boletim, out var l)) porCaminho[boletim] = l = new();
            l.AddRange(new[] { b.MesAtual, b.ProximoMes, b.AbaValores, b.AbaNomes }.Where(x => !string.IsNullOrEmpty(x)));
            l.Add(null);
        }
        return porCaminho.Select(kv => new Entrada(kv.Key, kv.Value.ToArray(), false)).ToList();
    }

    /// <summary>Automação TEC: grade presencial, Acompanhar Alunos, RelMatAtivas e grade EAD.</summary>
    public static int GerarJsonsTec(AppConfig cfg, Action<string>? log = null)
    {
        int total = Gerar(cfg, EntradasTec(cfg), log);
        if (total == 0) throw new InvalidOperationException("Nenhuma planilha foi encontrada. Confira os caminhos exibidos acima.");
        return total;
    }

    /// <summary>Sincronia: todas as outras fontes e o Boletim.</summary>
    public static int GerarJsonsSincronizacao(AppConfig cfg, Action<string>? log = null)
    {
        int total = Gerar(cfg, EntradasSincronia(cfg), log);
        if (total == 0) throw new InvalidOperationException("Nenhuma fonte foi sincronizada. Confira os caminhos em Configurações.");
        log?.Invoke($"SINCRONIA FINALIZADA: {total} JSON(s) criado(s).");
        return total;
    }

    /// <summary>
    /// Converte só as planilhas que mudaram desde o último JSON (Excel mais novo que o JSON,
    /// ou JSON inexistente). Devolve os caminhos convertidos. Usado pela sincronia automática.
    /// </summary>
    public static List<string> ConverterAlteradas(AppConfig cfg, Action<string>? log = null)
    {
        string pasta = cfg.P("tec_json_dir");
        // A Base Prepara só é convertida se o JSON dela ainda não existir (conversão única).
        var unicas = DataHub.Fontes.Where(f => SoConvertidasUmaVez.Contains(f.Chave) && cfg.P(f.ChaveCaminho) != "")
            .Select(f => new Entrada(cfg.P(f.ChaveCaminho), f.Abas, false))
            .Where(e => !File.Exists(FonteJson.CaminhoEspelho(pasta, e.Caminho)));
        var alteradas = EntradasTec(cfg).Concat(EntradasSincronia(cfg)).Concat(unicas)
            .Where(e => e.Caminho != "" && File.Exists(e.Caminho))
            .Where(e =>
            {
                string json = FonteJson.CaminhoEspelho(pasta, e.Caminho);
                return !File.Exists(json) || File.GetLastWriteTimeUtc(e.Caminho) > File.GetLastWriteTimeUtc(json);
            })
            .ToList();
        if (alteradas.Count == 0) return new();
        Gerar(cfg, alteradas, log);
        return alteradas.Select(e => e.Caminho).ToList();
    }

    /// <summary>Copia todas as abas de uma planilha para o JSON espelho dela.</summary>
    public static string GerarJsonCompleto(AppConfig cfg, string caminhoExcel)
    {
        string destino = FonteJson.CaminhoEspelho(cfg.P("tec_json_dir"), caminhoExcel);
        FonteJson.Gravar(destino, caminhoExcel, LeitorPlanilha.LerTodas(caminhoExcel));
        return destino;
    }

    private static int Gerar(AppConfig cfg, List<Entrada> entradas, Action<string>? log)
    {
        string pasta = cfg.P("tec_json_dir");
        if (pasta == "") throw new InvalidOperationException("A pasta dos JSONs não está configurada.");
        Directory.CreateDirectory(pasta);
        log?.Invoke($"Pasta de destino: {pasta}");

        int total = 0;
        foreach (var (caminho, abasPedidas, limparTexto) in entradas)
        {
            if (caminho == "") { log?.Invoke("IGNORADO: caminho vazio na configuração."); continue; }
            if (!File.Exists(caminho)) { log?.Invoke($"NÃO ENCONTRADO: {caminho}"); continue; }
            log?.Invoke($"Lendo: {Path.GetFileName(caminho)}");
            List<Planilha> todas;
            try { todas = LeitorPlanilha.LerTodas(caminho); }
            catch (Exception e)
            {
                // Ex.: alguém salvando a planilha neste exato momento. O JSON antigo continua valendo
                // e a próxima rodada tenta de novo.
                log?.Invoke($"  Erro ao ler: {e.Message}");
                Log.Erro($"sincronizar {caminho}: {e.Message}");
                continue;
            }

            // As abas pedidas e a primeira aba do arquivo. Algumas abas pedidas são só
            // alternativas de nome (ex.: "Sheet" ou "Base Prepara", mês atual ou próximo mês),
            // então a falta de uma delas é normal e não é erro.
            var escolhidas = new List<Planilha>();
            foreach (var aba in abasPedidas.Append(null))
            {
                var p = aba is null ? todas[0] : todas.FirstOrDefault(t => string.Equals(t.Nome, aba, StringComparison.OrdinalIgnoreCase));
                if (p is null) continue;
                if (escolhidas.Contains(p)) continue;
                escolhidas.Add(p);
                log?.Invoke($"  Aba {p.Nome}: {p.NLinhas} linha(s)");
            }
            if (limparTexto) escolhidas = escolhidas.Select(LimparTexto).ToList();

            string destino = FonteJson.CaminhoEspelho(pasta, caminho);
            FonteJson.Gravar(destino, caminho, escolhidas);
            total++;
            log?.Invoke($"JSON GRAVADO: {Path.GetFileName(destino)} ({escolhidas.Count} aba(s))");
        }
        return total;
    }

    /// <summary>Junta espaços repetidos nos textos (nomes vindos do sistema costumam ter espaço duplo).</summary>
    private static Planilha LimparTexto(Planilha p) =>
        new(p.Linhas.Select(l => l.Select(v => v is string s ? (object?)Texto.ColapsarEspacos(s) : v).ToList()).ToList(), p.Nome);
}
