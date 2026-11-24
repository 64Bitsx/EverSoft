using EverSoft.Core.Configuracao;
using EverSoft.Core.Planilhas;
using EverSoft.Core.Util;

namespace EverSoft.Core.Dados;

/// <summary>
/// Central de dados: carrega as planilhas (pelos JSONs espelho) e responde às consultas
/// que antes eram fórmulas PROCV / ÍNDICE+CORRESP da planilha.
/// </summary>
public class DataHub
{
    /// <summary>Uma fonte de dados: qual caminho do config e quais abas tentar (null = primeira).</summary>
    public record Fonte(string Chave, string ChaveCaminho, string?[] Abas, string Rotulo);

    public static readonly IReadOnlyList<Fonte> Fontes = new Fonte[]
    {
        new("acomp", "acompanhar_alunos", new[] { "Acompanhar Alunos.xlsx", null }, "Acompanhar Alunos"),
        new("dados", "dados_alunos", new[] { "Sheet1", null }, "Dados dos Alunos"),
        new("coments", "coments", new[] { "Planilha1", null }, "Coments"),
        new("matativas", "mat_ativas", new[] { "RelMatAtivas", null }, "RelMatAtivas"),
        new("bloqueio", "bloqueio", new[] { "Plan1", null }, "Bloqueio"),
        new("hist1", "historico", new[] { "Sheet1", null }, "Historico (lista R)"),
        new("histpres", "historico", new string?[] { "Presencas" }, "Historico (Presencas)"),
        new("obs", "observacoes", new[] { "Sheet1", null }, "Observacoes"),
        new("prepara", "base_prepara", new[] { "Sheet", "Base Prepara", null }, "Base Prepara"),
    };

    /// <summary>As quatro planilhas que a Automação TEC atualiza; as outras vêm da Sincronia.</summary>
    public static readonly HashSet<string> ArquivosAutomacaoTec = new(StringComparer.OrdinalIgnoreCase)
    {
        "acompanhar alunos.xlsx", "relmatativas.xls", "gradehorariosaction.xls", "gradehorariosaction (1).xls",
    };

    public static Fonte FontePorChave(string chave) => Fontes.First(f => f.Chave == chave);

    private readonly AppConfig _cfg;
    private readonly object _trava = new();
    private readonly Dictionary<string, Planilha?> _pl = new();
    private readonly Dictionary<string, DateTime> _modificado = new();
    private readonly Dictionary<(string, int), Dictionary<string, int>> _indices = new();
    private readonly Dictionary<string, (DateTime Data, Planilha Pl)> _boletim = new(StringComparer.OrdinalIgnoreCase);
    private int _versao;

    /// <summary>Erros de leitura por fonte (para mostrar na tela).</summary>
    public Dictionary<string, string> Erros { get; } = new();

    /// <summary>Muda sempre que alguma fonte é recarregada (a tela usa para saber se precisa redesenhar).</summary>
    public int VersaoDados { get { lock (_trava) return _versao; } }

    public DataHub(AppConfig cfg) => _cfg = cfg;

    public string CaminhoExcel(string chave) => _cfg.P(FontePorChave(chave).ChaveCaminho);
    public string CaminhoJson(string chave) => FonteJson.CaminhoEspelho(_cfg.P("tec_json_dir"), CaminhoExcel(chave));

    public static string OrientacaoJson(string caminhoExcel) =>
        ArquivosAutomacaoTec.Contains(Path.GetFileName(caminhoExcel.Replace('\\', '/')))
            ? "Execute a Automação TEC para gerar o JSON correspondente."
            : "Execute a Sincronia para gerar o JSON correspondente.";

    // ------------------------------------------------------------------ carga
    /// <summary>
    /// Lê uma fonte. Normalmente pelo JSON espelho; com <paramref name="diretoDoExcel"/> lê a planilha
    /// (usado logo depois que o próprio app grava nela).
    /// </summary>
    public void Carregar(string chave, bool diretoDoExcel = false)
    {
        var fonte = FontePorChave(chave);
        string excel = CaminhoExcel(chave);
        string json = CaminhoJson(chave);
        string caminho = diretoDoExcel ? excel : json;
        Planilha? pl = null;
        string? erro = null;
        try
        {
            if (string.IsNullOrEmpty(caminho) || !File.Exists(caminho))
                throw new FileNotFoundException(diretoDoExcel
                    ? $"Arquivo não encontrado: {excel}"
                    : $"JSON não encontrado para {Path.GetFileName(excel)}. {OrientacaoJson(excel)}");
            pl = caminho.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? FonteJson.Ler(caminho, fonte.Abas)
                : LeitorPlanilha.Ler(caminho, fonte.Abas);
        }
        catch (FileNotFoundException e) { erro = e.Message; }
        catch (AbaNaoEncontradaException e) { erro = $"Aba não encontrada ({e.Message})"; }
        catch (Exception e) { erro = $"Erro ao ler ({e.GetType().Name}): {e.Message}"; }

        lock (_trava)
        {
            if (pl is not null)
            {
                _pl[chave] = pl;
                _modificado[chave] = File.GetLastWriteTimeUtc(caminho);
                _versao++;
                Erros.Remove(chave);
            }
            else
            {
                // Se já havia uma cópia carregada, continua usando, mas avisa o erro.
                _pl.TryAdd(chave, null);
                Erros[chave] = erro!;
                Log.Erro($"carregar {chave}: {erro}");
            }
            foreach (var k in _indices.Keys.Where(k => k.Item1 == chave).ToList()) _indices.Remove(k);
        }
    }

