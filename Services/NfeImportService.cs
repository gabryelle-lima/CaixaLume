using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using CaixaLume.Data;
using CaixaLume.Models;

namespace CaixaLume.Services;

public sealed record NfeImportSelection(int LineNumber, int ProductId, decimal Quantity, decimal UnitCost);
public sealed record NfeImportPreviewItem(int LineNumber, string SupplierCode, string Name, string Gtin,
    string Unit, decimal Quantity, decimal UnitPrice, decimal Total, int? SuggestedProductId);
public sealed record NfeImportPreview(string Key, string Number, string Series, DateTime IssueDate,
    string SupplierName, string SupplierCnpj, string RecipientName, string RecipientCnpj,
    decimal InvoiceTotal, bool AlreadyImported, IReadOnlyList<NfeImportPreviewItem> Items);
public sealed record NfeImportResult(long Id, string Key, int ItemCount);
public sealed record NfeImportInvoice(long Id, string Key, string Number, string Series, DateTime Date, DateTime IssueDate,
    string SupplierName, string SupplierCnpj, string RecipientCnpj, decimal InvoiceTotal, int ItemCount);
public sealed record NfeImportEntry(long InvoiceId, string Number, DateTime Date, string SupplierName,
    string ProductName, decimal Quantity, string Unit, decimal UnitCost, decimal TotalCost);
public sealed record NfeImportReportData(IReadOnlyList<NfeImportInvoice> Invoices, IReadOnlyList<NfeImportEntry> Entries);
public sealed record NfeImportDetailRow(int LineNumber, string XmlName, string ProductName,
    decimal XmlQuantity, string XmlUnit, decimal Quantity, string Unit, decimal UnitCost, decimal TotalCost);
public sealed record NfeImportDetails(IReadOnlyList<NfeImportDetailRow> Rows);

/// <summary>Conferência e entrada local de XML autorizado de NF-e de compra (modelo 55).</summary>
/// <remarks>O protocolo no arquivo é verificado estruturalmente; esta rotina não consulta o estado atual na SEFAZ.</remarks>
internal sealed class NfeImportService
{
    private static readonly XNamespace NfeNs = "http://www.portalfiscal.inf.br/nfe";
    private const int MaxXmlCharacters = 8_000_000;
    private const int MaxItems = 1_000;

    public NfeImportPreview Preview(string xml)
    {
        Autorizacao.ExigirAdministrador();
        ParsedInvoice invoice = Parse(xml);
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        bool alreadyImported = db.Query("SELECT 1 FROM NfeImports WHERE Chave=@Chave LIMIT 1;",
            new Dictionary<string, object?> { ["@Chave"] = invoice.Key }).Count != 0;
        var productByBarcode = db.Query("SELECT Id,CodigoBarras FROM Produtos WHERE Ativo=1 AND ExcluidoEm IS NULL AND CodigoBarras IS NOT NULL;")
            .Where(row => !string.IsNullOrWhiteSpace(Convert.ToString(row["CodigoBarras"])))
            .GroupBy(row => Convert.ToString(row["CodigoBarras"])!.Trim(), StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key,
                group => Convert.ToInt32(group.Single()["Id"], CultureInfo.InvariantCulture), StringComparer.Ordinal);
        var items = invoice.Items.Select(item => new NfeImportPreviewItem(item.LineNumber, item.SupplierCode,
            item.Name, item.Gtin, item.Unit, item.Quantity, item.UnitPrice, item.Total,
            productByBarcode.TryGetValue(item.Gtin, out int id) ? id : null)).ToList();
        return new NfeImportPreview(invoice.Key, invoice.Number, invoice.Series, invoice.IssueDate,
            invoice.SupplierName, invoice.SupplierCnpj, invoice.RecipientName, invoice.RecipientCnpj,
            invoice.InvoiceTotal, alreadyImported, items);
    }

