using System.Security.Cryptography;
using System.Text;
using CaixaLume.Data;

internal sealed class Registry(string path)
{
    internal static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private DatabaseConnection Open() => new(path);
    internal void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var db = Open();
        db.Execute("CREATE TABLE IF NOT EXISTS Stores(Id TEXT PRIMARY KEY, Name TEXT NOT NULL);");
        db.Execute("CREATE TABLE IF NOT EXISTS Invites(Hash TEXT PRIMARY KEY, StoreId TEXT NOT NULL REFERENCES Stores(Id), Expires TEXT NOT NULL, Used INTEGER NOT NULL DEFAULT 0);");
        db.Execute("CREATE TABLE IF NOT EXISTS Devices(Id TEXT PRIMARY KEY, StoreId TEXT NOT NULL REFERENCES Stores(Id), SecretHash TEXT NOT NULL UNIQUE, Revoked INTEGER NOT NULL DEFAULT 0, LastSeen TEXT, Version TEXT, SchemaVersion INTEGER);");
    }
    internal object CreateStore(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120) throw new ArgumentException("Informe um nome de até 120 caracteres.");
        string id = Guid.NewGuid().ToString("D"); using var db = Open();
        db.Execute("INSERT INTO Stores(Id,Name) VALUES(@Id,@Name);", new Dictionary<string,object?>{{"@Id",id},{"@Name",name.Trim()}});
        return new { id, name = name.Trim() };
    }
    internal object? Invite(string storeId)
    {
        using var db = Open();
        if (!db.Query("SELECT Id FROM Stores WHERE Id=@Id;", new Dictionary<string,object?>{{"@Id",storeId}}).Any()) return null;
        string code=Secret(), expires=DateTimeOffset.UtcNow.AddMinutes(30).ToString("O");
        db.Execute("INSERT INTO Invites(Hash,StoreId,Expires) VALUES(@Hash,@Store,@Expires);",new Dictionary<string,object?>{{"@Hash",Hash(code)},{"@Store",storeId},{"@Expires",expires}});
        return new { code, expires };
    }
    internal object? Enroll(string code)
    {
        if (code.Length != 64) return null;
        using var db=Open(); db.Execute("BEGIN IMMEDIATE;");
        try {
            var args=new Dictionary<string,object?>{{"@Hash",Hash(code)},{"@Now",DateTimeOffset.UtcNow.ToString("O")}};
            var invite=db.Query("SELECT StoreId FROM Invites WHERE Hash=@Hash AND Used=0 AND Expires>@Now;",args).FirstOrDefault();
            if(invite is null){db.Execute("ROLLBACK;");return null;}
            string id=Guid.NewGuid().ToString("D"), token=Secret(), store=Convert.ToString(invite["StoreId"])!;
            db.Execute("UPDATE Invites SET Used=1 WHERE Hash=@Hash;",new Dictionary<string,object?>{{"@Hash",Hash(code)}});
            db.Execute("INSERT INTO Devices(Id,StoreId,SecretHash) VALUES(@Id,@Store,@Hash);",new Dictionary<string,object?>{{"@Id",id},{"@Store",store},{"@Hash",Hash(token)}});
            db.Execute("COMMIT;"); return new { installationId=id, establishmentId=store, token };
        } catch {db.Execute("ROLLBACK;");throw;}
    }
    internal bool Heartbeat(string token, string version, int schema)
    {
        if(token.Length!=64 || version.Length>40 || !Version.TryParse(version,out _) || schema<1 || schema>10000) return false;
        using var db=Open();
        db.Execute("UPDATE Devices SET LastSeen=@Now,Version=@Version,SchemaVersion=@Schema WHERE SecretHash=@Hash AND Revoked=0;",new Dictionary<string,object?>{{"@Now",DateTimeOffset.UtcNow.ToString("O")},{"@Version",version},{"@Schema",schema},{"@Hash",Hash(token)}});
        return db.Changes==1;
    }
    internal object List()
    {
        using var db=Open(); return db.Query("SELECT s.Id AS establishmentId,s.Name AS name,d.Id AS installationId,d.Revoked AS revoked,d.LastSeen AS lastSeen,d.Version AS version,d.SchemaVersion AS schemaVersion FROM Stores s LEFT JOIN Devices d ON d.StoreId=s.Id ORDER BY s.Name,d.Id;");
    }
    internal bool Revoke(string id)
    {
        using var db=Open();db.Execute("UPDATE Devices SET Revoked=1 WHERE Id=@Id;",new Dictionary<string,object?>{{"@Id",id}});return db.Changes==1;
    }
}
