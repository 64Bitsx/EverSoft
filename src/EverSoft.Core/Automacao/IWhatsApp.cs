namespace EverSoft.Core.Automacao;

/// <summary>
/// O que a automação precisa do WhatsApp Web. A versão real (WhatsAppWeb) usa o Playwright
/// para controlar o navegador; os testes usam uma versão falsa.
/// </summary>
public interface IWhatsApp : IAsyncDisposable
{
    /// <summary>Abre (ou reaproveita) o navegador com o WhatsApp Web e espera estar logado.</summary>
    Task AbrirAsync(Action<string> log, CancellationToken ct);

    /// <summary>Abre a conversa do número. Devolve false quando o WhatsApp diz que o número não existe.</summary>
    Task<bool> AbrirConversaAsync(string numero, CancellationToken ct);

    /// <summary>Abre a conversa de um grupo (ou contato salvo) pelo nome, usando a busca.</summary>
    Task AbrirGrupoAsync(string nome, CancellationToken ct);

    /// <summary>Escreve e envia um texto na conversa aberta (quebras de linha são respeitadas).</summary>
    Task EnviarTextoAsync(string texto, CancellationToken ct);

    /// <summary>Anexa e envia um arquivo (áudio, imagem...) na conversa aberta.</summary>
    Task EnviarArquivoAsync(string caminho, CancellationToken ct);
}
