using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EverSoft.Core.Configuracao;
using EverSoft.Core.Dados;
using EverSoft.Core.Dominio;
using EverSoft.Core.Planilhas;
using EverSoft.Core.Util;

namespace EverSoft.Core.Servicos;

/// <summary>
/// O "cérebro" do app: junta configuração, estado e dados, e tem um método para cada
/// botão/macro da planilha antiga. A tela (interface) só chama estes métodos.
/// </summary>
public class Nucleo
{
    public AppConfig Cfg { get; }
    public Estado Estado { get; }
    public DataHub Hub { get; }
    public List<Slot> Slots => Estado.Slots;

    /// <summary>Relógio do app. Nos testes trocamos por uma data fixa.</summary>
    public Func<DateTime> Agora { get; set; } = () => DateTime.Now;
    /// <summary>Sorteio dos modelos de lembrete. Nos testes usamos uma semente fixa.</summary>
    public Random Sorteio { get; set; } = Random.Shared;

    private readonly Dictionary<(int, string), LinhaAluno> _cacheLinhas = new();
    private readonly object _trava = new();

    public Nucleo(AppConfig cfg, Estado? estado = null, DataHub? hub = null)
    {
        Cfg = cfg;
        Estado = estado ?? Estado.Carregar(cfg.CaminhoEstado);
        Hub = hub ?? new DataHub(cfg);
        InicioCap2 = new InicioCap2(Path.Combine(cfg.PastaDados, "inicio_cap2.json"));
    }

    /// <summary>Datas de entrada no Cap 2 (coluna INÍCIO de quem está no Cap 2).</summary>
    public InicioCap2 InicioCap2 { get; }

    private DateOnly Hoje => DateOnly.FromDateTime(Agora());

    private GravadorPlanilha Gravador => new(Cfg.PastaBackups, Cfg.P("tec_json_dir"), Cfg.BackupAntesDeGravar);

    public void Salvar() => Estado.Salvar();

    public Slot? Slot(string nome) => Slots.FirstOrDefault(s => s.Nome == nome);

    // ------------------------------------------------------------------ linhas calculadas
    public LinhaAluno Linha(string matricula)
    {
        var chave = (Hub.VersaoDados, Texto.Norm((matricula ?? "").Trim()));
        lock (_trava)
        {
            if (_cacheLinhas.TryGetValue(chave, out var l)) return l;
            if (_cacheLinhas.Count > 0 && _cacheLinhas.Keys.First().Item1 != chave.Item1) _cacheLinhas.Clear();
        }
        var nova = LinhaAluno.Calcular(Hub, matricula ?? "", Hoje, InicioCap2.Buscar);
        lock (_trava) _cacheLinhas[chave] = nova;
        return nova;
    }

    public List<LinhaAluno> Linhas(Slot slot) => slot.Matriculas.Select(Linha).ToList();

    // ------------------------------------------------------------------ PreencherMatriculas
    /// <summary>Recria as abas de horário a partir das grades presencial e EAD.</summary>
    public List<string> PreencherMatriculas()
    {
        var g = Cfg.Grade;
        var avisos = new List<string>();
        // Guarda as marcas J pelo nome da aba + matrícula, para não perder ao reconstruir.
        var marcasAnteriores = new HashSet<(string, string)>();
        foreach (var s in Slots)
            for (int i = 0; i < s.Matriculas.Count; i++)
                if (s.Marcas[i] && !string.IsNullOrWhiteSpace(s.Matriculas[i]))
                    marcasAnteriores.Add((s.Nome.ToLowerInvariant(), Texto.Norm(s.Matriculas[i])));

        string presencial = Cfg.P("grade_presencial");
        if (presencial == "") throw new FileNotFoundException("Grade presencial não configurada em Configurações.");
        var pl = LerGrade(presencial);
        var novos = Grade.ProcessarGrade(pl, g.LinhasPresencial, "", "presencial", g.LinhaCabecalho);
        avisos.Add($"Grade presencial ({(pl.Nome == "" ? "primeira aba" : pl.Nome)}): {novos.Count} horário(s).");

        string ead = Cfg.P("grade_ead");
        if (ead != "")
        {
            var pe = LerGrade(ead);
            var linhasEad = Grade.BuscarLinhasEad(pe);
            if (linhasEad.Count > 0)
            {
                var slotsEad = Grade.ProcessarGrade(pe, linhasEad, " - EAD", "ead", g.LinhaCabecalho);
                novos.AddRange(slotsEad);
                avisos.Add($"Grade EAD ({(pe.Nome == "" ? "primeira aba" : pe.Nome)}): {slotsEad.Count} horário(s).");
            }
            else avisos.Add("Grade EAD: nenhum aluno encontrado nas linhas configuradas.");
        }
        if (novos.Count == 0)
            throw new InvalidOperationException("Nenhum horário foi encontrado na grade. Verifique a aba, a linha do cabeçalho " +
                                                "e se os horários estão no formato 13:00 - 15:00.");

        foreach (var s in novos)
        {
            for (int i = 0; i < s.Matriculas.Count; i++)
                s.Marcas[i] = marcasAnteriores.Contains((s.Nome.ToLowerInvariant(), Texto.Norm(s.Matriculas[i])));
            Grade.OrdenarSlotPorNome(Hub, s);
        }
        Estado.Slots = Grade.OrdenarAbas(novos);
        Salvar();
        return avisos;
    }