    /// <summary>Carrega todas as fontes em paralelo. Devolve os erros encontrados.</summary>
    public Dictionary<string, string> CarregarTudo()
    {
        Parallel.ForEach(Fontes, new ParallelOptions { MaxDegreeOfParallelism = 4 }, f => Carregar(f.Chave));
        lock (_trava) return new Dictionary<string, string>(Erros);
    }

    /// <summary>Recarrega só as fontes cujo JSON mudou no servidor. Devolve as chaves recarregadas.</summary>
    public List<string> RecarregarSeMudou()
    {
        var mudou = new List<string>();
        foreach (var f in Fontes)
        {
            string json = CaminhoJson(f.Chave);
            if (!File.Exists(json)) continue;
            DateTime m = File.GetLastWriteTimeUtc(json);
            bool diferente;
            lock (_trava) diferente = !_modificado.TryGetValue(f.Chave, out var antes) || antes != m;
            if (diferente)
            {
                Carregar(f.Chave);
                mudou.Add(f.Chave);
            }
        }
        lock (_trava) _boletim.Clear();
        return mudou;
    }

    public Planilha? Planilha(string chave)
    {
        lock (_trava)
            if (_pl.TryGetValue(chave, out var pl)) return pl;
        Carregar(chave);
        lock (_trava) return _pl.GetValueOrDefault(chave);
    }

    /// <summary>Para testes e para injetar dados prontos.</summary>
    public void Definir(string chave, Planilha pl)
    {
        lock (_trava)
        {
            _pl[chave] = pl;
            _versao++;
            foreach (var k in _indices.Keys.Where(k => k.Item1 == chave).ToList()) _indices.Remove(k);
        }
    }

    // ------------------------------------------------------------------ ÍNDICE + CORRESP
    private Dictionary<string, int> Indice(string chave, int col)
    {
        lock (_trava)
        {
            if (_indices.TryGetValue((chave, col), out var d)) return d;
        }
        var pl = Planilha(chave);
        var novo = new Dictionary<string, int>();
        if (pl is not null)
            for (int r = 1; r <= pl.NLinhas; r++)
            {
                string n = Texto.Norm(pl.V(r, col));
                if (n != "") novo.TryAdd(n, r);
            }
        lock (_trava) _indices[(chave, col)] = novo;
        return novo;
    }

    /// <summary>ÍNDICE(colValor; CORRESP(valor; colChave; 0)). Devolve null se não achar.</summary>
    public object? Buscar(string chave, object? valor, int colChave, int colValor)
    {
        string n = Texto.Norm(valor);
        if (n == "") return null;
        if (!Indice(chave, colChave).TryGetValue(n, out int r)) return null;
        return Planilha(chave)?.V(r, colValor);
    }

    public bool Existe(string chave, object? valor, int colChave = 1)
    {
        string n = Texto.Norm(valor);
        return n != "" && Indice(chave, colChave).ContainsKey(n);
    }

    private string B(string chave, object? valor, int colChave, int colValor) =>
        Texto.Txt(Buscar(chave, valor, colChave, colValor));

    // ------------------------------------------------------------------ consultas do dia a dia
    public string NomePorMatricula(string mat) => Texto.Proper(B("matativas", mat, 2, 3));
    public string MatriculaPorNome(string nome) => B("matativas", nome, 3, 2);
    public string APagar(string mat) => B("matativas", mat, 2, 16);
    public string MateriaAtual(string mat) => Texto.Proper(B("acomp", mat, 1, 5));
    public string AulaAtual(string mat) => Texto.Proper(B("acomp", mat, 1, 6));

    /// <summary>"Há 3 dias" desde a última presença.</summary>
    public string UltimaPresenca(string mat, DateOnly hoje)
    {
        var d = Texto.ParseData(Buscar("acomp", mat, 1, 7));
        if (d is null) return "";
        int n = hoje.DayNumber - d.Value.DayNumber;
        return n == 1 ? "Há 1 dia" : $"Há {n} dias";
    }

