using ClosedXML.Excel;
using EverSoft.Core.Configuracao;
using EverSoft.Core.Servicos;

namespace EverSoft.Tests;

/// <summary>
/// Monta uma "rede de mentira" numa pasta temporária: planilhas com alunos fictícios,
/// config apontando para elas e os JSONs espelho já gerados.
/// </summary>
public sealed class Cenario : IDisposable
{
    public string Pasta { get; } = Path.Combine(Path.GetTempPath(), "eversoft-teste-" + Guid.NewGuid().ToString("N")[..8]);
    public AppConfig Cfg { get; }
    public string Arq(string nome) => Path.Combine(Pasta, "rede", nome);

    public Cenario()
    {
        Directory.CreateDirectory(Path.Combine(Pasta, "rede"));
        Directory.CreateDirectory(Path.Combine(Pasta, "dados_app"));

        // RelMatAtivas: B = matrícula, C = nome, P = a pagar
        CriarXlsx("RelMatAtivas.xlsx", "RelMatAtivas", new object?[][]
        {
            new object?[] { null, "Matrícula", "Nome" },
            Linha(16, (2, 101), (3, "ANA LIMA"), (16, "R$ 50,00")),
            Linha(16, (2, 102), (3, "BRUNO  REIS"), (16, "")),
            Linha(16, (2, 103), (3, "CARLA SOUZA")),
        });
        // O sistema exporta como .xls (por dentro é xlsx); o leitor detecta.
        File.Move(Arq("RelMatAtivas.xlsx"), Arq("RelMatAtivas.xls"));
        // Acompanhar Alunos: A = matrícula, B = nome, C = telefone, E = apostila, F = aula, G = última presença
        CriarXlsx("Acompanhar Alunos.xlsx", "Acompanhar Alunos.xlsx", new object?[][]
        {
            new object?[] { "Mat", "Nome", "Tel", null, "Apostila", "Aula", "Ultima" },
            new object?[] { 101, "Ana Lima", "(83) 9876-5432", null, "INFORMÁTICA", "AULA 3", new DateTime(2026, 11, 20) },
            new object?[] { 102, "Bruno Reis", "83911112222", null, "INGLÊS", "AULA 7", new DateTime(2026, 11, 22) },
            new object?[] { 103, "Carla Souza", "", null, "DESIGN", "AULA 1", null },
        });
        CriarXlsx("Dados dos Alunos.xlsx", "Sheet1", new object?[][]
        {
            new object?[] { "x", "Nome", "y", "Tel2", "Tel3", "Insta" },
            new object?[] { null, "Ana Lima", null, "83988887777", "0", "@ana" },
        });
        CriarXlsx("Observacoes.xlsx", "Sheet1", new object?[][]
        {
            new object?[] { "Nome", "Obs", "Sit", "", "" },
            new object?[] { "Ana Lima", "bem", "OK", null, null },
        });
        CriarXlsx("Bloqueio.xlsx", "Plan1", new object?[][]
        {
            new object?[] { "Nome", "Prática", "Apostila", "Digi", "Prova" },
            new object?[] { "Bruno Reis", "X", null, null, null },
        });
        CriarXlsx("Coments.xlsx", "Planilha1", new object?[][]
        {
            new object?[] { "Nome", "Comentário", "Conquistas", "Pontos" },
            new object?[] { "Ana Lima", "Ótima aluna", null, "10" },
            new object?[] { "Bruno Reis", null, null, null },
        });
        // Histórico: Sheet1 = lista R (reposição), Presencas = quem veio hoje (coluna B)
        using (var wb = new XLWorkbook())
        {
            var s1 = wb.AddWorksheet("Sheet1");
            s1.Cell(1, 1).Value = "Nome";
            s1.Cell(2, 1).Value = "Carla Souza";
            var f = wb.AddWorksheet("Faltas");
            f.Cell(1, 1).Value = "Nome";
            f.Cell(2, 1).Value = "Carla Souza";
            f.Cell(3, 1).Value = "Bruno Reis";
            var p = wb.AddWorksheet("Presencas");
            p.Cell(1, 2).Value = "Código";
            p.Cell(2, 2).Value = 101;
            wb.SaveAs(Arq("Historico.xlsx"));
        }
        CriarXlsx("Base Prepara.xlsx", "Sheet", new object?[][] { new object?[] { "a", "b", "Nome", null, null, null, null, "Início" } });
        // Boletim: aba Outubro, B = nome, C = aproveitamento
        CriarXlsx("Boletim.xlsx", "Notas do mês", new object?[][]
        {
            new object?[] { null, "Nome", "Aprov" },
            new object?[] { null, "Ana Lima", 0.85 },
            new object?[] { null, "Bruno Reis", null },
        });
        // Grade: linha 3 = horários; presencial usa as linhas configuradas abaixo
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("RelGradeHorarios");
            ws.Cell(3, 2).Value = "13:00 - 15:00";
            ws.Cell(3, 3).Value = "07:45 - 09:40";
            ws.Cell(4, 2).Value = 103;
            ws.Cell(6, 2).Value = 101;
            ws.Cell(4, 3).Value = 102;
            wb.SaveAs(Arq("GradeHorariosAction.xlsx"));
        }

