using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using EverSoft.App.ViewModels;
using EverSoft.App.Views;

[assembly: AvaloniaTestApplication(typeof(EverSoft.Tests.AppDeTeste))]

namespace EverSoft.Tests;

public class AppDeTeste
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<EverSoft.App.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>Abre a janela de verdade (sem mostrar na tela) e confere se tudo aparece.</summary>
public class TelaTests : IDisposable
{
    private readonly Cenario _c = new();
    public void Dispose() => _c.Dispose();

    private static void Esperar(Func<bool> pronto)
    {
        var fim = DateTime.Now.AddSeconds(15);
        while (!pronto() && DateTime.Now < fim) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); }
    }

    [AvaloniaFact]
    public void Janela_abre_com_as_abas_e_mostra_o_aluno_selecionado()
    {
        var nucleo = _c.NovoNucleo(new DateTime(2026, 11, 23, 13, 30, 0));
        var w = new MainWindow(nucleo);
        w.Show();
        var vm = w.ViewModel;
        Esperar(() => vm.Abas.Count > 0 && !vm.Ocupado);

        Assert.Equal(new[] { "07h45 - 09h40", "13h00 - 15h00", "Justifica", "Agenda", "Boletim", "MSG" }, vm.Abas.Select(a => a.Titulo));
        vm.AbaSelecionada = vm.Abas[1];
        var slot = (SlotVM)vm.Abas[1].Conteudo;
        Assert.Equal("2 PCs usados", slot.Usados);
        slot.Selecionada = slot.Linhas[0];
        Assert.Equal("Ana Lima", vm.Painel.Nome);
        Assert.Equal("(83) 9876-5432", vm.Painel.Tel1);
        Assert.Equal("83988887777", vm.Painel.Tel2);
        Assert.True(vm.MostrarPainel);

        // Marcar J grava no estado
        slot.Linhas[1].J = true;
        Assert.True(nucleo.Slot("13h00 - 15h00")!.Marcas[1]);

        vm.AbaSelecionada = vm.Abas.First(a => a.Id == "__just");
        Assert.False(vm.MostrarPainel);
        Assert.Equal("13:00 ÀS 15:00", vm.Justifica.Faixa);
        w.Close();
    }

    [AvaloniaFact]
    public void Editar_matricula_na_tabela_reordena_e_seleciona_o_aluno()
    {
        var nucleo = _c.NovoNucleo(new DateTime(2026, 11, 23, 13, 30, 0));
        var w = new MainWindow(nucleo);
        w.Show();
        var vm = w.ViewModel;
        Esperar(() => vm.Abas.Count > 0 && !vm.Ocupado);
        var slot = (SlotVM)vm.Abas.First(a => a.Id == "13h00 - 15h00").Conteudo;

        slot.Linhas[2].Matricula = "102";                    // Bruno entra no PC 3
        Esperar(() => slot.Selecionada?.Matricula == "102");
        Assert.Equal(new[] { "101", "102", "103" }, slot.Slot.Matriculas.Take(3)); // Ana, Bruno, Carla
        Assert.Equal("Bruno Reis", slot.Selecionada!.Nome);
        w.Close();
    }
}
