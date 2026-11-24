using System.Text.Json.Nodes;
using EverSoft.Core.Configuracao;
using EverSoft.Core.Dominio;
using EverSoft.Core.Planilhas;

namespace EverSoft.Tests;

public class NucleoTests : IDisposable
{
    private readonly Cenario _c = new();
    private static readonly DateTime Segunda1330 = new(2026, 11, 23, 13, 30, 0);
    public void Dispose() => _c.Dispose();

    [Fact]
    public void PreencherMatriculas_cria_abas_ordenadas_e_alunos_em_ordem_alfabetica()
    {
        var n = _c.NovoNucleo(Segunda1330);
        var avisos = n.PreencherMatriculas();
        Assert.Contains("2 horário(s)", avisos[0]);
        Assert.Equal(new[] { "07h45 - 09h40", "13h00 - 15h00" }, n.Slots.Select(s => s.Nome));
        var tarde = n.Slot("13h00 - 15h00")!;
        Assert.Equal(new[] { "101", "103", "" }, tarde.Matriculas.Take(3));   // Ana antes de Carla
        Assert.Equal(19, tarde.Matriculas.Count);
        Assert.Equal(2, tarde.Usados);
        Assert.True(File.Exists(_c.Cfg.CaminhoEstado));
    }

    [Fact]
    public void PreencherMatriculas_mantem_a_marca_J_do_mesmo_aluno()
    {
        var n = _c.NovoNucleo(Segunda1330);
        n.PreencherMatriculas();
        var tarde = n.Slot("13h00 - 15h00")!;
        tarde.Marcas[1] = true;                     // Carla (103)
        n.PreencherMatriculas();
        tarde = n.Slot("13h00 - 15h00")!;
        Assert.False(tarde.Marcas[0]);
        Assert.True(tarde.Marcas[1]);
        Assert.Equal("103", tarde.Matriculas[1]);
    }

    [Fact]
    public void Linha_calcula_as_colunas_da_aba()
    {
        var n = _c.NovoNucleo(Segunda1330);
        var ana = n.Linha("101");
        Assert.Equal("Ana Lima", ana.Nome);
        Assert.Equal("Informática", ana.Materia);
        Assert.Equal("Aula 3", ana.Aula);
        Assert.Equal("Há 3 dias", ana.Ultima);
        Assert.Equal("R$ 50,00", ana.APagar);
        Assert.Equal("OK", ana.Sit);
        Assert.True(ana.Coment);

        var bruno = n.Linha("102");
        Assert.Equal("Bruno Reis", bruno.Nome);           // espaço duplo limpo pela Automação TEC
        Assert.Equal("X", bruno.Pratica);
        Assert.Equal("Há 1 dia", bruno.Ultima);
        Assert.False(bruno.Coment);

        var carla = n.Linha("103");
        Assert.Equal("X", carla.R);
        Assert.Null(carla.Coment);
        Assert.True(n.Linha("").Vazia);
    }

    [Fact]
    public void TransferirParaJustifica_so_leva_quem_faltou_e_nao_tem_J()
    {
        var n = _c.NovoNucleo(Segunda1330);
        n.PreencherMatriculas();
        var novos = n.TransferirParaJustifica(n.Slot("13h00 - 15h00")!);
        Assert.Equal(new[] { "Carla Souza" }, novos);   // Ana (101) está na aba Presencas
        Assert.Empty(n.TransferirParaJustifica(n.Slot("13h00 - 15h00")!)); // não repete
    }

