# EverSoft (C# / .NET 8)

Nova versão do EverSoft, reescrita em C# a partir do `EverSoft.py`.

| Projeto | O que é |
|---|---|
| `src/EverSoft.Core` | As regras e os dados (etapa 1) |
| `src/EverSoft.App` | A janela do programa, feita com Avalonia (etapa 2) |
| `tests/EverSoft.Tests` | Testes automáticos, inclusive abrindo a janela |

## Etapa 2: a interface

- Abas de horário em "chips" coloridos (presencial azul-escuro, EAD azul-claro), tabela dos 19 PCs com J, matrícula
  editável (duplo clique), situação colorida e menu no botão direito.
- Painel do aluno com foto, matéria, última presença, telefones e Instagram.
- Formulários Status, J (justificativa), Coments, Bloqueio e Info, com as mesmas regras de antes.
- Aba **Justifica** com a escolha da faixa de horário (substitui os botões que mudavam o relógio do Windows).
  "Enviar justificados" copia a mensagem para colar no grupo, grava o JSON das justificativas e limpa a faixa.
- Abas **Agenda**, **Boletim** e **MSG**.
- Menu Arquivo: Automação TEC, Sincronia (F5), Configurações e pasta de dados.
- Sincronia automática das planilhas em segundo plano.

**Pasta de dados separada:** a versão nova usa `...\EverSoft\dados_app_v2`. Na primeira abertura ela copia o
`config.json` e o `estado.json` da pasta `dados_app` do Python. Assim as duas versões podem rodar ao mesmo tempo
sem uma apagar o que a outra gravou. Os JSONs das planilhas continuam compartilhados.

**Ainda não tem (etapa 3):** envio pelo WhatsApp (lembretes, boletins, disparo) e remover faltas.

## Rodar o programa

No Visual Studio, clique com o botão direito em **EverSoft.App** > **Definir como Projeto de Inicialização** e aperte **F5**.

Para gerar o executável único (um arquivo só, não precisa instalar .NET):

```
dotnet publish src/EverSoft.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publicar
```

## Etapa 1: o núcleo

Tudo o que o app **sabe fazer**, sem a parte visual ainda:

| Pasta | O que tem | Equivale no EverSoft.py |
|---|---|---|
| `Util/` | Funções de texto, JSON e log | `norm`, `txt`, `proper`, `parse_data`, `tratar_contatos`... |
| `Configuracao/` | `config.json` e `estado.json` | `Config`, `Estado` |
| `Planilhas/` | Ler `.xlsx`, `.xls` (inclusive os que são HTML ou XML por dentro), gravar com backup, JSONs espelho | `ler_planilha`, `gravar_no_topo`, `_ler_json_fonte`... |
| `Dados/` | Central de consultas (as antigas fórmulas PROCV) | `DataHub` |
| `Dominio/` | Abas de horário, linha do aluno, faixas de horário, mensagens | `Slot`, `processar_grade`, `calcular_linha`, `faixa_*`, `mensagem_*` |
| `Servicos/` | Um método por botão: preencher matrículas, status, bloqueio, coments, justifica, lembretes, boletins, Automação TEC, Sincronia | `Nucleo`, `gerar_jsons_*` |

**Compatível com a versão Python:** lê o mesmo `config.json`, o mesmo `estado.json` e os mesmos
JSONs espelho. Dá para usar as duas versões em paralelo durante a transição.

**80 testes automáticos** conferem as regras, usando planilhas fictícias criadas na hora.
Vários resultados esperados foram tirados rodando o próprio `EverSoft.py`.

**Testado com as planilhas reais:** a Automação TEC, a Sincronia e a montagem das 9 abas de horário
deram exatamente o mesmo resultado na versão Python e na C# (92 alunos, todas as colunas iguais).

## Planilhas sempre em dia (Excel → JSON → app)

As planilhas são editadas o tempo todo (por você, pelo app e por colegas). Por isso:

1. **O app grava** (Status, Bloqueio, Coments, Notificação): grava no Excel, gera o JSON na hora e relê.
2. **Alguém edita o Excel direto**: a cada 60 segundos (`auto_atualizar_segundos`) o app confere quais
   planilhas ficaram mais novas que o JSON, converte só essas e relê. Se a planilha estiver sendo salva
   naquele instante, o JSON anterior é mantido e ele tenta de novo na rodada seguinte.
3. **Outro PC converteu**: o app percebe que o JSON mudou e relê.
4. Os botões **Automação TEC** e **Sincronia** continuam existindo para forçar a conversão.

Para voltar ao comportamento antigo (só converter pelos botões), use `"sincronia_automatica": false`
no `config.json`. Quem faz isso é a classe `Servicos/MonitorPlanilhas.cs`.

## Como abrir no Visual Studio

1. Instale o **Visual Studio 2022 Community** (gratuito): https://visualstudio.microsoft.com/pt-br/vs/community/
   Na instalação, marque a carga de trabalho **"Desenvolvimento para desktop com .NET"**.
2. Descompacte a pasta e dê dois cliques em **`EverSoft.sln`**.
3. Para rodar os testes: menu **Teste > Gerenciador de Testes** e clique em **Executar todos** (o botão ▶▶).
   Todos devem ficar verdes.

