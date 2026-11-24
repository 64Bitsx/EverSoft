using System.Diagnostics;
using EverSoft.Core.Configuracao;
using EverSoft.Core.Util;

namespace EverSoft.Core.Automacao;

/// <summary>
/// O que a automação faz no Windows. A versão real usa o nircmd, o teclado e a área de transferência;
/// os testes usam uma versão falsa que só anota os passos.
/// </summary>
public interface ISistemaTeclado
{
    /// <summary>Roda o nircmd com os argumentos; devolve false se ele falhar.</summary>
    bool Nircmd(params string[] args);
    void Atalho(params ushort[] teclas);
    void Digitar(string texto);
    void CopiarTexto(string texto);
    void CopiarArquivo(string caminho);
    string TituloDaJanelaDaFrente();
}

/// <summary>A janela do WhatsApp não está mais na frente: o envio inteiro para.</summary>
public class JanelaPerdidaException(string mensagem) : InvalidOperationException(mensagem);

/// <summary>
/// WhatsApp Web do jeito da versão Python: o nircmd traz para a frente e maximiza a janela
/// do WhatsApp já aberta (em qualquer navegador) e cada conversa começa com Ctrl+Alt+N.
/// Antes de cada passo confere se a janela da frente ainda é a do WhatsApp; se não for,
/// ativa de novo, e se mesmo assim não der, para o envio em vez de digitar em outro lugar.
/// </summary>
public sealed class WhatsAppTeclado : IWhatsApp
{
    private readonly WhatsAppConfig _cfg;
    private readonly ISistemaTeclado _so;
    /// <summary>Multiplica todas as esperas (os testes usam 0).</summary>
    public double Ritmo { get; init; } = 1.0;

    public WhatsAppTeclado(WhatsAppConfig cfg, ISistemaTeclado? sistema = null)
    {
        _cfg = cfg;
        _so = sistema ?? new SistemaWindows(cfg.Nircmd);
    }

    private string Titulo => string.IsNullOrWhiteSpace(_cfg.JanelaTitulo) ? "WhatsApp" : _cfg.JanelaTitulo.Trim();

    private Task Esperar(double segundos, CancellationToken ct) =>
        segundos * Ritmo <= 0 ? Task.CompletedTask : Task.Delay(TimeSpan.FromSeconds(segundos * Ritmo), ct);

    public async Task AbrirAsync(Action<string> log, CancellationToken ct)
    {
        for (int i = Math.Max(0, _cfg.ContagemRegressiva); i > 0; i--)
        {
            log($"Começando em {i} s... (não use o mouse e o teclado; Parar para cancelar)");
            await Esperar(1, ct);
        }
        log($"Trazendo a janela \"{Titulo}\" para a frente...");
        if (!_so.Nircmd("win", "activate", "ititle", Titulo))
            throw new InvalidOperationException($"O nircmd não achou a janela \"{Titulo}\". Deixe o WhatsApp Web aberto na guia ativa do navegador.");
        await Esperar(1, ct);
        _so.Nircmd("win", "max", "ititle", Titulo);
        await Esperar(1, ct);
        await GarantirNaFrente(ct);
    }

    /// <summary>Confere se o WhatsApp está na frente; se não estiver, ativa de novo.</summary>
    private async Task GarantirNaFrente(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (NaFrente()) return;
        _so.Nircmd("win", "activate", "ititle", Titulo);
        await Esperar(_cfg.EsperaAtivar > 0 ? Math.Min(_cfg.EsperaAtivar, 2) : 1, ct);
        if (!NaFrente())
            throw new JanelaPerdidaException(
                $"A janela do WhatsApp saiu da frente (está na frente: \"{_so.TituloDaJanelaDaFrente()}\"). Envio parado para não digitar em outro lugar.");
    }

    private bool NaFrente() => _so.TituloDaJanelaDaFrente().Contains(Titulo, StringComparison.OrdinalIgnoreCase);

    public async Task<bool> AbrirConversaAsync(string numero, CancellationToken ct)
    {
        await NovaConversa(numero, ct);
        return true;
    }

