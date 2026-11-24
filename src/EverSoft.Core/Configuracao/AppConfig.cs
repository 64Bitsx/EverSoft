using System.Text.Json;
using System.Text.Json.Nodes;
using EverSoft.Core.Util;

namespace EverSoft.Core.Configuracao;

/// <summary>
/// Configuração do app (config.json). Mesmo formato do EverSoft.py, então o config.json
/// que você já usa continua valendo.
///
/// DICA C#: "{ get; set; } = valor" cria uma propriedade com valor padrão.
/// É parecido com um atributo de classe Python, só que com tipo declarado.
/// </summary>
public class AppConfig
{
    /// <summary>
    /// Pasta de dados da versão nova. Fica separada da "dados_app" do EverSoft.py para que as duas
    /// versões possam rodar ao mesmo tempo sem uma sobrescrever o estado.json da outra.
    /// Os JSONs das planilhas continuam compartilhados.
    /// </summary>
    public const string PastaPadraoServidor = @"\\servidortec\TEC_CURSOS\Pedagógico\Ewerton\EverSoft\dados_app_v2";
    /// <summary>Pasta de dados do EverSoft.py (de onde a configuração é copiada na primeira abertura).</summary>
    public const string PastaVersaoPython = @"\\servidortec\TEC_CURSOS\Pedagógico\Ewerton\EverSoft\dados_app";
    private const string Srv = @"\\servidortec\TEC_CURSOS\Pedagógico\";

    public string DadosAppDir { get; set; } = PastaPadraoServidor;

    /// <summary>Caminhos das planilhas e pastas (BD!B1:B13 da planilha original).</summary>
    public Dictionary<string, string> Caminhos { get; set; } = new()
    {
        ["fotos_alunos"] = Srv + @"Fotos dos Alunos\",
        ["mat_ativas"] = Srv + @"Documentos\Excel\RelMatAtivas.xls",
        ["grade_presencial"] = Srv + @"Documentos\Excel\GradeHorariosAction.xls",
        ["bloqueio"] = Srv + @"Documentos\Excel\Bloqueio.xlsx",
        ["historico"] = Srv + @"Documentos\Historico\Historico.xlsx",
        ["observacoes"] = Srv + @"Documentos\Excel\Observacoes.xlsx",
        ["link_aluno"] = "https://sistemaescolaead.grupooportunidade.com.br/seonline/private/onbyte/MatriculasEdit.jsf?matriculasCodigo=",
        ["fotos_bmp"] = Srv + @"Fotos dos Alunos\BitMap\",
        ["grade_ead"] = Srv + @"Documentos\Excel\GradeHorariosAction (1).xls",
        ["coments"] = Srv + @"Documentos\Excel\Coments.xlsx",
        ["acompanhar_alunos"] = Srv + @"Documentos\Excel\Acompanhar Alunos.xlsx",
        ["dados_alunos"] = Srv + @"Documentos\Excel\Dados dos Alunos.xlsm",
        ["base_prepara"] = Srv + @"Base Dados\Base Prepara.xls",
        ["boletim_xlsm"] = Srv + @"BD_Boletim\Boletim.xlsm",
        ["boletim_imagem"] = Srv + @"BD_Boletim\Boletim.png",
        ["audios_lembretes"] = Srv + @"BD_Audios\Lembretes",
        ["tec_json_dir"] = Srv + @"Documentos\Excel\",
        ["justificativas_json_dir"] = Srv + @"Documentos\Excel\Justificativas",
    };

    /// <summary>Modelos de lembrete com {NOME}, {HORA} e {CURSO} (BD!D1:D200).</summary>
    public List<string> ModelosLembrete { get; set; } = new();
    public LembreteConfig Lembrete { get; set; } = new();
    public MsgConfig Msg { get; set; } = new();
    public BoletimConfig Boletim { get; set; } = new();
    public GradeConfig Grade { get; set; } = new();
    public BloqueioConfig Bloqueio { get; set; } = new();
    public HistoricoConfig Historico { get; set; } = new();
    public WhatsAppConfig Whatsapp { get; set; } = new();
    public RemoverFaltasConfig RemoverFaltas { get; set; } = new();
    public Dictionary<string, bool> ColunasVisiveis { get; set; } = new()
    {
        ["R"] = false, ["Término1"] = false, ["Término2"] = false, ["Esconder"] = false,
    };
    public bool BackupAntesDeGravar { get; set; } = true;
    /// <summary>De quantos em quantos segundos o app confere se as planilhas mudaram.</summary>
    public int AutoAtualizarSegundos { get; set; } = 60;
    /// <summary>
    /// true = quando uma planilha da rede muda (editada por você, por outra pessoa ou pelo sistema),
    /// o app converte para JSON sozinho e relê. false = só pelos botões Automação TEC / Sincronia.
    /// </summary>
    public bool SincroniaAutomatica { get; set; } = true;
    /// <summary>EverSoft.exe da rede usado para saber se há versão nova (compara a data de modificação).</summary>
    public string ExecutavelAtualizacao { get; set; } = @"\\servidortec\TEC_CURSOS\Pedagógico\Ewerton\EverSoft\EverSoft.exe";

