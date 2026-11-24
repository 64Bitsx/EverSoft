namespace EverSoft.Core.Automacao;

public static class Telefone
{
    /// <summary>
    /// Número no formato que o WhatsApp entende: só dígitos e com o 55 do Brasil.
    /// "83991343421" vira "5583991343421"; quem já tem 55 (12 ou 13 dígitos) fica igual.
    /// </summary>
    public static string ParaWhatsApp(string numero)
    {
        string d = new(numero.Where(char.IsDigit).ToArray());
        if (d.StartsWith("0")) d = d.TrimStart('0');
        if (d.Length is 10 or 11) d = "55" + d;
        return d;
    }
}
