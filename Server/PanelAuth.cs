using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CaixaLume.Data;
using Microsoft.AspNetCore.DataProtection;

internal sealed class PanelAuth(string path, IDataProtectionProvider protection)
{
    private readonly IDataProtector _protector=protection.CreateProtector("CaixaLume.Panel.Totp.v1");
    internal string CookieName => "CaixaLumePanel-"+Registry.Hash(path)[..12];
    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private DatabaseConnection Open()=>new(path);
    internal void Initialize()
    {
        using var db=Open();
        db.ExecuteScript("""
            CREATE TABLE IF NOT EXISTS PanelAccount(Id INTEGER PRIMARY KEY CHECK(Id=1), Login TEXT NOT NULL UNIQUE, PasswordHash TEXT NOT NULL, Totp TEXT NOT NULL, LastStep INTEGER NOT NULL DEFAULT -1, Failures INTEGER NOT NULL DEFAULT 0, LockedUntil INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS PanelSetup(Hash TEXT PRIMARY KEY, Login TEXT NOT NULL, PasswordHash TEXT NOT NULL, Totp TEXT NOT NULL, Expires INTEGER NOT NULL, Kind TEXT NOT NULL, Failures INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS PanelSessions(Hash TEXT PRIMARY KEY, AccountId INTEGER NOT NULL REFERENCES PanelAccount(Id), Csrf TEXT NOT NULL, Expires INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS PanelRecovery(Hash TEXT PRIMARY KEY, AccountId INTEGER NOT NULL REFERENCES PanelAccount(Id));
            CREATE TABLE IF NOT EXISTS PanelAudit(Id INTEGER PRIMARY KEY AUTOINCREMENT, Created INTEGER NOT NULL, Action TEXT NOT NULL);
            """);
        db.Execute("DELETE FROM PanelSessions WHERE Expires<@Now;",Args(("@Now",Now)));
        db.Execute("DELETE FROM PanelSetup WHERE Expires<@Now;",Args(("@Now",Now)));
    }
    internal bool Configured(){using var db=Open();return db.Query("SELECT Id FROM PanelAccount;").Count>0;}
    internal static bool Equal(string a,string b)=>CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)),SHA256.HashData(Encoding.UTF8.GetBytes(b)));
    private static Dictionary<string,object?> Args(params (string,object?)[] values)=>values.ToDictionary(v=>v.Item1,v=>v.Item2);
    private static void Audit(DatabaseConnection db,string action)=>db.Execute("INSERT INTO PanelAudit(Created,Action) VALUES(@Now,@Action);",Args(("@Now",Now),("@Action",action)));
    private static string Normalize(string value)=>value.Trim().ToLowerInvariant();
    private static void Validate(string login,string password)
    {
        if(login.Length<3||login.Length>64||login.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not '.' and not '_' and not '-'))throw new ArgumentException("Use um login de 3 a 64 letras, números, ponto, hífen ou sublinhado.");
        if(password.Length<14||password.Length>256||string.IsNullOrWhiteSpace(password))throw new ArgumentException("Use uma senha exclusiva de 14 a 256 caracteres.");
    }
    private static string PasswordHash(string password)
    {
        byte[] salt=RandomNumberGenerator.GetBytes(16);
        byte[] hash=Rfc2898DeriveBytes.Pbkdf2(password,salt,600000,HashAlgorithmName.SHA256,32);
        return Convert.ToBase64String(salt)+":"+Convert.ToBase64String(hash);
    }
    private static bool VerifyPassword(string password,string stored)
    {
        try{var parts=stored.Split(':');return parts.Length==2&&CryptographicOperations.FixedTimeEquals(Rfc2898DeriveBytes.Pbkdf2(password,Convert.FromBase64String(parts[0]),600000,HashAlgorithmName.SHA256,32),Convert.FromBase64String(parts[1]));}
        catch(FormatException){return false;}
    }
    internal object BeginSetup(string login,string password)
    {
        login=Normalize(login);Validate(login,password);
        using var db=Open();if(db.Query("SELECT Id FROM PanelAccount;").Count>0)throw new ArgumentException("O acesso inicial já foi configurado.");
        string secret=Base32(RandomNumberGenerator.GetBytes(20)),challenge=Registry.Secret();
        db.Execute("DELETE FROM PanelSetup WHERE Expires<@Now;",Args(("@Now",Now)));
        db.Execute("INSERT INTO PanelSetup(Hash,Login,PasswordHash,Totp,Expires,Kind) VALUES(@Hash,@Login,@Password,@Totp,@Expires,'INITIAL');",Args(("@Hash",Registry.Hash(challenge)),("@Login",login),("@Password",PasswordHash(password)),("@Totp",_protector.Protect(secret)),("@Expires",Now+600)));
        return SetupView(login,secret,challenge);
    }
    private static object SetupView(string login,string secret,string challenge)=>new{challenge,secret,uri="otpauth://totp/"+Uri.EscapeDataString("CaixaLume:"+login)+"?secret="+secret+"&issuer=CaixaLume&algorithm=SHA1&digits=6&period=30"};
    internal (Session Session,string[] Recovery)? FinishSetup(string challenge,string code)
    {
        using var db=Open();db.Execute("BEGIN IMMEDIATE;");
        try{
            var row=db.Query("SELECT * FROM PanelSetup WHERE Hash=@Hash AND Expires>@Now AND Kind='INITIAL' AND Failures<5;",Args(("@Hash",Registry.Hash(challenge)),("@Now",Now))).FirstOrDefault();
            if(row is null||db.Query("SELECT Id FROM PanelAccount;").Count>0){db.Execute("ROLLBACK;");return null;}
            long step=Match(_protector.Unprotect((string)row["Totp"]!),code,-1);
            if(step<0){db.Execute("UPDATE PanelSetup SET Failures=Failures+1 WHERE Hash=@Hash;",Args(("@Hash",Registry.Hash(challenge))));db.Execute("COMMIT;");return null;}
            db.Execute("INSERT INTO PanelAccount(Id,Login,PasswordHash,Totp,LastStep) VALUES(1,@Login,@Password,@Totp,@Step);",Args(("@Login",row["Login"]),("@Password",row["PasswordHash"]),("@Totp",row["Totp"]),("@Step",step)));
            db.Execute("DELETE FROM PanelSetup;");string[] recovery=Recovery(db);var session=Issue(db,(string)row["Login"]!,false);Audit(db,"SETUP_COMPLETED");db.Execute("COMMIT;");return(session,recovery);
        }catch{db.Execute("ROLLBACK;");throw;}
    }
    internal Session? Login(string login,string password,string code,bool remember)
    {
        if(password.Length>256||code.Length>80)return null;
        using var db=Open();db.Execute("BEGIN IMMEDIATE;");
        try{
            var row=db.Query("SELECT * FROM PanelAccount WHERE Id=1;").FirstOrDefault();
            if(row is null){db.Execute("ROLLBACK;");return null;}
            bool passwordValid=VerifyPassword(password,(string)row["PasswordHash"]!);
            bool valid=Equal(Normalize(login),(string)row["Login"]!)&&Convert.ToInt64(row["LockedUntil"])<=Now&&passwordValid;
            if(valid)valid=UseFactor(db,row,code);
            if(!valid){Fail(db);db.Execute("COMMIT;");return null;}
            db.Execute("UPDATE PanelAccount SET Failures=0,LockedUntil=0 WHERE Id=1;");var session=Issue(db,(string)row["Login"]!,remember);Audit(db,"LOGIN");db.Execute("COMMIT;");return session;
        }catch{db.Execute("ROLLBACK;");throw;}
    }
    private static void Fail(DatabaseConnection db)
    {
        db.Execute("UPDATE PanelAccount SET Failures=CASE WHEN LockedUntil>@Now THEN Failures WHEN Failures>=4 THEN 0 ELSE Failures+1 END,LockedUntil=CASE WHEN LockedUntil>@Now THEN LockedUntil WHEN Failures>=4 THEN @Until ELSE 0 END WHERE Id=1;",Args(("@Now",Now),("@Until",Now+300)));
        Audit(db,"LOGIN_REFUSED");
    }
    private bool UseFactor(DatabaseConnection db,Dictionary<string,object?> row,string code)
    {
        long step=Match(_protector.Unprotect((string)row["Totp"]!),code,Convert.ToInt64(row["LastStep"]));
        if(step>=0){db.Execute("UPDATE PanelAccount SET LastStep=@Step WHERE Id=1;",Args(("@Step",step)));return true;}
        string normalized=code.Trim().Replace("-","").ToUpperInvariant();
        if(normalized.Length!=32||!normalized.All(char.IsAsciiHexDigit))return false;
        db.Execute("DELETE FROM PanelRecovery WHERE Hash=@Hash AND AccountId=1;",Args(("@Hash",Registry.Hash(normalized))));return db.Changes==1;
    }
    internal Session? Get(string? token)
    {
        if(token?.Length!=64)return null;using var db=Open();
        var row=db.Query("SELECT a.Login,s.Csrf,s.Expires FROM PanelSessions s JOIN PanelAccount a ON a.Id=s.AccountId WHERE s.Hash=@Hash AND s.Expires>@Now;",Args(("@Hash",Registry.Hash(token)),("@Now",Now))).FirstOrDefault();
        return row is null?null:new(token,(string)row["Login"]!,(string)row["Csrf"]!,Convert.ToInt64(row["Expires"]));
    }
    internal void Logout(string token,bool all)
    {
        using var db=Open();if(all)db.Execute("DELETE FROM PanelSessions;");else db.Execute("DELETE FROM PanelSessions WHERE Hash=@Hash;",Args(("@Hash",Registry.Hash(token))));Audit(db,all?"LOGOUT_ALL":"LOGOUT");
    }
    internal Session? ChangePassword(string current,string next,string code)
    {
        using var db=Open();db.Execute("BEGIN IMMEDIATE;");
        try{
            var row=db.Query("SELECT * FROM PanelAccount WHERE Id=1;").Single();Validate((string)row["Login"]!,next);
            if(current.Length>256||code.Length>80||Convert.ToInt64(row["LockedUntil"])>Now||!VerifyPassword(current,(string)row["PasswordHash"]!)||!UseFactor(db,row,code)){Fail(db);db.Execute("COMMIT;");return null;}
            db.Execute("UPDATE PanelAccount SET PasswordHash=@Password,Failures=0,LockedUntil=0 WHERE Id=1;",Args(("@Password",PasswordHash(next))));db.Execute("DELETE FROM PanelSessions;");var session=Issue(db,(string)row["Login"]!,false);Audit(db,"PASSWORD_CHANGED");db.Execute("COMMIT;");return session;
        }catch{db.Execute("ROLLBACK;");throw;}
    }
    internal object? BeginTotpReset(string password)
    {
        using var db=Open();var row=db.Query("SELECT * FROM PanelAccount WHERE Id=1;").Single();
        if(password.Length>256||Convert.ToInt64(row["LockedUntil"])>Now||!VerifyPassword(password,(string)row["PasswordHash"]!)){Fail(db);return null;}
        string secret=Base32(RandomNumberGenerator.GetBytes(20)),challenge=Registry.Secret();
        db.Execute("INSERT INTO PanelSetup(Hash,Login,PasswordHash,Totp,Expires,Kind) VALUES(@Hash,@Login,'',@Totp,@Expires,'RESET');",Args(("@Hash",Registry.Hash(challenge)),("@Login",row["Login"]),("@Totp",_protector.Protect(secret)),("@Expires",Now+600)));
        return SetupView((string)row["Login"]!,secret,challenge);
    }
    internal (Session Session,string[] Recovery)? FinishTotpReset(string challenge,string code)
    {
        using var db=Open();db.Execute("BEGIN IMMEDIATE;");
        try{
            var row=db.Query("SELECT * FROM PanelSetup WHERE Hash=@Hash AND Expires>@Now AND Kind='RESET' AND Failures<5;",Args(("@Hash",Registry.Hash(challenge)),("@Now",Now))).FirstOrDefault();
            if(row is null){db.Execute("ROLLBACK;");return null;}
            long step=Match(_protector.Unprotect((string)row["Totp"]!),code,-1);
            if(step<0){db.Execute("UPDATE PanelSetup SET Failures=Failures+1 WHERE Hash=@Hash;",Args(("@Hash",Registry.Hash(challenge))));db.Execute("COMMIT;");return null;}
            db.Execute("UPDATE PanelAccount SET Totp=@Totp,LastStep=@Step WHERE Id=1;",Args(("@Totp",row["Totp"]),("@Step",step)));db.Execute("DELETE FROM PanelSetup;");db.Execute("DELETE FROM PanelSessions;");string[] recovery=Recovery(db);var session=Issue(db,(string)row["Login"]!,false);Audit(db,"TOTP_CHANGED");db.Execute("COMMIT;");return(session,recovery);
        }catch{db.Execute("ROLLBACK;");throw;}
    }
    private static Session Issue(DatabaseConnection db,string login,bool remember)
    {
        db.Execute("DELETE FROM PanelSessions WHERE Expires<=@Now;",Args(("@Now",Now)));
        // Limita a quantidade de sessões persistentes sem invalidar a que será criada.
        db.Execute("DELETE FROM PanelSessions WHERE Hash IN (SELECT Hash FROM PanelSessions ORDER BY Expires DESC LIMIT -1 OFFSET 19);");
        var session=new Session(Registry.Secret(),login,Registry.Secret(),Now+(remember?604800:43200));
        db.Execute("INSERT INTO PanelSessions(Hash,AccountId,Csrf,Expires) VALUES(@Hash,1,@Csrf,@Expires);",Args(("@Hash",Registry.Hash(session.Token)),("@Csrf",session.Csrf),("@Expires",session.Expires)));return session;
    }
    private static string[] Recovery(DatabaseConnection db)
    {
        db.Execute("DELETE FROM PanelRecovery;");string[] codes=Enumerable.Range(0,10).Select(_=>Convert.ToHexString(RandomNumberGenerator.GetBytes(16))).ToArray();
        foreach(string code in codes)db.Execute("INSERT INTO PanelRecovery(Hash,AccountId) VALUES(@Hash,1);",Args(("@Hash",Registry.Hash(code))));return codes;
    }
    internal static string Base32(byte[] bytes)
    {
        const string alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";var result=new StringBuilder();int buffer=0,bits=0;
        foreach(byte b in bytes){buffer=(buffer<<8)|b;bits+=8;while(bits>=5){bits-=5;result.Append(alphabet[(buffer>>bits)&31]);}}if(bits>0)result.Append(alphabet[(buffer<<(5-bits))&31]);return result.ToString();
    }
    private static byte[] Decode32(string text)
    {
        const string alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";var result=new List<byte>();int buffer=0,bits=0;
        foreach(char c in text){int value=alphabet.IndexOf(c);if(value<0)throw new FormatException();buffer=(buffer<<5)|value;bits+=5;if(bits>=8){bits-=8;result.Add((byte)(buffer>>bits));}}return result.ToArray();
    }
    internal static string Totp(string secret,long step)
    {
        Span<byte> counter=stackalloc byte[8];BinaryPrimitives.WriteInt64BigEndian(counter,step);byte[] hash=HMACSHA1.HashData(Decode32(secret),counter);int offset=hash[^1]&15;int number=BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset,4))&0x7fffffff;return(number%1000000).ToString("D6",CultureInfo.InvariantCulture);
    }
    private static long Match(string secret,string code,long last)
    {
        if(code.Length!=6||!code.All(char.IsAsciiDigit))return -1;long now=Now/30;
        foreach(long step in new[]{now,now-1,now+1})if(step>last&&Equal(Totp(secret,step),code))return step;return -1;
    }
    internal sealed record Session(string Token,string Login,string Csrf,long Expires);
}
