using System.Globalization;
using System.IO.Compression;
using System.Xml;
using CaixaLume.Models;

namespace CaixaLume.Services;

internal static class RelatorioExcelService
{
    private const string SheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string DocumentRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    internal static void Salvar(ResumoRelatorio report, string storeName, string destination)
    {
        Autorizacao.ExigirAdministrador();
        var sheets = BuildSheets(report, storeName);
        SaveSheets(sheets, destination);
    }

    internal static void SalvarEntradasNfe(NfeImportReportData report, string storeName, DateTime start, DateTime end, string destination)
    {
        Autorizacao.ExigirAdministrador();
        static string Date(DateTime value) => value.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR"));
        decimal costTotal = report.Entries.Sum(entry => entry.TotalCost);
        var sheets = new List<Sheet>
        {
            new("Resumo", ["Indicador", "Valor"], new object?[][]
            {
                ["Loja", storeName], ["Entrada de", start.ToString("dd/MM/yyyy")], ["Entrada até", end.ToString("dd/MM/yyyy")],
                ["Notas importadas", report.Invoices.Count], ["Linhas recebidas", report.Entries.Count],
                ["Custo dos itens recebidos (R$)", costTotal],
                ["Situação fiscal", "Não consultada online; confira o XML e o recebimento físico"]
            }),
            new("Notas", ["Número", "Série", "Emissão", "Entrada em", "Fornecedor", "CNPJ fornecedor", "CNPJ destinatário", "Chave de acesso", "Total da nota (R$)", "Itens"],
                report.Invoices.Select(x => new object?[] { x.Number, x.Series, Date(x.IssueDate), Date(x.Date), x.SupplierName, x.SupplierCnpj, x.RecipientCnpj, x.Key, x.InvoiceTotal, x.ItemCount })),
            new("Entradas", ["Número da nota", "Entrada em", "Fornecedor", "Produto", "Quantidade", "Unidade", "Custo unitário (R$)", "Custo total (R$)"],
                report.Entries.Select(x => new object?[] { x.Number, Date(x.Date), x.SupplierName, x.ProductName, x.Quantity, x.Unit, x.UnitCost, x.TotalCost }))
        };
        SaveSheets(sheets, destination);
    }

