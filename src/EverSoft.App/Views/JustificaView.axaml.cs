using Avalonia.Controls;
using Avalonia.Input;
using EverSoft.App.ViewModels;

namespace EverSoft.App.Views;

public partial class JustificaView : UserControl
{
    public JustificaView() => InitializeComponent();

    private void Grade_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is JustificaVM vm) vm.EditarCommand.Execute(null);
    }
}