    public (string Pratica, string Apostila) BloqueioFlags(string nome) => (B("bloqueio", nome, 1, 2), B("bloqueio", nome, 1, 3));
    public bool TemReposicao(string nome) => Existe("hist1", nome, 1);
    public string Situacao(string nome) => B("obs", nome, 1, 3);
    public string Inicio(string nome) => B("prepara", nome, 3, 8);

    public (string Comentario, string Conquistas, string Pontos) Comentarios(string nome) =>
        (B("coments", nome, 1, 2), B("coments", nome, 1, 3), B("coments", nome, 1, 4));

    /// <summary>null = o aluno não está na planilha Coments.</summary>
    public bool? TemComentario(string nome)
    {
        if (!Existe("coments", nome, 1)) return null;
        var (a, b, c) = Comentarios(nome);
        return a != "" || b != "" || c != "";
    }

    public (string T1, string T2, string T3, string Insta) Telefones(string nome)
    {
        static string Ou(string v, string padrao) => v == "" ? padrao : v;
        return (Ou(B("acomp", nome, 2, 3), "A Cadastrar"), Ou(B("dados", nome, 2, 4), "A Cadastrar"),
                Ou(B("dados", nome, 2, 5), "A Cadastrar"), Ou(B("dados", nome, 2, 6), "Sem Insta"));
    }

    /// <summary>Os três telefones usados para enviar mensagens.</summary>
    public List<string> TelefonesContato(string nome)
    {
        string d = B("dados", nome, 2, 5);
        return new List<string> { B("acomp", nome, 2, 3), B("dados", nome, 2, 4), d is "" or "0" ? "" : d };
    }

    public string ApostilaPorNome(string nome) => Texto.Proper(B("acomp", nome, 2, 5));
    public string AulaPorNome(string nome) => Texto.Proper(B("acomp", nome, 2, 6));
    public string NovatoPorNome(string nome) => B("obs", nome, 1, 3);

    /// <summary>Matrículas presentes hoje: coluna B, linhas 2 a 31 da aba Presencas do histórico.</summary>
    public HashSet<string> PresencasHoje()
    {
        var pl = Planilha("histpres");
        var s = new HashSet<string>();
        if (pl is null) return s;
        for (int r = 2; r <= Math.Min(pl.NLinhas, 31); r++)
        {
            string n = Texto.Norm(pl.V(r, 2));
            if (n != "") s.Add(n);
        }
        return s;
    }

    // ------------------------------------------------------------------ Boletim
    /// <summary>Aba do Boletim pelo nome; null no nome = primeira aba do arquivo.</summary>
    private Planilha? AbaBoletim(string? aba)
    {
        string json = FonteJson.CaminhoEspelho(_cfg.P("tec_json_dir"), _cfg.P("boletim_xlsm"));
        if (json == "" || !File.Exists(json)) return null;
        DateTime m = File.GetLastWriteTimeUtc(json);
        string chave = aba ?? "\u0000primeira";
        lock (_trava)
            if (_boletim.TryGetValue(chave, out var c) && c.Data == m) return c.Pl;
        Planilha pl;
        try { pl = FonteJson.Ler(json, new[] { aba }); }
        catch (AbaNaoEncontradaException) { return null; }
        lock (_trava) _boletim[chave] = (m, pl);
        return pl;
    }

    /// <summary>
    /// Aproveitamento do mês (0,8 = 80%). null quando não há valor válido.
    /// Lê o JSON do Boletim.xlsm. Por padrão usa a PRIMEIRA aba (alunos e notas);
    /// "aba_valores" / "aba_nomes" no config permitem apontar outra aba.
    /// </summary>
    public double? Aproveitamento(string nome)
    {
        var b = _cfg.Boletim;
        string? abaVal = b.AbaValores != "" ? b.AbaValores : null;
        string? abaNom = b.AbaNomes != "" ? b.AbaNomes : abaVal;
        int cn = Texto.ColIdx(b.ColNome), cv = Texto.ColIdx(b.ColValor);
        var pn = AbaBoletim(abaNom);
        var pv = string.Equals(abaNom, abaVal, StringComparison.OrdinalIgnoreCase) ? pn : AbaBoletim(abaVal);
        if (pn is null || pv is null) return null;
        string n = Texto.Norm(nome);
        if (n == "") return null;
        for (int r = 1; r <= pn.NLinhas; r++)
        {
            if (Texto.Norm(pn.V(r, cn)) != n) continue;
            return pv.V(r, cv) switch
            {
                double d => d,
                string s => LerPercentual(s),
                _ => null,
            };
        }
        return null;
    }

    private static double? LerPercentual(string s)
    {
        s = s.Trim().Replace(',', '.');
        bool pct = s.EndsWith('%');
        if (pct) s = s[..^1];
        if (!double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v))
            return null;
        return pct ? v / 100 : v;
    }
}
