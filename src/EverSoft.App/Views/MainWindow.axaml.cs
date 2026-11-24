using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EverSoft.App.ViewModels;
using EverSoft.Core.Automacao;
using EverSoft.Core.Configuracao;
using EverSoft.Core.Servicos;
using EverSoft.Core.Util;

namespace EverSoft.App.Views;

/// <summary>
/// Janela principal. Aqui ficam só as coisas "de tela" (abrir diálogos, copiar texto);
/// as regras ficam no MainViewModel e no EverSoft.Core.
/// </summary>
public partial class MainWindow : Window, IJanelas
{
    private readonly MainViewModel _vm;

    public MainWindow() : this(null) { }

    public MainWindow(Nucleo? nucleo)
    {
        InitializeComponent();
        _vm = new MainViewModel(nucleo ?? new Nucleo(AppConfig.Carregar())) { Janelas = this };
        DataContext = _vm;
        Opened += async (_, _) => { if (IniciarAoAbrir) await _vm.IniciarAsync(); };
        Closing += (_, _) => { if (_encerrado) return; _vm.Encerrar(); _vm.Nucleo.Salvar(); };
    }

    public MainViewModel ViewModel => _vm;
    private bool _encerrado;

    // ------------------------------------------------------------------ atualização
    private DispatcherTimer? _timerAtualizacao;
    private bool _perguntando;
    private DateTime? _versaoRecusada;

    /// <summary>Confere a cada minuto se o EverSoft.exe da rede ficou mais novo e pergunta se quer atualizar.</summary>
    public void VerificarAtualizacoesACadaMinuto()
    {
        _timerAtualizacao = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timerAtualizacao.Tick += async (_, _) => await ConferirAtualizacao();
        _timerAtualizacao.Start();
    }

    private async Task ConferirAtualizacao()
    {
        if (_perguntando) return;
        string rede = _vm.Nucleo.Cfg.ExecutavelAtualizacao;
        bool nova = await Task.Run(() => Atualizador.TemVersaoNova(Atualizacao.ExeInstalado, rede));
        if (!nova) return;
        var data = Atualizador.DataDoExecutavel(rede);
        if (data == _versaoRecusada) return;           // já disse "não" para esta versão
        _perguntando = true;
        try
        {
            bool sim = await Confirmar($"Há uma versão nova do EverSoft na rede (de {data:dd/MM/yyyy HH:mm}).\n\n" +
                                       "Quer atualizar agora? O EverSoft vai fechar e abrir de novo, já atualizado, " +
                                       "e vai mostrar o que mudou.");
            if (!sim) { _versaoRecusada = data; return; }
            await AtualizarAgora(rede);
        }
        finally { _perguntando = false; }
    }

    private async Task AtualizarAgora(string rede)
    {
        _vm.Encerrar();
        _vm.Nucleo.Salvar();
        var tela = new SplashWindow("Atualização: baixando a versão mais nova do EverSoft...");
        tela.Show();
        try
        {
            await Task.Run(() => Atualizador.Instalar(rede, Atualizacao.ExeInstalado));
            Atualizacao.AbrirInstalado();
            _encerrado = true;
            tela.Close();
            (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        catch (Exception e)
        {
            Log.Erro("atualizar: " + e);
            tela.Close();
            await Mensagem("Não foi possível atualizar agora:\n" + e.Message + "\n\nO EverSoft continua aberto na versão atual.");
            await _vm.IniciarAsync();   // religa a sincronia automática
        }
    }

    /// <summary>Falso quando a abertura já foi feita atrás da tela de carregamento (App.axaml.cs).</summary>
    public bool IniciarAoAbrir { get; set; } = true;

    private void Sair_Click(object? sender, RoutedEventArgs e) => Close();

    // ------------------------------------------------------------------ IJanelas
    public Task Mensagem(string texto) => Dialogo.Mensagem(this, texto);
    public Task<bool> Confirmar(string texto) => Dialogo.Confirmar(this, texto);

    public async Task CopiarTexto(string texto)
    {
        if (Clipboard is not null) await Clipboard.SetTextAsync(texto);
    }

    public async Task<bool> StatusAluno(string nome, Func<string, bool, bool, bool, bool[], bool, Task<bool>> gravar)
    {
        var d = new Dialogo("Status do aluno");
        d.Titulo(nome);
        var atrasado = d.Check("Aluno ATRASADO");
        var naoPratica = d.Check("    NÃO PRATICA");
        naoPratica.IsVisible = false;
        var ok = d.Check("Aluno OK");
        var adiantado = d.Check("Aluno ADIANTADO");
        var linhaCaps = d.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(0, 6, 0, 0) });
        var caps = Enumerable.Range(1, 4).Select(i => new CheckBox { Content = $"Cap {i}" }).ToArray();
        foreach (var c in caps) linhaCaps.Children.Add(c);

        // Mesmas regras do formulário antigo: OK, ADIANTADO e ATRASADO não combinam entre si.
        void Exclusivo(CheckBox marcado, params CheckBox[] outros)
        {
            if (marcado.IsChecked == true) foreach (var o in outros) o.IsChecked = false;
            naoPratica.IsVisible = atrasado.IsChecked == true;
            if (!naoPratica.IsVisible) naoPratica.IsChecked = false;
        }
        atrasado.IsCheckedChanged += (_, _) => Exclusivo(atrasado, ok, adiantado);
        ok.IsCheckedChanged += (_, _) => Exclusivo(ok, atrasado, adiantado);
        adiantado.IsCheckedChanged += (_, _) => Exclusivo(adiantado, ok, atrasado);

        d.Botao("Cancelar", () => d.Fechar(false)).IsCancel = true;
        d.Botao("Enviar", async () =>
        {
            if (await gravar(nome, atrasado.IsChecked == true, ok.IsChecked == true, adiantado.IsChecked == true,
                             caps.Select(c => c.IsChecked == true).ToArray(), naoPratica.IsChecked == true))
                d.Fechar(true);
        }, "dourado").IsDefault = true;
        await d.ShowDialog(this);
        return d.Confirmado;
    }