Pelo terminal (opcional): `dotnet test` dentro da pasta.

## Diferenças em relação ao Python

- Os caminhos de rede ficam só no `config.json`. O PC guarda apenas onde está a pasta de dados,
  em `%APPDATA%\EverSoft\local.json`.
- Planilhas são lidas mesmo quando alguém está com elas abertas no Excel.
- O padrão de `justificativas_json_dir` agora aponta para o servidor, e não para a pasta do seu usuário
  (se o seu `config.json` já tem esse caminho, ele continua valendo).
- Ainda **não** portado: a cópia local de dados para abrir sem rede (vem com a interface),
  importar a planilha .xlsm antiga, WhatsApp, remover faltas e
  os botões que mudam o relógio do Windows (substituídos pela escolha da faixa: `MensagemFaltosos(faixa: "13:00 ÀS 15:00")`).

## Etapa 3: WhatsApp Web (em andamento)

Igual à versão Python (`src/EverSoft.Core/Automacao/WhatsAppTeclado.cs`): o **nircmd** traz para a frente
(`win activate ititle WhatsApp`) e maximiza (`win max`) a janela do WhatsApp Web já aberta, em qualquer
navegador, e cada conversa começa com **Ctrl+Alt+N** → número (ou nome do grupo) → Enter. Áudios e textos
vão pela área de transferência + Ctrl+V. O `nircmd.exe` pode ficar ao lado do `EverSoft.exe`, no PATH,
na pasta do Windows ou no caminho informado em Configurações.

Mais seguro que antes: antes de cada passo o app confere se a janela da frente ainda é a do WhatsApp;
se não for, ativa de novo, e se não conseguir, **para o envio** em vez de digitar em outra janela.

- **Enviar lembretes** (aba de horário): monta a Agenda sem sair da aba e já começa o envio.
  Modo `audio`: um `.ogg` sorteado da pasta da faixa (`<audios>\Semana|Sabado\0745...`) e depois "Aluno: Nome".
  Modo `texto`: a mensagem montada na Agenda.
- **Reenviar lembretes** (aba Agenda): manda de novo a Agenda que já está montada.
- **Enviar justificados** (aba Justifica): manda a mensagem da faixa ao grupo configurado; só depois de enviada
  as justificativas vão para o JSON e a faixa é limpa.
- Uma janelinha no canto (que não rouba o foco) mostra o progresso e tem o botão **Parar**.

## Instalação e atualização automática

- Aberto pela rede (ou de qualquer outro lugar), o `EverSoft.exe` se copia para **C:\EverSoft** e abre de lá.
- Ao abrir de C:\EverSoft, compara a **data de modificação** com o EverSoft.exe da rede
  (Configurações > "EverSoft.exe da rede usado na atualização"). Se o da rede for mais novo, mostra a tela de
  atualização, copia e reabre.
- Com o programa aberto, confere a cada 1 minuto e pergunta se quer atualizar.
- Cada versão traz suas novidades em `src/EverSoft.App/Assets/novidades.txt` (seção `## VX.X`), mostradas
  uma vez na primeira abertura da versão. **Ao lançar uma versão: suba o `<Version>` no
  `EverSoft.App.csproj` e escreva a seção nova no novidades.txt.**
- Rodando pelo Visual Studio, nada disso acontece (só vale para o .exe publicado).

## Próximas etapas

3. Continuar o WhatsApp: boletins (com o Boletim.png do caminho configurado) e disparo da aba MSG
4. Remover faltas no Sistema Escola
5. Executável (`EverSoft.exe`) e instalador

## Guia rápido: C# para quem sabe Python

| Python | C# |
|---|---|
| `nome = "Ana"` | `string nome = "Ana";` ou `var nome = "Ana";` |
| `idade: int = 15` | `int idade = 15;` |
| `def soma(a, b): return a + b` | `int Soma(int a, int b) => a + b;` |
| `if x: ... elif y: ... else: ...` | `if (x) { ... } else if (y) { ... } else { ... }` |
| `for aluno in alunos:` | `foreach (var aluno in alunos) { ... }` |
| `lista = []` / `lista.append(x)` | `var lista = new List<string>();` / `lista.Add(x);` |
| `d = {}` / `d["k"] = 1` | `var d = new Dictionary<string, int>();` / `d["k"] = 1;` |
| `None` | `null` (e `string?` quer dizer "pode ser null") |
| `f"Olá {nome}"` | `$"Olá {nome}"` |
| `[a.nome for a in alunos if a.ativo]` | `alunos.Where(a => a.Ativo).Select(a => a.Nome).ToList()` |
| `try: ... except Exception as e:` | `try { ... } catch (Exception e) { ... }` |
| `@dataclass` | `record` |
| `# comentário` | `// comentário` e `/// <summary>` para documentar |

Principal diferença: o C# confere os tipos **antes** de rodar. O Visual Studio sublinha de vermelho
um erro que no Python só apareceria quando aquela linha fosse executada.

## Decisões

- **Boas-vindas em PowerPoint:** removido (não é mais usado).
- **Imagem do boletim (`Boletim.png`):** muda todo mês, então não vai dentro do programa. O app lê o arquivo
  do caminho configurado (`boletim_imagem`) na hora de cada envio, sempre a versão mais nova.
