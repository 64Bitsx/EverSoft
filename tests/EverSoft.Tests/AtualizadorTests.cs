using EverSoft.Core.Servicos;
using Xunit;

namespace EverSoft.Tests;

public class AtualizadorTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("eversoft-atu").FullName;
    public void Dispose() { try { Directory.Delete(_pasta, true); } catch { } }

    private string Exe(string sub, string conteudo, DateTime quando)
    {
        string p = Path.Combine(_pasta, sub, "EverSoft.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, conteudo);
        File.SetLastWriteTimeUtc(p, quando);
        return p;
    }

    [Fact]
    public void Versao_da_rede_mais_nova_e_detectada_pela_data()
    {
        var hoje = DateTime.UtcNow.AddHours(-1);
        string local = Exe("c", "v1", hoje);
        string rede = Exe("rede", "v2", hoje.AddMinutes(5));
        Assert.True(Atualizador.TemVersaoNova(local, rede));
        Assert.False(Atualizador.TemVersaoNova(rede, local));
        File.SetLastWriteTimeUtc(rede, hoje.AddSeconds(1));      // diferença de arredondamento da rede
        Assert.False(Atualizador.TemVersaoNova(local, rede));
        Assert.False(Atualizador.TemVersaoNova(local, Path.Combine(_pasta, "nao-existe.exe")));
    }

    [Fact]
    public void Instalar_troca_o_exe_mesmo_existindo_e_mantem_a_data()
    {
        var t = DateTime.UtcNow.AddHours(-2);
        string local = Exe("c", "v1", t);
        string rede = Exe("rede", "v2-nova", t.AddHours(1));
        Atualizador.Instalar(rede, local);
        Assert.Equal("v2-nova", File.ReadAllText(local));
        Assert.Equal(File.GetLastWriteTimeUtc(rede), File.GetLastWriteTimeUtc(local));
        Assert.False(Atualizador.TemVersaoNova(local, rede));
        Assert.True(File.Exists(local + ".old"));                // o antigo fica de lado até a próxima abertura
        Atualizador.LimparSobras(Path.GetDirectoryName(local)!);
        Assert.False(File.Exists(local + ".old"));
    }

    [Fact]
    public void Instalar_na_primeira_vez_cria_a_pasta()
    {
        string rede = Exe("rede", "v1", DateTime.UtcNow.AddHours(-1));
        string destino = Path.Combine(_pasta, "C_EverSoft", "EverSoft.exe");
        Atualizador.Instalar(rede, destino);
        Assert.Equal("v1", File.ReadAllText(destino));
    }

    [Fact]
    public void Novidades_pega_so_a_secao_da_versao()
    {
        string texto = "## V2.2\n- coisa nova\n- outra\n\n## V2.1\n- antiga\n";
        Assert.Equal("- coisa nova\n- outra", Atualizador.Novidades(texto, "V2.2"));
        Assert.Equal("- antiga", Atualizador.Novidades(texto, "V2.1"));
        Assert.Equal("", Atualizador.Novidades(texto, "V9.9"));
    }
}
