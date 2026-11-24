using ClosedXML.Excel;
using EverSoft.Core.Planilhas;
using EverSoft.Core.Servicos;

namespace EverSoft.Tests;

public class MonitorTests : IDisposable
{
    private readonly Cenario _c = new();
    public void Dispose() => _c.Dispose();

    private static void TornarMaisNovo(string arquivo) =>
        File.SetLastWriteTimeUtc(arquivo, DateTime.UtcNow.AddMinutes(5));

    [Fact]
    public void Planilha_editada_por_outra_pessoa_e_convertida_e_relida_sozinha()
    {
        var n = _c.NovoNucleo(new DateTime(2026, 11, 23, 13, 30, 0));
        Assert.Equal("OK", n.Linha("101").Sit);
        using var monitor = new MonitorPlanilhas(n.Cfg, n.Hub);
        var avisos = new List<string>();
        monitor.DadosAtualizados += chaves => avisos.AddRange(chaves);

        Assert.Empty(monitor.VerificarAgora());              // nada mudou ainda

        // Um colega muda a situação da Ana direto no Excel.
        string obs = _c.Cfg.P("observacoes");
        using (var wb = new XLWorkbook(obs)) { wb.Worksheet(1).Cell(2, 3).Value = "Atrasado"; wb.Save(); }
        TornarMaisNovo(obs);

        var relidas = monitor.VerificarAgora();
        Assert.Contains("obs", relidas);
        Assert.Contains("obs", avisos);
        Assert.Equal("Atrasado", n.Linha("101").Sit);
        Assert.Equal("Atrasado", FonteJson.Ler(FonteJson.CaminhoEspelho(_c.Cfg.P("tec_json_dir"), obs)).V(2, 3));
    }

    [Fact]
    public void Planilha_sendo_salva_no_momento_nao_estraga_o_json()
    {
        var n = _c.NovoNucleo(new DateTime(2026, 11, 23, 13, 30, 0));
        using var monitor = new MonitorPlanilhas(n.Cfg, n.Hub);
        string obs = _c.Cfg.P("observacoes");
        File.WriteAllBytes(obs, new byte[] { (byte)'P', (byte)'K', 3, 4, 1, 2, 3 });   // arquivo pela metade
        TornarMaisNovo(obs);

        monitor.VerificarAgora();
        Assert.Equal("OK", n.Linha("101").Sit);              // continua com os dados anteriores
    }

    [Fact]
    public void Sincronia_automatica_desligada_so_rele_jsons()
    {
        var n = _c.NovoNucleo(new DateTime(2026, 11, 23, 13, 30, 0));
        n.Cfg.SincroniaAutomatica = false;
        using var monitor = new MonitorPlanilhas(n.Cfg, n.Hub);
        string obs = _c.Cfg.P("observacoes");
        using (var wb = new XLWorkbook(obs)) { wb.Worksheet(1).Cell(2, 3).Value = "Atrasado"; wb.Save(); }
        TornarMaisNovo(obs);
        monitor.VerificarAgora();
        Assert.Equal("OK", n.Linha("101").Sit);

        Sincronizacao.GerarJsonsSincronizacao(n.Cfg);          // botão "Executar Sincronia"
        monitor.VerificarAgora();
        Assert.Equal("Atrasado", n.Linha("101").Sit);
    }

    [Fact]
    public void Base_Prepara_convertida_uma_vez_continua_sendo_lida_sem_o_excel()
    {
        // Coloca uma aluna na Base Prepara, converte uma vez e apaga o Excel.
        _c.CriarXlsx("Base Prepara.xlsx", "Sheet", new object?[][]
        {
            new object?[] { "a", "b", "Nome", null, null, null, null, "Início" },
            new object?[] { null, null, "Ana Lima", null, null, null, null, "02/2026" },
        });
        Sincronizacao.GerarJsonsSincronizacao(_c.Cfg);
        File.Delete(_c.Cfg.P("base_prepara"));

        var n = _c.NovoNucleo(new DateTime(2026, 11, 23, 13, 30, 0));
        using var monitor = new MonitorPlanilhas(n.Cfg, n.Hub);
        monitor.VerificarAgora();
        Assert.Equal("02/2026", n.Linha("101").Inicio);
        Assert.True(Sincronizacao.GerarJsonsSincronizacao(_c.Cfg) > 0);   // as outras seguem sincronizando
    }
}
