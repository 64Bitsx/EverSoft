using EverSoft.App.Views;
using Xunit;

namespace EverSoft.Tests;

public class TarefaTests
{
    [Theory]
    [InlineData("NÃO ENCONTRADO: X:\\a.xls", true)]
    [InlineData("  Erro ao ler: arquivo corrompido", true)]
    [InlineData("  Aba Turmas não existe.", true)]
    [InlineData("ERRO: sem rede", true)]
    [InlineData("Lendo: a.xls", false)]
    [InlineData("JSON GRAVADO: a.json (2 aba(s))", false)]
    [InlineData("Pronto.", false)]
    public void ReconheceLinhasDeProblema(string linha, bool esperado) =>
        Assert.Equal(esperado, MainWindow.EhProblema(linha));
}
