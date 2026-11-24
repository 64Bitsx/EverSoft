using EverSoft.Core.Util;
using Microsoft.Playwright;

namespace EverSoft.Core.Automacao;

/// <summary>
/// Controla o WhatsApp Web com o Playwright, usando o Google Chrome (ou o Edge) já instalado no computador.
/// O navegador abre com um perfil só do EverSoft (%APPDATA%\EverSoft\whatsapp-perfil): na primeira vez
/// é preciso ler o QR code com o celular; depois o login fica guardado.
/// Diferente da versão em Python, não mexe no mouse nem no teclado do Windows: dá para usar o
/// computador enquanto envia (só não feche a janela do WhatsApp).
/// </summary>
public sealed class WhatsAppWeb : IWhatsApp
{
    public static string PastaPerfilPadrao =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EverSoft", "whatsapp-perfil");

    /// <summary>Endereço do WhatsApp Web (os testes trocam por uma página de simulação).</summary>
    public string Url { get; init; } = "https://web.whatsapp.com/";
    public string PastaPerfil { get; init; } = PastaPerfilPadrao;
    /// <summary>Caminho de um navegador específico; vazio = Chrome, depois Edge.</summary>
    public string? ExecutavelNavegador { get; init; }
    /// <summary>Sem janela (só para os testes automáticos).</summary>
    public bool Headless { get; init; }
    public TimeSpan TempoParaLogin { get; init; } = TimeSpan.FromMinutes(3);
    public TimeSpan TempoParaAbrirConversa { get; init; } = TimeSpan.FromSeconds(60);
    public double EsperaAnexo { get; init; } = 3.0;
    /// <summary>Usado pelos testes para trocar o site pela simulação.</summary>
    public Func<IBrowserContext, Task>? AoCriarContexto { get; init; }

    private IPlaywright? _pw;
    private IBrowserContext? _ctx;
    private IPage? _page;

    // Seletores do WhatsApp Web (em português e inglês). Se o WhatsApp mudar o site, é aqui que se ajusta.
    private const string SelLogado = "#pane-side, #side";
    private const string SelQr = "canvas[aria-label], div[data-ref]";
    private const string SelCaixaTexto = "#main footer div[contenteditable='true']";
    private const string SelNumeroInvalido = "div[role='dialog'], div[data-animate-modal-popup='true']";
    private const string SelBotaoEnviar =
        "span[data-icon='send'], span[data-icon='wds-ic-send-filled'], div[role='button'][aria-label='Enviar'], " +
        "div[role='button'][aria-label='Send'], button[aria-label='Enviar'], button[aria-label='Send']";
    private const string SelBotaoAnexar =
        "button[title='Anexar'], div[title='Anexar'], [aria-label='Anexar'], [aria-label='Attach'], " +
        "span[data-icon='plus-rounded'], span[data-icon='plus'], span[data-icon='clip']";
    private const string SelBusca = "#side div[contenteditable='true'], #side input[type='text']";

    public bool Aberto => _page is { IsClosed: false };

