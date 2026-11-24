using EverSoft.Core.Dados;
using EverSoft.Core.Planilhas;
using EverSoft.Core.Util;

namespace EverSoft.Core.Dominio;

/// <summary>
/// Monta as abas de horário a partir da planilha "Grade de Horários" (macro PreencherMatriculas).
/// </summary>
public static class Grade
{
    public static string SanitizarNomeAba(string nome)
    {
        string n = nome.Trim().Replace(":", "h");
        foreach (char ch in "\\/?*[]") n = n.Replace(ch.ToString(), "");
        if (n.Length > 31) n = n[..31];
        return n == "" ? "Horario" : n;
    }

    public static string NomeAba(string cabecalho, string sufixo)
    {
        string n = SanitizarNomeAba(cabecalho) + sufixo;
        return n.Length > 31 ? n[..31] : n;
    }

    /// <summary>"07h45 - 09h40" vira 745 (para ordenar pela hora de início).</summary>
    public static int ChaveInicio(string nome)
    {
        int pos = nome.IndexOf('h');
        if (pos > 0)
        {
            string hh = nome[..pos];
            string mm = nome.Substring(pos + 1, Math.Min(2, nome.Length - pos - 1));
            if (hh.All(char.IsDigit))
                return int.Parse(hh) * 100 + (mm.Length > 0 && mm.All(char.IsDigit) ? int.Parse(mm) : 0);
        }
        return 0;
    }

    /// <summary>Grade EAD: linhas 4, 6, 8... que têm laboratório na coluna C (no máximo 19).</summary>
    public static List<int> BuscarLinhasEad(Planilha pl)
    {
        var lista = new List<int>();
        int r = 4;
        while (lista.Count < Slot.NLinhas)
        {
            string b = Texto.Txt(pl.V(r, 2));
            if (b.StartsWith("Total", StringComparison.Ordinal)) break;
            if (Texto.Txt(pl.V(r, 3)) == "") break;
            lista.Add(r);
            r += 2;
        }
        return lista;
    }

    /// <summary>Uma aba por horário encontrado na linha de cabeçalho. Colunas repetidas: a última vence.</summary>
    public static List<Slot> ProcessarGrade(Planilha pl, IReadOnlyList<int> linhasOrigem, string sufixo, string tipo, int cabecalho = 3)
    {
        var slots = new Dictionary<string, Slot>(StringComparer.OrdinalIgnoreCase);
        var ordem = new List<string>();
        int ult = pl.UltimaColunaDaLinha(cabecalho);
        for (int col = 1; col <= ult; col++)
        {
            string h = Texto.Txt(pl.V(cabecalho, col));
            if (h == "" || !h.Contains(':')) continue;
            string nome = NomeAba(h, sufixo);
            if (!slots.TryGetValue(nome, out var s))
            {
                s = new Slot(nome, tipo);
                slots[nome] = s;
                ordem.Add(nome);
            }
            for (int i = 0; i < Math.Min(linhasOrigem.Count, Slot.NLinhas); i++)
                s.Matriculas[i] = Texto.Txt(pl.V(linhasOrigem[i], col));
        }
        return ordem.Select(n => slots[n]).ToList();
    }

    /// <summary>Ordena pela hora de início; a EAD vem logo depois da presencial do mesmo horário.</summary>
    public static List<Slot> OrdenarAbas(IEnumerable<Slot> slots) =>
        slots.OrderBy(s => ChaveInicio(s.Nome) * 2 + (s.Nome.EndsWith(" - EAD", StringComparison.Ordinal) ? 1 : 0)).ToList();

    /// <summary>
    /// Ordena os alunos de A a Z e deixa as posições vazias no final.
    /// A marca "J" acompanha a matrícula, para não passar para outro aluno.
    /// </summary>
    public static void OrdenarSlotPorNome(DataHub hub, Slot slot)
    {
        var itens = new List<(string Nome, string Mat, bool Marca)>();
        for (int i = 0; i < slot.Matriculas.Count; i++)
        {
            string m = (slot.Matriculas[i] ?? "").Trim();
            if (m == "") continue;
            itens.Add((hub.NomePorMatricula(m).Trim(), m, i < slot.Marcas.Count && slot.Marcas[i]));
        }
        // Matrículas sem nome conhecido ficam depois das que foram encontradas.
        var ordenados = itens
            .OrderBy(x => x.Nome == "")
            .ThenBy(x => x.Nome.ToLowerInvariant(), StringComparer.Ordinal)
            .ThenBy(x => x.Mat.ToLowerInvariant(), StringComparer.Ordinal)
            .ToList();
        slot.Matriculas = ordenados.Select(x => x.Mat).ToList();
        slot.Marcas = ordenados.Select(x => x.Marca).ToList();
        slot.Normalizar();
    }
}
