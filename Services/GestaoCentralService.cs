using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using CaixaLume.Data;

namespace CaixaLume.Services;

/// <summary>Comunicação opcional; nunca participa de transações de venda.</summary>
internal sealed class GestaoCentralService
{
    private sealed record Connection(string Server, string EstablishmentId, string InstallationId, string Token);
    private static readonly SemaphoreSlim Gate = new(1,1);
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=TimeSpan.FromSeconds(8), MaxResponseContentBufferSize=16384 };
    private readonly HttpClient _client;
    internal GestaoCentralService(HttpClient? client=null) { _client=client ?? Client; }
    private static string PathConfig => DatabaseInitializer.DatabasePath + ".gestao.protected";
    private static string _status="Ainda não conectado nesta execução";
    private static DateTimeOffset? _lastSuccess;
    internal object Status()
    {
        Autorizacao.ExigirAdministrador();
        try {var c=Read(); return new { enabled=c is not null, server=c?.Server, establishmentId=c?.EstablishmentId, installationId=c?.InstallationId, status=_status, lastSuccess=_lastSuccess };}
        catch { return new { enabled=false, status="Vínculo indisponível neste usuário Windows. Desconecte e vincule novamente." }; }
    }
    internal static Uri ValidateServer(string value)
    {
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri) || uri.Scheme!="https" || !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath!="/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Informe o endereço HTTPS do servidor, sem caminho, usuário ou parâmetros.");
        return uri;
    }
    internal async Task Connect(string address,string code)
    {
        Autorizacao.ExigirAdministrador();
        var server=ValidateServer(address.Trim());
        if(code.Length!=64 || !code.All(char.IsAsciiHexDigit)) throw new InvalidOperationException("Informe o código temporário fornecido pelo suporte.");
        await Gate.WaitAsync();
        try {
            if(File.Exists(PathConfig)) throw new InvalidOperationException("Desconecte o vínculo atual antes de conectar outro.");
            using var response=await _client.PostAsJsonAsync(new Uri(server,"api/enroll"),new { code });
            if(!response.IsSuccessStatusCode)
            {
                string reason = (int)response.StatusCode switch
                {
                    401 => "O servidor recusou o código: ele pode estar incorreto, expirado, já utilizado ou ter sido gerado em outro servidor. Gere um código no painel do Aurora.",
                    429 => "O servidor recebeu muitas tentativas. Aguarde um minuto antes de tentar novamente.",
                    404 => "Esse endereço não oferece o serviço de vínculo. Confira o endereço e a porta do servidor.",
                    400 => "O servidor recusou o formato da solicitação de vínculo. Solicite a verificação da versão do serviço.",
                    >= 500 => "O serviço apresentou uma falha interna. Consulte os registros no Aurora antes de gerar outro código.",
                    _ => "O servidor recusou a solicitação. Informe o código HTTP ao suporte."
                };
                throw new InvalidOperationException($"{reason} (HTTP {(int)response.StatusCode})");
            }
            var result=await response.Content.ReadFromJsonAsync<JsonElement>();
            var connection=new Connection(server.AbsoluteUri,result.GetProperty("establishmentId").GetString()!,result.GetProperty("installationId").GetString()!,result.GetProperty("token").GetString()!);
            if(!Guid.TryParse(connection.EstablishmentId,out _) || !Guid.TryParse(connection.InstallationId,out _) || connection.Token.Length!=64) throw new InvalidOperationException("Resposta de vínculo inválida.");
            Autorizacao.ExigirAdministrador();
            Write(connection); _status="Vinculado; aguardando comunicação";
        } finally {Gate.Release();}
    }
    internal void Disconnect()
    {
        Autorizacao.ExigirAdministrador(); Gate.Wait();
        try { if(File.Exists(PathConfig)) File.Delete(PathConfig); _status="Desconectado";_lastSuccess=null; }
        finally {Gate.Release();}
    }
    internal async Task SendHeartbeat(CancellationToken cancellationToken=default)
    {
        if(!await Gate.WaitAsync(0,cancellationToken)) return;
        try {
            var c=Read(); if(c is null)return;
            var server=ValidateServer(c.Server);
            using var db=new DatabaseConnection(DatabaseInitializer.DatabasePath);
            int schema=Convert.ToInt32(db.Query("SELECT MAX(Version) AS Version FROM SchemaMigrations;").Single()["Version"]);
            string version=typeof(GestaoCentralService).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
            using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(server,"api/heartbeat"));
            request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",c.Token);
            request.Content=JsonContent.Create(new { version, schemaVersion=schema });
            using var response=await _client.SendAsync(request,cancellationToken);
            if(response.IsSuccessStatusCode){_lastSuccess=DateTimeOffset.UtcNow;_status="Conectado";}
            else _status=response.StatusCode==System.Net.HttpStatusCode.Unauthorized?"Vínculo recusado pelo servidor; contate o suporte":"Servidor indisponível";
        } catch(Exception e) when(e is HttpRequestException or IOException or JsonException or CryptographicException or InvalidOperationException or OperationCanceledException or UnauthorizedAccessException){_status="Sem comunicação; o caixa continua funcionando localmente";}
        finally {Gate.Release();}
    }
    internal static async Task Run(CancellationToken cancellationToken)
    {
        var service=new GestaoCentralService();
        try {while(!cancellationToken.IsCancellationRequested){await service.SendHeartbeat(cancellationToken);await Task.Delay(TimeSpan.FromMinutes(5),cancellationToken);}}
        catch(OperationCanceledException) { }
    }
    private static Connection? Read() => !File.Exists(PathConfig)?null:JsonSerializer.Deserialize<Connection>(Protect(File.ReadAllBytes(PathConfig),false));
    private static void Write(Connection c)
    {
        string temporary=PathConfig+".tmp-"+Guid.NewGuid().ToString("N");
        try {File.WriteAllBytes(temporary,Protect(JsonSerializer.SerializeToUtf8Bytes(c),true));File.Move(temporary,PathConfig);}
        finally {if(File.Exists(temporary))File.Delete(temporary);}
    }
    // DPAPI no escopo do usuário Windows; credencial não entra no banco, JS ou backup comercial.
    internal static byte[] Protect(byte[] bytes,bool encrypt)
    {
        var input=new Blob { Length=bytes.Length, Data=Marshal.AllocHGlobal(bytes.Length) };
        try {
            Marshal.Copy(bytes,0,input.Data,bytes.Length);
            Blob output;
            bool ok=encrypt?CryptProtectData(ref input,null,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output):CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if(!ok)throw new CryptographicException("Não foi possível proteger a credencial neste usuário Windows.");
            try {var result=new byte[output.Length];Marshal.Copy(output.Data,result,0,result.Length);return result;} finally {LocalFree(output.Data);}
        } finally {Marshal.FreeHGlobal(input.Data);}
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob {public int Length;public IntPtr Data;}
    [DllImport("crypt32.dll",SetLastError=true,CharSet=CharSet.Unicode)] private static extern bool CryptProtectData(ref Blob input,string? description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("crypt32.dll",SetLastError=true)] private static extern bool CryptUnprotectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