    private Planilha LerGrade(string caminhoExcel)
    {
        string json = FonteJson.CaminhoEspelho(Cfg.P("tec_json_dir"), caminhoExcel);
        if (!File.Exists(json))
            throw new FileNotFoundException($"JSON não encontrado para {Path.GetFileName(caminhoExcel)}. {DataHub.OrientacaoJson(caminhoExcel)}");
        return FonteJson.Ler(json, new[] { Cfg.Grade.Aba, null });
    }

    // ------------------------------------------------------------------ formulários de gravação
    /// <summary>Formulário Status: grava a situação do aluno em Observacoes.</summary>
    public void RegistrarStatus(string nome, string obs, bool atrasado = false, bool ok = false, bool adiantado = false,
                                IReadOnlyList<bool>? caps = null, bool naoPratica = false)
    {
        string c = "";
        if (atrasado) c = "Atrasado";
        else if (ok) c = "OK";
        else if (adiantado) c = "Adiantado";
        else if (caps is not null)
            for (int i = 0; i < Math.Min(4, caps.Count); i++)
                if (caps[i]) { c = $"Cap {i + 1}"; break; }
        var valores = new Dictionary<int, object?>
        {
            [1] = nome, [2] = obs, [3] = c, [4] = "", [5] = naoPratica ? " e não pratica" : "",
        };
        Gravador.GravarNoTopo(Cfg.P("observacoes"), "Sheet1", valores, 5);
        if (c == "Cap 2")
            try { InicioCap2.Registrar(nome, Hoje); } catch (Exception e) { Log.Erro($"início do Cap 2 de {nome}: {e.Message}"); }
        Hub.Carregar("obs");
    }

    /// <summary>Formulário Bloqueio: 4 marcas (prática, apostila, digitação, prova).</summary>
    public void RegistrarBloqueio(string nome, IReadOnlyList<bool> marcas)
    {
        var valores = new Dictionary<int, object?> { [1] = nome };
        for (int i = 0; i < 4; i++) valores[2 + i] = i < marcas.Count && marcas[i] ? "X" : "";
        Gravador.GravarNoTopo(Cfg.P("bloqueio"), Cfg.Bloqueio.Aba, valores, 5);
        Hub.Carregar("bloqueio");
    }

    /// <summary>Formulário Coments.</summary>
    public void RegistrarComents(string nome, string comentario, string conquistas, string pontos)
    {
        var valores = new Dictionary<int, object?> { [1] = nome, [2] = comentario, [3] = conquistas, [4] = pontos };
        Gravador.GravarNoTopo(Cfg.P("coments"), "Planilha1", valores, 4);
        Hub.Carregar("coments");
    }

    /// <summary>NotificaReposicao: tira o aluno da lista de faltas do histórico.</summary>
    public int RemoverNotificacao(string nome)
    {
        int n = Gravador.RemoverLinhasPorNome(Cfg.P("historico"), new[] { Cfg.Historico.AbaFaltas, "Sheet1" }, nome);
        Hub.Carregar("hist1");
        return n;
    }

