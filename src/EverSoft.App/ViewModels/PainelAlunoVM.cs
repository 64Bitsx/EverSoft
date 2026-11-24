using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using EverSoft.Core.Dominio;

namespace EverSoft.App.ViewModels;

/// <summary>Painel da direita: foto e dados do aluno selecionado.</summary>
public partial class PainelAlunoVM : ObservableObject
{
    [ObservableProperty] private string _nome = "Nenhum aluno selecionado";
    [ObservableProperty] private string _matricula = "—";
    [ObservableProperty] private string _materia = "—";
    [ObservableProperty] private string _ultima = "—";
    [ObservableProperty] private string _tel1 = "—";
    [ObservableProperty] private string _tel2 = "—";
    [ObservableProperty] private string _tel3 = "—";
    [ObservableProperty] private string _insta = "—";
    [ObservableProperty] private string _situacao = "";
    [ObservableProperty] private IBrush _fundoSituacao = Brushes.Transparent;
    [ObservableProperty] private IBrush _textoSituacao = Brushes.Gray;
    [ObservableProperty] private Bitmap? _foto;
    [ObservableProperty] private string _avisoFoto = "Selecione um aluno";
    [ObservableProperty] private bool _temAluno;

    private readonly Dictionary<string, Bitmap?> _fotos = new();
    private string _nomeAtual = "";

    public void Limpar()
    {
        TemAluno = false;
        _nomeAtual = "";
        Nome = "Nenhum aluno selecionado";
        Matricula = Materia = Ultima = Tel1 = Tel2 = Tel3 = Insta = "—";
        Situacao = "";
        FundoSituacao = Brushes.Transparent;
        Foto = null;
        AvisoFoto = "Selecione um aluno";
    }

    public void Mostrar(Core.Servicos.Nucleo nucleo, LinhaAluno linha)
    {
        if (linha.Nome == "") { Limpar(); if (linha.Matricula != "") { Matricula = linha.Matricula; Nome = "Matrícula não encontrada"; } return; }
        TemAluno = true;
        Nome = linha.Nome;
        Matricula = linha.Matricula;
        Materia = linha.Materia == "" ? "—" : linha.Materia + (linha.Aula == "" ? "" : $" · {linha.Aula}");
        Ultima = linha.Ultima == "" ? "—" : linha.Ultima;
        var (t1, t2, t3, insta) = nucleo.Hub.Telefones(linha.Nome);
        (Tel1, Tel2, Tel3, Insta) = (t1, t2, t3, insta);
        Situacao = linha.Sit == "" ? "SEM STATUS" : linha.Sit.ToUpperInvariant();
        var cores = LinhaAluno.CoresDaSituacao(linha.Sit);
        FundoSituacao = cores is { } c ? Brush.Parse(c.Fundo) : Brush.Parse("#E2E8F0");
        TextoSituacao = cores is { } c2 ? Brush.Parse(c2.Texto) : Brush.Parse("#64748B");
        if (_nomeAtual != linha.Nome) CarregarFoto(nucleo, linha.Nome);
        _nomeAtual = linha.Nome;
    }

    /// <summary>A foto vem da rede: carrega em segundo plano para a tela não travar.</summary>
    private void CarregarFoto(Core.Servicos.Nucleo nucleo, string nome)
    {
        if (_fotos.TryGetValue(nome, out var pronta)) { Foto = pronta; AvisoFoto = pronta is null ? "(sem foto)" : ""; return; }
        Foto = null;
        AvisoFoto = "Carregando foto...";
        Task.Run(() =>
        {
            Bitmap? bmp = null;
            try
            {
                string? caminho = nucleo.FotoAluno(nome);
                if (caminho is not null)
                {
                    using var fs = File.OpenRead(caminho);
                    bmp = Bitmap.DecodeToWidth(fs, 260);
                }
            }
            catch { /* sem foto */ }
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _fotos[nome] = bmp;
                if (_nomeAtual == nome) { Foto = bmp; AvisoFoto = bmp is null ? "(sem foto)" : ""; }
            });
        });
    }
}
