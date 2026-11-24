using EverSoft.Core.Automacao;
using EverSoft.Core.Configuracao;
using Xunit;

namespace EverSoft.Tests;

public class WhatsAppTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("eversoft-wa").FullName;
    public void Dispose() { try { Directory.Delete(_pasta, true); } catch { } }

    private static readonly WhatsAppConfig SemEspera = new() { EsperaEntreAlunos = 0 };

    private string CriarAudios(params string[] nomes)
    {
        string p = Path.Combine(_pasta, "Semana", "1300");
        Directory.CreateDirectory(p);
        foreach (var n in nomes) File.WriteAllBytes(Path.Combine(p, n), new byte[] { 79, 103, 103, 83, 1, 2, 3 });
        return p;
    }

    private static List<ItemAgenda> Agenda(params (string Nome, string Numero)[] c) =>
        c.Select(x => new ItemAgenda { Nome = x.Nome, Numero = x.Numero, Mensagem = $"Oi {x.Nome}, hoje tem aula!" }).ToList();

    /// <summary>WhatsApp de mentira que só anota o que foi pedido.</summary>
    private sealed class WhatsFalso : IWhatsApp
    {
        public List<string> Feito { get; } = new();
        public HashSet<string> SemWhats { get; } = new();
        public Action? AoEnviar { get; set; }
        private string _atual = "";
        public Task AbrirAsync(Action<string> log, CancellationToken ct) { Feito.Add("abrir"); return Task.CompletedTask; }
        public Task<bool> AbrirConversaAsync(string numero, CancellationToken ct) { _atual = numero; return Task.FromResult(!SemWhats.Contains(numero)); }
        public Task AbrirGrupoAsync(string nome, CancellationToken ct) { _atual = nome; return Task.CompletedTask; }
        public Task EnviarTextoAsync(string texto, CancellationToken ct) { Feito.Add($"{_atual}|texto|{texto}"); AoEnviar?.Invoke(); return Task.CompletedTask; }
        public Task EnviarArquivoAsync(string caminho, CancellationToken ct) { Feito.Add($"{_atual}|arquivo|{Path.GetFileName(caminho)}"); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Modo_audio_manda_audio_e_depois_o_nome_do_aluno()
    {
        string pasta = CriarAudios("a.ogg", "b.ogg");
        var wa = new WhatsFalso();
        wa.SemWhats.Add("83900000000");
        var r = await EnvioLembretes.EnviarAsync(wa, Agenda(("Ana Lima", "83991111111"), ("Sem Zap", "83900000000"), ("Bruno Reis", "83992222222")),
            "audio", pasta, SemEspera, _ => { }, sorteio: new Random(1));
        Assert.Equal(2, r.Enviados);
        Assert.Equal(3, r.Total);
        Assert.Single(r.Falhas);
        Assert.Contains("Sem Zap", r.Falhas[0]);
        Assert.Matches(@"^83991111111\|arquivo\|[ab]\.ogg$", wa.Feito[1]);
        Assert.Equal("83991111111|texto|Aluno: Ana Lima", wa.Feito[2]);
        Assert.Equal("83992222222|texto|Aluno: Bruno Reis", wa.Feito[4]);
    }

    [Fact]
    public async Task Modo_texto_manda_a_mensagem_da_agenda()
    {
        var wa = new WhatsFalso();
        var r = await EnvioLembretes.EnviarAsync(wa, Agenda(("Ana Lima", "83991111111")), "texto", null, SemEspera, _ => { });
        Assert.Equal(1, r.Enviados);
        Assert.Equal("83991111111|texto|Oi Ana Lima, hoje tem aula!", wa.Feito[1]);
    }

    [Fact]
    public async Task Sem_audio_na_pasta_nao_comeca()
    {
        string vazia = Directory.CreateDirectory(Path.Combine(_pasta, "vazia")).FullName;
        var wa = new WhatsFalso();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EnvioLembretes.EnviarAsync(wa, Agenda(("Ana", "83991111111")), "audio", vazia, SemEspera, _ => { }));
        Assert.Empty(wa.Feito);
    }

    [Fact]
    public async Task Parar_interrompe_o_envio()
    {
        using var cts = new CancellationTokenSource();
        var wa = new WhatsFalso { AoEnviar = () => cts.Cancel() };
        var r = await EnvioLembretes.EnviarAsync(wa, Agenda(("Ana", "83991111111"), ("Bia", "83992222222")), "texto", null, SemEspera, _ => { }, ct: cts.Token);
        Assert.True(r.Cancelado);
        Assert.DoesNotContain(wa.Feito, f => f.Contains("Bia"));
    }

    // ---------------------------------------------------------------- nircmd + teclado (com um Windows de mentira)
    private sealed class WindowsFalso : ISistemaTeclado
    {
        public List<string> Passos { get; } = new();
        public string Frente { get; set; } = "Outra coisa";
        public bool AchaJanela { get; set; } = true;
        /// <summary>Depois de N passos de teclado, outra janela "rouba" a frente.</summary>
        public int? PerdeFocoApos { get; set; }
        public bool Nircmd(params string[] args)
        {
            Passos.Add("nircmd " + string.Join(' ', args));
            if (args[1] == "activate" && AchaJanela && PerdeFocoApos is null) Frente = "(3) WhatsApp - Google Chrome";
            return AchaJanela;
        }
        private void Tecla(string t)
        {
            Passos.Add(t);
            if (PerdeFocoApos is int n && Passos.Count(p => !p.StartsWith("nircmd")) >= n) Frente = "Bloco de Notas";
        }
        public void Atalho(params ushort[] teclas) => Tecla("tecla " + string.Join('+', teclas.Select(Nome)));
        public void Digitar(string texto) => Tecla("digita " + texto);
        public void CopiarTexto(string texto) => Passos.Add("copia texto " + texto);
        public void CopiarArquivo(string caminho) => Passos.Add("copia arquivo " + Path.GetFileName(caminho));
        public string TituloDaJanelaDaFrente() => Frente;
        private static string Nome(ushort k) => k switch { 0x11 => "Ctrl", 0x12 => "Alt", 0x0D => "Enter", 0x1B => "Esc", _ => ((char)k).ToString() };
    }

    private static WhatsAppConfig Rapido() => new() { ContagemRegressiva = 0, EsperaEntreAlunos = 0 };

    [Fact]
    public async Task Teclado_ativa_maximiza_e_usa_Ctrl_Alt_N_como_no_Python()
    {
        string pasta = CriarAudios("a.ogg");
        var so = new WindowsFalso();
        var wa = new WhatsAppTeclado(Rapido(), so) { Ritmo = 0 };
        var r = await EnvioLembretes.EnviarAsync(wa, Agenda(("Ana Lima", "83991111111")), "audio", pasta, Rapido(), _ => { });
        Assert.Equal(1, r.Enviados);
        Assert.Equal(new[]
        {
            "nircmd win activate ititle WhatsApp", "nircmd win max ititle WhatsApp",
            "tecla Esc", "tecla Ctrl+Alt+N", "digita 83991111111", "tecla Enter",
            "copia arquivo a.ogg", "tecla Ctrl+V", "tecla Enter",
            "copia texto Aluno: Ana Lima", "tecla Ctrl+V", "tecla Enter",
        }, so.Passos);
    }

    [Fact]
    public async Task Justificados_vao_para_o_grupo_pelo_Ctrl_Alt_N()
    {
        var so = new WindowsFalso();
        var wa = new WhatsAppTeclado(Rapido(), so) { Ritmo = 0 };
        await wa.AbrirAsync(_ => { }, default);
        await wa.AbrirGrupoAsync("TEC GUARABIRA", default);
        await wa.EnviarTextoAsync("FALTOSOS 13:00 ÀS 15:00\nAna", default);
        Assert.Contains("digita TEC GUARABIRA", so.Passos);
        Assert.Equal("copia texto FALTOSOS 13:00 ÀS 15:00\nAna", so.Passos[^3]);
        Assert.Equal("tecla Enter", so.Passos[^1]);
    }

    [Fact]
    public async Task Sem_a_janela_do_WhatsApp_nada_e_digitado()
    {
        var so = new WindowsFalso { AchaJanela = false };
        var wa = new WhatsAppTeclado(Rapido(), so) { Ritmo = 0 };
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EnvioLembretes.EnviarAsync(wa, Agenda(("Ana", "83991111111")), "texto", null, Rapido(), _ => { }));
        Assert.Contains("WhatsApp", e.Message);
        Assert.DoesNotContain(so.Passos, p => p.StartsWith("tecla") || p.StartsWith("digita"));
    }

    [Fact]
    public async Task Se_outra_janela_vier_para_frente_o_envio_para_em_vez_de_digitar_nela()
    {
        var so = new WindowsFalso { Frente = "(1) WhatsApp - Microsoft Edge", PerdeFocoApos = 2 };
        var wa = new WhatsAppTeclado(Rapido(), so) { Ritmo = 0 };
        var r = await EnvioLembretes.EnviarAsync(wa, Agenda(("Ana", "83991111111"), ("Bia", "83992222222")), "texto", null, Rapido(), _ => { });
        Assert.Equal(0, r.Enviados);
        Assert.True(r.Cancelado);
        Assert.Single(r.Falhas);                                   // parou no primeiro, não tentou a Bia
        Assert.Contains("saiu da frente", r.Falhas[0]);
        Assert.DoesNotContain(so.Passos, p => p.StartsWith("digita"));
    }
}