    // ------------------------------------------------------------------ Justifica
    /// <summary>Registra a justificativa de falta (com ou sem reposição marcada).</summary>
    public void Justificar(string slotNome, string nome, string texto, bool marcou)
    {
        string sufixo = marcou ? "/MARCOU REPOSIÇÃO" : "/NÃO MARCOU REPOSIÇÃO";
        var reg = Estado.Justifica.FirstOrDefault(r => Texto.Norm(r.Nome) == Texto.Norm(nome));
        if (reg is null)
        {
            reg = new RegistroJustifica();
            Estado.Justifica.Add(reg);
        }
        reg.Nome = nome;
        reg.Justificativa = texto + sufixo;
        reg.Data = Hoje.AddDays(1).ToString("yyyy-MM-dd");
        reg.Horario = slotNome;
        Salvar();
    }

    /// <summary>
    /// Grava as justificativas da faixa atual em "dd.MM.yy.json" (usado pela remoção de faltas).
    /// Só cria o arquivo se houver ao menos uma justificativa preenchida.
    /// </summary>
    public int GravarJsonJustificativas(string codigoFaixa, DateOnly? dataRef = null)
    {
        if (codigoFaixa == "") return 0;
        var registros = new List<Dictionary<string, string>>();
        var vistos = new HashSet<string>();
        foreach (var reg in Estado.Justifica)
        {
            if (Texto.NormalizarHorario(reg.Horario) != Texto.NormalizarHorario(codigoFaixa)) continue;
            string nome = reg.Nome.Trim();
            string just = reg.Justificativa.Split('/', 2)[0].Trim();
            if (nome == "" || just == "") continue;
            string codigo = Hub.MatriculaPorNome(nome).Trim();
            if (codigo == "" || !vistos.Add(Texto.Norm(codigo))) continue;
            registros.Add(new() { ["codigo"] = codigo, ["nome"] = nome, ["justificativa"] = just });
        }
        if (registros.Count == 0) return 0;

        string pasta = Cfg.P("justificativas_json_dir");
        if (pasta == "") throw new InvalidOperationException("Pasta dos JSONs de justificativas não está configurada.");
        string destino = Path.Combine(pasta, (dataRef ?? Hoje).ToString("dd.MM.yy") + ".json");

        var existentes = new List<Dictionary<string, string>>();
        if (File.Exists(destino))
        {
            try
            {
                var no = JsonNode.Parse(File.ReadAllText(destino));
                JsonArray? arr = no as JsonArray ?? (no?["justificativas"] ?? no?["registros"]) as JsonArray;
                if (arr is not null)
                    foreach (var item in arr.OfType<JsonObject>())
                        existentes.Add(item.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? ""));
            }
            catch (Exception e)
            {
                Log.Erro($"JSON de justificativas inválido ({destino}): {e.Message}");
            }
        }

