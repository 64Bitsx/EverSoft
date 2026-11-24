using System.Text.Json;
using EverSoft.Core.Util;

namespace EverSoft.Core.Dados;

/// <summary>
/// Data em que cada aluno entrou no Cap 2 (arquivo inicio_cap2.json na pasta de dados do app).
/// Quando um aluno recebe o status "Cap 2" pela primeira vez, a data do dia é guardada;
/// se ele já estiver no arquivo, a data original fica. A coluna INÍCIO usa essa data para quem
/// está no Cap 2; os demais continuam vindo da Base Prepara.
/// </summary>
public class InicioCap2
{
    private readonly string _arquivo;
    private readonly object _trava = new();
    private Dictionary<string, Registro> _dados = new();
    private DateTime _lidoEm = DateTime.MinValue;

    public record Registro(string Nome, string Data);

    public InicioCap2(string arquivo) => _arquivo = arquivo;
    public string Arquivo => _arquivo;

    /// <summary>Data (dd/MM/aaaa) em que o aluno entrou no Cap 2, ou null.</summary>
    public string? Buscar(string nome)
    {
        lock (_trava)
        {
            RelerSeMudou();
            return _dados.TryGetValue(Texto.Norm(nome), out var r) ? r.Data : null;
        }
    }

    /// <summary>Guarda a data do aluno se ele ainda não estiver no arquivo. Devolve true se gravou.</summary>
    public bool Registrar(string nome, DateOnly data)
    {
        if (string.IsNullOrWhiteSpace(nome)) return false;
        lock (_trava)
        {
            _lidoEm = DateTime.MinValue;   // relê do disco: outro computador pode ter gravado
            RelerSeMudou();
            string chave = Texto.Norm(nome);
            if (_dados.ContainsKey(chave)) return false;
            _dados[chave] = new Registro(nome.Trim(), data.ToString("dd/MM/yyyy"));
            Json.SalvarAtomico(_arquivo, JsonSerializer.Serialize(
                _dados.Values.OrderBy(r => r.Nome, StringComparer.OrdinalIgnoreCase).ToList(),
                new JsonSerializerOptions(Json.Opcoes) { WriteIndented = true }));
            _lidoEm = File.GetLastWriteTimeUtc(_arquivo);
            return true;
        }
    }

    private void RelerSeMudou()
    {
        if (!File.Exists(_arquivo)) { _dados = new(); return; }
        var quando = File.GetLastWriteTimeUtc(_arquivo);
        if (quando == _lidoEm) return;
        try
        {
            var lista = JsonSerializer.Deserialize<List<Registro>>(File.ReadAllText(_arquivo), Json.Opcoes) ?? new();
            _dados = lista.Where(r => !string.IsNullOrWhiteSpace(r.Nome))
                          .GroupBy(r => Texto.Norm(r.Nome)).ToDictionary(g => g.Key, g => g.First());
            _lidoEm = quando;
        }
        catch (Exception e) { Log.Erro($"inicio_cap2.json: {e.Message}"); }
    }
}