    public async Task<bool> Coments(string nome, (string, string, string) atuais, Func<string, string, string, Task<bool>> gravar)
    {
        var d = new Dialogo("Comentários do aluno", 560);
        d.Titulo(nome);
        d.Rotulo("Comentário"); var c = d.Campo(atuais.Item1, 5);
        d.Rotulo("Conquistas"); var con = d.Campo(atuais.Item2, 3);
        d.Rotulo("Pontos"); var p = d.Campo(atuais.Item3, 2);
        d.Botao("Cancelar", () => d.Fechar(false)).IsCancel = true;
        d.Botao("Enviar", async () =>
        {
            if (await gravar((c.Text ?? "").Trim(), (con.Text ?? "").Trim(), (p.Text ?? "").Trim())) d.Fechar(true);
        }, "verde");
        await d.ShowDialog(this);
        return d.Confirmado;
    }

    public async Task<bool> Bloqueio(string nome, IReadOnlyList<string> rotulos, (string Pratica, string Apostila) atuais, Func<bool[], Task<bool>> gravar)
    {
        var d = new Dialogo("Bloqueio");
        d.Titulo(nome);
        var marcas = rotulos.Take(4).Select((r, i) => d.Check(r, i == 0 ? atuais.Pratica != "" : i == 1 && atuais.Apostila != "")).ToArray();
        d.Botao("Cancelar", () => d.Fechar(false)).IsCancel = true;
        d.Botao("Enviar", async () =>
        {
            if (await gravar(marcas.Select(m => m.IsChecked == true).ToArray())) d.Fechar(true);
        }, "azul");
        await d.ShowDialog(this);
        return d.Confirmado;
    }