    [Fact]
    public void Justificar_mensagem_faltosos_json_e_limpar_faixa()
    {
        var n = _c.NovoNucleo(Segunda1330);
        n.Justificar("13h00 - 15h00", "Ana Lima", "Consulta médica", marcou: true);
        n.Estado.Justifica.Add(new RegistroJustifica { Nome = "Bruno Reis", Horario = "13h00 - 15h00" });

        var m = n.MensagemFaltosos()!;
        Assert.Equal("13h00-15h00", m.Codigo);
        Assert.Contains("- 101 - ANA LIMA: *CONSULTA MÉDICA/MARCOU REPOSIÇÃO*", m.Mensagem);
        Assert.Equal((1, 1), (m.Com, m.Sem));
        Assert.Equal("2026-11-24", n.Estado.Justifica[0].Data);

        Assert.Equal(1, n.GravarJsonJustificativas(m.Codigo));
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(_c.Cfg.P("justificativas_json_dir"), "23.11.26.json")))!.AsArray();
        Assert.Equal("101", json[0]!["codigo"]!.ToString());
        Assert.Equal("Consulta médica", json[0]!["justificativa"]!.ToString());

        Assert.Equal(2, n.LimparFaixa(m.Codigo));
        Assert.Null(n.MensagemFaltosos(new DateTime(2026, 11, 23, 22, 0, 0)));
    }

    [Fact]
    public void Faixa_escolhida_na_mao_substitui_mudar_o_relogio()
    {
        var n = _c.NovoNucleo(new DateTime(2026, 11, 23, 18, 0, 0));      // já são 18h
        n.Justificar("13h00 - 15h00", "Ana Lima", "Viagem", marcou: false);
        Assert.Contains("13:00 ÀS 15:00", Faixas.FaixasDoDia(n.Agora()));
        Assert.DoesNotContain("ANA LIMA", n.MensagemFaltosos()!.Mensagem);   // faixa atual (17h às 19h)
        var m = n.MensagemFaltosos(faixa: "13:00 ÀS 15:00")!;
        Assert.Equal("13h00-15h00", m.Codigo);
        Assert.Contains("- 101 - ANA LIMA: *VIAGEM/NÃO MARCOU REPOSIÇÃO*", m.Mensagem);
        Assert.Equal(4, Faixas.FaixasDoDia(new DateTime(2026, 11, 28)).Count);   // sábado
    }

    [Fact]
    public void PrepararLembretes_monta_agenda_sem_J_e_com_numeros_limpos()
    {
        var n = _c.NovoNucleo(Segunda1330);
        n.Cfg.ModelosLembrete = new() { "{NOME}: aula às {HORA} de {CURSO}." };
        n.Cfg.Lembrete.SufixoTexto = "";
        n.PreencherMatriculas();
        var agenda = n.PrepararLembretes(n.Slot("13h00 - 15h00")!);
        Assert.Equal("15:00h", n.Estado.HoraLembrete);
        Assert.Equal(new[] { ("Ana Lima", "83998765432"), ("Ana Lima", "83988887777") },
                     agenda.Select(a => (a.Nome, a.Numero)));   // Carla não tem telefone
        Assert.Equal("Ana Lima: aula às 15:00h de Informática.", agenda[0].Mensagem);
    }

    [Fact]
    public void ColetarBoletins_so_quem_tem_aproveitamento()
    {
        var n = _c.NovoNucleo(Segunda1330);
        n.PreencherMatriculas();
        var lista = n.ColetarBoletins();
        Assert.All(lista, i => Assert.Equal("Ana Lima", i.Nome));
        Assert.Equal(0.85, n.Hub.Aproveitamento("ana lima"));
        Assert.Null(n.BoletimDoAluno("Bruno Reis"));
        Assert.Contains("`85%!`", n.BoletimDoAluno("Ana Lima"));
    }

    [Fact]
    public void RegistrarStatus_grava_na_planilha_e_o_app_ve_na_hora()
    {
        var n = _c.NovoNucleo(Segunda1330);
        n.RegistrarStatus("Ana Lima", "faltou prova", caps: new[] { false, true });
        Assert.Equal("Cap 2", n.Linha("101").Sit);
        Assert.Equal(2, LeitorPlanilha.Ler(_c.Cfg.P("observacoes")).NLinhas); // substituiu o registro antigo
    }

    [Fact]
    public void Bloqueio_Coments_e_RemoverNotificacao()
    {
        var n = _c.NovoNucleo(Segunda1330);
        n.RegistrarBloqueio("Carla Souza", new[] { false, true });
        Assert.Equal(("", "X"), n.Hub.BloqueioFlags("Carla Souza"));
        n.RegistrarComents("Carla Souza", "Melhorou", "", "");
        Assert.True(n.Linha("103").Coment);
        Assert.Equal(1, n.RemoverNotificacao("Bruno Reis"));
    }

    [Fact]
    public void Config_le_o_config_json_antigo_e_completa_o_que_falta()
    {
        string pasta = Path.Combine(_c.Pasta, "cfg");
        Directory.CreateDirectory(pasta);
        File.WriteAllText(Path.Combine(pasta, "config.json"), """
            {"caminhos": {"historico": "X:\\Historico.xlsx"}, "boletim": {"mes_atual": "Março"},
             "grade": {"linhas_presencial": [1, 2]}, "whatsapp": {"clique_busca": [1, 2]}}
            """);
        var cfg = AppConfig.Carregar(pasta);
        Assert.Equal(@"X:\Historico.xlsx", cfg.P("historico"));
        Assert.Contains("RelMatAtivas", cfg.P("mat_ativas"));       // veio do padrão
        Assert.Equal("Março", cfg.Boletim.MesAtual);
        Assert.Equal("Novembro", cfg.Boletim.ProximoMes);
        Assert.Equal(new[] { 1, 2 }, cfg.Grade.LinhasPresencial);   // lista substituída, não somada
        Assert.Equal("RelGradeHorarios", cfg.Grade.Aba);
        Assert.Equal(pasta, cfg.PastaDados);
    }

    [Fact]
    public void Primeira_abertura_copia_config_e_estado_da_versao_python()
    {
        string antiga = Path.Combine(_c.Pasta, "dados_app_py"), nova = Path.Combine(_c.Pasta, "dados_app_v2");
        Directory.CreateDirectory(antiga);
        File.WriteAllText(Path.Combine(antiga, "config.json"), """{"boletim": {"mes_atual": "Março"}}""");
        File.WriteAllText(Path.Combine(antiga, "estado.json"), """{"hora_lembrete": "13:00h"}""");
        Assert.True(AppConfig.ImportarDaVersaoAnterior(antiga, nova));
        Assert.Equal("Março", AppConfig.Carregar(nova).Boletim.MesAtual);
        Assert.False(AppConfig.ImportarDaVersaoAnterior(antiga, nova));     // só na primeira vez
    }

    [Fact]
    public void Estado_le_o_estado_json_do_python()
    {
        string arq = Path.Combine(_c.Pasta, "estado.json");
        File.WriteAllText(arq, """
            {"slots": [{"nome": "13h00 - 15h00", "tipo": "presencial", "matriculas": ["101", ""], "marcas": [true]}],
             "justifica": [{"nome": "Ana", "justificativa": "x", "data": "", "horario": "13h00 - 15h00", "frequencia": ""}],
             "agenda": [], "disparo": [], "hora_lembrete": "13:00h", "aba_ativa": "__just"}
            """);
        var e = Estado.Carregar(arq);
        Assert.Equal(19, e.Slots[0].Matriculas.Count);
        Assert.True(e.Slots[0].Marcas[0]);
        Assert.Equal("13:00h", e.HoraLembrete);
        e.Salvar();
        Assert.Contains("\"hora_lembrete\": \"13:00h\"", File.ReadAllText(arq));
    }
}

public class GradeTests
{
    [Fact]
    public void Funcoes_da_grade_iguais_ao_python()
    {
        Assert.Equal(745, Grade.ChaveInicio("07h45 - 09h40"));
        Assert.Equal("13h00 - 15h00 - EAD", Grade.NomeAba("13:00 - 15:00", " - EAD"));
        var ordem = Grade.OrdenarAbas(new[] { new Slot("13h00 - 15h00 - EAD"), new Slot("13h00 - 15h00"), new Slot("07h45 - 09h40") });
        Assert.Equal(new[] { "07h45 - 09h40", "13h00 - 15h00", "13h00 - 15h00 - EAD" }, ordem.Select(s => s.Nome));
    }

    [Fact]
    public void BuscarLinhasEad_para_no_total_ou_na_linha_vazia()
    {
        var linhas = new List<List<object?>>();
        for (int i = 0; i < 12; i++) linhas.Add(new() { null, null, null });
        linhas[3][2] = "LAB1"; linhas[5][2] = "LAB2"; linhas[7][1] = "Total geral"; linhas[7][2] = "x";
        Assert.Equal(new[] { 4, 6 }, Grade.BuscarLinhasEad(new Planilha(linhas)));
    }
}
