using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EverSoft.Core.Configuracao;
using EverSoft.Core.Dominio;
using EverSoft.Core.Util;

namespace EverSoft.App.ViewModels;

/// <summary>Linha da aba Justifica, com a matrícula buscada pelo nome.</summary>
public class LinhaJustificaVM
{
    public RegistroJustifica Registro { get; }
    public string Matricula { get; }
    public string Data => Texto.ParseData(Registro.Data) is { } d ? d.ToString("dd/MM/yyyy") : "";
    public LinhaJustificaVM(RegistroJustifica r, string matricula) { Registro = r; Matricula = matricula; }
}

/// <summary>Aba Justifica: faltosos do dia e envio da lista ao grupo.</summary>
public partial class JustificaVM : ObservableObject
{
    private readonly MainViewModel _main;
    public ObservableCollection<LinhaJustificaVM> Linhas { get; } = new();
    public ObservableCollection<string> Faixas { get; } = new();
    [ObservableProperty] private LinhaJustificaVM? _selecionada;
    [ObservableProperty] private string? _faixa;
    [ObservableProperty] private string _previa = "";
    [ObservableProperty] private string _resumo = "";

    public JustificaVM(MainViewModel main) => _main = main;

    public void Recarregar()
    {
        var n = _main.Nucleo;
        Linhas.Clear();
        foreach (var r in n.Estado.Justifica) Linhas.Add(new LinhaJustificaVM(r, n.Hub.MatriculaPorNome(r.Nome)));

        // Faixas do dia; começa na faixa atual (ou na última que já passou).
        var agora = n.Agora();
        var lista = Core.Dominio.Faixas.FaixasDoDia(agora);
        string atual = Core.Dominio.Faixas.FaixaHorarioJustifica(agora);
        if (!Faixas.SequenceEqual(lista))
        {
            Faixas.Clear();
            foreach (var f in lista) Faixas.Add(f);
        }
        if (Faixa is null || !Faixas.Contains(Faixa))
            Faixa = atual != "" ? atual : Faixas.LastOrDefault(f => Inicio(f) <= agora.TimeOfDay) ?? Faixas.FirstOrDefault();
        AtualizarPrevia();
    }

    private static TimeSpan Inicio(string faixa) => TimeSpan.Parse(faixa[..5]);

    partial void OnFaixaChanged(string? value) => AtualizarPrevia();

    private void AtualizarPrevia()
    {
        if (Faixa is null) { Previa = ""; Resumo = ""; return; }
        var m = _main.Nucleo.MensagemFaltosos(faixa: Faixa);
        Previa = m?.Mensagem ?? "";
        Resumo = m is null ? "" : $"{m.Com} com retorno · {m.Sem} sem retorno";
    }

    [RelayCommand] private Task Enviar() => _main.EnviarJustificados(Faixa);
    [RelayCommand] private Task CopiarMensagem() => _main.CopiarTexto(Previa);
    [RelayCommand] private Task Adicionar() => _main.EditarLinhaJustifica(null);
    [RelayCommand] private Task Editar() => Selecionada is null ? Task.CompletedTask : _main.EditarLinhaJustifica(Selecionada.Registro);
    [RelayCommand] private Task Remover() => _main.RemoverLinhasJustifica(Selecionada is null ? Array.Empty<RegistroJustifica>() : new[] { Selecionada.Registro });
}

/// <summary>Aba Agenda: contatos dos lembretes.</summary>
public partial class AgendaVM : ObservableObject
{
    private readonly MainViewModel _main;
    public ObservableCollection<ItemAgenda> Itens { get; } = new();
    [ObservableProperty] private string _resumo = "";
    public AgendaVM(MainViewModel main) => _main = main;

    public void Recarregar()
    {
        Itens.Clear();
        foreach (var i in _main.Nucleo.Estado.Agenda) Itens.Add(i);
        string hora = _main.Nucleo.Estado.HoraLembrete;
        Resumo = $"{Itens.Count} contato(s) · hora dos lembretes: {(hora == "" ? "-" : hora)}";
    }

    [RelayCommand]
    private void Limpar()
    {
        _main.Nucleo.Estado.Agenda.Clear();
        _main.Nucleo.Salvar();
        Recarregar();
    }
}

/// <summary>Linha da aba Boletim.</summary>
public record LinhaBoletim(string Nome, string Numero, string Aproveitamento, string Mensagem);

/// <summary>Aba Boletim: aproveitamento do mês e mensagens.</summary>
public partial class BoletimVM : ObservableObject
{
    private readonly MainViewModel _main;
    public ObservableCollection<LinhaBoletim> Itens { get; } = new();
    [ObservableProperty] private string _mesAtual = "";
    [ObservableProperty] private string _proximoMes = "";
    [ObservableProperty] private string _resumo = "";
    public BoletimVM(MainViewModel main) => _main = main;

    public void Recarregar()
    {
        var n = _main.Nucleo;
        var b = n.Cfg.Boletim;
        MesAtual = b.MesAtual;
        ProximoMes = b.ProximoMes;
        Itens.Clear();
        var cache = new Dictionary<string, double?>();
        foreach (var d in n.Estado.Disparo)
        {
            string k = Texto.Norm(d.Nome);
            if (!cache.TryGetValue(k, out var frac)) cache[k] = frac = n.Hub.Aproveitamento(d.Nome);
            if (frac is null) continue;   // nunca mostra linha sem nota
            string msg = Mensagens.MensagemBoletim(d.Nome, b.MesAtual, b.ProximoMes, frac.Value).Replace("\n", " ↵ ");
            Itens.Add(new LinhaBoletim(d.Nome, d.Numero, Mensagens.PctTexto(frac.Value) + "%", msg));
        }
        Resumo = $"{Itens.Count} contato(s) com aproveitamento";
    }

    [RelayCommand]
    private void Aplicar()
    {
        _main.Nucleo.Cfg.Boletim.MesAtual = MesAtual.Trim();
        _main.Nucleo.Cfg.Boletim.ProximoMes = ProximoMes.Trim();
        _main.Nucleo.Cfg.Salvar();
        Recarregar();
    }

    [RelayCommand] private Task PuxarBoletins() => _main.PuxarBoletins();
}

/// <summary>Aba MSG: aviso para disparo em massa.</summary>
public partial class MsgVM : ObservableObject
{
    private readonly MainViewModel _main;
    [ObservableProperty] private string _b1 = "";
    [ObservableProperty] private string _b2 = "";
    [ObservableProperty] private string _b3 = "";
    [ObservableProperty] private string _imagem = "";
    public MsgVM(MainViewModel main) => _main = main;

    public void Recarregar()
    {
        var m = _main.Nucleo.Cfg.Msg;
        (B1, B2, B3, Imagem) = (m.B1, m.B2, m.B3, m.Imagem);
    }

    [RelayCommand]
    private void Salvar()
    {
        var m = _main.Nucleo.Cfg.Msg;
        (m.B1, m.B2, m.B3, m.Imagem) = (B1.Trim(), B2.Trim(), B3.Trim(), Imagem.Trim());
        _main.Nucleo.Cfg.Salvar();
        _main.Status("Mensagens salvas.");
    }
}
