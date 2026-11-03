using System.Text;
using System.Security.Cryptography;
using CaixaLume.Data;
using CaixaLume.Models;
using CaixaLume.Services;

internal static class CentralScenarios
{
    internal static int Run()
    {
        int count=0;
        void Check(string name,Action action){action();Console.WriteLine($"PASSOU central {++count}: {name}");}
        void Block(Action action){try{action();}catch(InvalidOperationException){return;}throw new Exception("Acesso indevido aceito.");}
        DatabaseInitializer.Initialize();
        var users=new UsuarioService();users.CriarAdministradorInicial("Admin central","admincentral","senha-central-123");
        var user=users.Autenticar("admincentral","senha-central-123")!.Value;
        var central=new GestaoCentralService();
        Check("Gestão central exige autenticação",()=>{Block(()=>central.Status());Block(()=>central.Disconnect());Block(()=>central.Connect("https://example.com","x").GetAwaiter().GetResult());});
        SessaoUsuario.Iniciar(user.Id,user.Nome,user.Perfil);
        Check("Instalação começa desconectada",()=>{if(System.Text.Json.JsonSerializer.Serialize(central.Status()).Contains("\"enabled\":true"))throw new Exception();});
        Check("Servidor exige HTTPS e rejeita credenciais, caminhos e parâmetros",()=>{foreach(var url in new[]{"http://example.com","https://user:pass@example.com","https://example.com/path","https://example.com/?key=x","https://example.com/#x"})Block(()=>GestaoCentralService.ValidateServer(url));GestaoCentralService.ValidateServer("https://example.com:443");});
        Check("Credencial protegida pelo Windows e adulteração rejeitada",()=>{var secret=Encoding.UTF8.GetBytes("credencial-de-teste");var encrypted=GestaoCentralService.Protect(secret,true);if(encrypted.SequenceEqual(secret)||!GestaoCentralService.Protect(encrypted,false).SequenceEqual(secret))throw new Exception();encrypted[0]^=1;try{GestaoCentralService.Protect(encrypted,false);throw new Exception("Adulteração aceita");}catch(CryptographicException){}});
        Check("Sem vínculo não há tráfego e caixa continua operacional",()=>{central.SendHeartbeat().GetAwaiter().GetResult();var cash=new CaixaService();cash.Abrir(100);cash.RegistrarMovimentacao("SANGRIA",20,"teste local");if(cash.ObterAberto()!.ValorEsperadoDinheiro!=80)throw new Exception();});
        using var handler=new CentralHandler();using var client=new HttpClient(handler);var linked=new GestaoCentralService(client);
        Check("Vínculo salva credencial protegida e status não a expõe",()=>{linked.Connect("https://gestao.example",new string('A',64)).GetAwaiter().GetResult();string status=System.Text.Json.JsonSerializer.Serialize(linked.Status());if(!status.Contains("\"enabled\":true")||status.Contains(new string('B',64)))throw new Exception();var bytes=File.ReadAllBytes(DatabaseInitializer.DatabasePath+".gestao.protected");if(Encoding.UTF8.GetString(bytes).Contains(new string('B',64)))throw new Exception();});
        Check("Heartbeat só envia versão e schema usando credencial própria",()=>{linked.SendHeartbeat().GetAwaiter().GetResult();if(handler.Payload is null || handler.Payload.Contains("Nome") || handler.Payload.Contains("senha") || handler.Auth!="Bearer "+new string('B',64))throw new Exception();var payload=System.Text.Json.JsonDocument.Parse(handler.Payload);if(payload.RootElement.EnumerateObject().Count()!=2||!payload.RootElement.TryGetProperty("schemaVersion",out _))throw new Exception();});
        Check("Falha de rede não impede consulta e movimentação local",()=>{handler.Fail=true;linked.SendHeartbeat().GetAwaiter().GetResult();var cash=new CaixaService();cash.RegistrarMovimentacao("SUPRIMENTO",10,"teste offline");if(cash.ObterAberto()!.ValorEsperadoDinheiro!=90)throw new Exception();handler.Fail=false;});
        Check("Revogação recusa comunicação sem bloquear caixa",()=>{handler.Revoked=true;linked.SendHeartbeat().GetAwaiter().GetResult();if(!System.Text.Json.JsonSerializer.Serialize(linked.Status()).Contains("recusado"))throw new Exception();handler.Revoked=false;});
        Check("Desconexão remove vínculo e interrompe envio",()=>{linked.Disconnect();int before=handler.Requests;linked.SendHeartbeat().GetAwaiter().GetResult();if(handler.Requests!=before||File.Exists(DatabaseInitializer.DatabasePath+".gestao.protected"))throw new Exception();});
        Check("Recusas de vínculo distinguem HTTP sem expor credencial nem salvar conexão",()=>{
            foreach(int status in new[]{401,429,404,400,500}){
                handler.EnrollStatus=status;
                try{linked.Connect("https://gestao.example",new string('A',64)).GetAwaiter().GetResult();throw new Exception("Recusa não propagada");}
                catch(InvalidOperationException error){if(!error.Message.Contains($"HTTP {status}")||error.Message.Contains(new string('A',64)))throw new Exception("Diagnóstico inválido");}
                if(File.Exists(DatabaseInitializer.DatabasePath+".gestao.protected"))throw new Exception("Recusa salvou vínculo");
            }
            handler.EnrollStatus=200;
        });
        users.Salvar(0,"Operador","operadorcentral","OPERADOR","senha-operador-123");var op=users.Autenticar("operadorcentral","senha-operador-123")!.Value;SessaoUsuario.Iniciar(op.Id,op.Nome,op.Perfil);
        Check("Operador não consulta nem altera vínculo",()=>{Block(()=>central.Status());Block(()=>central.Disconnect());Block(()=>central.Connect("https://example.com",new string('A',64)).GetAwaiter().GetResult());});
        Console.WriteLine($"SUCESSO: {count} cenários centrais locais.");return 0;
    }
}

internal sealed class CentralHandler : HttpMessageHandler
{
    internal string? Payload,Auth;internal bool Fail,Revoked;internal int Requests;internal int EnrollStatus=200;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {
        Requests++;if(Fail)throw new HttpRequestException("falha simulada");
        if(request.RequestUri!.AbsolutePath=="/api/enroll")return new((System.Net.HttpStatusCode)EnrollStatus){Content=System.Net.Http.Json.JsonContent.Create(new{establishmentId=Guid.NewGuid().ToString(),installationId=Guid.NewGuid().ToString(),token=new string('B',64)})};
        Payload=await request.Content!.ReadAsStringAsync(cancellationToken);Auth=request.Headers.Authorization?.ToString();return new(Revoked?System.Net.HttpStatusCode.Unauthorized:System.Net.HttpStatusCode.NoContent);
    }
}
