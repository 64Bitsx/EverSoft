namespace EverSoft.Core.Dominio;

/// <summary>
/// Uma aba de horário, por exemplo "13h00 - 15h00" ou "13h00 - 15h00 - EAD".
/// Tem sempre 19 posições (os PCs do laboratório).
/// </summary>
public class Slot
{
    public const int NLinhas = 19;
    public const string CorPresencial = "#002060";
    public const string CorEad = "#9BC2E6";

    public string Nome { get; set; } = "";
    /// <summary>"presencial" ou "ead".</summary>
    public string Tipo { get; set; } = "presencial";
    public List<string> Matriculas { get; set; } = new();
    /// <summary>Marcação "J" (justificado / não entra nos lembretes) de cada posição.</summary>
    public List<bool> Marcas { get; set; } = new();

    public Slot() => Normalizar();

    public Slot(string nome, string tipo = "presencial") : this()
    {
        Nome = nome;
        Tipo = tipo;
    }

    public string Cor => Tipo == "ead" ? CorEad : CorPresencial;

    /// <summary>Quantos PCs estão ocupados (=COUNTA(A4:A22)).</summary>
    public int Usados => Matriculas.Count(m => !string.IsNullOrWhiteSpace(m));

    /// <summary>Garante exatamente 19 posições nas duas listas.</summary>
    public void Normalizar()
    {
        Matriculas = Ajustar(Matriculas ?? new(), "");
        Marcas = Ajustar(Marcas ?? new(), false);
    }

    private static List<T> Ajustar<T>(List<T> lista, T vazio)
    {
        var l = lista.Take(NLinhas).ToList();
        while (l.Count < NLinhas) l.Add(vazio);
        return l;
    }
}
