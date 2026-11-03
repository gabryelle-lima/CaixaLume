using System.Text.Json;
using System.Text.Json.Serialization;
using CaixaLume.Data;
using System.Drawing.Imaging;
using CaixaLume.Models;

namespace CaixaLume.Services;

/// <summary>Contrato pequeno e versionado entre a tela local e os serviços existentes.</summary>
internal sealed class PdvWebBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private readonly VendaService _sales = new();
    private readonly CaixaService _cash = new();
    private static readonly object ImageLock = new();
    private static readonly Dictionary<string,string> ImageCache = new();
    private static readonly Queue<string> ImageKeys = new();
    private static int ImageBytes;
    private const int MaxImageBytes = 8 * 1024 * 1024;

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
            "pdv.getImages" => GetImages(request.Payload),
            "pdv.checkout" => CompleteSale(request.Payload),
            "pdv.operationStatus" => GetOperationStatus(request.Payload),
            "pdv.openCash" => OpenCash(request.Payload),
            _ => throw new InvalidOperationException("Ação não autorizada pela ponte do aplicativo.")
        };
        return new BridgeResponse(1, request.Id, true, result, null);
    }

    private object GetPdvData()
    {
        Autorizacao.ExigirUsuario();
        var products = new ProdutoRepository().ListarParaPdv()
            .Select(product => new ProductDto(product.Id, product.Nome, product.CodigoBarras ?? string.Empty, product.CategoriaNome ?? "OUTROS", decimal.Round(product.PrecoVenda, 2, MidpointRounding.AwayFromZero),
                product.UnidadeMedida, product.EstoqueAtual, product.EstoqueMinimo, null, !string.IsNullOrWhiteSpace(product.LocalImagePath),product.Versao))
            .ToList();
        return new PdvDataDto(products, ["TODOS", .. products.Select(product => product.Category).Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase)],
            _cash.ObterAberto() is not null, SessaoUsuario.UsuarioId, SessaoUsuario.Nome, SessaoUsuario.Perfil, new ConfiguracaoService().ObterNomeLoja(), new AjustesVendaService().ObterPolitica());
    }

    private object GetImages(JsonElement payload)
    {
        Autorizacao.ExigirUsuario();
        if (!payload.TryGetProperty("ids",out var values) || values.ValueKind!=JsonValueKind.Array || values.GetArrayLength()>32)
            throw new InvalidOperationException("Solicite até 32 imagens por vez.");
        int[] ids=values.EnumerateArray().Select(v=>v.TryGetInt32(out int id)&&id>0?id:throw new InvalidOperationException("Produto inválido.")).Distinct().ToArray();
        using var db=new DatabaseConnection(DatabaseInitializer.DatabasePath);
        var args=ids.Select((id,index)=>(id,index)).ToDictionary(x=>"@Id"+x.index,x=>(object?)x.id);
        if(ids.Length==0)return new Dictionary<int,string?>();
        var rows=db.Query("SELECT Id,LocalImagePath FROM Produtos WHERE Ativo=1 AND ExcluidoEm IS NULL AND Id IN ("+string.Join(",",args.Keys)+");",args);
        return rows.ToDictionary(r=>Convert.ToInt32(r["Id"]),r=>GetImageData(Convert.ToString(r["LocalImagePath"])));
    }

    private static string? GetImageData(string? path)
    {
        try
        {
            path=ImagemProdutoService.ResolveExistingPath(path);
            if(path is null)return null;
            var info=new FileInfo(path);
            if(info.Length<=0 || info.Length>5*1024*1024)return null;
            string key=$"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            // Serialização limitada à geração de miniaturas evita decodificações simultâneas da mesma imagem.
            lock(ImageLock)
            {
                if(ImageCache.TryGetValue(key,out var cached))return cached;
                if(!ImagemProdutoService.TryOpen(path,out Image? source) || source is null)return null;
                using(source)
                using(var thumbnail=new Bitmap(96,96))
                using(var graphics=Graphics.FromImage(thumbnail))
                using(var output=new MemoryStream())
                {
                    graphics.Clear(Color.Transparent);graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    double scale=Math.Min(96d/source.Width,96d/source.Height);
                    int width=Math.Max(1,(int)Math.Round(source.Width*scale)),height=Math.Max(1,(int)Math.Round(source.Height*scale));
                    graphics.DrawImage(source,(96-width)/2,(96-height)/2,width,height);thumbnail.Save(output,ImageFormat.Png);
                    string data="data:image/png;base64,"+Convert.ToBase64String(output.ToArray());
                    int bytes=data.Length*2;
                    while(ImageKeys.Count>0 && (ImageCache.Count>=128 || ImageBytes+bytes>MaxImageBytes))
                    { string oldest=ImageKeys.Dequeue();ImageBytes-=ImageCache[oldest].Length*2;ImageCache.Remove(oldest); }
                    if(bytes<=MaxImageBytes){ImageCache[key]=data;ImageKeys.Enqueue(key);ImageBytes+=bytes;}
                    return data;
                }
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.ExternalException)
        { return null; } // Uma foto indisponível não impede a operação do caixa.
    }

    private object CompleteSale(JsonElement payload)
    {
        CheckoutRequest? checkout = payload.Deserialize<CheckoutRequest>(JsonOptions)
            ?? throw new InvalidOperationException("Dados de pagamento inválidos.");
        if (checkout.Items is null || checkout.Items.Count is < 1 or > 250) throw new InvalidOperationException("A venda precisa conter de 1 a 250 itens.");
        string payment = checkout.PaymentMethod?.Trim().ToUpperInvariant() ?? string.Empty;
        if (payment is not ("DINHEIRO" or "PIX" or "DÉBITO" or "CRÉDITO" or "MISTO")) throw new InvalidOperationException("Forma de pagamento inválida.");

        if (!Guid.TryParse(checkout.OperationId, out var operation) || operation == Guid.Empty)
            throw new InvalidOperationException("Informe um identificador único para finalizar a venda.");
        if (!checkout.ExpectedTotal.HasValue) throw new InvalidOperationException("Confira o total antes de finalizar a venda.");
        if (checkout.PaymentStatus is not null && checkout.PaymentStatus != "REGISTRADO_MANUALMENTE")
            throw new InvalidOperationException("O fluxo manual não confirma pagamentos externos.");
        var items = checkout.Items.Select(item => (ProdutoId: item.ProductId, Quantidade: item.Quantity)).ToList();
        long saleId = _sales.RegistrarPorProdutoIds(items, payment, checkout.AmountReceived, checkout.Adjustments, checkout.OperationId, checkout.ExpectedTotal, checkout.Payments);
        VendaOperacao sale = _sales.ObterPorOperacao(checkout.OperationId) ?? throw new InvalidOperationException("Consulte o histórico para verificar o resultado da venda.");
        return new CheckoutResult(saleId, sale.Total, sale.FormaPagamento, sale.ValorRecebido, sale.Troco);
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
    private sealed record ProductDto(int Id, string Name, string Barcode, string Category, decimal Price, string Unit, decimal Stock, decimal MinimumStock, string? ImageData, bool HasImage = false, long? ImageVersion = null);
    private sealed record PdvDataDto(List<ProductDto> Products, List<string> Categories, bool CashOpen, int? UserId, string UserName, string UserRole, string StoreName, PoliticaAjustesVenda AdjustmentsPolicy);
    private sealed record CheckoutRequest(List<CheckoutItem>? Items, string? PaymentMethod, decimal AmountReceived, AjusteVenda? Adjustments, string? OperationId, decimal? ExpectedTotal, List<PagamentoVenda>? Payments = null, string? PaymentStatus = null);
    private sealed record CheckoutItem(int ProductId, decimal Quantity);
    private sealed record CheckoutResult(long SaleId, decimal Total, string PaymentMethod, decimal AmountReceived, decimal Change, string PaymentStatus = "REGISTRADO_MANUALMENTE");
    private sealed record CashOpenRequest(decimal OpeningBalance);
    private sealed record CashOpenedResult(long CashId, decimal OpeningBalance);
    private sealed record OperationStatusResult(bool Found, long? SaleId, string? Status, decimal? Total, string? PaymentMethod, decimal? AmountReceived, decimal? Change);
}
