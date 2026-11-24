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
    public string APagar { get; init; } = "";
    public string Sit { get; init; } = "";
    public string Inicio { get; init; } = "";
    /// <summary>true/false se tem comentário; null quando o aluno nem está na planilha Coments.</summary>
    public bool? Coment { get; init; }

    public bool Vazia => Matricula == "";

    public static LinhaAluno Calcular(DataHub hub, string matricula, DateOnly hoje)
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
            APagar = hub.APagar(mat),
        };
        if (nome == "") return linha;
        var (pratica, apostila) = hub.BloqueioFlags(nome);
        return linha with
        {
            Pratica = pratica,
            Apostila = apostila,
            R = hub.TemReposicao(nome) ? "X" : "",
            Sit = hub.Situacao(nome),
            Inicio = hub.Inicio(nome),
            Coment = hub.TemComentario(nome),
        };
    }

    /// <summary>Cores da linha conforme a situação: (fundo, texto) ou null para o padrão.</summary>
    public static (string Fundo, string Texto)? CoresDaSituacao(string sit)
    {
        string s = Texto.Norm(sit);
        if (s == "ok") return ("#1D6FD8", "#FFFFFF");
        if (s is "cap 1" or "cap 2" or "cap 3" or "cap 4") return ("#1E293B", "#FFFFFF");
        if (s == "atrasado") return ("#E5383B", "#FFFFFF");
        return null;
    }
}