        Cfg = AppConfig.Carregar(Path.Combine(Pasta, "dados_app"));
        Cfg.Caminhos["mat_ativas"] = Arq("RelMatAtivas.xls");
        Cfg.Caminhos["acompanhar_alunos"] = Arq("Acompanhar Alunos.xlsx");
        Cfg.Caminhos["dados_alunos"] = Arq("Dados dos Alunos.xlsx");
        Cfg.Caminhos["observacoes"] = Arq("Observacoes.xlsx");
        Cfg.Caminhos["bloqueio"] = Arq("Bloqueio.xlsx");
        Cfg.Caminhos["coments"] = Arq("Coments.xlsx");
        Cfg.Caminhos["historico"] = Arq("Historico.xlsx");
        Cfg.Caminhos["base_prepara"] = Arq("Base Prepara.xlsx");
        Cfg.Caminhos["boletim_xlsm"] = Arq("Boletim.xlsx");
        Cfg.Caminhos["grade_presencial"] = Arq("GradeHorariosAction.xlsx");
        Cfg.Caminhos["grade_ead"] = "";
        Cfg.Caminhos["tec_json_dir"] = Path.Combine(Pasta, "json");
        Cfg.Caminhos["justificativas_json_dir"] = Path.Combine(Pasta, "justificativas");
        Cfg.Caminhos["fotos_alunos"] = Path.Combine(Pasta, "fotos");
        Cfg.Caminhos["fotos_bmp"] = "";
        Cfg.Grade.LinhasPresencial = new() { 4, 6, 8 };

        Sincronizacao.GerarJsonsTec(Cfg);
        Sincronizacao.GerarJsonsSincronizacao(Cfg);
        Sincronizacao.GerarJsonCompleto(Cfg, Cfg.P("base_prepara"));   // a Base Prepara foi convertida uma única vez
    }

    public Nucleo NovoNucleo(DateTime agora)
    {
        var n = new Nucleo(Cfg) { Agora = () => agora, Sorteio = new Random(1) };
        n.Hub.CarregarTudo();
        return n;
    }

    private static object?[] Linha(int tamanho, params (int Col, object? Valor)[] valores)
    {
        var l = new object?[tamanho];
        foreach (var (c, v) in valores) l[c - 1] = v;
        return l;
    }

    public void CriarXlsx(string nome, string aba, object?[][] linhas)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(aba);
        for (int r = 0; r < linhas.Length; r++)
            for (int c = 0; c < linhas[r].Length; c++)
                ws.Cell(r + 1, c + 1).Value = linhas[r][c] switch
                {
                    null => Blank.Value,
                    int i => i,
                    double d => d,
                    DateTime dt => dt,
                    var o => o.ToString(),
                };
        wb.SaveAs(Arq(nome));
    }

    public void Dispose()
    {
        try { Directory.Delete(Pasta, true); } catch { }
    }
}
