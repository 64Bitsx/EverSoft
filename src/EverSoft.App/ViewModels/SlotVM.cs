using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EverSoft.Core.Dominio;
using EverSoft.Core.Util;

namespace EverSoft.App.ViewModels;

/// <summary>Uma aba de horário: a lista de 19 PCs e os botões daquela turma.</summary>
public partial class SlotVM : ObservableObject
{
    private readonly MainViewModel _main;
    private bool _montando;
    public Slot Slot { get; }
    public string Nome => Slot.Nome;
    public string Tipo => Slot.Tipo == "ead" ? "EAD" : "PRESENCIAL";
    public string Cor => Slot.Cor;

    public ObservableCollection<LinhaVM> Linhas { get; } = new();
    [ObservableProperty] private LinhaVM? _selecionada;
    [ObservableProperty] private string _usados = "";
    // Números dos cartões de resumo no topo da turma
    [ObservableProperty] private int _qtdUsados;
    [ObservableProperty] private int _qtdSemStatus;
    [ObservableProperty] private int _qtdSumidos;
    [ObservableProperty] private int _qtdComentario;
    public int TotalPcs => Linhas.Count;

    public SlotVM(MainViewModel main, Slot slot)
    {
        _main = main;
        Slot = slot;
        Recarregar();
    }

    /// <summary>Recalcula as linhas (depois de uma sincronia ou gravação).</summary>
    public void Recarregar()
    {
        _montando = true;
        int? sel = Selecionada?.Indice;
        var dados = _main.Nucleo.Linhas(Slot);
        if (Linhas.Count != Slot.Matriculas.Count)
        {
            Linhas.Clear();
            for (int i = 0; i < Slot.Matriculas.Count; i++)
                Linhas.Add(new LinhaVM(this, i, Slot.Matriculas[i], Slot.Marcas[i], dados[i]));
        }
        else
        {
            for (int i = 0; i < Linhas.Count; i++)
            {
                Linhas[i].Matricula = Slot.Matriculas[i];
                Linhas[i].J = Slot.Marcas[i];
                if (!Equals(Linhas[i].Dados, dados[i])) Linhas[i].Dados = dados[i];
            }
        }
        Usados = $"{Slot.Usados} PCs usados";
        QtdUsados = Slot.Usados;
        QtdSemStatus = Linhas.Count(l => l.SemStatus);
        QtdSumidos = Linhas.Count(l => !l.Vazia && l.UltimaVermelha);
        QtdComentario = Linhas.Count(l => !l.Vazia && l.TemComentario);
        OnPropertyChanged(nameof(TotalPcs));
        _main.AtualizarBadges();
        _montando = false;
        if (sel is not null) Selecionada = Linhas[sel.Value];
    }

    partial void OnSelecionadaChanged(LinhaVM? value) => _main.MostrarAluno(this, value);

    internal void MarcaAlterada(LinhaVM linha, bool valor)
    {
        if (_montando) return;
        if (linha.Vazia && valor) { linha.J = false; return; }
        Slot.Marcas[linha.Indice] = valor;
        _main.Nucleo.Salvar();
    }

    internal void MatriculaAlterada(LinhaVM linha, string valor)
    {
        if (_montando) return;
        valor = (valor ?? "").Trim();
        if (valor == Slot.Matriculas[linha.Indice]) return;
        Slot.Matriculas[linha.Indice] = valor;
        // Reorganiza na hora: ordem alfabética e sem buracos no meio.
        Grade.OrdenarSlotPorNome(_main.Nucleo.Hub, Slot);
        _main.Nucleo.Salvar();
        // Adia para depois que a tabela terminar a edição.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Recarregar();
            int nova = Slot.Matriculas.FindIndex(m => Texto.Norm(m) == Texto.Norm(valor) && valor != "");
            if (nova >= 0) Selecionada = Linhas[nova];
        });
    }

    [RelayCommand] private Task Status() => _main.AbrirStatus(this);
    [RelayCommand] private Task Justificar() => _main.AbrirJustificar(this);
    [RelayCommand] private Task Coments() => _main.AbrirComents(this);
    [RelayCommand] private Task Bloqueio() => _main.AbrirBloqueio(this);
    [RelayCommand] private Task Info() => _main.AbrirInfo(this);
    [RelayCommand] private void AbrirSistema() => _main.AbrirSistemaEscola(Selecionada);
    internal void AbrirSistemaDaLinha(LinhaVM linha) => _main.AbrirSistemaEscola(linha);
    [RelayCommand] private Task CopiarNome() => _main.CopiarTexto(Selecionada?.Nome ?? "");
    [RelayCommand] private void AlternarJ() { if (Selecionada is { Vazia: false } l) l.J = !l.J; }
    [RelayCommand] private Task PreencheJustificados() => _main.TransferirParaJustifica(this);
    [RelayCommand] private Task Lembretes() => _main.EnviarLembretes(this);
}
