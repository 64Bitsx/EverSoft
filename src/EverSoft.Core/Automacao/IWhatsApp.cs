namespace EverSoft.Core.Automacao;

/// <summary>
/// O que a automação precisa do WhatsApp Web. A versão real (WhatsAppTeclado) usa o nircmd
/// e o teclado, como a versão em Python; os testes usam uma versão falsa.
/// </summary>
public interface IWhatsApp : IAsyncDisposable
{
    /// <summary>Traz a janela do WhatsApp Web para a frente (nircmd activate + max).</summary>
    Task AbrirAsync(Action<string> log, CancellationToken ct);

    /// <summary>Abre a conversa do número (Ctrl+Alt+N). Devolve false se não deu para abrir.</summary>
    Task<bool> AbrirConversaAsync(string numero, CancellationToken ct);

    /// <summary>Abre a conversa de um grupo pelo nome (Ctrl+Alt+N).</summary>
    Task AbrirGrupoAsync(string nome, CancellationToken ct);

    /// <summary>Escreve e envia um texto na conversa aberta (quebras de linha são respeitadas).</summary>
    Task EnviarTextoAsync(string texto, CancellationToken ct);

    /// <summary>Anexa e envia um arquivo (áudio, imagem...) na conversa aberta.</summary>
    Task EnviarArquivoAsync(string caminho, CancellationToken ct);
}
