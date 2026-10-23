using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using System.Drawing.Imaging;
using MercadinhoPdv.Models;

namespace MercadinhoPdv.Services;

/// <summary>Contrato pequeno e versionado entre a tela local e os serviços existentes.</summary>
internal sealed class PdvWebBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private readonly ProdutoService _products = new();
    private readonly VendaService _sales = new();
    private readonly CaixaService _cash = new();
    private static readonly ConcurrentDictionary<string, string> ImageCache = new();

    public object Handle(string message)
    {
        if (message.Length > 64_000) throw new InvalidOperationException("Mensagem excede o tamanho permitido.");
        var request = JsonSerializer.Deserialize<BridgeRequest>(message, JsonOptions)
            ?? throw new InvalidOperationException("Mensagem vazia ou inválida.");
        if (request.Version != 1 || string.IsNullOrWhiteSpace(request.Id) || request.Id.Length > 80)
            throw new InvalidOperationException("Versão ou identificador da mensagem inválido.");

        object result = request.Action switch
        {
            "pdv.getData" => GetPdvData(),
            "pdv.checkout" => CompleteSale(request.Payload),
            "pdv.operationStatus" => GetOperationStatus(request.Payload),
            "pdv.openCash" => OpenCash(request.Payload),
            _ => throw new InvalidOperationException("Ação não autorizada pela ponte do aplicativo.")
        };
        return new BridgeResponse(1, request.Id, true, result, null);
    }

    private object GetPdvData()
    {
        var products = _products.Listar(null).Where(product => product.Ativo).OrderBy(product => product.Nome)
            .Select(product => new ProductDto(product.Id, product.Nome, product.CodigoBarras ?? string.Empty, product.CategoriaNome ?? "OUTROS", decimal.Round(product.PrecoVenda, 2, MidpointRounding.AwayFromZero),
                product.UnidadeMedida, product.EstoqueAtual, product.EstoqueMinimo, GetImageData(product.LocalImagePath)))
            .ToList();
        return new PdvDataDto(products, ["TODOS", .. products.Select(product => product.Category).Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase)],
            _cash.ObterAberto() is not null, SessaoUsuario.UsuarioId, SessaoUsuario.Nome, SessaoUsuario.Perfil, new ConfiguracaoService().ObterNomeLoja(), new AjustesVendaService().ObterPolitica());
    }

    private static string? GetImageData(string? path)
    {
        path = ImagemProdutoService.ResolveExistingPath(path);
        if (path is null) return null;
        var info = new FileInfo(path);
        if (info.Length <= 0 || info.Length > 5 * 1024 * 1024) return null;
        string key = $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        if (ImageCache.TryGetValue(key, out string? cached)) return cached;
        if (!ImagemProdutoService.TryOpen(path, out Image? source) || source is null) return null;
        using (source)
        using (var thumbnail = new Bitmap(96, 96))
        using (var graphics = Graphics.FromImage(thumbnail))
        using (var output = new MemoryStream())
        {
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            double scale = Math.Min(96d / source.Width, 96d / source.Height);
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));
            graphics.DrawImage(source, (96 - width) / 2, (96 - height) / 2, width, height);
            thumbnail.Save(output, ImageFormat.Png);
            string imageData = "data:image/png;base64," + Convert.ToBase64String(output.ToArray());
            if (ImageCache.Count > 500) ImageCache.Clear();
            ImageCache.TryAdd(key, imageData);
            return imageData;
        }
    }

    private object CompleteSale(JsonElement payload)
    {
        CheckoutRequest? checkout = payload.Deserialize<CheckoutRequest>(JsonOptions)
            ?? throw new InvalidOperationException("Dados de pagamento inválidos.");
        if (checkout.Items is null || checkout.Items.Count is < 1 or > 250) throw new InvalidOperationException("A venda precisa conter de 1 a 250 itens.");
        string payment = checkout.PaymentMethod?.Trim().ToUpperInvariant() ?? string.Empty;
        if (payment is not ("DINHEIRO" or "PIX" or "DÉBITO" or "CRÉDITO")) throw new InvalidOperationException("Forma de pagamento inválida.");

        var catalog = _products.Listar(null).ToDictionary(product => product.Id);
        var items = new List<ItemCarrinho>(checkout.Items.Count);
        foreach (CheckoutItem item in checkout.Items)
        {
            if (item.ProductId <= 0 || item.Quantity <= 0 || item.Quantity > 100_000) throw new InvalidOperationException("Produto ou quantidade inválidos.");
            if (!catalog.TryGetValue(item.ProductId, out Produto? product)) throw new InvalidOperationException("Um dos produtos não está mais disponível.");
            items.Add(new ItemCarrinho { ProdutoId = product.Id, Produto = product.Nome, CodigoBarras = product.CodigoBarras,
                UnidadeMedida = product.UnidadeMedida, Quantidade = item.Quantity, PrecoUnitario = product.PrecoVenda, EstoqueDisponivel = product.EstoqueAtual });
        }
        long saleId = _sales.Registrar(items, payment, checkout.AmountReceived, checkout.Adjustments, checkout.OperationId, checkout.ExpectedTotal);
        decimal total = _sales.ObterTotais(saleId).Total;
        decimal received = payment == "DINHEIRO" ? checkout.AmountReceived : total;
        return new CheckoutResult(saleId, total, payment, received, payment == "DINHEIRO" ? received - total : 0m);
    }

    private object OpenCash(JsonElement payload)
    {
        CashOpenRequest? request = payload.Deserialize<CashOpenRequest>(JsonOptions)
            ?? throw new InvalidOperationException("Informe o saldo inicial do caixa.");
        if (request.OpeningBalance < 0 || request.OpeningBalance > 1_000_000_000) throw new InvalidOperationException("Saldo inicial inválido.");
        long cashId = _cash.Abrir(request.OpeningBalance);
        return new CashOpenedResult(cashId, request.OpeningBalance);
    }

    private object GetOperationStatus(JsonElement payload)
    {
        string? operationId = payload.GetProperty("operationId").GetString();
        VendaOperacao? sale = _sales.ObterPorOperacao(operationId);
        return sale is null
            ? new OperationStatusResult(false, null, null, null, null, null, null)
            : new OperationStatusResult(true, sale.Id, sale.Status, sale.Total, sale.FormaPagamento, sale.ValorRecebido, sale.Troco);
    }

    public static object Error(string id, string message) => new BridgeResponse(1, id, false, null, message);

    private sealed record BridgeRequest(int Version, string Id, string Action, JsonElement Payload);
    private sealed record BridgeResponse(int Version, string Id, bool Ok, object? Data, string? Error);
    private sealed record ProductDto(int Id, string Name, string Barcode, string Category, decimal Price, string Unit, decimal Stock, decimal MinimumStock, string? ImageData);
    private sealed record PdvDataDto(List<ProductDto> Products, List<string> Categories, bool CashOpen, int? UserId, string UserName, string UserRole, string StoreName, PoliticaAjustesVenda AdjustmentsPolicy);
    private sealed record CheckoutRequest(List<CheckoutItem>? Items, string? PaymentMethod, decimal AmountReceived, AjusteVenda? Adjustments, string? OperationId, decimal? ExpectedTotal);
    private sealed record CheckoutItem(int ProductId, decimal Quantity);
    private sealed record CheckoutResult(long SaleId, decimal Total, string PaymentMethod, decimal AmountReceived, decimal Change);
    private sealed record CashOpenRequest(decimal OpeningBalance);
    private sealed record CashOpenedResult(long CashId, decimal OpeningBalance);
    private sealed record OperationStatusResult(bool Found, long? SaleId, string? Status, decimal? Total, string? PaymentMethod, decimal? AmountReceived, decimal? Change);
}
