using EverSoft.Core.Dados;
using EverSoft.Core.Util;

namespace EverSoft.Core.Dominio;

/// <summary>
/// Tudo o que aparece numa linha da aba de horário (as fórmulas da antiga aba ESCRAVO).
///
/// DICA C#: "record" é uma classe feita para guardar dados, parecida com um @dataclass do Python.
/// </summary>
public record LinhaAluno
{
    public string Matricula { get; init; } = "";
    public string Nome { get; init; } = "";
    public string Materia { get; init; } = "";
    public string Aula { get; init; } = "";
    public string Pratica { get; init; } = "";
    public string Apostila { get; init; } = "";
    /// <summary>"X" quando o aluno tem reposição pendente.</summary>
    public string R { get; init; } = "";
    public string Ultima { get; init; } = "";
    /// <summary>Dias desde a última aula (null se não há data).</summary>
    public int? DiasSemAula { get; init; }
    /// <summary>Mais de 30 dias sem vir: a célula ÚLTIMA fica vermelha.</summary>
    public bool SumidoHaMuitoTempo => DiasSemAula > 30;
    public string APagar { get; init; } = "";
    public string Sit { get; init; } = "";
    public string Inicio { get; init; } = "";
    /// <summary>true/false se tem comentário; null quando o aluno nem está na planilha Coments.</summary>
    public bool? Coment { get; init; }

    public bool Vazia => Matricula == "";

    public static LinhaAluno Calcular(DataHub hub, string matricula, DateOnly hoje, Func<string, string?>? inicioCap2 = null)
    {
        string mat = (matricula ?? "").Trim();
        if (mat == "") return new LinhaAluno();
        string nome = hub.NomePorMatricula(mat);
        var linha = new LinhaAluno
        {
            Matricula = mat,
            Nome = nome,
            Materia = hub.MateriaAtual(mat),
            Aula = hub.AulaAtual(mat),
            Ultima = hub.UltimaPresenca(mat, hoje),
            DiasSemAula = hub.DiasDesdeUltimaPresenca(mat, hoje),
            APagar = hub.APagar(mat),
        };
        if (nome == "") return linha;
        var (pratica, apostila) = hub.BloqueioFlags(nome);
        string sit = hub.Situacao(nome);
        // Quem já passou pelo Cap 2 mostra a data em que entrou nele (mesmo que o status mude depois);
        // os demais, a Base Prepara.
        string inicio = inicioCap2?.Invoke(nome) ?? hub.Inicio(nome);
        return linha with
        {
            Pratica = pratica,
            Apostila = apostila,
            R = hub.TemReposicao(nome) ? "X" : "",
            Sit = sit,
            Inicio = inicio,
            Coment = hub.TemComentario(nome),
        };
    }

    public static bool EhCap2(string sit) => Texto.Norm(sit).StartsWith("cap 2");

    /// <summary>Cores da linha conforme a situação: (fundo, texto) ou null para o padrão.</summary>
    public static (string Fundo, string Texto)? CoresDaSituacao(string sit)
    {
        string s = Texto.Norm(sit);
        if (s == "ok") return ("#1D6FD8", "#FFFFFF");
        if (s is "cap 1" or "cap 2" or "cap 3" or "cap 4") return ("#000000", "#FFFFFF");
        if (s == "atrasado") return ("#E5383B", "#FFFFFF");
        if (s == "adiantado") return ("#7C3AED", "#FFFFFF");
        return null;
    }
}
