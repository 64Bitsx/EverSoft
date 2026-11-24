using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace EverSoft.App.Views;

/// <summary>
/// Tela de carregamento com o logo do EverSoft (o PNG vai embutido dentro do .exe).
/// Aparece na abertura e enquanto a Automação TEC ou a Sincronia trabalham.
/// </summary>
public class SplashWindow : Window
{
    private readonly TextBlock _mensagem;

    public SplashWindow(string mensagem = "Carregando...")
    {
        Title = "EverSoft";
        Icon = Icone();
        Width = 460;
        Height = 430;
        CanResize = false;
        SystemDecorations = SystemDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush.Parse("#0B1B33");
        Topmost = false;

        _mensagem = new TextBlock
        {
            Text = mensagem, Foreground = Brush.Parse("#CBD5E1"), FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, MaxWidth = 400, Height = 40,
        };
        Content = new Border
        {
            BorderBrush = Brush.Parse("#14B8A6"), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(14),
            Child = new StackPanel
            {
                Margin = new Thickness(24), Spacing = 10, VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new Image { Source = Logo(), Width = 250, Height = 250, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = "EverSoft", Foreground = Brushes.White, FontSize = 24, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center },
                    _mensagem,
                    new ProgressBar { IsIndeterminate = true, Height = 6, Width = 300, Foreground = Brush.Parse("#14B8A6"), Background = Brush.Parse("#1E3A5F") },
                },
            },
        };
    }

    public string Mensagem { get => _mensagem.Text ?? ""; set => _mensagem.Text = value; }

    public static Bitmap Logo() => new(AssetLoader.Open(new Uri("avares://EverSoft/Assets/eversoft-logo.png")));
    public static WindowIcon Icone() => new(AssetLoader.Open(new Uri("avares://EverSoft/Assets/eversoft-icon.ico")));
}
