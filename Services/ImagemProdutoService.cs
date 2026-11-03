using System.Security.Cryptography;
using CaixaLume.Data;

namespace CaixaLume.Services;

public sealed class ImagemProdutoService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(6) };
    private const int MaxBytes = 5 * 1024 * 1024;

    public async Task<string?> CachearUrlAsync(string? url, string? codigoBarras, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.Equals("openfoodfacts.org", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".openfoodfacts.org", StringComparison.OrdinalIgnoreCase))) return null;
        string directory = CacheDirectory();
        string urlHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(uri.AbsoluteUri)))[..12];
        string barcodePart = string.IsNullOrWhiteSpace(codigoBarras) ? "produto" : new string(codigoBarras.Where(char.IsLetterOrDigit).ToArray());
        string key = $"{barcodePart}-{urlHash}";
        string finalPath = Path.Combine(directory, $"{key}.jpg");
        if (File.Exists(finalPath)) return finalPath;
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > MaxBytes) return null;
        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0 || buffer.Length > MaxBytes) return null;
        string temporary = Path.Combine(directory, $".{key}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, buffer.ToArray(), cancellationToken);
            using (var image = Image.FromFile(temporary)) { if (image.Width > 12000 || image.Height > 12000) return null; }
            File.Move(temporary, finalPath, true);
            return finalPath;
        }
        catch (ArgumentException) { return null; }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public string ImportarArquivo(string sourcePath)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("A imagem selecionada não foi encontrada.", sourcePath);
        var info = new FileInfo(sourcePath);
        if (info.Length is <= 0 or > MaxBytes) throw new InvalidOperationException("A imagem deve ter até 5 MB.");
        using (var image = Image.FromFile(sourcePath))
        {
            if (image.Width > 12000 || image.Height > 12000) throw new InvalidOperationException("A resolução da imagem é muito grande.");
        }
        string destination = Path.Combine(CacheDirectory(), $"manual-{Guid.NewGuid():N}{Path.GetExtension(sourcePath).ToLowerInvariant()}");
        File.Copy(sourcePath, destination);
        return destination;
    }

    public static bool TryOpen(string? path, out Image? image)
    {
        image = null;
        string? localPath = ResolveExistingPath(path);
        if (localPath is null) return false;
        try { using var source = Image.FromFile(localPath); image = new Bitmap(source); return true; }
        catch (Exception ex) when (ex is ArgumentException or IOException or OutOfMemoryException) { return false; }
    }

    public static string? ResolveExistingPath(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return null;
        if (File.Exists(storedPath)) return storedPath;
        // Bancos antigos guardam caminhos absolutos. Ao recuperar banco e imagens em
        // outro perfil Windows, o arquivo pode estar na pasta local com o mesmo nome.
        string fileName = Path.GetFileName(storedPath);
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        string restoredPath = Path.Combine(CacheDirectory(), fileName);
        return File.Exists(restoredPath) ? restoredPath : null;
    }

    private static string CacheDirectory()
    {
        string path = string.IsNullOrWhiteSpace(CaixaLume.Data.CaminhosAplicacao.BancoConfigurado)
            ? CaminhosAplicacao.Imagens
            : Path.Combine(Path.GetDirectoryName(DatabaseInitializer.DatabasePath)!, "imagens");
        Directory.CreateDirectory(path);
        return path;
    }
}