    public async Task<(string Texto, bool Marcou)?> Justificar(string nome, string horario, string? jaRegistrada, bool temJ,
                                                               Func<Task> desmarcarJ, Func<Task> removerNotificacao)
    {
        var d = new Dialogo("Justificativa de falta", 520);
        d.Titulo(nome);
        d.Texto($"Horário: {horario}", "#64748B");
        if (jaRegistrada is not null) d.Texto($"Já registrada: {jaRegistrada}", "#EA580C");
        d.Rotulo("Justificativa");
        var t = d.Campo(maiusculas: true);
        bool? marcou = null;
        var extras = d.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) });
        if (temJ)
        {
            var b = new Button { Content = "Desmarcar J" };
            b.Click += async (_, _) => { await desmarcarJ(); d.Fechar(false); };
            extras.Children.Add(b);
        }
        var n = new Button { Content = "Remover notificação de reposição" };
        n.Click += async (_, _) => await removerNotificacao();
        extras.Children.Add(n);

        async Task Salvar(bool m)
        {
            if (string.IsNullOrWhiteSpace(t.Text)) { await Mensagem("Digite a justificativa."); return; }
            marcou = m;
            d.Fechar(true);
        }
        d.Botao("Não marcou reposição", () => _ = Salvar(false), "laranja");
        d.Botao("Marcou reposição", () => _ = Salvar(true), "verde");
        d.Opened += (_, _) => t.Focus();
        await d.ShowDialog(this);
        return d.Confirmado && marcou is not null ? (t.Text!.Trim(), marcou.Value) : null;
    }

    public async Task<RegistroJustifica?> LinhaJustifica(RegistroJustifica? atual)
    {
        var d = new Dialogo("Linha da Justifica");
        var data = Texto.ParseData(atual?.Data);
        d.Rotulo("Nome"); var nome = d.Campo(atual?.Nome ?? "");
        d.Rotulo("Justificativa"); var just = d.Campo(atual?.Justificativa ?? "", maiusculas: true);
        d.Rotulo("Data (dd/mm/aaaa)"); var dt = d.Campo(data?.ToString("dd/MM/yyyy") ?? "");
        d.Rotulo("Horário (ex.: 13h00 - 15h00)"); var hor = d.Campo(atual?.Horario ?? "");
        d.Rotulo("Frequência"); var freq = d.Campo(atual?.Frequencia ?? "");
        RegistroJustifica? resultado = null;
        d.Botao("Cancelar", () => d.Fechar(false)).IsCancel = true;
        d.Botao("Salvar", async () =>
        {
            if (string.IsNullOrWhiteSpace(nome.Text)) { await Mensagem("Informe o nome."); return; }
            var dd = string.IsNullOrWhiteSpace(dt.Text) ? null : Texto.ParseData(dt.Text);
            if (!string.IsNullOrWhiteSpace(dt.Text) && dd is null) { await Mensagem("Data inválida. Use dd/mm/aaaa."); return; }
            resultado = new RegistroJustifica
            {
                Nome = nome.Text!.Trim(), Justificativa = (just.Text ?? "").Trim(), Data = dd?.ToString("yyyy-MM-dd") ?? "",
                Horario = (hor.Text ?? "").Trim(), Frequencia = (freq.Text ?? "").Trim(),
            };
            d.Fechar(true);
        }, "verde");
        await d.ShowDialog(this);
        return resultado;
    }

    public async Task Info(Nucleo.InfoAluno info, string? foto)
    {
        var d = new Dialogo("Informações do aluno", 420);
        if (foto is not null)
        {
            try
            {
                using var fs = File.OpenRead(foto);
                d.Add(new Image { Source = Bitmap.DecodeToWidth(fs, 280), MaxHeight = 300, Margin = new Thickness(0, 0, 0, 8) });
            }
            catch { d.Texto("(sem foto)", "#64748B"); }
        }
        else d.Texto("(sem foto)", "#64748B");
        d.Titulo(info.Nome);
        foreach (var (rot, val) in new[] { ("Telefone 1", info.Num1), ("Telefone 2", info.Num2), ("Telefone 3", info.Num3), ("Instagram", info.Insta),
                                          ("Comentário", info.Comentario), ("Conquistas", info.Conquistas), ("Pontos", info.Pontos) })
        {
            d.Rotulo(rot);
            d.Add(new SelectableTextBlock { Text = val == "" ? "—" : val, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        }
        d.Botao("Fechar", () => d.Fechar(true), "azul").IsDefault = true;
        await d.ShowDialog(this);
    }

    public async Task<bool> Configuracoes(AppConfig cfg)
    {
        var d = new Dialogo("Configurações", 760);
        d.ComRolagem();
        d.Titulo("Arquivos e pastas");
        var campos = new Dictionary<string, TextBox>();
        foreach (var (chave, rotulo) in RotulosCaminhos)
        {
            d.Rotulo(rotulo);
            campos[chave] = d.Campo(cfg.P(chave));
        }
        d.Titulo("Geral");
        d.Rotulo("Pasta de dados do aplicativo (config, estado e backups)");
        var pasta = d.Campo(cfg.PastaDados);
        var auto = d.Check("Converter e reler as planilhas sozinho quando elas mudarem", cfg.SincroniaAutomatica);
        d.Rotulo("Verificar mudanças a cada (segundos)");
        var seg = d.Campo(cfg.AutoAtualizarSegundos.ToString());
        var backup = d.Check("Fazer backup antes de gravar nas planilhas", cfg.BackupAntesDeGravar);
        d.Rotulo("Grupo do WhatsApp para os justificados");
        var grupo = d.Campo(cfg.Whatsapp.GrupoJustifica);
        d.Rotulo("Modo do lembrete (audio ou texto)");
        var modo = d.Campo(cfg.Lembrete.Modo);
        d.Rotulo("Título da janela do WhatsApp (trecho usado no nircmd win activate)");
        var janela = d.Campo(cfg.Whatsapp.JanelaTitulo);
        d.Rotulo("Caminho do nircmd.exe (vazio = ao lado do EverSoft.exe, no PATH ou no Windows)");
        var nircmd = d.Campo(cfg.Whatsapp.Nircmd);
        d.Rotulo("EverSoft.exe da rede usado na atualização (compara a data de modificação)");
        var exeAtualizacao = d.Campo(cfg.ExecutavelAtualizacao);
        d.Rotulo("Contagem antes de começar o envio (segundos)");
        var contagem = d.Campo(cfg.Whatsapp.ContagemRegressiva.ToString());

        d.Botao("Cancelar", () => d.Fechar(false)).IsCancel = true;
        d.Botao("Salvar", async () =>
        {
            if (!int.TryParse(seg.Text, out int s) || s < 0) { await Mensagem("Informe um número de segundos válido."); return; }
            if (!int.TryParse(contagem.Text, out int cont) || cont < 0) { await Mensagem("Informe uma contagem válida (segundos)."); return; }
            foreach (var (k, t) in campos) cfg.Caminhos[k] = (t.Text ?? "").Trim();
            cfg.DadosAppDir = (pasta.Text ?? "").Trim();
            cfg.SincroniaAutomatica = auto.IsChecked == true;
            cfg.AutoAtualizarSegundos = s;
            cfg.BackupAntesDeGravar = backup.IsChecked == true;
            cfg.Whatsapp.GrupoJustifica = (grupo.Text ?? "").Trim();
            cfg.Lembrete.Modo = (modo.Text ?? "").Trim() == "texto" ? "texto" : "audio";
            cfg.Whatsapp.JanelaTitulo = string.IsNullOrWhiteSpace(janela.Text) ? "WhatsApp" : janela.Text.Trim();
            cfg.Whatsapp.Nircmd = (nircmd.Text ?? "").Trim();
            cfg.Whatsapp.ContagemRegressiva = cont;
            cfg.ExecutavelAtualizacao = (exeAtualizacao.Text ?? "").Trim();
            d.Fechar(true);
        }, "verde");
        await d.ShowDialog(this);
        return d.Confirmado;
    }

    private static readonly (string, string)[] RotulosCaminhos =
    {
        ("tec_json_dir", "Pasta dos JSONs (Automação TEC e Sincronia)"),
        ("acompanhar_alunos", "Acompanhar Alunos.xlsx"),
        ("dados_alunos", "Dados dos Alunos.xlsm"),
        ("mat_ativas", "RelMatAtivas.xls"),
        ("grade_presencial", "Grade de horários presencial"),
        ("grade_ead", "Grade de horários EAD"),
        ("bloqueio", "Bloqueio.xlsx"),
        ("historico", "Historico.xlsx"),
        ("observacoes", "Observacoes.xlsx"),
        ("coments", "Coments.xlsx"),
        ("base_prepara", "Base Prepara.xls"),
        ("boletim_xlsm", "Boletim.xlsm"),
        ("boletim_imagem", "Imagem do boletim (Boletim.png, trocada todo mês)"),
        ("fotos_alunos", "Pasta das fotos dos alunos"),
        ("fotos_bmp", "Pasta das fotos BMP"),
        ("audios_lembretes", "Pasta dos áudios de lembrete"),
        ("justificativas_json_dir", "Pasta dos JSONs de justificativas"),
        ("link_aluno", "Link do aluno no Sistema Escola"),
    };

    /// <summary>Janela com o andamento da Automação TEC / Sincronia.</summary>
    /// <summary>
    /// Automação TEC / Sincronia: mostra a tela de carregamento enquanto trabalha e some sozinha.
    /// A janela com o relatório só aparece quando algo deu errado.
    /// </summary>
    public async Task Tarefa(string titulo, Func<Action<string>, Task> trabalho)
    {
        var linhas = new List<string>();
        var problemas = new List<string>();
        var splash = new SplashWindow(titulo + "...") { WindowStartupLocation = WindowStartupLocation.CenterOwner };
        void Escrever(string m)
        {
            lock (linhas)
            {
                linhas.Add(m);
                if (EhProblema(m)) problemas.Add(m);
            }
            Dispatcher.UIThread.Post(() => splash.Mensagem = $"{titulo}: {m.Trim()}");
        }
        splash.Opened += async (_, _) =>
        {
            try { await trabalho(Escrever); }
            catch (Exception e) { Log.Erro(e.ToString()); Escrever("ERRO: " + e.Message); }
            finally { splash.Close(); }
        };
        await splash.ShowDialog(this);
        if (problemas.Count == 0) return;

        var log = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 360,
            FontFamily = new FontFamily("Consolas,Menlo,monospace"), FontSize = 12, Background = Brush.Parse("#0B1B33"), Foreground = Brushes.White,
            Text = string.Join(Environment.NewLine, linhas),
        };
        var d = new Dialogo($"{titulo}: terminou com {problemas.Count} problema(s)", 680);
        d.Texto("Algumas planilhas não puderam ser lidas. Veja abaixo o que aconteceu:", "#B91C1C");
        d.Add(log);
        d.Botao("Fechar", () => d.Fechar(true), "azul").IsDefault = true;
        await d.ShowDialog(this);
    }

    /// <summary>Linhas do relatório que indicam erro (arquivo sumido, aba faltando, falha de leitura).</summary>
    public static bool EhProblema(string linha)
    {
        var l = linha.Trim();
        return l.StartsWith("ERRO", StringComparison.OrdinalIgnoreCase)
            || l.StartsWith("NÃO ENCONTRADO", StringComparison.OrdinalIgnoreCase)
            || l.StartsWith("Erro ao", StringComparison.OrdinalIgnoreCase)
            || (l.StartsWith("Aba ") && l.EndsWith("não existe."));
    }

    /// <summary>
    /// Janelinha do envio pelo WhatsApp: mostra quem está sendo enviado, a barra de progresso e o botão Parar.
    /// Fecha sozinha ao terminar. Não rouba o foco da janela do WhatsApp.
    /// </summary>
    public async Task<ResultadoEnvio?> Envio(string titulo,
        Func<Action<string>, Action<int, int>, CancellationToken, Task<ResultadoEnvio>> trabalho)
    {
        using var cts = new CancellationTokenSource();
        var texto = new TextBlock { Text = "Preparando...", TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = Brush.Parse("#0F172A") };
        var barra = new ProgressBar { Minimum = 0, Maximum = 1, Height = 10, IsIndeterminate = true, Foreground = Brush.Parse("#14B8A6") };
        var parar = new Button { Content = "Parar envio", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 110, HorizontalContentAlignment = HorizontalAlignment.Center };
        parar.Classes.Add("vermelho");
        var janela = new Window
        {
            Title = titulo, Icon = SplashWindow.Icone(), Width = 460, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = true, Topmost = true, ShowActivated = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 12,
                Children =
                {
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children =
                    {
                        new Image { Source = SplashWindow.Logo(), Width = 40, Height = 40 },
                        new TextBlock { Text = titulo, FontWeight = FontWeight.Bold, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 },
                    } },
                    texto, barra, parar,
                },
            },
        };
        // canto inferior direito da tela, para não atrapalhar
        var tela = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (tela is not null)
        {
            var area = tela.WorkingArea;
            janela.Position = new PixelPoint(area.Right - (int)(480 * tela.Scaling), area.Bottom - (int)(230 * tela.Scaling));
        }
        parar.Click += (_, _) => { cts.Cancel(); parar.IsEnabled = false; parar.Content = "Parando..."; };
        janela.Closing += (_, _) => cts.Cancel();

        ResultadoEnvio? resultado = null;
        janela.Show(this);
        try
        {
            resultado = await Task.Run(() => trabalho(
                m => Dispatcher.UIThread.Post(() => texto.Text = m.Trim()),
                (i, n) => Dispatcher.UIThread.Post(() => { barra.IsIndeterminate = false; barra.Maximum = n; barra.Value = i; }),
                cts.Token));
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Log.Erro("WhatsApp: " + e);
            janela.Close();
            await Mensagem("O envio pelo WhatsApp parou com erro:\n" + e.Message);
            return null;
        }
        janela.Close();
        return resultado;
    }
}
