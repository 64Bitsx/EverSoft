using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace EverSoft.App.Views;

/// <summary>
/// Janela de diálogo simples: título, corpo empilhado e botões no rodapé.
/// Os formulários (Status, Coments, Justifica...) são montados com ela.
/// </summary>
public class Dialogo : Window
{
    protected readonly StackPanel Corpo = new() { Spacing = 6 };
    private readonly StackPanel _botoes = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
    public bool Confirmado { get; protected set; }

    public Dialogo(string titulo, double largura = 480)
    {
        Title = titulo;
        Width = largura;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Content = new DockPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                Ancorar(new Border { Child = _botoes, Margin = new Thickness(0, 16, 0, 0) }, Dock.Bottom),
                new ScrollViewer { Content = Corpo, MaxHeight = 640 },
            },
        };
    }

    private static Control Ancorar(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }

    // ---------- peças para montar os formulários
    public TextBlock Rotulo(string texto) =>
        Add(new TextBlock { Text = texto.ToUpperInvariant(), Classes = { "rotulo" }, Margin = new Thickness(0, 6, 0, 0) });

    public TextBlock Titulo(string texto) =>
        Add(new TextBlock { Text = texto, FontSize = 17, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });

    public TextBlock Texto(string texto, string? cor = null) =>
        Add(new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse(cor ?? "#334155") });

    public TextBox Campo(string valor = "", int linhas = 1, bool maiusculas = false)
    {
        var t = new TextBox { Text = valor, AcceptsReturn = linhas > 1, TextWrapping = TextWrapping.Wrap, MinHeight = linhas > 1 ? 22 * linhas : 0 };
        if (maiusculas) t.TextChanged += (_, _) =>
        {
            if (t.Text is { } s && s != s.ToUpperInvariant()) { int c = t.CaretIndex; t.Text = s.ToUpperInvariant(); t.CaretIndex = c; }
        };
        return Add(t);
    }

    public CheckBox Check(string texto, bool marcado = false) => Add(new CheckBox { Content = texto, IsChecked = marcado });

    public T Add<T>(T c) where T : Control { Corpo.Children.Add(c); return c; }

    public Button Botao(string texto, Action acao, string? classe = null)
    {
        var b = new Button { Content = texto, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (classe is not null) b.Classes.Add(classe);
        b.Click += (_, _) => acao();
        _botoes.Children.Add(b);
        return b;
    }

    public void Fechar(bool ok) { Confirmado = ok; Close(); }

    // ---------- caixas prontas
    public static async Task Mensagem(Window dono, string texto, string titulo = "EverSoft")
    {
        var d = new Dialogo(titulo, 460);
        d.Texto(texto);
        d.Botao("OK", () => d.Fechar(true), "azul").IsDefault = true;
        await d.ShowDialog(dono);
    }

    public static async Task<bool> Confirmar(Window dono, string texto, string titulo = "EverSoft")
    {
        var d = new Dialogo(titulo, 520);
        d.Texto(texto);
        d.Botao("Cancelar", () => d.Fechar(false)).IsCancel = true;
        d.Botao("OK", () => d.Fechar(true), "azul").IsDefault = true;
        await d.ShowDialog(dono);
        return d.Confirmado;
    }
}