    public async Task AbrirGrupoAsync(string nome, CancellationToken ct) => await NovaConversa(nome, ct);

    /// <summary>Ctrl+Alt+N → digita o número ou o nome → Enter.</summary>
    private async Task NovaConversa(string busca, CancellationToken ct)
    {
        await GarantirNaFrente(ct);
        _so.Atalho(Win32.VK_ESCAPE);                    // fecha prévia/menu que tenha ficado aberto
        await Esperar(0.5, ct);
        _so.Atalho(Win32.VK_CONTROL, Win32.VK_MENU, (ushort)'N');
        await Esperar(_cfg.EsperaAbrirConversa + 1, ct);
        await GarantirNaFrente(ct);
        _so.Digitar(busca);
        await Esperar(_cfg.EsperaAposNumero, ct);
        _so.Atalho(Win32.VK_RETURN);
        await Esperar(_cfg.EsperaAbrirConversa, ct);
    }

    public async Task EnviarTextoAsync(string texto, CancellationToken ct)
    {
        _so.CopiarTexto(texto);
        await Esperar(0.4, ct);
        await GarantirNaFrente(ct);
        _so.Atalho(Win32.VK_CONTROL, (ushort)'V');
        await Esperar(1, ct);
        _so.Atalho(Win32.VK_RETURN);
        await Esperar(1, ct);
    }

    public async Task EnviarArquivoAsync(string caminho, CancellationToken ct)
    {
        if (!File.Exists(caminho)) throw new FileNotFoundException("Arquivo não encontrado: " + caminho);
        _so.CopiarArquivo(caminho);
        await GarantirNaFrente(ct);
        _so.Atalho(Win32.VK_CONTROL, (ushort)'V');      // cola o arquivo como anexo
        await Esperar(_cfg.EsperaAnexo, ct);
        _so.Atalho(Win32.VK_RETURN);
        await Esperar(_cfg.EsperaAnexo, ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>A versão real, para o Windows.</summary>
    private sealed class SistemaWindows : ISistemaTeclado
    {
        private readonly string _nircmdConfig;
        public SistemaWindows(string nircmd) => _nircmdConfig = nircmd;

        public static string? AcharNircmd(string configurado)
        {
            var candidatos = new List<string>();
            if (!string.IsNullOrWhiteSpace(configurado)) candidatos.Add(configurado.Trim());
            candidatos.Add(Path.Combine(AppContext.BaseDirectory, "nircmd.exe"));
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                candidatos.Add(Path.Combine(dir.Trim('"'), "nircmd.exe"));
            candidatos.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "nircmd.exe"));
            return candidatos.FirstOrDefault(File.Exists);
        }

        public bool Nircmd(params string[] args)
        {
            string exe = AcharNircmd(_nircmdConfig) ?? throw new InvalidOperationException(
                "Não encontrei o nircmd.exe. Coloque-o ao lado do EverSoft.exe (ou informe o caminho em Configurações).");
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            if (!p.WaitForExit(10_000)) { try { p.Kill(); } catch { } return false; }
            if (p.ExitCode != 0) Log.Erro($"nircmd {string.Join(' ', args)}: código {p.ExitCode} {p.StandardError.ReadToEnd()}");
            return p.ExitCode == 0;
        }

        public void Atalho(params ushort[] teclas) => Win32.Atalho(teclas);
        public void Digitar(string texto) => Win32.Digitar(texto);
        public void CopiarTexto(string texto) => Win32.CopiarTexto(texto);
        public string TituloDaJanelaDaFrente() => Win32.TituloDaJanelaDaFrente();

        /// <summary>Igual ao Ctrl+C no Explorer (a versão Python usava o mesmo Set-Clipboard -LiteralPath).</summary>
        public void CopiarArquivo(string caminho)
        {
            var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (var a in new[] { "-NoProfile", "-STA", "-Command", $"Set-Clipboard -LiteralPath '{caminho.Replace("'", "''")}'" })
                psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            if (!p.WaitForExit(60_000) || p.ExitCode != 0)
                throw new InvalidOperationException("Não foi possível copiar o arquivo para colar no WhatsApp: " + caminho);
        }
    }
}