    public NfeImportResult Import(string xml, IReadOnlyList<NfeImportSelection> selections)
    {
        int userId = Autorizacao.ExigirUsuario(administrador: true);
        ParsedInvoice invoice = Parse(xml);
        if (selections is null || selections.Count != invoice.Items.Count)
            throw new InvalidOperationException("Associe todos os itens da NF-e antes de importar.");
        var byLine = new Dictionary<int, NfeImportSelection>();
        foreach (NfeImportSelection selection in selections)
        {
            if (!byLine.TryAdd(selection.LineNumber, selection))
                throw new InvalidOperationException($"O item {selection.LineNumber} foi associado mais de uma vez.");
            if (selection.ProductId <= 0 || selection.Quantity <= 0 || selection.Quantity > 1_000_000_000m ||
                selection.UnitCost < 0 || selection.UnitCost > 1_000_000_000m ||
                decimal.Round(selection.UnitCost, 6) != selection.UnitCost)
                throw new InvalidOperationException($"Confira o produto, a quantidade e o custo do item {selection.LineNumber}.");
        }
        if (invoice.Items.Any(item => !byLine.ContainsKey(item.LineNumber)))
            throw new InvalidOperationException("Há itens da NF-e sem associação de produto.");

        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        db.Execute("BEGIN IMMEDIATE;");
        try
        {
            string storeCnpj = Text(db.Query("SELECT Valor FROM Configuracoes WHERE Chave='CnpjLoja' LIMIT 1;").FirstOrDefault()?.GetValueOrDefault("Valor"));
            if (storeCnpj.Length != 14)
                throw new InvalidOperationException("Configure o CNPJ da loja em Configurações antes de importar uma NF-e.");
            if (storeCnpj != invoice.RecipientCnpj)
                throw new InvalidOperationException("O destinatário da NF-e não corresponde ao CNPJ configurado para a loja.");
            if (db.Query("SELECT 1 FROM NfeImports WHERE Chave=@Chave LIMIT 1;",
                    new Dictionary<string, object?> { ["@Chave"] = invoice.Key }).Count != 0)
                throw new InvalidOperationException("Esta NF-e já foi importada. O estoque não foi alterado novamente.");
            if (db.Query("SELECT 1 FROM MovimentacoesEstoque WHERE Documento=@Chave LIMIT 1;",
                    new Dictionary<string, object?> { ["@Chave"] = invoice.Key }).Count != 0)
                throw new InvalidOperationException("Já há movimentação de estoque com esta chave. Confira as entradas manuais antes de importar.");

            var products = new Dictionary<int, ProductForEntry>();
            var prepared = new List<PreparedItem>(invoice.Items.Count);
            foreach (ParsedItem item in invoice.Items)
            {
                NfeImportSelection selection = byLine[item.LineNumber];
                if (!products.TryGetValue(selection.ProductId, out ProductForEntry? product))
                {
                    var row = db.Query("SELECT Id,Nome,UnidadeMedida FROM Produtos WHERE Id=@Id AND Ativo=1 AND ExcluidoEm IS NULL LIMIT 1;",
                        new Dictionary<string, object?> { ["@Id"] = selection.ProductId }).FirstOrDefault()
                        ?? throw new InvalidOperationException($"O produto associado ao item {item.LineNumber} não existe ou está inativo.");
                    product = new ProductForEntry(Convert.ToInt32(row["Id"], CultureInfo.InvariantCulture),
                        Convert.ToString(row["Nome"]) ?? "", Convert.ToString(row["UnidadeMedida"]) ?? "");
                    products.Add(selection.ProductId, product);
                }
                if (product.Unit is not ("UN" or "KG"))
                    throw new InvalidOperationException($"A unidade interna do produto {product.Name} não é compatível com a entrada.");
                if (product.Unit == "UN" && selection.Quantity % 1 != 0)
                    throw new InvalidOperationException($"O item {item.LineNumber} exige quantidade inteira para o produto {product.Name}.");
                if (product.Unit == "KG" && selection.Quantity * 1_000 % 1 != 0)
                    throw new InvalidOperationException($"O item {item.LineNumber} aceita até três casas decimais em KG.");
                prepared.Add(new PreparedItem(item, selection, product));
            }

            long supplierId = FindOrCreateSupplier(db, invoice.SupplierCnpj, invoice.SupplierName);
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            db.Execute("""
                INSERT INTO NfeImports (Chave,Numero,Serie,DataEmissao,ImportadaEm,FornecedorId,FornecedorNome,
                    FornecedorCnpj,DestinatarioNome,DestinatarioCnpj,ValorTotal,Protocolo,XmlOriginal,UsuarioId)
                VALUES (@Chave,@Numero,@Serie,@Emissao,@Entrada,@FornecedorId,@FornecedorNome,
                    @FornecedorCnpj,@DestinatarioNome,@DestinatarioCnpj,@Total,@Protocolo,@Xml,@Usuario);
                """, new Dictionary<string, object?>
            {
                ["@Chave"] = invoice.Key, ["@Numero"] = invoice.Number, ["@Serie"] = invoice.Series,
                ["@Emissao"] = invoice.IssueDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                ["@Entrada"] = now, ["@FornecedorId"] = supplierId, ["@FornecedorNome"] = invoice.SupplierName,
                ["@FornecedorCnpj"] = invoice.SupplierCnpj, ["@DestinatarioNome"] = invoice.RecipientName,
                ["@DestinatarioCnpj"] = invoice.RecipientCnpj, ["@Total"] = invoice.InvoiceTotal,
                ["@Protocolo"] = invoice.Protocol, ["@Xml"] = xml, ["@Usuario"] = userId
            });
            long importId = db.LastInsertRowId;
            if (importId <= 0) throw new InvalidOperationException("Não foi possível registrar a NF-e.");

            foreach (PreparedItem entry in prepared)
            {
                ParsedItem item = entry.XmlItem;
                NfeImportSelection selected = entry.Selection;
                db.Execute("""
                    INSERT INTO NfeImportItems (NfeImportId,NumeroItem,CodigoFornecedor,NomeXml,Gtin,UnidadeXml,
                        QuantidadeXml,ValorUnitarioXml,ValorTotalXml,ProdutoId,QuantidadeEstoque,UnidadeEstoque,CustoUnitario)
                    VALUES (@Nota,@Linha,@Codigo,@Nome,@Gtin,@UnidadeXml,@QuantidadeXml,@ValorUnitarioXml,
                        @ValorTotalXml,@Produto,@Quantidade,@Unidade,@Custo);
                    """, new Dictionary<string, object?>
                {
                    ["@Nota"] = importId, ["@Linha"] = item.LineNumber, ["@Codigo"] = item.SupplierCode,
                    ["@Nome"] = item.Name, ["@Gtin"] = item.Gtin.Length == 0 ? null : item.Gtin,
                    ["@UnidadeXml"] = item.Unit, ["@QuantidadeXml"] = item.Quantity,
                    ["@ValorUnitarioXml"] = item.UnitPrice, ["@ValorTotalXml"] = item.Total,
                    ["@Produto"] = selected.ProductId, ["@Quantidade"] = selected.Quantity,
                    ["@Unidade"] = entry.Product.Unit, ["@Custo"] = selected.UnitCost
                });
                long itemId = db.LastInsertRowId;
                db.Execute("""
                    INSERT INTO MovimentacoesEstoque (ProdutoId,Tipo,Quantidade,UnidadeMedida,DataMovimentacao,
                        Motivo,UsuarioId,FornecedorId,Documento)
                    VALUES (@Produto,'Entrada',@Quantidade,@Unidade,@Data,@Motivo,@Usuario,@Fornecedor,@Documento);
                    """, new Dictionary<string, object?>
                {
                    ["@Produto"] = selected.ProductId, ["@Quantidade"] = selected.Quantity,
                    ["@Unidade"] = entry.Product.Unit, ["@Data"] = now,
                    ["@Motivo"] = $"IMPORTAÇÃO NF-E {invoice.Number}/{invoice.Series} · ITEM {item.LineNumber}",
                    ["@Usuario"] = userId, ["@Fornecedor"] = supplierId, ["@Documento"] = invoice.Key
                });
                long movementId = db.LastInsertRowId;
                db.Execute("UPDATE NfeImportItems SET MovimentacaoId=@Movimento WHERE Id=@Id;",
                    new Dictionary<string, object?> { ["@Movimento"] = movementId, ["@Id"] = itemId });
            }

            foreach (IGrouping<int, PreparedItem> group in prepared.GroupBy(entry => entry.Selection.ProductId))
            {
                decimal quantity = group.Sum(entry => entry.Selection.Quantity);
                decimal totalCost = group.Sum(entry => entry.Selection.Quantity * entry.Selection.UnitCost);
                decimal unitCost = decimal.Round(totalCost / quantity, 6, MidpointRounding.AwayFromZero);
                db.Execute("""
                    UPDATE Produtos SET EstoqueAtual=ROUND(EstoqueAtual+@Quantidade,3),PrecoCusto=@Custo,FornecedorId=@Fornecedor
                    WHERE Id=@Produto AND Ativo=1 AND ExcluidoEm IS NULL;
                    """, new Dictionary<string, object?>
                {
                    ["@Quantidade"] = quantity, ["@Custo"] = unitCost,
                    ["@Fornecedor"] = supplierId, ["@Produto"] = group.Key
                });
                if (db.Changes != 1) throw new InvalidOperationException("Um produto foi alterado durante a importação. Nada foi aplicado.");
            }

            db.Execute("""
                INSERT INTO Auditoria (UsuarioId,DataHora,Acao,Entidade,Descricao)
                VALUES (@Usuario,@Data,'IMPORTACAO','NFE_COMPRA',@Descricao);
                """, new Dictionary<string, object?>
            {
                ["@Usuario"] = userId, ["@Data"] = now,
                ["@Descricao"] = $"NF-e {invoice.Number}/{invoice.Series}, chave {invoice.Key}, fornecedor {invoice.SupplierCnpj}, {prepared.Count} item(ns)"
            });
            db.Execute("COMMIT;");
            return new NfeImportResult(importId, invoice.Key, prepared.Count);
        }
        catch
        {
            db.Execute("ROLLBACK;");
            throw;
        }
    }

