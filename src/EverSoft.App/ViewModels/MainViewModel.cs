using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EverSoft.Core.Automacao;
using EverSoft.Core.Configuracao;
using EverSoft.Core.Planilhas;
using EverSoft.Core.Servicos;
using EverSoft.Core.Util;

namespace EverSoft.App.ViewModels;

/// <summary>O que a janela precisa oferecer ao ViewModel (caixas de mensagem, diálogos, área de transferência).</summary>
public interface IJanelas
{
    Task Mensagem(string texto);
    Task<bool> Confirmar(string texto);
    Task CopiarTexto(string texto);
    Task<bool> StatusAluno(string nome, Func<string, bool, bool, bool, bool[], bool, Task<bool>> gravar);
    Task<bool> Coments(string nome, (string, string, string) atuais, Func<string, string, string, Task<bool>> gravar);
    Task<bool> Bloqueio(string nome, IReadOnlyList<string> rotulos, (string Pratica, string Apostila) atuais, Func<bool[], Task<bool>> gravar);
    Task<(string Texto, bool Marcou)?> Justificar(string nome, string horario, string? jaRegistrada, bool temJ, Func<Task> desmarcarJ, Func<Task> removerNotificacao);
    Task<RegistroJustifica?> LinhaJustifica(RegistroJustifica? atual);
    Task Info(Nucleo.InfoAluno info, string? foto);
    Task<bool> Configuracoes(AppConfig cfg);
    Task Tarefa(string titulo, Func<Action<string>, Task> trabalho);
    /// <summary>Janelinha de progresso de um envio pelo WhatsApp, com botão Parar.</summary>
    Task<ResultadoEnvio?> Envio(string titulo, Func<Action<string>, Action<int, int>, CancellationToken, Task<ResultadoEnvio>> trabalho);
}

/// <summary>Uma aba da barra de cima (horários + Justifica, Agenda, Boletim, MSG).</summary>
public record AbaVM(string Id, string Titulo, string Cor, string CorTexto, object Conteudo);

/// <summary>Cérebro da janela principal.</summary>
public partial class MainViewModel : ObservableObject
{
    public Nucleo Nucleo { get; private set; }
    public IJanelas Janelas { get; set; } = null!;
    private MonitorPlanilhas? _monitor;
    private Dictionary<string, DateTime> _gradesVistas = new();

    public ObservableCollection<AbaVM> Abas { get; } = new();
    [ObservableProperty] private AbaVM? _abaSelecionada;
    public PainelAlunoVM Painel { get; } = new();
    public JustificaVM Justifica { get; }
    public AgendaVM Agenda { get; }
    public BoletimVM Boletim { get; }
    public MsgVM Msg { get; }

    [ObservableProperty] private string _statusTexto = "Abrindo...";
    /// <summary>O painel do aluno só aparece nas abas de horário.</summary>
    [ObservableProperty] private bool _mostrarPainel = true;
    [ObservableProperty] private bool _ocupado;
    [ObservableProperty] private string _corStatus = "#16A34A";
    public static string Versao => $"V{typeof(MainViewModel).Assembly.GetName().Version?.ToString(2)}";
    public string Titulo => $"EverSoft {Versao}";

    private SlotVM? _slotDoAluno;

    public MainViewModel(Nucleo nucleo)
    {
        Nucleo = nucleo;
        Justifica = new JustificaVM(this);
        Agenda = new AgendaVM(this);
        Boletim = new BoletimVM(this);
        Msg = new MsgVM(this);
        Painel.Copiar = CopiarTexto;
    }