    // ------------------------------------------------------------------ caminhos derivados
    /// <summary>Caminho configurado, sem espaços ou quebras de linha sobrando.</summary>
    public string P(string chave) => Caminhos.TryGetValue(chave, out var v) ? (v ?? "").Trim() : "";

    public string PastaDados => DadosAppDir.Trim();
    public string PastaBackups => Path.Combine(PastaDados, "backups");
    public string CaminhoConfig => Path.Combine(PastaDados, "config.json");
    public string CaminhoEstado => Path.Combine(PastaDados, "estado.json");

    // ------------------------------------------------------------------ carregar / salvar
    /// <summary>Arquivo local (no PC) que guarda só onde fica a pasta de dados.</summary>
    public static string CaminhoLocal =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EverSoft", "local.json");

    /// <summary>
    /// Carrega o config.json da pasta de dados, completando o que faltar com os valores padrão.
    /// Sem <paramref name="pastaDados"/>, usa a pasta gravada no PC (ou a do servidor).
    /// </summary>
    public static AppConfig Carregar(string? pastaDados = null)
    {
        if (pastaDados is null)
        {
            pastaDados = LerPastaLocal() ?? PastaPadraoServidor;
            if (pastaDados == PastaPadraoServidor) ImportarDaVersaoAnterior(PastaVersaoPython, pastaDados);
        }
        var padrao = new AppConfig { DadosAppDir = pastaDados };
        string arquivo = Path.Combine(pastaDados, "config.json");
        JsonNode? lido = null;
        try
        {
            if (File.Exists(arquivo)) lido = JsonNode.Parse(File.ReadAllText(arquivo));
        }
        catch (Exception e)
        {
            Log.Erro($"config.json inválido: {e.Message}");
        }

        var cfg = padrao;
        if (lido is JsonObject)
        {
            var mesclado = Json.Mesclar(JsonSerializer.SerializeToNode(padrao, Json.Opcoes), lido);
            cfg = mesclado.Deserialize<AppConfig>(Json.Opcoes) ?? padrao;
        }
        // A versão Python vinha com a pasta das justificativas no Documentos do computador
        // (C:\Users\...\TEC_CURSOS\...). O certo é a mesma pasta na rede.
        cfg.Caminhos["justificativas_json_dir"] = CaminhoNaRede(cfg.P("justificativas_json_dir"));
        // A versão Python ativava "Chrome"; agora a janela é a do WhatsApp, em qualquer navegador.
        if (string.IsNullOrWhiteSpace(cfg.Whatsapp.JanelaTitulo) || cfg.Whatsapp.JanelaTitulo.Trim().Equals("Chrome", StringComparison.OrdinalIgnoreCase))
            cfg.Whatsapp.JanelaTitulo = "WhatsApp";
        // A pasta que de fato abrimos é a que vale (o config pode ter vindo de outro PC).
        cfg.DadosAppDir = pastaDados;
        Log.Pasta = cfg.PastaDados;
        return cfg;
    }

    public void Salvar()
    {
        Json.Salvar(CaminhoConfig, this);
        try
        {
            Json.Salvar(CaminhoLocal, new Dictionary<string, string> { ["dados_app_dir"] = PastaDados });
        }
        catch (Exception e)
        {
            Log.Erro($"não foi possível gravar {CaminhoLocal}: {e.Message}");
        }
    }

    /// <summary>
    /// Primeira abertura: copia config.json e estado.json da versão Python para a pasta nova,
    /// assim os caminhos, mensagens e a Justifica já vêm prontos. Só copia se a pasta nova ainda não tem config.
    /// </summary>
    public static bool ImportarDaVersaoAnterior(string pastaAntiga, string pastaNova)
    {
        try
        {
            if (File.Exists(Path.Combine(pastaNova, "config.json"))) return false;
            string cfgAntigo = Path.Combine(pastaAntiga, "config.json");
            if (!File.Exists(cfgAntigo)) return false;
            Directory.CreateDirectory(pastaNova);
            foreach (var nome in new[] { "config.json", "estado.json" })
            {
                string origem = Path.Combine(pastaAntiga, nome);
                if (File.Exists(origem)) File.Copy(origem, Path.Combine(pastaNova, nome));
            }
            return true;
        }
        catch (Exception e)
        {
            Log.Erro($"importar configuração da versão anterior: {e.Message}");
            return false;
        }
    }

