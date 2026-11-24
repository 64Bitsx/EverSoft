using System.Data;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using ClosedXML.Excel;
using ExcelDataReader;
using HtmlAgilityPack;

namespace EverSoft.Core.Planilhas;

/// <summary>
/// Lê planilhas sem precisar do Excel instalado.
/// Um ".xls" pode ser, na verdade, um xlsx renomeado, uma página HTML, um XML do Excel 2003
/// ou um xls de verdade: o leitor olha o começo do arquivo e escolhe o jeito certo.
/// </summary>
public static class LeitorPlanilha
{
    static LeitorPlanilha()
    {
        // Necessário para ler .xls antigos (codificação Windows-1252).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Lê a primeira aba que existir da lista <paramref name="abas"/> (null = primeira aba do arquivo).
    /// </summary>
    public static Planilha Ler(string caminho, IEnumerable<string?>? abas = null)
    {
        var lista = (abas ?? new string?[] { null }).ToList();
        var todas = LerTodas(caminho);
        foreach (var aba in lista)
        {
            if (aba is null) return todas[0];
            var achada = todas.FirstOrDefault(p => string.Equals(p.Nome, aba, StringComparison.OrdinalIgnoreCase));
            if (achada is not null) return achada;
        }
        throw new AbaNaoEncontradaException(
            $"aba não encontrada: [{string.Join(", ", lista.Select(a => a ?? "primeira"))}] em {Path.GetFileName(caminho)}");
    }

    /// <summary>Lê todas as abas do arquivo, na ordem em que aparecem.</summary>
    public static List<Planilha> LerTodas(string caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho))
            throw new FileNotFoundException($"Arquivo não encontrado: {(string.IsNullOrWhiteSpace(caminho) ? "(caminho vazio)" : caminho)}", caminho);

        // FileShare.ReadWrite permite ler mesmo com o arquivo aberto no Excel por outra pessoa.
        byte[] bytes;
        using (var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var ms = new MemoryStream())
        {
            fs.CopyTo(ms);
            bytes = ms.ToArray();
        }