    public async Task AbrirAsync(Action<string> log, CancellationToken ct)
    {
        if (!Aberto)
        {
            await FecharAsync();
            log("Abrindo o WhatsApp Web...");
            // No .exe de arquivo único, a pasta .playwright fica ao lado do EverSoft.exe.
            if (Environment.GetEnvironmentVariable("PLAYWRIGHT_DRIVER_SEARCH_PATH") is null
                && Directory.Exists(Path.Combine(AppContext.BaseDirectory, ".playwright")))
                Environment.SetEnvironmentVariable("PLAYWRIGHT_DRIVER_SEARCH_PATH", AppContext.BaseDirectory);
            _pw ??= await Playwright.CreateAsync();
            _ctx = await AbrirNavegadorAsync();
            _ctx.Close += (_, _) => { _page = null; _ctx = null; };
            if (AoCriarContexto is not null) await AoCriarContexto(_ctx);
            _page = _ctx.Pages.FirstOrDefault() ?? await _ctx.NewPageAsync();
        }
        var page = _page!;
        if (!page.Url.StartsWith(Url, StringComparison.OrdinalIgnoreCase))
            await page.GotoAsync(Url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 90_000 });
        await page.BringToFrontAsync();
        await EsperarLoginAsync(page, log, ct);
    }

    private async Task<IBrowserContext> AbrirNavegadorAsync()
    {
        Directory.CreateDirectory(PastaPerfil);
        BrowserTypeLaunchPersistentContextOptions Opcoes(string? canal) => new()
        {
            Headless = Headless,
            Channel = canal,
            ExecutablePath = string.IsNullOrEmpty(ExecutavelNavegador) ? null : ExecutavelNavegador,
            ViewportSize = ViewportSize.NoViewport,
            Args = new[] { "--start-maximized" },
            Locale = "pt-BR",
        };
        if (!string.IsNullOrEmpty(ExecutavelNavegador))
            return await _pw!.Chromium.LaunchPersistentContextAsync(PastaPerfil, Opcoes(null));

        Exception? ultimo = null;
        foreach (var canal in new[] { "chrome", "msedge" })
        {
            try { return await _pw!.Chromium.LaunchPersistentContextAsync(PastaPerfil, Opcoes(canal)); }
            catch (PlaywrightException e) { ultimo = e; Log.Erro($"WhatsApp: não abriu o canal {canal}: {e.Message}"); }
        }
        throw new InvalidOperationException(
            "Não foi possível abrir o Google Chrome nem o Microsoft Edge para o WhatsApp Web. " +
            "Se uma janela do WhatsApp do EverSoft já estiver aberta, feche-a e tente de novo.", ultimo);
    }

    private async Task EsperarLoginAsync(IPage page, Action<string> log, CancellationToken ct)
    {
        var limite = DateTime.UtcNow + TempoParaLogin;
        bool avisouQr = false;
        while (DateTime.UtcNow < limite)
        {
            ct.ThrowIfCancellationRequested();
            if (await page.Locator(SelLogado).First.IsVisibleAsync()) return;
            if (!avisouQr && await page.Locator(SelQr).First.IsVisibleAsync())
            {
                avisouQr = true;
                log("Leia o QR code na janela do WhatsApp com o celular (só é preciso na primeira vez).");
            }
            await Task.Delay(500, ct);
        }
        throw new TimeoutException("O WhatsApp Web não carregou (ou o QR code não foi lido) a tempo.");
    }

    public async Task<bool> AbrirConversaAsync(string numero, CancellationToken ct)
    {
        var page = Pagina();
        string n = Telefone.ParaWhatsApp(numero);
        await page.GotoAsync($"{Url.TrimEnd('/')}/send?phone={n}", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 90_000 });
        var caixa = page.Locator(SelCaixaTexto).First;
        var invalido = page.Locator(SelNumeroInvalido).Filter(new() { HasTextRegex = new System.Text.RegularExpressions.Regex("inválido|invalid|não está no WhatsApp|isn't on WhatsApp", System.Text.RegularExpressions.RegexOptions.IgnoreCase) }).First;
        var limite = DateTime.UtcNow + TempoParaAbrirConversa;
        while (DateTime.UtcNow < limite)
        {
            ct.ThrowIfCancellationRequested();
            if (await caixa.IsVisibleAsync()) return true;
            if (await invalido.IsVisibleAsync())
            {
                await page.Keyboard.PressAsync("Escape");
                return false;
            }
            await Task.Delay(300, ct);
        }
        throw new TimeoutException("A conversa não abriu a tempo.");
    }

    public async Task AbrirGrupoAsync(string nome, CancellationToken ct)
    {
        var page = Pagina();
        var busca = page.Locator(SelBusca).First;
        await busca.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        await page.Keyboard.InsertTextAsync(nome);
        await Task.Delay(2000, ct);
        await page.Keyboard.PressAsync("Enter");
        await page.Locator(SelCaixaTexto).First.WaitForAsync(new() { Timeout = 30_000 });
    }

    public async Task EnviarTextoAsync(string texto, CancellationToken ct)
    {
        var page = Pagina();
        var caixa = page.Locator(SelCaixaTexto).First;
        await caixa.ClickAsync();
        var linhas = texto.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < linhas.Length; i++)
        {
            if (i > 0) await page.Keyboard.PressAsync("Shift+Enter");
            if (linhas[i] != "") await page.Keyboard.InsertTextAsync(linhas[i]);
        }
        await Task.Delay(300, ct);
        await page.Keyboard.PressAsync("Enter");
        // espera a caixa esvaziar (mensagem saiu)
        var limite = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < limite && (await caixa.InnerTextAsync()).Trim() != "")
            await Task.Delay(200, ct);
        await Task.Delay(800, ct);
    }

    public async Task EnviarArquivoAsync(string caminho, CancellationToken ct)
    {
        if (!File.Exists(caminho)) throw new FileNotFoundException("Arquivo não encontrado: " + caminho);
        var page = Pagina();
        var enviar = page.Locator(SelBotaoEnviar).First;

        // 1º jeito: "colar" o arquivo na caixa de texto, como o Ctrl+V da versão antiga.
        await ColarArquivoAsync(page, caminho);
        if (!await EsperarVisivelAsync(enviar, TimeSpan.FromSeconds(Math.Max(5, EsperaAnexo * 2)), ct))
        {
            // 2º jeito: pelo botão de anexar (clipe / +).
            await page.Locator(SelBotaoAnexar).First.ClickAsync(new() { Timeout = 10_000 });
            await Task.Delay(800, ct);
            var entradas = page.Locator("input[type='file']");
            int qtd = await entradas.CountAsync();
            if (qtd == 0) throw new InvalidOperationException("Não achei onde anexar o arquivo no WhatsApp.");
            ILocator alvo = entradas.Last;
            for (int i = 0; i < qtd; i++)
                if ((await entradas.Nth(i).GetAttributeAsync("accept") ?? "*") == "*") { alvo = entradas.Nth(i); break; }
            await alvo.SetInputFilesAsync(caminho);
            if (!await EsperarVisivelAsync(enviar, TimeSpan.FromSeconds(20), ct))
                throw new InvalidOperationException("O anexo não abriu para envio.");
        }
        await Task.Delay(TimeSpan.FromSeconds(Math.Min(EsperaAnexo, 2)), ct);
        await enviar.ClickAsync();
        // espera a prévia fechar e a caixa de texto voltar
        await page.Locator(SelCaixaTexto).First.WaitForAsync(new() { Timeout = 30_000 });
        await Task.Delay(TimeSpan.FromSeconds(EsperaAnexo), ct);
    }

    private static async Task ColarArquivoAsync(IPage page, string caminho)
    {
        string b64 = Convert.ToBase64String(await File.ReadAllBytesAsync(caminho));
        string tipo = Path.GetExtension(caminho).ToLowerInvariant() switch
        {
            ".ogg" or ".opus" => "audio/ogg",
            ".mp3" => "audio/mpeg",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream",
        };
        await page.Locator(SelCaixaTexto).First.ClickAsync();
        await page.EvaluateAsync(@"([sel, b64, nome, tipo]) => {
            const bin = atob(b64); const bytes = new Uint8Array(bin.length);
            for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
            const dt = new DataTransfer();
            dt.items.add(new File([bytes], nome, { type: tipo }));
            const alvo = document.querySelector(sel);
            alvo.focus();
            alvo.dispatchEvent(new ClipboardEvent('paste', { clipboardData: dt, bubbles: true, cancelable: true }));
        }", new object[] { SelCaixaTexto, b64, Path.GetFileName(caminho), tipo });
    }

    private static async Task<bool> EsperarVisivelAsync(ILocator loc, TimeSpan tempo, CancellationToken ct)
    {
        var limite = DateTime.UtcNow + tempo;
        while (DateTime.UtcNow < limite)
        {
            ct.ThrowIfCancellationRequested();
            if (await loc.IsVisibleAsync()) return true;
            await Task.Delay(250, ct);
        }
        return false;
    }

    private IPage Pagina() => _page is { IsClosed: false } p ? p
        : throw new InvalidOperationException("A janela do WhatsApp foi fechada.");

    public async Task FecharAsync()
    {
        try { if (_ctx is not null) await _ctx.CloseAsync(); } catch { /* já fechado */ }
        _ctx = null; _page = null;
    }

    public async ValueTask DisposeAsync()
    {
        await FecharAsync();
        _pw?.Dispose();
        _pw = null;
    }
}