        var porCodigo = new Dictionary<string, Dictionary<string, string>>();
        var ordem = new List<string>();
        foreach (var item in existentes.Concat(registros))
        {
            string codigo = item.GetValueOrDefault("codigo", "").Trim();
            if (codigo == "") continue;
            string k = Texto.Norm(codigo);
            if (!porCodigo.ContainsKey(k)) ordem.Add(k);
            porCodigo[k] = new()
            {
                ["codigo"] = codigo,
                ["nome"] = item.GetValueOrDefault("nome", "").Trim(),
                ["justificativa"] = item.GetValueOrDefault("justificativa", "").Split('/', 2)[0].Trim(),
            };
        }
        Json.Salvar(destino, ordem.Select(k => porCodigo[k]).ToList());
        return registros.Count;
    }

    /// <summary>
    /// Passa para a Justifica os alunos da aba que não estão presentes hoje
    /// (compara com a aba Presencas do histórico) e que não têm marca J.
    /// </summary>
    public List<string> TransferirParaJustifica(Slot slot)
    {
        string excel = Cfg.P("historico");
        if (excel == "" || !File.Exists(excel))
            throw new FileNotFoundException($"Arquivo de histórico não encontrado: {(excel == "" ? "(caminho não configurado)" : excel)}\n" +
                                            "Confira o caminho em Configurações > Histórico.");
        // O JSON é refeito a cada clique, para refletir o Excel atualizado.
        string json = Sincronizacao.GerarJsonCompleto(Cfg, excel);
        if (!FonteJson.LerTodas(json).Any(p => string.Equals(p.Nome, "Presencas", StringComparison.OrdinalIgnoreCase)))
            throw new AbaNaoEncontradaException($"A aba 'Presencas' não existe no arquivo {Path.GetFileName(excel)}.");
        Hub.Carregar("histpres");
        if (Hub.Planilha("histpres") is null)
            throw new InvalidOperationException($"Não foi possível consultar a aba 'Presencas' ({Hub.Erros.GetValueOrDefault("histpres", "sem detalhes")}).");

        var presentes = Hub.PresencasHoje();
        var existentes = Estado.Justifica.Select(r => Texto.Norm(r.Nome)).ToHashSet();
        var novos = new List<string>();
        var linhas = Linhas(slot);
        for (int i = 0; i < linhas.Count; i++)
        {
            string nome = linhas[i].Nome;
            if (nome == "" || slot.Marcas[i]) continue;
            if (presentes.Contains(Texto.Norm(slot.Matriculas[i])) || existentes.Contains(Texto.Norm(nome))) continue;
            Estado.Justifica.Add(new RegistroJustifica { Nome = nome, Horario = slot.Nome });
            existentes.Add(Texto.Norm(nome));
            novos.Add(nome);
        }
        Salvar();
        return novos;
    }

    public record MensagemFaltososInfo(string Mensagem, string Codigo, string Faixa, string Dia, int Com, int Sem);

    /// <summary>
    /// Monta a mensagem de faltosos. Sem <paramref name="faixa"/>, usa a faixa do horário atual
    /// (null fora do horário de aula). Com <paramref name="faixa"/> (ex.: "13:00 ÀS 15:00"),
    /// monta a de uma faixa anterior: substitui os antigos botões que voltavam o relógio do Windows.
    /// </summary>
    public MensagemFaltososInfo? MensagemFaltosos(DateTime? agora = null, string? faixa = null)
    {
        var a = agora ?? Agora();
        faixa ??= Faixas.FaixaHorarioJustifica(a);
        if (faixa == "") return null;
        string cod = Faixas.CodigoFaixa(faixa);
        string dia = Faixas.DiaSemanaTexto(a);
        var (msg, com, sem) = Mensagens.MontarFaltosos(Estado.Justifica, Hub, dia, faixa, cod);
        return new MensagemFaltososInfo(msg, cod, faixa, dia, com, sem);
    }

    /// <summary>Limpa da Justifica os alunos de uma faixa (depois de enviar ao grupo).</summary>
    public int LimparFaixa(string codigo)
    {
        int antes = Estado.Justifica.Count;
        Estado.Justifica.RemoveAll(r => Texto.NormalizarHorario(r.Horario) == codigo);
        Salvar();
        return antes - Estado.Justifica.Count;
    }

    // ------------------------------------------------------------------ Lembretes
    public string GerarTextoLembrete(string nome, string hora, string curso)
    {
        var modelos = Cfg.ModelosLembrete.Count > 0
            ? Cfg.ModelosLembrete
            : new List<string> { "{NOME}, hoje às {HORA} tem aula de *{CURSO}* aqui na TEC. Contamos com você! 💙" };
        string t = modelos[Sorteio.Next(modelos.Count)];
        return t.Replace("{NOME}", nome).Replace("{HORA}", hora).Replace("{CURSO}", curso) + Cfg.Lembrete.SufixoTexto;
    }

    /// <summary>Monta a Agenda de lembretes: alunos da aba sem J, com telefones limpos.</summary>
    /// <summary>Pasta dos áudios da faixa atual: &lt;audios&gt;\Semana|Sabado\0745... (null fora de faixa).</summary>
    public string? PastaAudioAtual(DateTime? agora = null)
    {
        var f = Faixas.FaixaAudio(agora ?? Agora());
        return f is null ? null : Path.Combine(Cfg.P("audios_lembretes"), f.Value.Dia, f.Value.Faixa);
    }

    public List<ItemAgenda> PrepararLembretes(Slot slot, DateTime? agora = null)
    {
        string? rot = Faixas.RotuloHoraLembrete(agora ?? Agora());
        if (rot is not null) Estado.HoraLembrete = rot;
        var linhas = Linhas(slot);
        var pares = new List<(string, string)>();
        for (int i = 0; i < linhas.Count; i++)
        {
            if (linhas[i].Nome == "" || slot.Marcas[i]) continue;
            foreach (var tel in Hub.TelefonesContato(linhas[i].Nome))
                if (tel.Trim() != "") pares.Add((linhas[i].Nome, tel.Trim()));
        }
        var agenda = Texto.TratarContatos(pares).Select(p =>
        {
            string curso = Hub.ApostilaPorNome(p.Nome);
            return new ItemAgenda
            {
                Nome = p.Nome, Numero = p.Telefone,
                Mensagem = GerarTextoLembrete(p.Nome, Estado.HoraLembrete, curso),
                Apostila = curso, Aula = Hub.AulaPorNome(p.Nome), Novatos = Hub.NovatoPorNome(p.Nome),
            };
        }).ToList();
        Estado.Agenda = agenda;
        Salvar();
        return agenda;
    }

    // ------------------------------------------------------------------ Boletim
    /// <summary>Lista de envio dos boletins: só alunos que têm aproveitamento válido.</summary>
    public List<ItemDisparo> ColetarBoletins()
    {
        var pares = new List<(string, string)>();
        var cache = new Dictionary<string, double?>();
        foreach (var s in Slots)
            foreach (var l in Linhas(s))
            {
                string nome = l.Nome.Trim();
                if (nome == "") continue;
                string k = Texto.Norm(nome);
                if (!cache.TryGetValue(k, out var ap)) cache[k] = ap = Hub.Aproveitamento(nome);
                if (ap is null) continue;
                foreach (var tel in Hub.TelefonesContato(nome))
                    if (tel.Trim() != "") pares.Add((nome, tel.Trim()));
            }
        Estado.Disparo = Texto.TratarContatos(pares).Select(p => new ItemDisparo { Nome = p.Nome, Numero = p.Telefone }).ToList();
        Salvar();
        return Estado.Disparo;
    }

    public string? BoletimDoAluno(string nome)
    {
        var frac = Hub.Aproveitamento(nome);
        return frac is null ? null : Mensagens.MensagemBoletim(nome, Cfg.Boletim.MesAtual, Cfg.Boletim.ProximoMes, frac.Value);
    }

    // ------------------------------------------------------------------ aluno selecionado
    public record InfoAluno(string Nome, string Num1, string Num2, string Num3, string Insta,
                            string Comentario, string Conquistas, string Pontos);

    public InfoAluno Info(string nome)
    {
        var (t1, t2, t3, insta) = Hub.Telefones(nome);
        var (com, con, pts) = Hub.Comentarios(nome);
        return new InfoAluno(nome, t1, t2, t3, insta, com, con, pts);
    }

    /// <summary>Caminho da foto do aluno (.bmp ou .png), ou null.</summary>
    public string? FotoAluno(string nome)
    {
        nome = (nome ?? "").Trim();
        if (nome == "") return null;
        var cand = new List<string>();
        string b = Cfg.P("fotos_alunos");
        if (b != "") cand.AddRange(new[] { Path.Combine(b, nome + ".bmp"), Path.Combine(b, nome + ".png") });
        string bmp = Cfg.P("fotos_bmp");
        if (bmp != "") cand.Add(Path.Combine(bmp, nome + ".bmp"));
        return cand.FirstOrDefault(File.Exists);
    }

    public string UrlAluno(string matricula) => Cfg.P("link_aluno") + (matricula ?? "").Trim();

    /// <summary>Nome seguro para arquivo (sem \ / : * ? " &lt; &gt; |).</summary>
    public static string NomeArquivoSeguro(string nome) => Regex.Replace(nome, @"[\\/:*?""<>|]", "").Trim();
}
