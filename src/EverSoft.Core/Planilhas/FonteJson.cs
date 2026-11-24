using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace EverSoft.Core.Planilhas;

/// <summary>
/// Os "JSONs espelho" das planilhas: uma cópia rápida de cada Excel, gerada pela
/// Automação TEC ou pela Sincronia. Mesmo formato do EverSoft.py:
/// <code>{"versao":1, "origem":"...", "atualizado_em":"...", "planilhas":{"Aba":{"rows":[[...]]}}}</code>
/// Datas ficam como {"__tipo__":"datetime","valor":"2024-01-05T00:00:00"}.
/// </summary>
public static class FonteJson
{
    /// <summary>Caminho do JSON espelho de uma planilha: pasta_json/NomeDoArquivo.json</summary>
    public static string CaminhoEspelho(string pastaJson, string caminhoExcel)
    {
        if (string.IsNullOrWhiteSpace(caminhoExcel)) return "";
        string pasta = string.IsNullOrWhiteSpace(pastaJson) ? (Path.GetDirectoryName(caminhoExcel) ?? "") : pastaJson;
        return Path.Combine(pasta, NomeSemExtensao(caminhoExcel) + ".json");
    }

    /// <summary>Path.GetFileNameWithoutExtension que entende "\" mesmo rodando fora do Windows.</summary>
    internal static string NomeSemExtensao(string caminho)
    {
        string nome = caminho.Replace('\\', '/');
        nome = nome[(nome.LastIndexOf('/') + 1)..];
        int ponto = nome.LastIndexOf('.');
        return ponto > 0 ? nome[..ponto] : nome;
    }

    /// <summary>Lê a primeira aba da lista que existir no JSON (null = primeira aba).</summary>
    public static Planilha Ler(string caminhoJson, IEnumerable<string?>? abas = null)
    {
        var todas = LerTodas(caminhoJson);
        if (todas.Count == 0) throw new InvalidDataException($"JSON sem planilhas: {Path.GetFileName(caminhoJson)}");
        var lista = (abas ?? new string?[] { null }).ToList();
        foreach (var aba in lista)
        {
            if (aba is null) return todas[0];
            var p = todas.FirstOrDefault(x => string.Equals(x.Nome, aba, StringComparison.OrdinalIgnoreCase));
            if (p is not null) return p;
        }
        throw new AbaNaoEncontradaException($"aba {lista.FirstOrDefault() ?? "primeira"} não existe em {Path.GetFileName(caminhoJson)}");
    }

    public static List<Planilha> LerTodas(string caminhoJson)
    {
        using var fs = new FileStream(caminhoJson, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var doc = JsonDocument.Parse(fs);
        var saida = new List<Planilha>();
        if (!doc.RootElement.TryGetProperty("planilhas", out var planilhas) || planilhas.ValueKind != JsonValueKind.Object)
            return saida;
        foreach (var aba in planilhas.EnumerateObject())
        {
            JsonElement rows = aba.Value.ValueKind == JsonValueKind.Object && aba.Value.TryGetProperty("rows", out var r) ? r : aba.Value;
            var linhas = new List<List<object?>>();
            if (rows.ValueKind == JsonValueKind.Array)
                foreach (var row in rows.EnumerateArray())
                    linhas.Add(row.ValueKind == JsonValueKind.Array ? row.EnumerateArray().Select(LerValor).ToList() : new List<object?>());
            saida.Add(new Planilha(linhas, aba.Name));
        }
        return saida;
    }

    private static object? LerValor(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Object when e.TryGetProperty("__tipo__", out var t) && e.TryGetProperty("valor", out var v) =>
            DateTime.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                ? (t.GetString() == "date" ? dt.Date : dt)
                : v.GetString(),
        _ => null,
    };

    /// <summary>Grava as abas num JSON espelho (de forma atômica).</summary>
    public static void Gravar(string caminhoJson, string origem, IEnumerable<Planilha> abas)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteNumber("versao", 1);
            w.WriteString("origem", origem);
            w.WriteString("atualizado_em", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
            w.WriteStartObject("planilhas");
            foreach (var p in abas)
            {
                w.WriteStartObject(p.Nome);
                w.WriteStartArray("rows");
                foreach (var linha in p.Linhas)
                {
                    w.WriteStartArray();
                    foreach (var v in linha) EscreverValor(w, v);
                    w.WriteEndArray();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        Util.Json.SalvarAtomico(caminhoJson, Encoding.UTF8.GetString(ms.ToArray()));
    }

    private static void EscreverValor(Utf8JsonWriter w, object? v)
    {
        switch (v)
        {
            case null: w.WriteNullValue(); break;
            case string s: w.WriteStringValue(s); break;
            case bool b: w.WriteBooleanValue(b); break;
            case double d when Util.Texto.EhInteiro(d): w.WriteNumberValue((long)d); break;
            case double d: w.WriteNumberValue(d); break;
            case DateTime dt:
                w.WriteStartObject();
                w.WriteString("__tipo__", "datetime");
                w.WriteString("valor", dt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
                w.WriteEndObject();
                break;
            default: w.WriteStringValue(Convert.ToString(v, CultureInfo.InvariantCulture)); break;
        }
    }
}
