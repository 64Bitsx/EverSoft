using System.Text.RegularExpressions;

namespace EverSoft.Core.Dominio;

/// <summary>
/// Faixas de horário das aulas: qual turma está acontecendo agora, qual pasta de áudio usar etc.
/// Cada tabela é avaliada em ordem (a primeira que bater vence), igual ao If/ElseIf do VBA.
/// </summary>
public static class Faixas
{
    // (início, fim exclusivo ou null, valor). Horas no formato hhmm (ex.: 1310 = 13h10).
    private static readonly (int Ini, int? Fim, string Valor)[] RotuloSemana =
        { (700, 820, "07:45h"), (850, 930, "09:40h"), (1010, 1320, "13:00h"), (1300, 1520, "15:00h"), (1500, 1720, "17:00h"), (1700, null, "19:00h") };
    private static readonly (int, int?, string)[] RotuloSabado =
        { (700, 810, "07:40h"), (830, 930, "09:30h"), (1020, 1120, "11:20h"), (1150, 1310, "13:10h"), (1400, 1500, "15:00h"), (1700, null, "19:00h") };
    private static readonly (int, int?, string)[] AudioSemana =
        { (700, 810, "0745"), (850, 930, "0940"), (1010, 1320, "1300"), (1300, 1520, "1500"), (1500, 1720, "1700"), (1700, null, "1900") };
    private static readonly (int, int?, string)[] AudioSabado =
        { (700, 810, "0740"), (830, 930, "0930"), (1020, 1120, "1120"), (1150, 1310, "1310"), (1400, 1500, "1500"), (1700, null, "1900") };
    // Estas duas usam minutos desde a meia-noite (465 = 07h45).
    private static readonly (int, int?, string)[] JustSemana =
        { (465, 580, "07:45 ÀS 09:40"), (580, 700, "09:40 ÀS 11:40"), (780, 900, "13:00 ÀS 15:00"), (900, 1020, "15:00 ÀS 17:00"), (1020, 1140, "17:00 ÀS 19:00"), (1140, 1260, "19:00 ÀS 21:00") };
    private static readonly (int, int?, string)[] JustSabado =
        { (460, 570, "07:40 ÀS 09:30"), (570, 680, "09:30 ÀS 11:20"), (680, 790, "11:20 ÀS 13:10"), (790, 900, "13:10 ÀS 15:00") };

    public static readonly string[] Dias =
        { "SEGUNDA FEIRA", "TERÇA FEIRA", "QUARTA FEIRA", "QUINTA FEIRA", "SEXTA FEIRA", "SÁBADO", "DOMINGO" };

    private static string? Buscar((int Ini, int? Fim, string Valor)[] tabela, int valor)
    {
        foreach (var (ini, fim, v) in tabela)
            if (valor >= ini && (fim is null || valor < fim)) return v;
        return null;
    }

    private static int Hhmm(DateTime agora) => agora.Hour * 100 + agora.Minute;
    public static bool EhSabado(DateTime agora) => agora.DayOfWeek == DayOfWeek.Saturday;

    /// <summary>Hora que vai nos lembretes ("13:00h"); null = não muda.</summary>
    public static string? RotuloHoraLembrete(DateTime agora) =>
        Buscar(EhSabado(agora) ? RotuloSabado : RotuloSemana, Hhmm(agora));

    /// <summary>Pasta de áudio da turma atual: ("Semana" ou "Sabado", "0745"...). Null no domingo ou fora de faixa.</summary>
    public static (string Dia, string Faixa)? FaixaAudio(DateTime agora)
    {
        if (agora.DayOfWeek == DayOfWeek.Sunday) return null;
        bool sab = EhSabado(agora);
        string? f = Buscar(sab ? AudioSabado : AudioSemana, Hhmm(agora));
        return f is null ? null : (sab ? "Sabado" : "Semana", f);
    }

    /// <summary>Dia da semana em maiúsculas ("SEGUNDA FEIRA").</summary>
    public static string DiaSemanaTexto(DateTime agora) => Dias[((int)agora.DayOfWeek + 6) % 7];

    /// <summary>Faixa da Justifica ("13:00 ÀS 15:00"); vazio se fora de horário.</summary>
    public static string FaixaHorarioJustifica(DateTime agora) =>
        Buscar(EhSabado(agora) ? JustSabado : JustSemana, agora.Hour * 60 + agora.Minute) ?? "";

    /// <summary>
    /// Todas as faixas da Justifica daquele dia (sábado tem grade própria).
    /// Usado para escolher a faixa na mão quando você esqueceu de enviar os justificados na hora,
    /// sem precisar mexer no relógio do Windows.
    /// </summary>
    public static List<string> FaixasDoDia(DateTime dia) =>
        (EhSabado(dia) ? JustSabado : JustSemana).Select(f => f.Item3).ToList();

    /// <summary>"07:45 ÀS 09:40" vira "07h45-09h40".</summary>
    public static string CodigoFaixa(string faixa)
    {
        if (!Regex.IsMatch(faixa ?? "", @"^\d\d:\d\d ÀS \d\d:\d\d$")) return "";
        return faixa!.Replace(" ÀS ", "-").Replace(":", "h");
    }
}
