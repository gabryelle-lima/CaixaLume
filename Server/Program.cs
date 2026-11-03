using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;

if (!OperatingSystem.IsWindows())
    NativeLibrary.SetDllImportResolver(typeof(Registry).Assembly, (name, assembly, path) =>
        name == "winsqlite3.dll" ? NativeLibrary.Load("libsqlite3.so.0") : IntPtr.Zero);
var builder=WebApplication.CreateBuilder(args);
string admin=Environment.GetEnvironmentVariable("CAIXALUME_ADMIN_TOKEN") ?? "";
string database=Environment.GetEnvironmentVariable("CAIXALUME_MANAGEMENT_DB") ?? "";
if(!Path.IsPathFullyQualified(database)) throw new InvalidOperationException("Configure CAIXALUME_MANAGEMENT_DB com caminho absoluto persistente, fora da pasta pública.");
string publicRoot=Path.GetFullPath(builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath,"wwwroot"))+Path.DirectorySeparatorChar;
if(Path.GetFullPath(database).StartsWith(publicRoot,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))
    throw new InvalidOperationException("O banco central não pode ficar na pasta pública.");
var registry=new Registry(database); registry.Initialize();
string keyDirectory=database+".authkeys";
Directory.CreateDirectory(keyDirectory);
if(!OperatingSystem.IsWindows())File.SetUnixFileMode(keyDirectory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
var protection=builder.Services.AddDataProtection().SetApplicationName("CaixaLume.Management.Panel").PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
if(OperatingSystem.IsWindows())protection.ProtectKeysWithDpapi();
builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=4096);
builder.Services.AddRateLimiter(o=>{
    o.RejectionStatusCode=429;
    o.AddFixedWindowLimiter("auth", x=>{x.PermitLimit=30;x.Window=TimeSpan.FromMinutes(1);x.QueueLimit=0;});
    o.AddFixedWindowLimiter("api", x=>{x.PermitLimit=120;x.Window=TimeSpan.FromMinutes(1);x.QueueLimit=0;});
});
var app=builder.Build();
var auth=new PanelAuth(database,app.Services.GetRequiredService<IDataProtectionProvider>());auth.Initialize();
if(!auth.Configured()&&admin.Length<64) throw new InvalidOperationException("Configure CAIXALUME_ADMIN_TOKEN com pelo menos 64 caracteres aleatórios para o primeiro cadastro.");
app.Use(async(context,next)=>{
    context.Response.Headers.CacheControl="no-store";
    context.Response.Headers["X-Content-Type-Options"]="nosniff";
    context.Response.Headers["Referrer-Policy"]="no-referrer";
    context.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self'; frame-ancestors 'none'; base-uri 'none'";
    bool localTest=app.Environment.IsDevelopment() && context.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip);
    if(!context.Request.IsHttps && !localTest){context.Response.StatusCode=400;return;}
    bool adminPath=context.Request.Path.StartsWithSegments("/admin");
    bool authPath=context.Request.Path.StartsWithSegments("/auth");
    if(adminPath||authPath){
        bool localAdmin=context.Connection.RemoteIpAddress is {} peer && IPAddress.IsLoopback(peer)
            && context.Request.Host.Host is "localhost" or "127.0.0.1" or "::1" or "[::1]";
        if(!localAdmin){context.Response.StatusCode=403;return;}
        bool write=!HttpMethods.IsGet(context.Request.Method)&&!HttpMethods.IsHead(context.Request.Method);
        if(write){
            string origin=context.Request.Headers.Origin.ToString();
            string expectedOrigin=context.Request.Scheme+"://"+context.Request.Host;
            if(context.Request.Headers["X-CaixaLume-Request"]!="1" || (origin.Length>0&&!string.Equals(origin,expectedOrigin,StringComparison.OrdinalIgnoreCase)) || context.Request.Headers["Sec-Fetch-Site"]=="cross-site"){
                context.Response.StatusCode=403;return;
            }
        }
        if(adminPath||context.Request.Path.StartsWithSegments("/auth/logout")||context.Request.Path.StartsWithSegments("/auth/security")){
            var session=auth.Get(context.Request.Cookies[auth.CookieName]);
            if(session is null){context.Response.StatusCode=401;return;}
            if(write&&!PanelAuth.Equal(session.Csrf,context.Request.Headers["X-CSRF-Token"].ToString())){context.Response.StatusCode=403;return;}
            context.Items["PanelSession"]=session;
        }
    }
    try {await next(context);} catch(ArgumentException error){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(new{message=error.Message});}
});
app.UseRateLimiter();
app.UseDefaultFiles();app.UseStaticFiles();
var api=app.MapGroup("").RequireRateLimiting("api");
api.MapGet("/auth/status",()=>Results.Ok(new{configured=auth.Configured()}));
api.MapGet("/auth/session",(HttpContext ctx)=>{
    var session=auth.Get(ctx.Request.Cookies[auth.CookieName]);
    return session is null?Results.Unauthorized():Results.Ok(new{user=session.Login,csrf=session.Csrf,expires=DateTimeOffset.FromUnixTimeSeconds(session.Expires)});
});
api.MapPost("/auth/setup/start",(SetupRequest r)=>{
    if(auth.Configured()||!PanelAuth.Equal(admin,r.BootstrapToken??""))return Results.Unauthorized();
    return Results.Ok(auth.BeginSetup(r.Login??"",r.Password??""));
}).RequireRateLimiting("auth");
api.MapPost("/auth/setup/finish",(HttpContext ctx,FactorRequest r)=>{
    var result=auth.FinishSetup(r.Challenge??"",r.Code??"");if(result is null)return Results.Unauthorized();
    SetCookie(ctx,auth.CookieName,result.Value.Session);
    return Results.Ok(new{recoveryCodes=result.Value.Recovery});
}).RequireRateLimiting("auth");
api.MapPost("/auth/login",(HttpContext ctx,LoginRequest r)=>{
    var session=auth.Login(r.Login??"",r.Password??"",r.Code??"",r.Remember);if(session is null)return Results.Unauthorized();
    SetCookie(ctx,auth.CookieName,session);return Results.Ok(new{user=session.Login,csrf=session.Csrf,expires=DateTimeOffset.FromUnixTimeSeconds(session.Expires)});
}).RequireRateLimiting("auth");
api.MapPost("/auth/logout",(HttpContext ctx)=>{
    auth.Logout(Current(ctx).Token,false);ctx.Response.Cookies.Delete(auth.CookieName,CookieOptions(ctx));return Results.NoContent();
});
api.MapPost("/auth/security/logout-all",(HttpContext ctx)=>{
    auth.Logout(Current(ctx).Token,true);ctx.Response.Cookies.Delete(auth.CookieName,CookieOptions(ctx));return Results.NoContent();
});
api.MapPost("/auth/security/password",(HttpContext ctx,PasswordRequest r)=>{
    var session=auth.ChangePassword(r.CurrentPassword??"",r.NewPassword??"",r.Code??"");if(session is null)return Results.Unauthorized();
    SetCookie(ctx,auth.CookieName,session);return Results.Ok(new{csrf=session.Csrf});
}).RequireRateLimiting("auth");
api.MapPost("/auth/security/totp/start",(ResetRequest r)=>auth.BeginTotpReset(r.Password??"") is {} result?Results.Ok(result):Results.Unauthorized()).RequireRateLimiting("auth");
api.MapPost("/auth/security/totp/finish",(HttpContext ctx,FactorRequest r)=>{
    var result=auth.FinishTotpReset(r.Challenge??"",r.Code??"");if(result is null)return Results.Unauthorized();
    SetCookie(ctx,auth.CookieName,result.Value.Session);return Results.Ok(new{recoveryCodes=result.Value.Recovery});
}).RequireRateLimiting("auth");
api.MapGet("/health",()=>Results.Ok(new { status="ok" }));
api.MapPost("/admin/stores",(StoreRequest r)=>Results.Ok(registry.CreateStore(r.Name ?? "")));
api.MapGet("/admin/stores",()=>Results.Ok(registry.List()));
api.MapPost("/admin/stores/{id}/invites",(string id)=>registry.Invite(id) is {} r?Results.Ok(r):Results.NotFound());
api.MapPost("/admin/installations/{id}/revoke",(string id)=>registry.Revoke(id)?Results.NoContent():Results.NotFound());
api.MapPost("/api/enroll",(EnrollRequest r)=>registry.Enroll(r.Code ?? "") is {} result?Results.Ok(result):Results.Unauthorized());
api.MapPost("/api/heartbeat",(HttpContext ctx,HeartbeatRequest r)=>registry.Heartbeat(Bearer(ctx),r.Version ?? "",r.SchemaVersion)?Results.NoContent():Results.Unauthorized());
app.Run();
static PanelAuth.Session Current(HttpContext ctx)=>(PanelAuth.Session)ctx.Items["PanelSession"]!;
static CookieOptions CookieOptions(HttpContext ctx)=>new(){HttpOnly=true,Secure=ctx.Request.IsHttps,SameSite=SameSiteMode.Strict,Path="/",IsEssential=true};
static void SetCookie(HttpContext ctx,string name,PanelAuth.Session session){var options=CookieOptions(ctx);options.Expires=DateTimeOffset.FromUnixTimeSeconds(session.Expires);ctx.Response.Cookies.Append(name,session.Token,options);}
static string Bearer(HttpContext c){string h=c.Request.Headers.Authorization.ToString();return h.StartsWith("Bearer ",StringComparison.Ordinal)?h[7..]:"";}
record StoreRequest(string? Name);
record EnrollRequest(string? Code);
record HeartbeatRequest(string? Version,int SchemaVersion);

record SetupRequest(string? BootstrapToken,string? Login,string? Password);
record FactorRequest(string? Challenge,string? Code);
record LoginRequest(string? Login,string? Password,string? Code,bool Remember);
record PasswordRequest(string? CurrentPassword,string? NewPassword,string? Code);
record ResetRequest(string? Password);