    public NfeImportReportData List(DateTime start, DateTime end)
    {
        Autorizacao.ExigirAdministrador();
        if (end.Date < start.Date) throw new InvalidOperationException("A data final deve ser igual ou posterior à inicial.");
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        var parameters = new Dictionary<string, object?>
        {
            ["@Inicio"] = start.Date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["@Fim"] = end.Date.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
        };
        var invoices = db.Query("""
            SELECT n.Id,n.Chave,n.Numero,n.Serie,n.ImportadaEm,n.DataEmissao,n.FornecedorNome,n.FornecedorCnpj,
                n.DestinatarioCnpj,n.ValorTotal,COUNT(i.Id) AS ItemCount
            FROM NfeImports n JOIN NfeImportItems i ON i.NfeImportId=n.Id
            WHERE n.ImportadaEm>=@Inicio AND n.ImportadaEm<@Fim
            GROUP BY n.Id ORDER BY n.Id DESC;
            """, parameters).Select(row => new NfeImportInvoice(
                Long(row["Id"]), Text(row["Chave"]), Text(row["Numero"]), Text(row["Serie"]),
                Date(row["ImportadaEm"]), Date(row["DataEmissao"]), Text(row["FornecedorNome"]), Text(row["FornecedorCnpj"]),
                Text(row["DestinatarioCnpj"]), Number(row["ValorTotal"]), Convert.ToInt32(row["ItemCount"], CultureInfo.InvariantCulture)))
            .ToList();
        var entries = db.Query("""
            SELECT n.Id AS InvoiceId,n.Numero,n.ImportadaEm,n.FornecedorNome,p.Nome AS ProductName,
                i.QuantidadeEstoque,i.UnidadeEstoque,i.CustoUnitario
            FROM NfeImportItems i JOIN NfeImports n ON n.Id=i.NfeImportId JOIN Produtos p ON p.Id=i.ProdutoId
            WHERE n.ImportadaEm>=@Inicio AND n.ImportadaEm<@Fim
            ORDER BY n.Id DESC,i.NumeroItem;
            """, parameters).Select(row =>
            {
                decimal quantity = Number(row["QuantidadeEstoque"]), cost = Number(row["CustoUnitario"]);
                return new NfeImportEntry(Long(row["InvoiceId"]), Text(row["Numero"]), Date(row["ImportadaEm"]),
                    Text(row["FornecedorNome"]), Text(row["ProductName"]), quantity, Text(row["UnidadeEstoque"]),
                    cost, decimal.Round(quantity * cost, 2, MidpointRounding.AwayFromZero));
            }).ToList();
        return new NfeImportReportData(invoices, entries);
    }

