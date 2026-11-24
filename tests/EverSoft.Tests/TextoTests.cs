using EverSoft.Core.Util;

namespace EverSoft.Tests;

// Os valores esperados foram conferidos rodando as mesmas funções no EverSoft.py.
public class TextoTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData(123.0, "123")]
    [InlineData("  Ana Maria ", "ana maria")]
    [InlineData("JOSÉ", "josé")]
    public void Norm_compara_sem_maiusculas_e_espacos(object? entrada, string esperado) =>
        Assert.Equal(esperado, Texto.Norm(entrada));

    [Fact]
    public void Txt_formata_numeros_datas_e_booleanos()
    {
        Assert.Equal("1,5", Texto.Txt(1.5));
        Assert.Equal("85,6", Texto.Txt(85.6));
        Assert.Equal("83912345678", Texto.Txt(83912345678.0));
        Assert.Equal("05/01/2024", Texto.Txt(new DateTime(2024, 1, 5)));
        Assert.Equal("VERDADEIRO", Texto.Txt(true));
        Assert.Equal("", Texto.Txt(null));
    }

    [Fact]
    public void Proper_igual_ao_excel() =>
        Assert.Equal("João D'Arc Da Silva", Texto.Proper("JOÃO D'ARC da silva"));

    [Fact]
    public void ParseData_entende_texto_serial_e_datetime()
    {
        Assert.Equal(new DateOnly(2024, 3, 15), Texto.ParseData("15/03/2024"));
        Assert.Equal(new DateOnly(2024, 3, 15), Texto.ParseData("2024-03-15 10:00:00"));
        Assert.Equal(new DateOnly(2024, 3, 15), Texto.ParseData(45366.0));
        Assert.Equal(new DateOnly(2024, 3, 15), Texto.ParseData(new DateTime(2024, 3, 15, 8, 0, 0)));
        Assert.Null(Texto.ParseData("ontem"));
        Assert.Null(Texto.ParseData(12.0));
    }

    [Fact]
    public void ColIdx_converte_letras() {
        Assert.Equal(1, Texto.ColIdx("A"));
        Assert.Equal(27, Texto.ColIdx("AA"));
        Assert.Equal(16, Texto.ColIdx("p"));
    }

    [Fact]
    public void TratarContatos_limpa_poe_o_nove_e_remove_repetidos()
    {
        var r = Texto.TratarContatos(new[]
        {
            ("A", "(83) 9876-5432"), ("B", "83987654321"), ("C", "12345"), ("D", "8398765432"), ("E", "83912345678"),
        });
        Assert.Equal(new[] { ("A", "83998765432"), ("B", "83987654321"), ("E", "83912345678") }, r);
    }

    [Fact]
    public void NormalizarHorario_tira_espacos() =>
        Assert.Equal("13h00-15h00", Texto.NormalizarHorario("13H00 - 15H00"));
}

public class CaminhoNaRedeTests
{
    [Theory]
    [InlineData(@"C:\Users\Ewerton\Documents\TEC_CURSOS\Pedagógico\Documentos\Excel\Justificativas", @"\\servidortec\TEC_CURSOS\Pedagógico\Documentos\Excel\Justificativas")]
    [InlineData(@"\\servidortec\TEC_CURSOS\Pedagógico\Documentos\Excel\Justificativas", @"\\servidortec\TEC_CURSOS\Pedagógico\Documentos\Excel\Justificativas")]
    [InlineData(@"D:\Outra\Pasta", @"D:\Outra\Pasta")]
    public void Pasta_das_justificativas_do_Python_vai_para_a_rede(string antes, string depois) =>
        Assert.Equal(depois, EverSoft.Core.Configuracao.AppConfig.CaminhoNaRede(antes));
}
