using System.Text;
using ClosedXML.Excel;
using EverSoft.Core.Planilhas;

namespace EverSoft.Tests;

public class PlanilhaTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "eversoft-pl-" + Guid.NewGuid().ToString("N")[..8]);
    public PlanilhaTests() => Directory.CreateDirectory(_pasta);
    public void Dispose() { try { Directory.Delete(_pasta, true); } catch { } }
    private string Arq(string n) => Path.Combine(_pasta, n);

    [Fact]
    public void Le_xlsx_escolhe_aba_e_traz_valores_calculados()
    {
        using (var wb = new XLWorkbook())
        {
            wb.AddWorksheet("Primeira").Cell(1, 1).Value = "x";
            var ws = wb.AddWorksheet("Dados");
            ws.Cell(2, 2).Value = 10;
            ws.Cell(2, 3).FormulaA1 = "B2*2";
            ws.Cell(3, 1).Value = new DateTime(2024, 5, 1);
            wb.SaveAs(Arq("a.xlsx"));
        }
        var pl = LeitorPlanilha.Ler(Arq("a.xlsx"), new[] { "dados" });
        Assert.Equal("Dados", pl.Nome);
        Assert.Null(pl.V(1, 1));
        Assert.Equal(10.0, pl.V(2, 2));
        Assert.Equal(new DateTime(2024, 5, 1), pl.V(3, 1));
        Assert.Equal(3, pl.UltimaColunaDaLinha(2));
        Assert.Equal("Primeira", LeitorPlanilha.Ler(Arq("a.xlsx")).Nome);
        Assert.Throws<AbaNaoEncontradaException>(() => LeitorPlanilha.Ler(Arq("a.xlsx"), new[] { "Nao existe" }));
    }

    [Fact]
    public void Le_xls_de_verdade_do_excel_97_2003()
    {
        string arq = Path.Combine(AppContext.BaseDirectory, "Amostras", "biff.xls");
        var pl = LeitorPlanilha.Ler(arq, new[] { "relmatativas" });
        Assert.Equal("RelMatAtivas", pl.Nome);
        Assert.Equal(101.0, pl.V(2, 2));
        Assert.Equal("JOSÉ  DA SILVA", pl.V(2, 3));
        Assert.Equal(2.5, pl.V(3, 2));
        Assert.Equal(new DateTime(2024, 3, 15), pl.V(3, 3));
        Assert.Equal(2, LeitorPlanilha.LerTodas(arq).Count);
    }

    [Fact]
    public void Xlsx_renomeado_para_xls_tambem_funciona()
    {
        using (var wb = new XLWorkbook()) { wb.AddWorksheet("S").Cell(1, 1).Value = "ok"; wb.SaveAs(Arq("b.xlsx")); }
        File.Move(Arq("b.xlsx"), Arq("b.xls"));
        Assert.Equal("ok", LeitorPlanilha.Ler(Arq("b.xls")).V(1, 1));
    }

    [Fact]
    public void Xls_que_e_html_pega_a_maior_tabela()
    {
        File.WriteAllText(Arq("h.xls"), """
            <html><body>
            <table><tr><td>cabeçalho pequeno</td></tr></table>
            <table>
              <tr><th>Matrícula</th><th colspan="2">Nome</th><td>Obs</td></tr>
              <tr><td>101</td><td>Ana&nbsp;  Lima</td><td></td><td>linha<br>dois</td></tr>
            </table></body></html>
            """, Encoding.UTF8);
        var pl = LeitorPlanilha.Ler(Arq("h.xls"));
        Assert.Equal(2, pl.NLinhas);
        Assert.Equal("Matrícula", pl.V(1, 1));
        Assert.Equal("Nome", pl.V(1, 2));
        Assert.Null(pl.V(1, 3));
        Assert.Equal("Obs", pl.V(1, 4));
        Assert.Equal("Ana Lima", pl.V(2, 2));
        Assert.Equal("linha dois", pl.V(2, 4));
    }

    [Fact]
    public void Xls_que_e_xml_do_excel_2003()
    {
        File.WriteAllText(Arq("x.xls"), """
            <?xml version="1.0"?>
            <Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet" xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
             <Worksheet ss:Name="RelGradeHorarios"><Table>
              <Row><Cell><Data ss:Type="String">A</Data></Cell><Cell ss:Index="3"><Data ss:Type="Number">7</Data></Cell></Row>
              <Row><Cell><Data ss:Type="DateTime">2024-02-03T00:00:00.000</Data></Cell></Row>
             </Table></Worksheet>
            </Workbook>
            """);
        var pl = LeitorPlanilha.Ler(Arq("x.xls"), new[] { "RelGradeHorarios" });
        Assert.Equal("A", pl.V(1, 1));
        Assert.Null(pl.V(1, 2));
        Assert.Equal(7.0, pl.V(1, 3));
        Assert.Equal(new DateTime(2024, 2, 3), pl.V(2, 1));
    }

    [Fact]
    public void Json_espelho_le_o_formato_do_python_e_grava_igual()
    {
        // Trecho de um JSON gerado pelo EverSoft.py
        File.WriteAllText(Arq("Acompanhar Alunos.json"), """
            {"versao": 1, "origem": "\\\\servidortec\\x.xlsx", "planilhas": {"Acompanhar Alunos.xlsx": {"rows": [
              ["Mat", "Nome", null], [101, "Ana", {"__tipo__": "datetime", "valor": "2026-11-20T00:00:00"}], [1.5, true, {"__tipo__": "date", "valor": "2026-11-21"}]
            ]}}}
            """);
        var pl = FonteJson.Ler(Arq("Acompanhar Alunos.json"), new[] { "acompanhar alunos.xlsx" });
        Assert.Equal(101.0, pl.V(2, 1));
        Assert.Equal(new DateTime(2026, 11, 20), pl.V(2, 3));
        Assert.Equal(new DateTime(2026, 11, 21), pl.V(3, 3));
        Assert.Equal(true, pl.V(3, 2));

        FonteJson.Gravar(Arq("copia.json"), "origem", new[] { pl });
        string texto = File.ReadAllText(Arq("copia.json"));
        Assert.Contains("\"__tipo__\":\"datetime\"", texto);
        Assert.Contains("[101,\"Ana\"", texto);
        var de_novo = FonteJson.Ler(Arq("copia.json"));
        Assert.Equal(pl.V(2, 3), de_novo.V(2, 3));
        Assert.Equal(1.5, de_novo.V(3, 1));
    }

    [Fact]
    public void CaminhoEspelho_entende_caminho_do_windows() =>
        Assert.Equal(Path.Combine("pasta", "GradeHorariosAction (1).json"),
                     FonteJson.CaminhoEspelho("pasta", @"\\servidortec\Excel\GradeHorariosAction (1).xls"));
}

