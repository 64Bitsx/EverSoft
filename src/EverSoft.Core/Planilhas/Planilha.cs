namespace EverSoft.Core.Planilhas;

/// <summary>
/// Os valores de uma aba, com linhas e colunas começando em 1 (como no Excel).
/// Cada célula é null, string, double, bool ou DateTime.
/// </summary>
public class Planilha
{
    public List<List<object?>> Linhas { get; }
    public string Nome { get; }
    public int NLinhas => Linhas.Count;
    public int NColunas => Linhas.Count == 0 ? 0 : Linhas.Max(l => l.Count);

    public Planilha(List<List<object?>> linhas, string nome = "")
    {
        Linhas = linhas;
        Nome = nome;
    }

    /// <summary>Valor da célula (linha r, coluna c). Fora da área devolve null.</summary>
    public object? V(int r, int c)
    {
        if (r < 1 || c < 1 || r > Linhas.Count) return null;
        var linha = Linhas[r - 1];
        return c <= linha.Count ? linha[c - 1] : null;
    }

    /// <summary>Igual a Cells(r, Columns.Count).End(xlToLeft).Column do VBA.</summary>
    public int UltimaColunaDaLinha(int r)
    {
        if (r < 1 || r > Linhas.Count) return 0;
        var linha = Linhas[r - 1];
        for (int i = linha.Count; i > 0; i--)
        {
            var v = linha[i - 1];
            if (v is not null && !(v is string s && s == "")) return i;
        }
        return 0;
    }
}

/// <summary>O arquivo está aberto por outra pessoa e não pode ser gravado agora.</summary>
public class ArquivoEmUsoException : Exception
{
    public ArquivoEmUsoException(string mensagem, Exception? interna = null) : base(mensagem, interna) { }
}

/// <summary>A aba pedida não existe no arquivo.</summary>
public class AbaNaoEncontradaException : Exception
{
    public AbaNaoEncontradaException(string mensagem) : base(mensagem) { }
}
