using System.Net.Http.Headers;
using System.Text.Json;

namespace MercadinhoPdv.Services;

public sealed record ProdutoCatalogoPublico(string CodigoBarras, string Nome, string? Marca, string? Categoria, string? ImagemUrl);

public sealed class OpenFoodFactsService
{
    private static readonly HttpClient DefaultClient = CreateClient();
    private readonly HttpClient _client;

    public OpenFoodFactsService(HttpClient? client = null) => _client = client ?? DefaultClient;

    public async Task<ProdutoCatalogoPublico?> BuscarPorCodigoAsync(string codigoBarras, CancellationToken cancellationToken = default)
    {
        string code = new((codigoBarras ?? "").Where(char.IsDigit).ToArray());
        if (code.Length is < 8 or > 14) throw new InvalidOperationException("Informe um código de barras com 8 a 14 dígitos.");
        try { return await ConsultarAsync(code, cancellationToken); }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("O catálogo demorou para responder. Tente novamente ou continue o cadastro manual.", exception);
        }
        catch (JsonException exception) { throw new InvalidOperationException("O catálogo retornou uma resposta inválida. O cadastro manual continua disponível.", exception); }
    }

    private async Task<ProdutoCatalogoPublico?> ConsultarAsync(string code, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync($"https://world.openfoodfacts.org/api/v3/product/{Uri.EscapeDataString(code)}.json?lc=pt&cc=br&fields=code,product_name,product_name_pt,product_name_pt-BR,generic_name,generic_name_pt,brands,categories,image_front_url,image_url,selected_images", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"O catálogo está indisponível (HTTP {(int)response.StatusCode}). Tente mais tarde ou continue manualmente.", null, response.StatusCode);
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("status", out var status)) throw new InvalidOperationException("O catálogo retornou uma resposta sem status.");
        if (!IsSuccessfulStatus(status)) return null;
        if (!root.TryGetProperty("product", out var product) || product.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("O catálogo não retornou dados válidos do produto.");
        string? name = FirstString(product, "product_name_pt", "product_name_pt-BR", "product_name", "generic_name_pt", "generic_name");
        if (string.IsNullOrWhiteSpace(name)) return null;
        string? brand = FirstString(product, "brands");
        string? category = FirstString(product, "categories");
        string? image = FirstString(product, "image_front_url", "image_url");
        if (image is null && product.TryGetProperty("selected_images", out var selected)
            && selected.TryGetProperty("front", out var front) && front.TryGetProperty("display", out var display))
            image = display.EnumerateObject().Select(x => x.Value.ValueKind == JsonValueKind.String ? x.Value.GetString() : null).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        return new ProdutoCatalogoPublico(code, name.Trim(), Clean(brand), Clean(category), Clean(image));
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MercadinhoPdv/1.0 (Windows desktop product catalog)");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("pt-BR,pt;q=0.9,en;q=0.7");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static string? FirstString(JsonElement element, params string[] keys)
    {
        foreach (string key in keys)
            if (element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())) return value.GetString();
        return null;
    }

    private static bool IsSuccessfulStatus(JsonElement status) => status.ValueKind switch
    {
        JsonValueKind.Number => status.TryGetInt32(out int value) && value == 1,
        JsonValueKind.String => status.GetString() is "success" or "success_with_errors",
        _ => false
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