    private static void SaveSheets(IReadOnlyList<Sheet> sheets, string destination)
    {
        string path = Path.GetFullPath(destination);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteContentTypes(zip, sheets.Count);
                WriteRootRelationships(zip);
                WriteWorkbook(zip, sheets);
                WriteWorkbookRelationships(zip, sheets.Count);
                for (int index = 0; index < sheets.Count; index++) WriteSheet(zip, index + 1, sheets[index]);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record Sheet(string Name, string[] Headers, IEnumerable<object?[]> Rows);

    private static List<Sheet> BuildSheets(ResumoRelatorio r, string storeName)
    {
        static string Date(DateTime value) => value.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR"));
        static string? OptionalDate(DateTime? value) => value?.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR"));
        return
        [
            new("Resumo", ["Indicador", "Valor"], new object?[][]
            {
                ["Loja", storeName], ["De", r.Inicio.ToString("dd/MM/yyyy")], ["Até", r.Fim.ToString("dd/MM/yyyy")],
                ["Vendas concluídas", r.QuantidadeVendas], ["Faturamento (R$)", r.TotalVendido], ["Ticket médio (R$)", r.TicketMedio],
                ["Despesas (R$)", r.TotalDespesas], ["Custo estimado (R$)", r.CustoEstimado],
                ["Lucro bruto estimado (R$)", r.LucroBrutoEstimado], ["Margem média (%)", r.MargemMediaPercentual],
                ["Subtotal (R$)", r.SubtotalVendas], ["Descontos (R$)", r.TotalDescontos], ["Acréscimos (R$)", r.TotalAcrescimos]
            }),
            new("Vendas", ["Venda", "Data", "Pagamento", "Subtotal (R$)", "Desconto (R$)", "Acréscimo (R$)", "Total líquido (R$)"], r.Vendas.Select(x => new object?[] { x.Id, Date(x.Data), x.FormaPagamento, x.Subtotal, x.Desconto, x.Acrescimo, x.Total })),
            new("Mais vendidos", ["Produto", "Quantidade", "Unidade", "Total bruto (R$)"], r.ProdutosMaisVendidos.Select(x => new object?[] { x.Produto, x.Quantidade, x.Unidade, x.Total })),
            new("Menos vendidos", ["Produto", "Quantidade", "Unidade", "Total bruto (R$)"], r.ProdutosMenosVendidos.Select(x => new object?[] { x.Produto, x.Quantidade, x.Unidade, x.Total })),
            new("Pagamentos", ["Forma de pagamento", "Vendas", "Total (R$)"], r.Pagamentos.Select(x => new object?[] { x.FormaPagamento, x.Quantidade, x.Total })),
            new("Despesas", ["Data", "Descrição", "Pagamento", "Valor (R$)"], r.Despesas.Select(x => new object?[] { Date(x.Data), x.Descricao, x.FormaPagamento, x.Valor })),
            new("Estoque baixo", ["Produto", "Estoque", "Mínimo", "Unidade"], r.EstoqueBaixo.Select(x => new object?[] { x.Produto, x.Estoque, x.Minimo, x.Unidade })),
            new("Validade", ["Produto", "Validade", "Dias restantes", "Situação"], r.Validades.Select(x => new object?[] { x.Produto, x.Validade.ToString("dd/MM/yyyy"), x.Dias, x.Status })),
            new("Movimentações", ["Data", "Produto", "Código", "Tipo", "Quantidade", "Unidade", "Motivo", "Usuário"], r.MovimentacoesEstoque.Select(x => new object?[] { Date(x.Data), x.Produto, x.CodigoBarras, x.Tipo, x.Quantidade, x.UnidadeMedida, x.Motivo, x.Usuario })),
            new("Caixas", ["Abertura", "Fechamento", "Saldo inicial (R$)", "Vendas (R$)", "Despesas (R$)", "Esperado (R$)", "Contado (R$)", "Diferença (R$)", "Status"], r.Caixas.Select(x => new object?[] { Date(x.Abertura), OptionalDate(x.Fechamento), x.SaldoInicial, x.Vendas, x.Despesas, x.ValorEsperado, x.ValorContado, x.Diferenca, x.Status }))
        ];
    }

    private static XmlWriter Writer(ZipArchive zip, string name)
        => XmlWriter.Create(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new XmlWriterSettings { Encoding = System.Text.Encoding.UTF8, Indent = false, CloseOutput = true });

    private static void WriteContentTypes(ZipArchive zip, int count)
    {
        const string ns = "http://schemas.openxmlformats.org/package/2006/content-types";
        using var xml = Writer(zip, "[Content_Types].xml");
        xml.WriteStartDocument(); xml.WriteStartElement("Types", ns);
        xml.WriteStartElement("Default", ns); xml.WriteAttributeString("Extension", "rels"); xml.WriteAttributeString("ContentType", "application/vnd.openxmlformats-package.relationships+xml"); xml.WriteEndElement();
        xml.WriteStartElement("Default", ns); xml.WriteAttributeString("Extension", "xml"); xml.WriteAttributeString("ContentType", "application/xml"); xml.WriteEndElement();
        void Part(string name, string type) { xml.WriteStartElement("Override", ns); xml.WriteAttributeString("PartName", name); xml.WriteAttributeString("ContentType", type); xml.WriteEndElement(); }
        Part("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
        for (int i = 1; i <= count; i++) Part($"/xl/worksheets/sheet{i}.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
        xml.WriteEndElement(); xml.WriteEndDocument();
    }

    private static void WriteRootRelationships(ZipArchive zip)
    {
        using var xml = Writer(zip, "_rels/.rels");
        xml.WriteStartDocument(); xml.WriteStartElement("Relationships", PackageRelNs);
        xml.WriteStartElement("Relationship", PackageRelNs); xml.WriteAttributeString("Id", "rId1"); xml.WriteAttributeString("Type", DocumentRelNs + "/officeDocument"); xml.WriteAttributeString("Target", "xl/workbook.xml"); xml.WriteEndElement();
        xml.WriteEndElement(); xml.WriteEndDocument();
    }

    private static void WriteWorkbook(ZipArchive zip, IReadOnlyList<Sheet> sheets)
    {
        using var xml = Writer(zip, "xl/workbook.xml");
        xml.WriteStartDocument(); xml.WriteStartElement("workbook", SheetNs); xml.WriteAttributeString("xmlns", "r", null, DocumentRelNs);
        xml.WriteStartElement("sheets", SheetNs);
        for (int i = 0; i < sheets.Count; i++)
        {
            xml.WriteStartElement("sheet", SheetNs); xml.WriteAttributeString("name", sheets[i].Name); xml.WriteAttributeString("sheetId", (i + 1).ToString(CultureInfo.InvariantCulture)); xml.WriteAttributeString("r", "id", DocumentRelNs, $"rId{i + 1}"); xml.WriteEndElement();
        }
        xml.WriteEndElement(); xml.WriteEndElement(); xml.WriteEndDocument();
    }

    private static void WriteWorkbookRelationships(ZipArchive zip, int count)
    {
        using var xml = Writer(zip, "xl/_rels/workbook.xml.rels");
        xml.WriteStartDocument(); xml.WriteStartElement("Relationships", PackageRelNs);
        for (int i = 1; i <= count; i++)
        {
            xml.WriteStartElement("Relationship", PackageRelNs); xml.WriteAttributeString("Id", $"rId{i}"); xml.WriteAttributeString("Type", DocumentRelNs + "/worksheet"); xml.WriteAttributeString("Target", $"worksheets/sheet{i}.xml"); xml.WriteEndElement();
        }
        xml.WriteEndElement(); xml.WriteEndDocument();
    }

    private static void WriteSheet(ZipArchive zip, int index, Sheet sheet)
    {
        using var xml = Writer(zip, $"xl/worksheets/sheet{index}.xml");
        xml.WriteStartDocument(); xml.WriteStartElement("worksheet", SheetNs);
        xml.WriteStartElement("sheetViews", SheetNs); xml.WriteStartElement("sheetView", SheetNs); xml.WriteAttributeString("workbookViewId", "0"); xml.WriteEndElement(); xml.WriteEndElement();
        xml.WriteStartElement("cols", SheetNs);
        for (int i = 0; i < sheet.Headers.Length; i++) { xml.WriteStartElement("col", SheetNs); xml.WriteAttributeString("min", (i + 1).ToString()); xml.WriteAttributeString("max", (i + 1).ToString()); xml.WriteAttributeString("width", i == 0 ? "30" : "20"); xml.WriteAttributeString("customWidth", "1"); xml.WriteEndElement(); }
        xml.WriteEndElement(); xml.WriteStartElement("sheetData", SheetNs);
        int row = 1;
        WriteRow(xml, row++, sheet.Headers.Cast<object?>().ToArray());
        foreach (object?[] cells in sheet.Rows) WriteRow(xml, row++, cells);
        xml.WriteEndElement(); xml.WriteEndElement(); xml.WriteEndDocument();
    }

    private static void WriteRow(XmlWriter xml, int rowNumber, object?[] cells)
    {
        xml.WriteStartElement("row", SheetNs); xml.WriteAttributeString("r", rowNumber.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < cells.Length; i++)
        {
            object? value = cells[i];
            if (value is null) continue;
            xml.WriteStartElement("c", SheetNs); xml.WriteAttributeString("r", $"{Column(i)}{rowNumber}");
            if (value is decimal or int or long)
            {
                xml.WriteStartElement("v", SheetNs); xml.WriteString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0"); xml.WriteEndElement();
            }
            else
            {
                xml.WriteAttributeString("t", "inlineStr"); xml.WriteStartElement("is", SheetNs); xml.WriteStartElement("t", SheetNs); xml.WriteString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""); xml.WriteEndElement(); xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
    }

    private static string Column(int zeroBased)
    {
        string name = "";
        for (int index = zeroBased + 1; index > 0; index = (index - 1) / 26) name = (char)('A' + (index - 1) % 26) + name;
        return name;
    }
}
