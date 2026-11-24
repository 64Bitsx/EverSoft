using System.Text.Json;
using EverSoft.Core.Automacao;
using EverSoft.Core.Configuracao;
using Microsoft.Playwright;
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

    [Theory]
    [InlineData("83991343421", "5583991343421")]
    [InlineData("(83) 9134-3421", "558391343421")]
    [InlineData("5583991343421", "5583991343421")]
    [InlineData("083991343421", "5583991343421")]
    public void Numero_vai_com_55(string entrada, string esperado) => Assert.Equal(esperado, Telefone.ParaWhatsApp(entrada));

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

    // ---------------------------------------------------------------- navegador de verdade + página que imita o WhatsApp Web
    private const string Chromium = "/opt/pw-browsers/chromium-1243/chrome-linux64/chrome";

    private const string PaginaFalsa = """
        <!doctype html><html><body>
        <div id="side" style="display:none"><div id="pane-side">conversas</div></div>
        <div id="main" style="display:none"><footer><div id="cx" contenteditable="true" style="min-height:20px;border:1px solid #000"></div></footer></div>
        <div id="previa" style="display:none"><span data-icon="send" id="btn">enviar</span></div>
        <script>
          const fone = new URLSearchParams(location.search).get('phone');
          const reg = x => { const e = JSON.parse(localStorage.getItem('env') || '[]'); e.push(x); localStorage.setItem('env', JSON.stringify(e)); };
          let pendente = null;
          setTimeout(() => {
            document.getElementById('side').style.display = '';
            if (!fone) return;
            if (fone === '5511900000000') {
              const d = document.createElement('div'); d.setAttribute('role', 'dialog');
              d.textContent = 'O número de telefone compartilhado por URL é inválido.'; document.body.appendChild(d); return;
            }
            document.getElementById('main').style.display = '';
          }, 300);
          const cx = document.getElementById('cx');
          cx.addEventListener('keydown', e => {
            if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); reg({ fone, tipo: 'texto', v: cx.innerText }); cx.innerText = ''; }
          });
          cx.addEventListener('paste', e => {
            const f = e.clipboardData && e.clipboardData.files[0];
            if (!f) return; e.preventDefault(); pendente = f.name + ':' + f.type + ':' + f.size;
            document.getElementById('main').style.display = 'none'; document.getElementById('previa').style.display = '';
          });
          document.getElementById('btn').onclick = () => {
            reg({ fone, tipo: 'arquivo', v: pendente });
            document.getElementById('previa').style.display = 'none'; document.getElementById('main').style.display = '';
          };
        </script></body></html>
        """;

    [Fact]
    public async Task WhatsAppWeb_envia_audio_e_texto_na_pagina_de_simulacao()
    {
        if (!File.Exists(Chromium)) return;   // só roda onde há o navegador de teste
        string pasta = CriarAudios("lembrete.ogg");
        IBrowserContext? ctx = null;
        await using var wa = new WhatsAppWeb
        {
            ExecutavelNavegador = Chromium, Headless = true, PastaPerfil = Path.Combine(_pasta, "perfil"),
            EsperaAnexo = 0.2, TempoParaAbrirConversa = TimeSpan.FromSeconds(15), TempoParaLogin = TimeSpan.FromSeconds(15),
            AoCriarContexto = async c =>
            {
                ctx = c;
                await c.RouteAsync("https://web.whatsapp.com/**", r => r.FulfillAsync(new() { ContentType = "text/html", Body = PaginaFalsa }));
            },
        };
        var r = await EnvioLembretes.EnviarAsync(wa,
            Agenda(("Ana Lima", "83991111111"), ("Sem Zap", "11900000000"), ("Bruno Reis", "(83) 99222-2222")),
            "audio", pasta, SemEspera, _ => { });

        Assert.Equal(2, r.Enviados);
        Assert.Single(r.Falhas);
        string json = await ctx!.Pages[0].EvaluateAsync<string>("() => localStorage.getItem('env')");
        var env = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(json)!;
        Assert.Equal(4, env.Count);
        Assert.Equal(("5583991111111", "arquivo", "lembrete.ogg:audio/ogg:7"), (env[0]["fone"], env[0]["tipo"], env[0]["v"]));
        Assert.Equal(("5583991111111", "texto", "Aluno: Ana Lima"), (env[1]["fone"], env[1]["tipo"], env[1]["v"]));
        Assert.Equal(("5583992222222", "texto", "Aluno: Bruno Reis"), (env[3]["fone"], env[3]["tipo"], env[3]["v"]));
    }
}
