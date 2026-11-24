using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EverSoft.Core.Util;

/// <summary>Leitura e gravação de JSON com as mesmas regras do EverSoft.py.</summary>
public static class Json
{
    /// <summary>
    /// Nomes em snake_case ("dados_app_dir") para continuar lendo os config.json/estado.json
    /// que a versão Python já criou. No C# a propriedade se chama DadosAppDir.
    /// </summary>
    public static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // mantém acentos legíveis
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Grava de forma atômica: escreve num .tmp e depois troca pelo arquivo final.</summary>
    public static void SalvarAtomico(string caminho, string conteudo)
    {
        string? pasta = Path.GetDirectoryName(caminho);
        if (!string.IsNullOrEmpty(pasta)) Directory.CreateDirectory(pasta);
        string tmp = caminho + ".tmp";
        File.WriteAllText(tmp, conteudo, new System.Text.UTF8Encoding(false));
        File.Move(tmp, caminho, overwrite: true);
    }

    public static void Salvar<T>(string caminho, T dados) =>
        SalvarAtomico(caminho, JsonSerializer.Serialize(dados, Opcoes));

    /// <summary>Mistura dois JSON: objetos são combinados chave a chave; o resto é substituído (igual ao _merge do Python).</summary>
    public static JsonNode? Mesclar(JsonNode? baseNode, JsonNode? extra)
    {
        if (baseNode is JsonObject b && extra is JsonObject e)
        {
            var saida = (JsonObject)b.DeepClone();
            foreach (var (k, v) in e)
                saida[k] = saida[k] is JsonObject && v is JsonObject ? Mesclar(saida[k], v) : v?.DeepClone();
            return saida;
        }
        return extra?.DeepClone();
    }
}
