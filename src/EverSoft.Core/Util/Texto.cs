using System.Globalization;
using System.Text;

namespace EverSoft.Core.Util;

/// <summary>
/// Funções de texto usadas no app todo.
/// Equivalem às funções norm(), txt(), proper(), parse_data()... do EverSoft.py.
///
/// DICA C# (vindo do Python): uma classe "static" é só um agrupador de funções,
/// como um módulo Python. Você chama assim: Texto.Norm(valor).
/// </summary>
public static class Texto
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// Chave de comparação (como o MATCH(...,0) do Excel): sem diferenciar maiúsculas,
    /// sem espaços nas pontas. A matrícula numérica 123.0 vira "123".
    /// </summary>
    public static string Norm(object? v)
    {
        if (v is null) return "";
        string s = v switch
        {
            double d when EhInteiro(d) => ((long)d).ToString(Inv),
            double d => d.ToString("R", Inv),
            bool b => b ? "true" : "false",
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", Inv),
            _ => Convert.ToString(v, Inv) ?? "",
        };
        return s.Replace(' ', ' ').Trim().ToLowerInvariant();
    }

    /// <summary>Valor de célula para texto de exibição (datas em dd/MM/yyyy, decimais com vírgula).</summary>
    public static string Txt(object? v)
    {
        switch (v)
        {
            case null: return "";
            case bool b: return b ? "VERDADEIRO" : "FALSO";
            case double d when EhInteiro(d): return ((long)d).ToString(Inv);
            case double d: return d.ToString("G10", Inv).Replace('.', ',');
            case DateTime dt: return dt.ToString("dd/MM/yyyy", Inv);
            default: return (Convert.ToString(v, Inv) ?? "").Replace(' ', ' ').Trim();
        }
    }

    /// <summary>PROPER() do Excel (igual ao str.title() do Python): "JOÃO D'ARC" vira "João D'Arc".</summary>
    public static string Proper(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        bool anteriorLetra = false;
        foreach (char c in s)
        {
            bool letra = char.IsLetter(c);
            sb.Append(letra ? (anteriorLetra ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)) : c);
            anteriorLetra = letra;
        }
        return sb.ToString();
    }

    /// <summary>Converte célula (DateTime, número de série do Excel ou texto dd/mm/aaaa) em data.</summary>
    public static DateOnly? ParseData(object? v)
    {
        switch (v)
        {
            case null: return null;
            case DateTime dt: return DateOnly.FromDateTime(dt);
            case double d when d > 20000: return DateOnly.FromDateTime(new DateTime(1899, 12, 30).AddDays(d));
            case double: return null;
            case bool: return null;
        }
        string s = (Convert.ToString(v, Inv) ?? "").Trim();
        if (s == "") return null;
        string dez = s.Length > 10 ? s[..10] : s;
        foreach (var (fmt, texto) in new[] { ("dd/MM/yyyy", dez), ("dd/MM/yy", s), ("yyyy-MM-dd", dez), ("dd-MM-yyyy", dez) })
        {
            if (DateTime.TryParseExact(texto, fmt, Inv, DateTimeStyles.None, out var r))
                return DateOnly.FromDateTime(r);
        }
        return null;
    }

    /// <summary>Letra da coluna para número: "A" = 1, "AA" = 27.</summary>
    public static int ColIdx(string letras)
    {
        int n = 0;
        foreach (char ch in letras.ToUpperInvariant()) n = n * 26 + (ch - 'A' + 1);
        return n;
    }

    /// <summary>"13H00 - 15H00" vira "13h00-15h00".</summary>
    public static string NormalizarHorario(object? s) =>
        (Convert.ToString(s, Inv) ?? "").Trim().ToLowerInvariant().Replace(" ", "");

    public static string LimparNumero(string numero)
    {
        foreach (char ch in "() -") numero = numero.Replace(ch.ToString(), "");
        return numero;
    }

    /// <summary>
    /// Limpa a lista de contatos (macros FaltantesLembrete/ColetaBoletim):
    /// tira ( ) - e espaços, coloca o 9 em números de 10 dígitos, descarta números curtos
    /// e remove números repetidos (fica o primeiro).
    /// </summary>
    public static List<(string Nome, string Telefone)> TratarContatos(IEnumerable<(string Nome, string Telefone)> pares)
    {
        var vistos = new HashSet<string>();
        var saida = new List<(string, string)>();
        foreach (var (nome, telBruto) in pares)
        {
            string tel = Txt(telBruto);
            if (tel == "") continue;
            tel = LimparNumero(tel);
            if (tel.Length == 10) tel = tel[..2] + "9" + tel[2..];
            if (tel.Length < 10) continue;
            if (!vistos.Add(tel)) continue;
            saida.Add((nome, tel));
        }
        return saida;
    }

    /// <summary>Junta espaços repetidos e tira espaços das pontas.</summary>
    public static string ColapsarEspacos(string s) =>
        string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    internal static bool EhInteiro(double d) =>
        !double.IsNaN(d) && !double.IsInfinity(d) && Math.Floor(d) == d && Math.Abs(d) < 1e15;
}
