using EverSoft.Core.Configuracao;

namespace EverSoft.Core.Automacao;

/// <summary>Resultado de um envio em lote.</summary>
public record ResultadoEnvio(int Enviados, int Total, List<string> Falhas, bool Cancelado);

/// <summary>
/// Envio dos lembretes da Agenda (antigas macros EnviaMsgLembrete / EnviaMsgLembrete1):
/// modo "audio" manda um áudio .ogg sorteado da pasta da faixa e depois "Aluno: Nome";
/// modo "texto" manda a mensagem montada na Agenda.
/// </summary>
public static class EnvioLembretes
{
    public static List<string> AudiosDaPasta(string? pasta)
    {
        if (string.IsNullOrEmpty(pasta) || !Directory.Exists(pasta)) return new();
        return Directory.GetFiles(pasta, "*.ogg").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static async Task<ResultadoEnvio> EnviarAsync(
        IWhatsApp wa, IReadOnlyList<ItemAgenda> agenda, string modo, string? pastaAudio, WhatsAppConfig cfg,
        Action<string> log, Action<int, int>? progresso = null, CancellationToken ct = default, Random? sorteio = null)
    {
        sorteio ??= Random.Shared;
        bool audio = modo != "texto";
        var audios = audio ? AudiosDaPasta(pastaAudio) : new();
        if (audio && audios.Count == 0)
            throw new InvalidOperationException($"Não há áudio .ogg na pasta:\n{pastaAudio}");

        var itens = agenda.Where(a => !string.IsNullOrWhiteSpace(a.Numero)).ToList();
        var falhas = new List<string>();
        int enviados = 0;
        await wa.AbrirAsync(log, ct);
        try
        {
            for (int i = 0; i < itens.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var c = itens[i];
                progresso?.Invoke(i + 1, itens.Count);
                log($"[{i + 1}/{itens.Count}] {c.Nome} ({c.Numero})");
                try
                {
                    if (!await wa.AbrirConversaAsync(c.Numero, ct))
                    {
                        falhas.Add($"{c.Nome} ({c.Numero}): número sem WhatsApp");
                        log("  número sem WhatsApp, pulando.");
                        continue;
                    }
                    if (audio)
                    {
                        await wa.EnviarArquivoAsync(audios[sorteio.Next(audios.Count)], ct);
                        await wa.EnviarTextoAsync($"Aluno: {c.Nome}", ct);
                    }
                    else await wa.EnviarTextoAsync(c.Mensagem, ct);
                    enviados++;
                    await Task.Delay(TimeSpan.FromSeconds(cfg.EsperaEntreAlunos), ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (JanelaPerdidaException e)
                {
                    falhas.Add($"Envio parado em {c.Nome}: {e.Message}");
                    log("  ERRO: " + e.Message);
                    return new(enviados, itens.Count, falhas, true);
                }
                catch (Exception e)
                {
                    falhas.Add($"{c.Nome} ({c.Numero}): {e.Message}");
                    log("  ERRO: " + e.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            log("Envio interrompido.");
            return new(enviados, itens.Count, falhas, true);
        }
        return new(enviados, itens.Count, falhas, false);
    }
}
