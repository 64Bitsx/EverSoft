using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EverSoft.Core.Dominio;

namespace EverSoft.App.ViewModels;

/// <summary>
/// Uma linha da grade de alunos (um PC do laboratório).
///
/// DICA: "partial" + [ObservableProperty] gera sozinho a propriedade com aviso de mudança,
/// para a tela se atualizar quando o valor muda.
/// </summary>
public partial class LinhaVM : ObservableObject
{
    private readonly SlotVM _dono;
    public int Indice { get; }
    public int Pc => Indice + 1;

    [ObservableProperty] private bool _j;
    [ObservableProperty] private string _matricula = "";
    [ObservableProperty] private LinhaAluno _dados = new();

    public LinhaVM(SlotVM dono, int indice, string matricula, bool j, LinhaAluno dados)
    {
        _dono = dono;
        Indice = indice;
        _matricula = matricula;
        _j = j;
        _dados = dados;
    }

    // Atalhos para a tabela.
    public string Nome => Dados.Nome;
    public string Materia => Dados.Materia;
    public string Aula => Dados.Aula;
    public string Pratica => Dados.Pratica;
    public string Apostila => Dados.Apostila;
    public string R => Dados.R;
    public string Ultima => Dados.Ultima;
    public string APagar => Dados.APagar;
    public string Sit => Dados.Sit;
    public string Inicio => Dados.Inicio;
    /// <summary>true quando o aluno tem comentário, conquista ou pontos na planilha Coments.</summary>
    public bool TemComentario => Dados.Coment == true;
    public bool Vazia => string.IsNullOrWhiteSpace(Matricula);

    public IBrush FundoSit => LinhaAluno.CoresDaSituacao(Sit) is { } c ? Brush.Parse(c.Fundo) : Brushes.Transparent;
    public IBrush TextoSit => LinhaAluno.CoresDaSituacao(Sit) is { } c ? Brush.Parse(c.Texto) : Brush.Parse("#1E293B");

    /// <summary>Aluno sem status fica com a linha inteira laranja.</summary>
    public bool SemStatus => !Vazia && Dados.Nome != "" && string.IsNullOrWhiteSpace(Dados.Sit);
    public IBrush FundoLinha => SemStatus ? Brush.Parse("#FDBA74") : Brushes.Transparent;

    [RelayCommand] private void AbrirSistema() => _dono.AbrirSistemaDaLinha(this);

    partial void OnDadosChanged(LinhaAluno value)
    {
        foreach (var p in new[] { nameof(Nome), nameof(Materia), nameof(Aula), nameof(Pratica), nameof(Apostila), nameof(R),
                     nameof(Ultima), nameof(APagar), nameof(Sit), nameof(Inicio), nameof(TemComentario), nameof(FundoSit), nameof(TextoSit), nameof(SemStatus), nameof(FundoLinha) })
            OnPropertyChanged(p);
    }

    // Quando a pessoa marca/desmarca o J ou digita uma matrícula, avisa a aba.
    partial void OnJChanged(bool value) => _dono.MarcaAlterada(this, value);
    partial void OnMatriculaChanged(string value)
    {
        OnPropertyChanged(nameof(Vazia));
        _dono.MatriculaAlterada(this, value);
    }
}