    /// <summary>"C:\Users\X\Documents\TEC_CURSOS\Pedagógico\..." vira "\\servidortec\TEC_CURSOS\Pedagógico\...".</summary>
    public static string CaminhoNaRede(string caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho)) return caminho;
        var m = System.Text.RegularExpressions.Regex.Match(caminho,
            @"^[A-Za-z]:\\Users\\[^\\]+\\(?:Documents|Documentos)\\TEC_CURSOS\\(.*)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? @"\\servidortec\TEC_CURSOS\" + m.Groups[1].Value : caminho;
    }

    private static string? LerPastaLocal()
    {
        try
        {
            if (!File.Exists(CaminhoLocal)) return null;
            var no = JsonNode.Parse(File.ReadAllText(CaminhoLocal));
            string? p = no?["dados_app_dir"]?.GetValue<string>()?.Trim();
            return string.IsNullOrEmpty(p) ? null : p;
        }
        catch
        {
            return null;
        }
    }
}

public class LembreteConfig
{
    /// <summary>"audio" = áudio .ogg + "Aluno: Nome"; "texto" = modelos de texto.</summary>
    public string Modo { get; set; } = "audio";
    public string SufixoTexto { get; set; } = " E não esqueça de trazer Caderno, borracha, lápis e caneta na aula de hoje!";
}

public class MsgConfig
{
    public string B1 { get; set; } = "Olá aluno ou responsável! Fique de olho no aviso abaixo!";
    public string B2 { get; set; } = "Desejamos um ótimo feriado e te esperamos em sua próxima aula!";
    public string B3 { get; set; } = "";
    public string Imagem { get; set; } = @"\\servidortec\TEC_CURSOS\Pedagógico\Artes\SaoJao.jpg";
}

public class BoletimConfig
{
    public string MesAtual { get; set; } = "Outubro";
    public string ProximoMes { get; set; } = "Novembro";
    /// <summary>Vazio = usa a primeira aba do Boletim.xlsm (onde ficam alunos e notas).</summary>
    public string AbaValores { get; set; } = "";
    /// <summary>Vazio = mesma aba dos valores.</summary>
    public string AbaNomes { get; set; } = "";
    public string ColNome { get; set; } = "B";
    public string ColValor { get; set; } = "C";
}

public class GradeConfig
{
    public string Aba { get; set; } = "RelGradeHorarios";
    public int LinhaCabecalho { get; set; } = 3;
    public List<int> LinhasPresencial { get; set; } = new() { 24, 26, 28, 30, 32, 34, 36, 39, 41, 4, 6, 8, 10, 12, 14, 16, 18, 20, 22 };
    public bool OrdenarPorNome { get; set; } = true;
}

public class BloqueioConfig
{
    public string Aba { get; set; } = "Plan1";
    public List<string> Rotulos { get; set; } = new() { "Bloquear Prática", "Bloquear Apostila", "Reprovar Digi", "Bloqueio Prova" };
}

public class HistoricoConfig
{
    public string AbaPresencas { get; set; } = "Presencas";
    public string AbaFaltas { get; set; } = "Faltas";
}

public class WhatsAppConfig
{
    public string GrupoJustifica { get; set; } = "TEC GUARABIRA";
    /// <summary>Trecho do título da janela do WhatsApp Web (qualquer navegador), usado no "nircmd win activate ititle".</summary>
    public string JanelaTitulo { get; set; } = "WhatsApp";
    /// <summary>Caminho do nircmd.exe; vazio = procura ao lado do EverSoft.exe, no PATH e no Windows.</summary>
    public string Nircmd { get; set; } = "";
    public List<int> CliqueBusca { get; set; } = new() { 540, 170 };
    public List<string> AtalhoNovoChat { get; set; } = new() { "ctrl", "alt", "n" };
    public double EsperaAtivar { get; set; } = 4.0;
    public double EsperaCarregarWhatsapp { get; set; } = 20.0;
    public double EsperaAposNumero { get; set; } = 2.0;
    public double EsperaAbrirConversa { get; set; } = 2.0;
    public double EsperaAnexo { get; set; } = 3.0;
    public double EsperaEntreAlunos { get; set; } = 3.0;
    public int ContagemRegressiva { get; set; } = 5;
}

public class RemoverFaltasConfig
{
    public string UrlHub { get; set; } = "https://hub.grupooportunidade.com.br/home";
    public double EsperaHub { get; set; } = 5.0;
    public double EsperaSistema { get; set; } = 3.0;
    public double EsperaFrequencias { get; set; } = 3.0;
    public double EsperaLixeira { get; set; } = 10.0;
}