    public NfeImportDetails Details(long id)
    {
        Autorizacao.ExigirAdministrador();
        if (id <= 0) throw new InvalidOperationException("Selecione uma NF-e importada.");
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        if (db.Query("SELECT 1 FROM NfeImports WHERE Id=@Id LIMIT 1;",
                new Dictionary<string, object?> { ["@Id"] = id }).Count == 0)
            throw new InvalidOperationException("NF-e importada não encontrada.");
        var rows = db.Query("""
            SELECT i.NumeroItem,i.NomeXml,p.Nome AS ProductName,i.QuantidadeXml,i.UnidadeXml,
                i.QuantidadeEstoque,i.UnidadeEstoque,i.CustoUnitario
            FROM NfeImportItems i JOIN Produtos p ON p.Id=i.ProdutoId
            WHERE i.NfeImportId=@Id ORDER BY i.NumeroItem;
            """, new Dictionary<string, object?> { ["@Id"] = id }).Select(row =>
        {
            decimal quantity = Number(row["QuantidadeEstoque"]), cost = Number(row["CustoUnitario"]);
            return new NfeImportDetailRow(Convert.ToInt32(row["NumeroItem"], CultureInfo.InvariantCulture),
                Text(row["NomeXml"]), Text(row["ProductName"]), Number(row["QuantidadeXml"]),
                Text(row["UnidadeXml"]), quantity, Text(row["UnidadeEstoque"]), cost,
                decimal.Round(quantity * cost, 2, MidpointRounding.AwayFromZero));
        }).ToList();
        return new NfeImportDetails(rows);
    }

