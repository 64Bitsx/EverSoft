using EverSoft.Core.Configuracao;
using EverSoft.Core.Dados;
using EverSoft.Core.Dominio;
using EverSoft.Core.Planilhas;

namespace EverSoft.Tests;

public class FaixasTests
{
    // 23/11/2026 é segunda-feira; 28/11 é sábado; 29/11 é domingo.
    [Theory]
    [InlineData("2026-11-23 13:30", "15:00h", "Semana", "1500", "13:00 ÀS 15:00", "SEGUNDA FEIRA")]
    [InlineData("2026-11-28 08:00", "07:40h", "Sabado", "0740", "07:40 ÀS 09:30", "SÁBADO")]
    [InlineData("2026-11-29 08:00", "07:45h", null, null, "07:45 ÀS 09:40", "DOMINGO")]
    [InlineData("2026-11-23 22:00", "19:00h", "Semana", "1900", "", "SEGUNDA FEIRA")]
    [InlineData("2026-11-23 13:05", "13:00h", "Semana", "1300", "13:00 ÀS 15:00", "SEGUNDA FEIRA")]
    public void Faixas_iguais_ao_python(string quando, string rotulo, string? dia, string? audio, string just, string diaTexto)
    {
        var a = DateTime.Parse(quando);
        Assert.Equal(rotulo, Faixas.RotuloHoraLembrete(a));
        var fa = Faixas.FaixaAudio(a);
        Assert.Equal(dia, fa?.Dia);
        Assert.Equal(audio, fa?.Faixa);
        Assert.Equal(just, Faixas.FaixaHorarioJustifica(a));
        Assert.Equal(diaTexto, Faixas.DiaSemanaTexto(a));
    }

    [Fact]
    public void CodigoFaixa_converte_e_rejeita_texto_invalido()
    {
        Assert.Equal("07h45-09h40", Faixas.CodigoFaixa("07:45 ÀS 09:40"));
        Assert.Equal("", Faixas.CodigoFaixa("qualquer coisa"));
    }
}

public class MensagensTests
{
    [Theory]
    [InlineData(0.856, "85.6")]
    [InlineData(0.8, "80")]
    [InlineData(0.123456789, "12.3456789")]
    public void PctTexto_igual_ao_python(double frac, string esperado) => Assert.Equal(esperado, Mensagens.PctTexto(frac));

    [Fact]
    public void MensagemBoletim_igual_ao_python()
    {
        const string esperado = "Olá, `Ana!` Ou responsável? Tudo bem? Fique de olho no seu Nível de Aprendizado de `Outubro!`\n\n" +
            "Você obteve no mês de `Outubro` o Aprendizado de `85%!`\n\nFoi um Nível de Aprendizado `Excelente!` Mas sempre " +
            "podemos melhorar, vamos nos esforçar nesse mês de `Novembro` para conseguir um aprendizado ainda melhor!";
        Assert.Equal(esperado, Mensagens.MensagemBoletim("Ana", "Outubro", "Novembro", 0.85));
    }

    [Theory]
    [InlineData(0.5, "legal")]
    [InlineData(0.89, "Excelente")]
    [InlineData(0.95, "incrível")]
    [InlineData(1.0, "Perfeito")]
    public void MensagemNivel_por_faixa(double frac, string trecho) =>
        Assert.Contains(trecho, Mensagens.MensagemNivel(frac, "Dezembro"));

    [Fact]
    public void MontarFaltosos_separa_com_e_sem_retorno()
    {
        var hub = new DataHub(new AppConfig());
        hub.Definir("matativas", new Planilha(new()
        {
            new() { null, "Matrícula", "Nome" },
            new() { null, 101.0, "ANA LIMA" },
            new() { null, 102.0, "BRUNO REIS" },
        }));
        var lista = new[]
        {
            new RegistroJustifica { Nome = "Ana Lima", Justificativa = "doente", Horario = "13h00 - 15h00" },
            new RegistroJustifica { Nome = "Bruno Reis", Horario = "13h00 - 15h00" },
            new RegistroJustifica { Nome = "Outro", Horario = "15h00 - 17h00" },
        };
        var (texto, com, sem) = Mensagens.MontarFaltosos(lista, hub, "SEGUNDA FEIRA", "13:00 ÀS 15:00", "13h00-15h00");
        Assert.Equal(1, com);
        Assert.Equal(1, sem);
        Assert.Equal("FALTOSOS SEGUNDA FEIRA 13:00 ÀS 15:00\n\n✅ COM RETORNO:\n- 101 - ANA LIMA: *DOENTE*\n" +
                     "\n \n❌ SEM RETORNO:\n- 102 - BRUNO REIS: *MSG ENVIADA/SEM RETORNO*", texto);
    }

    [Fact]
    public void MontarFaltosos_sem_ninguem()
    {
        var (texto, _, _) = Mensagens.MontarFaltosos(Array.Empty<RegistroJustifica>(), new DataHub(new AppConfig()), "SÁBADO", "07:40 ÀS 09:30", "07h40-09h30");
        Assert.Equal("FALTOSOS SÁBADO 07:40 ÀS 09:30\n*Todos os alunos presentes!", texto);
    }
}
