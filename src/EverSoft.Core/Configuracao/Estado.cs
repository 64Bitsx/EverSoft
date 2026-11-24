using System.Text.Json;
using EverSoft.Core.Dominio;
using EverSoft.Core.Util;

namespace EverSoft.Core.Configuracao;

/// <summary>
/// O que o app guarda entre uma abertura e outra (estado.json): abas de horário,
/// lista da Justifica, Agenda, Disparo... Mesmo formato do EverSoft.py.
/// </summary>
public class Estado
{
    public List<Slot> Slots { get; set; } = new();
    public List<RegistroJustifica> Justifica { get; set; } = new();
    public List<ItemAgenda> Agenda { get; set; } = new();
    public List<ItemDisparo> Disparo { get; set; } = new();
    public string HoraLembrete { get; set; } = "";
    public string AbaAtiva { get; set; } = "";

    private readonly object _trava = new();
    private string _caminho = "";

    public static Estado Carregar(string caminho)
    {
        Estado e = new();
        try
        {
            if (File.Exists(caminho))
                e = JsonSerializer.Deserialize<Estado>(File.ReadAllText(caminho), Json.Opcoes) ?? new Estado();
        }
        catch (Exception ex)
        {
            Log.Erro($"estado.json inválido: {ex.Message}");
            e = new Estado();
        }
        e._caminho = caminho;
        foreach (var s in e.Slots) s.Normalizar();
        return e;
    }

    public void Salvar()
    {
        lock (_trava)
        {
            Json.Salvar(_caminho, this);
        }
    }
}

/// <summary>Uma linha da aba Justifica.</summary>
public class RegistroJustifica
{
    public string Nome { get; set; } = "";
    public string Justificativa { get; set; } = "";
    public string Data { get; set; } = "";
    public string Horario { get; set; } = "";
    public string Frequencia { get; set; } = "";
}

/// <summary>Uma linha da aba Agenda (lembretes).</summary>
public class ItemAgenda
{
    public string Nome { get; set; } = "";
    public string Numero { get; set; } = "";
    public string Mensagem { get; set; } = "";
    public string Apostila { get; set; } = "";
    public string Aula { get; set; } = "";
    public string Novatos { get; set; } = "";
}

/// <summary>Uma linha da aba Disparo (boletins).</summary>
public class ItemDisparo
{
    public string Nome { get; set; } = "";
    public string Numero { get; set; } = "";
}