    private static long FindOrCreateSupplier(DatabaseConnection db, string cnpj, string supplierName)
    {
        var matches = db.Query("SELECT Id,Documento,Ativo FROM Fornecedores WHERE Documento IS NOT NULL;")
            .Where(row => Digits(Convert.ToString(row["Documento"])) == cnpj).ToList();
        if (matches.Count > 1)
            throw new InvalidOperationException("Há fornecedores duplicados com o CNPJ da NF-e. Corrija o cadastro antes de importar.");
        if (matches.Count == 1)
        {
            if (Convert.ToInt64(matches[0]["Ativo"], CultureInfo.InvariantCulture) != 1)
                throw new InvalidOperationException("O fornecedor da NF-e está inativo. Reative-o antes de importar.");
            return Long(matches[0]["Id"]);
        }
        db.Execute("INSERT INTO Fornecedores (Nome,Documento,Ativo) VALUES (@Nome,@Documento,1);",
            new Dictionary<string, object?> { ["@Nome"] = supplierName.Trim().ToUpperInvariant(), ["@Documento"] = cnpj });
        long id = db.LastInsertRowId;
        if (id <= 0) throw new InvalidOperationException("Não foi possível cadastrar o fornecedor da NF-e.");
        return id;
    }

    private static ParsedInvoice Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > MaxXmlCharacters)
            throw new InvalidOperationException("Selecione um XML de NF-e válido, com até 8 milhões de caracteres.");
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxXmlCharacters,
                MaxCharactersFromEntities = 0,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true
            });
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException exception)
        {
            throw new InvalidOperationException("O XML da NF-e está inválido ou contém conteúdo não permitido.", exception);
        }
        XElement root = document.Root ?? throw new InvalidOperationException("O XML da NF-e está vazio.");
        if (root.Name != NfeNs + "nfeProc")
            throw new InvalidOperationException("Importe o XML nfeProc completo com protocolo de autorização.");
        XElement nfe = Required(root, "NFe");
        XElement infNfe = Required(nfe, "infNFe");
        XElement info = Required(infNfe, "ide");
        XElement supplier = Required(infNfe, "emit");
        XElement recipient = Required(infNfe, "dest");
        XElement protocol = Required(Required(root, "protNFe"), "infProt");

        string key = (string?)infNfe.Attribute("Id") ?? "";
        if (!key.StartsWith("NFe", StringComparison.Ordinal))
            throw new InvalidOperationException("O identificador da NF-e está ausente ou inválido.");
        key = key[3..];
        if (key.Length != 44 || !key.All(AsciiDigit))
            throw new InvalidOperationException("Esta versão local aceita apenas a chave numérica de 44 dígitos. NF-e com CNPJ/chave alfanumérica exige atualização do importador.");
        if (Value(protocol, "chNFe") != key)
            throw new InvalidOperationException("A chave do protocolo não corresponde à chave da NF-e.");
        string status = Value(protocol, "cStat");
        if (status is not ("100" or "150") || !Value(protocol, "nProt").All(AsciiDigit) || Value(protocol, "nProt").Length == 0)
            throw new InvalidOperationException("O XML não contém protocolo de NF-e autorizada.");
        if (Value(info, "mod") != "55")
            throw new InvalidOperationException("Somente NF-e de compra modelo 55 pode ser importada.");
        if (Value(info, "tpAmb") != "1" || Value(protocol, "tpAmb") != "1")
            throw new InvalidOperationException("NF-e de homologação não pode atualizar o estoque real.");

        string supplierCnpj = Value(supplier, "CNPJ");
        string recipientCnpj = Value(recipient, "CNPJ");
        if (supplierCnpj.Length != 14 || recipientCnpj.Length != 14 || !supplierCnpj.All(AsciiDigit) || !recipientCnpj.All(AsciiDigit))
            throw new InvalidOperationException("Esta versão local exige CNPJ numérico de 14 dígitos no emitente e destinatário. CNPJ alfanumérico ainda não é aceito.");
        string number = Value(info, "nNF"), series = Value(info, "serie");
        if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedNumber) || parsedNumber <= 0 ||
            !int.TryParse(series, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedSeries) || parsedSeries < 0)
            throw new InvalidOperationException("Número ou série da NF-e inválidos.");
        string issueText = Value(info, "dhEmi");
        DateTime issueDate;
        if (issueText.Length != 0 && DateTimeOffset.TryParse(issueText, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset issued))
            issueDate = issued.DateTime;
        else if (DateTime.TryParseExact(Value(info, "dEmi"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime issueDay))
            issueDate = issueDay;
        else throw new InvalidOperationException("A data de emissão da NF-e está inválida.");
        if (key.Substring(6, 14) != supplierCnpj || key.Substring(20, 2) != "55" ||
            key.Substring(22, 3) != parsedSeries.ToString("D3", CultureInfo.InvariantCulture) ||
            key.Substring(25, 9) != parsedNumber.ToString("D9", CultureInfo.InvariantCulture) ||
            key.Substring(2, 4) != issueDate.ToString("yyMM", CultureInfo.InvariantCulture) ||
            key.Substring(0, 2) != Value(info, "cUF") || key.Substring(34, 1) != Value(info, "tpEmis") ||
            key.Substring(35, 8) != Value(info, "cNF") || key.Substring(43, 1) != Value(info, "cDV") ||
            !ValidKeyDigit(key))
            throw new InvalidOperationException("A chave não corresponde aos dados de emissão da NF-e.");

        string supplierName = Value(supplier, "xNome"), recipientName = Value(recipient, "xNome");
        if (supplierName.Length == 0 || recipientName.Length == 0)
            throw new InvalidOperationException("O XML deve conter o nome do emitente e do destinatário.");
        XElement total = Required(Required(infNfe, "total"), "ICMSTot");
        decimal invoiceTotal = Decimal(total, "vNF");
        if (invoiceTotal < 0) throw new InvalidOperationException("O valor total da NF-e está inválido.");
        var itemElements = infNfe.Elements(NfeNs + "det").ToList();
        if (itemElements.Count is < 1 or > MaxItems)
            throw new InvalidOperationException("A NF-e precisa conter de 1 a 1.000 itens.");
        var items = new List<ParsedItem>(itemElements.Count);
        var lineNumbers = new HashSet<int>();
        foreach (XElement element in itemElements)
        {
            if (!int.TryParse((string?)element.Attribute("nItem"), NumberStyles.None, CultureInfo.InvariantCulture, out int line) ||
                line <= 0 || !lineNumbers.Add(line))
                throw new InvalidOperationException("A NF-e contém números de item ausentes ou repetidos.");
            XElement product = Required(element, "prod");
            string code = Value(product, "cProd"), name = Value(product, "xProd"), unit = Value(product, "uCom");
            if (code.Length == 0 || name.Length == 0 || unit.Length == 0)
                throw new InvalidOperationException($"O item {line} não contém código, descrição ou unidade comercial.");
            decimal quantity = Decimal(product, "qCom"), unitPrice = Decimal(product, "vUnCom"), itemTotal = Decimal(product, "vProd");
            if (quantity <= 0 || unitPrice < 0 || itemTotal < 0)
                throw new InvalidOperationException($"Quantidade ou valor inválido no item {line}.");
            string gtin = Value(product, "cEAN");
            if (!ValidGtin(gtin)) gtin = Value(product, "cEANTrib");
            if (!ValidGtin(gtin)) gtin = "";
            items.Add(new ParsedItem(line, code, name, gtin, unit, quantity, unitPrice, itemTotal));
        }
        return new ParsedInvoice(key, number, series, issueDate, supplierName, supplierCnpj,
            recipientName, recipientCnpj, invoiceTotal, Value(protocol, "nProt"), items.OrderBy(item => item.LineNumber).ToList());
    }

    private static XElement Required(XElement parent, string child)
        => parent.Element(NfeNs + child) ?? throw new InvalidOperationException($"O XML da NF-e não contém {child}.");
    private static string Value(XElement parent, string child) => ((string?)parent.Element(NfeNs + child) ?? "").Trim();
    private static decimal Decimal(XElement parent, string child)
    {
        string text = Value(parent, child);
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal result))
            throw new InvalidOperationException($"O valor {child} da NF-e está inválido.");
        return result;
    }
    private static string Digits(string? text) => new((text ?? "").Where(AsciiDigit).ToArray());
    private static bool AsciiDigit(char value) => value is >= '0' and <= '9';
    private static bool ValidKeyDigit(string key)
    {
        int weight = 2, sum = 0;
        for (int index = 42; index >= 0; index--)
        {
            sum += (key[index] - '0') * weight;
            weight = weight == 9 ? 2 : weight + 1;
        }
        int digit = 11 - sum % 11;
        if (digit >= 10) digit = 0;
        return key[43] - '0' == digit;
    }
    private static bool ValidGtin(string value)
    {
        if (value.Length is not (8 or 12 or 13 or 14) || !value.All(AsciiDigit)) return false;
        int sum = 0, weight = 3;
        for (int index = value.Length - 2; index >= 0; index--)
        {
            sum += (value[index] - '0') * weight;
            weight = weight == 3 ? 1 : 3;
        }
        return (10 - sum % 10) % 10 == value[^1] - '0';
    }
    private static string Text(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    private static long Long(object? value) => Convert.ToInt64(value, CultureInfo.InvariantCulture);
    private static decimal Number(object? value) => Convert.ToDecimal(value, CultureInfo.InvariantCulture);
    private static DateTime Date(object? value) => DateTime.Parse(Text(value), CultureInfo.InvariantCulture);

    private sealed record ParsedItem(int LineNumber, string SupplierCode, string Name, string Gtin,
        string Unit, decimal Quantity, decimal UnitPrice, decimal Total);
    private sealed record ParsedInvoice(string Key, string Number, string Series, DateTime IssueDate,
        string SupplierName, string SupplierCnpj, string RecipientName, string RecipientCnpj,
        decimal InvoiceTotal, string Protocol, IReadOnlyList<ParsedItem> Items);
    private sealed record ProductForEntry(int Id, string Name, string Unit);
    private sealed record PreparedItem(ParsedItem XmlItem, NfeImportSelection Selection, ProductForEntry Product);
}