public class GravadorTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "eversoft-gr-" + Guid.NewGuid().ToString("N")[..8]);
    public GravadorTests() => Directory.CreateDirectory(_pasta);
    public void Dispose() { try { Directory.Delete(_pasta, true); } catch { } }

    private string CriarObs()
    {
        string p = Path.Combine(_pasta, "Observacoes.xlsx");
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");
        ws.Cell(1, 1).Value = "Nome";
        ws.Cell(2, 1).Value = "Ana Lima"; ws.Cell(2, 3).Value = "Cap 1";
        ws.Cell(3, 1).Value = "Bruno"; ws.Cell(3, 3).Value = "OK";
        wb.SaveAs(p);
        return p;
    }

    [Fact]
    public void GravarNoTopo_insere_na_linha_2_remove_antigo_faz_backup_e_atualiza_json()
    {
        string p = CriarObs();
        var g = new GravadorPlanilha(Path.Combine(_pasta, "backups"), Path.Combine(_pasta, "json"), fazerBackup: true);
        g.GravarNoTopo(p, "sheet1", new Dictionary<int, object?> { [1] = "ANA LIMA", [2] = "obs", [3] = "Atrasado", [5] = "" }, 5);

        var pl = LeitorPlanilha.Ler(p);
        Assert.Equal(3, pl.NLinhas);
        Assert.Equal("ANA LIMA", pl.V(2, 1));
        Assert.Equal("Atrasado", pl.V(2, 3));
        Assert.Equal("Bruno", pl.V(3, 1));
        Assert.Single(Directory.GetFiles(Path.Combine(_pasta, "backups")));
        Assert.Equal("Atrasado", FonteJson.Ler(Path.Combine(_pasta, "json", "Observacoes.json")).V(2, 3));
    }

    [Fact]
    public void RemoverLinhasPorNome_usa_a_primeira_aba_que_existir()
    {
        string p = CriarObs();
        var g = new GravadorPlanilha(Path.Combine(_pasta, "b"), Path.Combine(_pasta, "json"), fazerBackup: false);
        Assert.Equal(1, g.RemoverLinhasPorNome(p, new[] { "Faltas", "Sheet1" }, "ana lima"));
        Assert.Equal(2, LeitorPlanilha.Ler(p).NLinhas);
        Assert.Throws<AbaNaoEncontradaException>(() => g.RemoverLinhasPorNome(p, new[] { "Nada" }, "x"));
    }

    [Fact]
    public void Recusa_gravar_quando_o_excel_esta_aberto()
    {
        string p = CriarObs();
        File.WriteAllText(Path.Combine(_pasta, "~$Observacoes.xlsx"), "");
        var g = new GravadorPlanilha(_pasta, _pasta, false);
        Assert.Throws<ArquivoEmUsoException>(() => g.GravarNoTopo(p, "Sheet1", new Dictionary<int, object?> { [1] = "x" }, 5));
    }
}