    // ------------------------------------------------------------------ abertura
    /// <summary>Carrega os dados em segundo plano, monta as abas e liga a sincronia automática.</summary>
    public async Task IniciarAsync()
    {
        Ocupado = true;
        Status("Carregando planilhas...");
        var avisos = new List<string>();
        Dictionary<string, string> erros = new();
        await Task.Run(() =>
        {
            if (Nucleo.Cfg.SincroniaAutomatica)
                try { Sincronizacao.ConverterAlteradas(Nucleo.Cfg); } catch (Exception e) { Log.Erro($"abertura: {e.Message}"); }
            erros = Nucleo.Hub.CarregarTudo();
            try { avisos.AddRange(Nucleo.PreencherMatriculas()); }
            catch (Exception e) { avisos.Add("Abas de horário: " + e.Message); }
            _gradesVistas = DatasDasGrades();
        });
        MontarAbas();
        Ocupado = false;
        if (erros.Count > 0)
            Status($"Pronto, com {erros.Count} planilha(s) sem dados: " +
                   string.Join(", ", erros.Keys.Select(k => Core.Dados.DataHub.FontePorChave(k).Rotulo)), "#F59E0B");
        else Status("Pronto");

        _monitor = new MonitorPlanilhas(Nucleo.Cfg, Nucleo.Hub);
        _monitor.Mensagem += m => Avalonia.Threading.Dispatcher.UIThread.Post(() => Status(m));
        _monitor.DadosAtualizados += chaves => Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = AoMudarDados());
        if (Nucleo.Cfg.AutoAtualizarSegundos > 0) _monitor.Iniciar();
    }

    public void Encerrar()
    {
        _monitor?.Dispose();
        if (_whats is not null) _ = _whats.DisposeAsync().AsTask();
    }

    private Dictionary<string, DateTime> DatasDasGrades()
    {
        var d = new Dictionary<string, DateTime>();
        foreach (var k in new[] { "grade_presencial", "grade_ead" })
        {
            string json = FonteJson.CaminhoEspelho(Nucleo.Cfg.P("tec_json_dir"), Nucleo.Cfg.P(k));
            if (json != "" && File.Exists(json)) d[k] = File.GetLastWriteTimeUtc(json);
        }
        return d;
    }

    /// <summary>Alguma planilha mudou: se foi a grade, refaz as abas; senão só recalcula.</summary>
    private async Task AoMudarDados()
    {
        var agora = DatasDasGrades();
        bool gradeMudou = !agora.OrderBy(k => k.Key).SequenceEqual(_gradesVistas.OrderBy(k => k.Key));
        if (gradeMudou)
        {
            _gradesVistas = agora;
            await Task.Run(() => { try { Nucleo.PreencherMatriculas(); } catch (Exception e) { Log.Erro(e.Message); } });
            MontarAbas();
        }
        else AtualizarTudo();
        Status($"Dados atualizados às {DateTime.Now:HH:mm}.");
    }

    /// <summary>Recria a barra de abas, tentando manter a aba que estava aberta.</summary>
    public void MontarAbas()
    {
        string? antes = AbaSelecionada?.Id ?? (Nucleo.Estado.AbaAtiva == "" ? null : Nucleo.Estado.AbaAtiva);
        Abas.Clear();
        foreach (var s in Nucleo.Slots)
            Abas.Add(new AbaVM(s.Nome, s.Nome, s.Cor, s.Tipo == "ead" ? "#0B1B33" : "#FFFFFF", new SlotVM(this, s)));
        Abas.Add(new AbaVM("__just", "Justifica", "#475569", "#FFFFFF", Justifica));
        Abas.Add(new AbaVM("__agenda", "Agenda", "#475569", "#FFFFFF", Agenda));
        Abas.Add(new AbaVM("__bol", "Boletim", "#475569", "#FFFFFF", Boletim));
        Abas.Add(new AbaVM("__msg", "MSG", "#475569", "#FFFFFF", Msg));
        Justifica.Recarregar(); Agenda.Recarregar(); Boletim.Recarregar(); Msg.Recarregar();
        AbaSelecionada = Abas.FirstOrDefault(a => a.Id == antes) ?? Abas.FirstOrDefault();
    }

    partial void OnAbaSelecionadaChanged(AbaVM? value)
    {
        if (value is null) return;
        Nucleo.Estado.AbaAtiva = value.Id;
        MostrarPainel = value.Conteudo is SlotVM;
        if (value.Conteudo is SlotVM s) MostrarAluno(s, s.Selecionada);
        else if (value.Conteudo is JustificaVM j) j.Recarregar();
    }

    /// <summary>Recalcula todas as telas (depois de gravar ou sincronizar).</summary>
    public void AtualizarTudo()
    {
        foreach (var a in Abas) if (a.Conteudo is SlotVM s) s.Recarregar();
        Justifica.Recarregar(); Agenda.Recarregar(); Boletim.Recarregar();
        if (_slotDoAluno is not null) MostrarAluno(_slotDoAluno, _slotDoAluno.Selecionada);
    }

    public void Status(string texto, string cor = "#16A34A")
    {
        StatusTexto = texto;
        CorStatus = cor;
    }

    // ------------------------------------------------------------------ aluno selecionado
    public void MostrarAluno(SlotVM slot, LinhaVM? linha)
    {
        _slotDoAluno = slot;
        if (linha is null || linha.Vazia) Painel.Limpar();
        else Painel.Mostrar(Nucleo, linha.Dados);
    }

    private async Task<string?> ExigirAluno(SlotVM slot)
    {
        var l = slot.Selecionada;
        if (l is null || l.Nome == "")
        {
            await Janelas.Mensagem("Selecione um aluno na tabela primeiro.");
            return null;
        }
        return l.Nome;
    }

    /// <summary>Faz uma gravação nas planilhas tratando os erros comuns (arquivo aberto, rede fora...).</summary>
    public async Task<bool> Gravar(Func<Task> acao)
    {
        try
        {
            Ocupado = true;
            await acao();
            AtualizarTudo();
            Status("Gravado com sucesso.");
            return true;
        }
        catch (ArquivoEmUsoException e)
        {
            await Janelas.Mensagem(e.Message);
        }
        catch (Exception e)
        {
            Log.Erro(e.ToString());
            await Janelas.Mensagem("Não foi possível gravar:\n" + e.Message);
        }
        finally { Ocupado = false; }
        return false;
    }

    public async Task AbrirStatus(SlotVM slot)
    {
        if (await ExigirAluno(slot) is not { } nome) return;
        await Janelas.StatusAluno(nome, (n, atrasado, ok, adiantado, caps, naoPratica) =>
            Gravar(() => Task.Run(() => Nucleo.RegistrarStatus(n, "", atrasado, ok, adiantado, caps, naoPratica))));
    }

    public async Task AbrirComents(SlotVM slot)
    {
        if (await ExigirAluno(slot) is not { } nome) return;
        await Janelas.Coments(nome, Nucleo.Hub.Comentarios(nome), (c, con, p) =>
            Gravar(() => Task.Run(() => Nucleo.RegistrarComents(nome, c, con, p))));
    }

    public async Task AbrirBloqueio(SlotVM slot)
    {
        if (await ExigirAluno(slot) is not { } nome) return;
        await Janelas.Bloqueio(nome, Nucleo.Cfg.Bloqueio.Rotulos, Nucleo.Hub.BloqueioFlags(nome), marcas =>
            Gravar(() => Task.Run(() => Nucleo.RegistrarBloqueio(nome, marcas))));
    }

    public async Task AbrirJustificar(SlotVM slot)
    {
        if (await ExigirAluno(slot) is not { } nome) return;
        var linha = slot.Selecionada!;
        var reg = Nucleo.Estado.Justifica.FirstOrDefault(r => Texto.Norm(r.Nome) == Texto.Norm(nome));
        var r = await Janelas.Justificar(nome, slot.Nome, reg?.Justificativa is { Length: > 0 } j ? j : null, linha.J,
            () => { linha.J = false; return Task.CompletedTask; },
            async () =>
            {
                if (!await Janelas.Confirmar($"Remover {nome} das notificações de reposição (aba de faltas do Histórico)?")) return;
                int n = 0;
                if (await Gravar(() => Task.Run(() => n = Nucleo.RemoverNotificacao(nome))))
                    await Janelas.Mensagem($"{n} linha(s) removida(s).");
            });
        if (r is null) return;
        Nucleo.Justificar(slot.Nome, nome, r.Value.Texto.ToUpperInvariant(), r.Value.Marcou);
        linha.J = true;
        Justifica.Recarregar();
        Status($"Justificativa de {nome} registrada.");
    }

    public async Task AbrirInfo(SlotVM slot)
    {
        if (await ExigirAluno(slot) is not { } nome) return;
        await Janelas.Info(Nucleo.Info(nome), Nucleo.FotoAluno(nome));
    }

    public void AbrirSistemaEscola(LinhaVM? linha)
    {
        if (linha is null || linha.Vazia) { Status("Linha sem matrícula.", "#F59E0B"); return; }
        AbrirNoNavegador(Nucleo.UrlAluno(linha.Matricula));
    }

    public static void AbrirNoNavegador(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) { Log.Erro($"abrir {url}: {e.Message}"); }
    }

    public async Task CopiarTexto(string texto)
    {
        if (texto == "") return;
        await Janelas.CopiarTexto(texto);
        Status("Copiado para a área de transferência.");
    }

    // ------------------------------------------------------------------ botões das abas de horário
    public async Task TransferirParaJustifica(SlotVM slot)
    {
        try
        {
            Ocupado = true;
            var novos = await Task.Run(() => Nucleo.TransferirParaJustifica(slot.Slot));
            Justifica.Recarregar(faixaDoRelogio: true);   // já deixa marcada a faixa do horário do PC
            AbaSelecionada = Abas.First(a => a.Id == "__just");
            await Janelas.Mensagem(novos.Count > 0 ? $"{novos.Count} aluno(s) enviados para a aba Justifica." : "Nenhum aluno novo para a aba Justifica.");
        }
        catch (Exception e) { Log.Erro(e.ToString()); await Janelas.Mensagem(e.Message); }
        finally { Ocupado = false; }
    }

    // ------------------------------------------------------------------ WhatsApp
    private IWhatsApp? _whats;
    private bool _enviando;
    /// <summary>Troca o WhatsApp real por um falso (testes e capturas de tela).</summary>
    public Func<IWhatsApp>? CriarWhatsApp { get; set; }
    private IWhatsApp WhatsApp => _whats ??= CriarWhatsApp?.Invoke() ?? new WhatsAppTeclado(Nucleo.Cfg.Whatsapp);

    /// <summary>
    /// Botão "Enviar lembretes": alimenta a Agenda (sem sair da aba atual) e já começa
    /// o envio pelo WhatsApp Web (áudio da faixa + "Aluno: Nome", ou texto).
    /// </summary>
    public async Task EnviarLembretes(SlotVM slot)
    {
        if (_enviando) { await Janelas.Mensagem("Já há um envio pelo WhatsApp em andamento."); return; }
        string modo = Nucleo.Cfg.Lembrete.Modo;
        string? pasta = null;
        if (modo != "texto")
        {
            pasta = Nucleo.PastaAudioAtual();
            if (pasta is null)
            {
                await Janelas.Mensagem($"Nenhuma faixa de horário de lembrete agora ({DateTime.Now:HH:mm}). Nada foi enviado.");
                return;
            }
            if (EnvioLembretes.AudiosDaPasta(pasta).Count == 0)
            {
                await Janelas.Mensagem($"Não há áudio .ogg na pasta da faixa atual:\n{pasta}");
                return;
            }
        }
        List<ItemAgenda> agenda;
        try { agenda = Nucleo.PrepararLembretes(slot.Slot); }
        catch (Exception e) { Log.Erro(e.ToString()); await Janelas.Mensagem(e.Message); return; }
        Agenda.Recarregar();
        if (agenda.Count == 0) { await Janelas.Mensagem("Nenhum aluno sem J com telefone válido nesta aba."); return; }
        await EnviarAgenda(agenda, modo, pasta);
    }

    /// <summary>Reenvia a Agenda que já está montada (botão na aba Agenda).</summary>
    public async Task ReenviarAgenda()
    {
        if (_enviando) { await Janelas.Mensagem("Já há um envio pelo WhatsApp em andamento."); return; }
        var agenda = Nucleo.Estado.Agenda.ToList();
        if (agenda.Count == 0) { await Janelas.Mensagem("A Agenda está vazia. Use \"Enviar lembretes\" numa aba de horário."); return; }
        string modo = Nucleo.Cfg.Lembrete.Modo;
        string? pasta = modo == "texto" ? null : Nucleo.PastaAudioAtual();
        if (modo != "texto" && EnvioLembretes.AudiosDaPasta(pasta).Count == 0)
        {
            await Janelas.Mensagem(pasta is null ? $"Nenhuma faixa de horário de lembrete agora ({DateTime.Now:HH:mm})."
                                                 : $"Não há áudio .ogg na pasta da faixa atual:\n{pasta}");
            return;
        }
        await EnviarAgenda(agenda, modo, pasta);
    }

    private async Task EnviarAgenda(List<ItemAgenda> agenda, string modo, string? pasta)
    {
        _enviando = true;
        try
        {
            var cfg = Nucleo.Cfg.Whatsapp;
            var r = await Janelas.Envio($"Enviando lembretes ({(modo == "texto" ? "texto" : "áudio + nome")})",
                (log, progresso, ct) => EnvioLembretes.EnviarAsync(WhatsApp, agenda, modo, pasta, cfg, log, progresso, ct));
            if (r is null) return;
            Status($"{r.Enviados} de {r.Total} lembrete(s) enviados{(r.Cancelado ? " (envio interrompido)" : "")}.",
                   r.Falhas.Count > 0 ? "#F59E0B" : "#16A34A");
            if (r.Falhas.Count > 0)
                await Janelas.Mensagem($"{r.Enviados} de {r.Total} lembrete(s) enviados. Não foram:\n\n" + string.Join("\n", r.Falhas));
        }
        finally { _enviando = false; }
    }

    // ------------------------------------------------------------------ Justifica
    public async Task EnviarJustificados(string? faixa)
    {
        if (_enviando) { await Janelas.Mensagem("Já há um envio pelo WhatsApp em andamento."); return; }
        var m = Nucleo.MensagemFaltosos(faixa: faixa);
        if (m is null) { await Janelas.Mensagem("Escolha uma faixa de horário."); return; }
        string grupo = Nucleo.Cfg.Whatsapp.GrupoJustifica;
        if (!await Janelas.Confirmar($"A mensagem da faixa {m.Faixa} vai para o grupo '{grupo}' pelo WhatsApp.\n" +
                                     "Depois do envio, as justificativas vão para o JSON e a faixa é limpa da lista.\n\n" + m.Mensagem))
            return;
        _enviando = true;
        ResultadoEnvio? r;
        try
        {
            r = await Janelas.Envio("Enviando justificados", async (log, progresso, ct) =>
            {
                await WhatsApp.AbrirAsync(log, ct);
                log($"Abrindo o grupo {grupo}...");
                await WhatsApp.AbrirGrupoAsync(grupo, ct);
                log("Enviando a mensagem...");
                await WhatsApp.EnviarTextoAsync(m.Mensagem, ct);
                return new ResultadoEnvio(1, 1, new(), false);
            });
        }
        finally { _enviando = false; }
        if (r is null || r.Enviados == 0)
        {
            Status("Os justificados não foram enviados; a lista ficou como estava.", "#F59E0B");
            return;
        }
        int qtd = 0;
        try { qtd = Nucleo.GravarJsonJustificativas(m.Codigo); }
        catch (Exception e) { Log.Erro($"JSON de justificativas: {e.Message}"); Status("Não foi possível gravar o JSON: " + e.Message, "#DC2626"); }
        int n = Nucleo.LimparFaixa(m.Codigo);
        Justifica.Recarregar();
        Status($"Faixa {m.Codigo}: enviada ao grupo; {n} linha(s) removida(s); {qtd} justificativa(s) no JSON.");
    }

    public async Task EditarLinhaJustifica(RegistroJustifica? atual)
    {
        var r = await Janelas.LinhaJustifica(atual);
        if (r is null) return;
        if (atual is null) Nucleo.Estado.Justifica.Add(r);
        else
        {
            int i = Nucleo.Estado.Justifica.IndexOf(atual);
            if (i >= 0) Nucleo.Estado.Justifica[i] = r;
        }
        Nucleo.Salvar();
        Justifica.Recarregar();
    }

    public async Task RemoverLinhasJustifica(IReadOnlyList<RegistroJustifica> linhas)
    {
        if (linhas.Count == 0) return;
        if (!await Janelas.Confirmar(linhas.Count > 1 ? $"Remover as {linhas.Count} linhas selecionadas?" : "Remover a linha selecionada?")) return;
        foreach (var l in linhas) Nucleo.Estado.Justifica.Remove(l);
        Nucleo.Salvar();
        Justifica.Recarregar();
    }

    // ------------------------------------------------------------------ Boletim
    public async Task PuxarBoletins()
    {
        if (Nucleo.Slots.Count == 0) { await Janelas.Mensagem("Ainda não há abas de horário. Execute a Automação TEC."); return; }
        try
        {
            Ocupado = true;
            var d = await Task.Run(Nucleo.ColetarBoletins);
            Boletim.Recarregar();
            Status($"{d.Count} contato(s) coletados para o boletim.");
        }
        catch (Exception e) { Log.Erro(e.ToString()); await Janelas.Mensagem(e.Message); }
        finally { Ocupado = false; }
    }

    // ------------------------------------------------------------------ menu Arquivo
    [RelayCommand]
    private Task AutomacaoTec() => Janelas.Tarefa("Automação TEC", async log =>
    {
        await Task.Run(() => Sincronizacao.GerarJsonsTec(Nucleo.Cfg, log));
        await RecarregarDepoisDeSincronizar(log);
    });

    [RelayCommand]
    private Task Sincronia() => Janelas.Tarefa("Sincronia", async log =>
    {
        await Task.Run(() => Sincronizacao.GerarJsonsSincronizacao(Nucleo.Cfg, log));
        await RecarregarDepoisDeSincronizar(log);
    });

    private async Task RecarregarDepoisDeSincronizar(Action<string> log)
    {
        log("Relendo os dados...");
        var avisos = await Task.Run(() => { Nucleo.Hub.CarregarTudo(); return Nucleo.PreencherMatriculas(); });
        foreach (var a in avisos) log(a);
        _gradesVistas = DatasDasGrades();
        MontarAbas();
        Status($"Sincronizado às {DateTime.Now:HH:mm}.");
        log("Pronto.");
    }

    [RelayCommand]
    private async Task Configuracoes()
    {
        string pastaAntes = Nucleo.Cfg.PastaDados;
        if (!await Janelas.Configuracoes(Nucleo.Cfg)) return;
        Nucleo.Cfg.Salvar();
        if (Nucleo.Cfg.PastaDados != pastaAntes)
        {
            await Janelas.Mensagem("A pasta de dados mudou. Feche e abra o EverSoft para usar a nova pasta.");
            return;
        }
        Status("Configurações salvas.");
        _ = Sincronia();
    }

    [RelayCommand]
    private void AbrirPastaDados()
    {
        Directory.CreateDirectory(Nucleo.Cfg.PastaDados);
        AbrirNoNavegador(Nucleo.Cfg.PastaDados);
    }

    [RelayCommand]
    private Task Sobre() => Janelas.Mensagem($"{Titulo}\n\nDesenvolvido por \"Inferno Branco\" e Gestão Pedagógica.\n\nDados do app: {Nucleo.Cfg.PastaDados}");
}