        var lista = Formato(bytes) switch
        {
            FormatoArquivo.Xlsx => LerXlsx(bytes),
            FormatoArquivo.SpreadsheetMl => LerSpreadsheetMl(Decodificar(bytes)),
            FormatoArquivo.Html => new List<Planilha> { new(LerTabelaHtml(Decodificar(bytes)), "Sheet1") },
            _ => LerXlsBinario(bytes),
        };
        if (lista.Count == 0) throw new InvalidDataException($"Nenhuma aba em {Path.GetFileName(caminho)}");
        return lista;
    }

    // ------------------------------------------------------------------ detecção do formato
    internal enum FormatoArquivo { Xlsx, SpreadsheetMl, Html, XlsBinario }

    internal static FormatoArquivo Formato(byte[] bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 'P' && bytes[1] == 'K' && bytes[2] == 3 && bytes[3] == 4)
            return FormatoArquivo.Xlsx;
        int i = 0;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) i = 3;
        while (i < bytes.Length && (bytes[i] == ' ' || bytes[i] == '\r' || bytes[i] == '\n' || bytes[i] == '\t')) i++;
        if (i < bytes.Length && bytes[i] == '<')
        {
            string inicio = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 6000));
            return inicio.Contains("urn:schemas-microsoft-com:office:spreadsheet")
                ? FormatoArquivo.SpreadsheetMl
                : FormatoArquivo.Html;
        }
        return FormatoArquivo.XlsBinario;
    }

    private static string Decodificar(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    // ------------------------------------------------------------------ .xlsx / .xlsm
    private static List<Planilha> LerXlsx(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var saida = new List<Planilha>();
        foreach (var ws in wb.Worksheets)
        {
            var linhas = new List<List<object?>>();
            var ultimaCelula = ws.LastCellUsed(XLCellsUsedOptions.Contents);
            if (ultimaCelula is not null)
            {
                int ultLinha = ws.LastRowUsed(XLCellsUsedOptions.Contents)!.RowNumber();
                int ultCol = ws.LastColumnUsed(XLCellsUsedOptions.Contents)!.ColumnNumber();
                for (int r = 1; r <= ultLinha; r++)
                {
                    var linha = new List<object?>(ultCol);
                    for (int c = 1; c <= ultCol; c++) linha.Add(ValorClosedXml(ws.Cell(r, c)));
                    linhas.Add(linha);
                }
            }
            saida.Add(new Planilha(linhas, ws.Name));
        }
        return saida;
    }

    /// <summary>Valor já calculado da célula (como data_only=True do openpyxl).</summary>
    internal static object? ValorClosedXml(IXLCell cel)
    {
        XLCellValue v;
        try
        {
            v = cel.HasFormula ? cel.CachedValue : cel.Value;
            // Fórmula que o Excel nunca calculou: tenta calcular aqui mesmo.
            if (cel.HasFormula && v.IsBlank) v = cel.Value;
        }
        catch
        {
            return null;
        }
        return v.Type switch
        {
            XLDataType.Blank => null,
            XLDataType.Error => null,
            XLDataType.Boolean => v.GetBoolean(),
            XLDataType.Number => v.GetNumber(),
            XLDataType.DateTime => v.GetDateTime(),
            XLDataType.TimeSpan => v.GetTimeSpan().TotalDays,
            XLDataType.Text => v.GetText() == "" ? null : v.GetText(),
            _ => null,
        };
    }

    // ------------------------------------------------------------------ .xls binário (Excel 97-2003)
    private static List<Planilha> LerXlsBinario(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var leitor = ExcelReaderFactory.CreateBinaryReader(ms);
        var saida = new List<Planilha>();
        do
        {
            var linhas = new List<List<object?>>();
            while (leitor.Read())
            {
                var linha = new List<object?>(leitor.FieldCount);
                for (int c = 0; c < leitor.FieldCount; c++)
                {
                    object? v = leitor.GetValue(c);
                    linha.Add(v switch
                    {
                        null or DBNull => null,
                        string s when s == "" => null,
                        string s => s,
                        double d => d,
                        int i => (double)i,
                        bool b => b,
                        DateTime dt => dt,
                        _ => Convert.ToString(v, CultureInfo.InvariantCulture),
                    });
                }
                linhas.Add(linha);
            }
            saida.Add(new Planilha(linhas, leitor.Name));
        } while (leitor.NextResult());
        return saida;
    }

    // ------------------------------------------------------------------ Excel 2003 XML
    private static List<Planilha> LerSpreadsheetMl(string texto)
    {
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var raiz = XDocument.Parse(texto).Root!;
        var saida = new List<Planilha>();
        foreach (var ws in raiz.Elements(ss + "Worksheet"))
        {
            var linhas = new List<List<object?>>();
            var tabela = ws.Element(ss + "Table");
            if (tabela is not null)
            {
                foreach (var row in tabela.Elements(ss + "Row"))
                {
                    var linha = new List<object?>();
                    foreach (var cell in row.Elements(ss + "Cell"))
                    {
                        var idx = (string?)cell.Attribute(ss + "Index");
                        if (idx is not null)
                            while (linha.Count < int.Parse(idx) - 1) linha.Add(null);
                        var d = cell.Element(ss + "Data");
                        object? v = null;
                        if (d is not null && d.Value != "")
                        {
                            string tipo = (string?)d.Attribute(ss + "Type") ?? "";
                            v = tipo switch
                            {
                                "Number" => double.Parse(d.Value, CultureInfo.InvariantCulture),
                                "DateTime" => DateTime.TryParse(d.Value.Length > 19 ? d.Value[..19] : d.Value,
                                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : d.Value,
                                _ => d.Value,
                            };
                        }
                        linha.Add(v);
                    }
                    linhas.Add(linha);
                }
            }
            saida.Add(new Planilha(linhas, (string?)ws.Attribute(ss + "Name") ?? ""));
        }
        return saida;
    }

    // ------------------------------------------------------------------ "xls" que é HTML
    /// <summary>Pega a maior tabela do HTML (muitos sistemas exportam "Excel" assim).</summary>
    internal static List<List<object?>> LerTabelaHtml(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var tabelas = doc.DocumentNode.Descendants("table").ToList();
        if (tabelas.Count == 0) throw new InvalidDataException("Nenhuma tabela encontrada no arquivo HTML");

        List<List<object?>> LerTabela(HtmlNode t)
        {
            var linhas = new List<List<object?>>();
            // Só as linhas desta tabela (não as de tabelas dentro dela).
            foreach (var tr in t.Descendants("tr").Where(tr => tr.Ancestors("table").First() == t))
            {
                var linha = new List<object?>();
                foreach (var td in tr.Elements("td").Concat(tr.Elements("th")).OrderBy(n => n.StreamPosition))
                {
                    foreach (var br in td.Descendants("br").ToList()) br.ParentNode!.ReplaceChild(doc.CreateTextNode(" "), br);
                    string texto = Util.Texto.ColapsarEspacos(HtmlEntity.DeEntitize(td.InnerText));
                    linha.Add(texto == "" ? null : texto);
                    int span = int.TryParse(td.GetAttributeValue("colspan", "1"), out var s) && s > 1 ? s : 1;
                    for (int k = 1; k < span; k++) linha.Add(null);
                }
                linhas.Add(linha);
            }
            return linhas;
        }

        return tabelas.Select(LerTabela).OrderByDescending(l => l.Count).First();
    }
}
