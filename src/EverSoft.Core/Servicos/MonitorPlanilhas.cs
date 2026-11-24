using EverSoft.Core.Configuracao;
using EverSoft.Core.Dados;
using EverSoft.Core.Util;

namespace EverSoft.Core.Servicos;

/// <summary>
/// Mantém o app em dia com as planilhas da rede, em segundo plano:
/// 1. confere quais planilhas foram alteradas (por você, por colegas ou pelo sistema);
/// 2. converte só essas para JSON;
/// 3. relê os JSONs que mudaram (inclusive os gerados por outro PC) e avisa a tela.
///
/// As gravações feitas pelo próprio app já atualizam o JSON na hora (ver GravadorPlanilha).
/// </summary>
public sealed class MonitorPlanilhas : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly DataHub _hub;
    private Timer? _timer;
    private int _rodando;

    /// <summary>Disparado quando alguma fonte foi relida. Recebe as chaves das fontes (acomp, obs...).</summary>
    public event Action<IReadOnlyList<string>>? DadosAtualizados;
    /// <summary>Mensagens de andamento (para a barra de status ou um log).</summary>
    public event Action<string>? Mensagem;

    public MonitorPlanilhas(AppConfig cfg, DataHub hub)
    {
        _cfg = cfg;
        _hub = hub;
    }

    public void Iniciar()
    {
        var intervalo = TimeSpan.FromSeconds(Math.Max(10, _cfg.AutoAtualizarSegundos));
        _timer = new Timer(_ => VerificarAgora(), null, intervalo, intervalo);
    }

    public void Parar() => _timer?.Change(Timeout.Infinite, Timeout.Infinite);

    /// <summary>Faz uma rodada agora. Se já houver uma em andamento, não faz nada.</summary>
    public IReadOnlyList<string> VerificarAgora()
    {
        if (Interlocked.Exchange(ref _rodando, 1) == 1) return Array.Empty<string>();
        try
        {
            if (_cfg.SincroniaAutomatica)
            {
                var convertidas = Sincronizacao.ConverterAlteradas(_cfg);
                foreach (var c in convertidas) Mensagem?.Invoke($"Planilha atualizada: {Path.GetFileName(c.Replace('\\', '/'))}");
            }
            var relidas = _hub.RecarregarSeMudou();
            if (relidas.Count > 0) DadosAtualizados?.Invoke(relidas);
            return relidas;
        }
        catch (Exception e)
        {
            // Rede fora do ar, por exemplo: tenta de novo na próxima rodada.
            Log.Erro($"monitor de planilhas: {e.Message}");
            Mensagem?.Invoke($"Não foi possível verificar as planilhas: {e.Message}");
            return Array.Empty<string>();
        }
        finally
        {
            Interlocked.Exchange(ref _rodando, 0);
        }
    }

    public void Dispose() => _timer?.Dispose();
}
