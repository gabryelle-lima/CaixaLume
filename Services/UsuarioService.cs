using System.Security.Cryptography;
using CaixaLume.Data;

namespace CaixaLume.Services;

public sealed class UsuarioService
{
    private const int Iterations = 210_000;

    public bool PrecisaAdministradorInicial()
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        return !db.Query("SELECT 1 FROM Usuarios WHERE SenhaHash IS NOT NULL AND SenhaHash <> '' LIMIT 1;").Any();
    }

    public void CriarAdministradorInicial(string nome, string login, string senha)
    {
        CriarAdministradorInicial(nome, login, senha, senha);
    }

    public void CriarAdministradorInicial(string nome, string login, string senha, string confirmacaoSenha)
    {
        nome = (nome ?? string.Empty).Trim().ToUpperInvariant();
        login = (login ?? string.Empty).Trim().ToLowerInvariant();
        if (nome.Length == 0) throw new InvalidOperationException("Informe o nome.");
        if (login.Length == 0) throw new InvalidOperationException("Informe o login.");
        if (login.Length < 3) throw new InvalidOperationException("O login deve possuir pelo menos 3 caracteres.");
        if (string.IsNullOrWhiteSpace(senha)) throw new InvalidOperationException("Informe a senha.");
        ValidarNovaSenha(senha);
        if (!string.Equals(senha, confirmacaoSenha, StringComparison.Ordinal)) throw new InvalidOperationException("As senhas não coincidem.");
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        db.Execute("BEGIN IMMEDIATE;");
        try
        {
            if (db.Query("SELECT 1 FROM Usuarios WHERE SenhaHash IS NOT NULL AND SenhaHash <> '' LIMIT 1;").Any()) throw new InvalidOperationException("A configuração inicial já foi concluída.");
            string hash = HashPassword(senha);
            var existing = db.Query("SELECT Id, SenhaHash FROM Usuarios WHERE Login=@Login LIMIT 1;", new Dictionary<string, object?> { ["@Login"] = login }).FirstOrDefault();
            if (existing is null)
                db.Execute("INSERT INTO Usuarios (Nome, Login, SenhaHash, Perfil, Ativo) VALUES (@Nome, @Login, @Hash, 'ADMINISTRADOR', 1);", new Dictionary<string, object?> { ["@Nome"] = nome, ["@Login"] = login, ["@Hash"] = hash });
            else
            {
                if (!string.IsNullOrWhiteSpace(Convert.ToString(existing.GetValueOrDefault("SenhaHash"))))
                    throw new InvalidOperationException("Já existe um administrador com este login.");
                db.Execute("UPDATE Usuarios SET Nome=@Nome, SenhaHash=@Hash, Perfil='ADMINISTRADOR', Ativo=1 WHERE Id=@Id;", new Dictionary<string, object?> { ["@Nome"] = nome, ["@Hash"] = hash, ["@Id"] = existing["Id"] });
            }
            db.Execute("COMMIT;");
        }
        catch (Exception exception)
        {
            try { db.Execute("ROLLBACK;"); } catch { /* preserva o erro original da operação */ }
            if (exception.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Já existe um administrador com este login.", exception);
            throw;
        }
    }

    public (int Id, string Nome, string Perfil)? Autenticar(string login, string senha)
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrEmpty(senha)) return null;
        var user = db.Query("SELECT Id, Nome, Login, Perfil, SenhaHash FROM Usuarios WHERE Login=@Login AND Ativo=1 LIMIT 1;", new Dictionary<string, object?> { ["@Login"] = login.Trim().ToLowerInvariant() }).FirstOrDefault();
        string? stored = Convert.ToString(user?.GetValueOrDefault("SenhaHash"));
        if (user is null || !VerifyPassword(senha, stored)) return null;
        db.Execute("INSERT INTO Auditoria (UsuarioId,DataHora,Acao,Entidade,Descricao) VALUES (@Usuario,@Data,'LOGIN','SESSAO',@Descricao);",new Dictionary<string,object?>{["@Usuario"]=user["Id"],["@Data"]=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),["@Descricao"]=$"Usuário {Convert.ToString(user["Login"])} autenticado"});
        return (Convert.ToInt32(user["Id"]), Convert.ToString(user["Nome"]) ?? "", Convert.ToString(user["Perfil"]) ?? "OPERADOR");
    }

    public List<UsuarioLinha> Listar()
    {
        Autorizacao.ExigirAdministrador();
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        return db.Query("SELECT Id,Nome,Login,Perfil,Ativo FROM Usuarios ORDER BY Nome;").Select(r => new UsuarioLinha { Id=Convert.ToInt32(r["Id"]),Nome=Convert.ToString(r["Nome"])??"",Login=Convert.ToString(r["Login"])??"",Perfil=Convert.ToString(r["Perfil"])??"OPERADOR",Ativo=Convert.ToInt32(r["Ativo"])==1 }).ToList();
    }

    public void Salvar(int id,string nome,string login,string perfil,string? senha=null)
    {
        Autorizacao.ExigirAdministrador();
        nome=nome.Trim().ToUpperInvariant();login=login.Trim().ToLowerInvariant();perfil=perfil.Trim().ToUpperInvariant();
        if(nome.Length<2||login.Length<3||perfil is not ("OPERADOR" or "ADMINISTRADOR"))throw new InvalidOperationException("Confira nome, login e perfil.");
        if(id==0 || !string.IsNullOrEmpty(senha)) ValidarNovaSenha(senha);
        using var db=new DatabaseConnection(DatabaseInitializer.DatabasePath);db.Execute("BEGIN IMMEDIATE;");
        try
        {
            Autorizacao.ExigirUsuario(db, administrador: true);
            if (id != 0)
            {
                if (!db.Query("SELECT 1 FROM Usuarios WHERE Id=@Id;", new Dictionary<string,object?> { ["@Id"]=id }).Any())
                    throw new InvalidOperationException("Usuário não encontrado.");
            }
            if(id==0)db.Execute("INSERT INTO Usuarios (Nome,Login,SenhaHash,Perfil,Ativo) VALUES (@Nome,@Login,@Hash,@Perfil,1);",new Dictionary<string,object?>{["@Nome"]=nome,["@Login"]=login,["@Hash"]=HashPassword(senha!),["@Perfil"]=perfil});
            else if(string.IsNullOrEmpty(senha))db.Execute("UPDATE Usuarios SET Nome=@Nome,Login=@Login,Perfil=@Perfil WHERE Id=@Id;",new Dictionary<string,object?>{["@Nome"]=nome,["@Login"]=login,["@Perfil"]=perfil,["@Id"]=id});
            else db.Execute("UPDATE Usuarios SET Nome=@Nome,Login=@Login,Perfil=@Perfil,SenhaHash=@Hash WHERE Id=@Id;",new Dictionary<string,object?>{["@Nome"]=nome,["@Login"]=login,["@Perfil"]=perfil,["@Hash"]=HashPassword(senha),["@Id"]=id});
            db.Execute("INSERT INTO Auditoria (UsuarioId,DataHora,Acao,Entidade,Descricao) VALUES (@Usuario,@Data,@Acao,'USUARIO',@Descricao);",new Dictionary<string,object?>{["@Usuario"]=Models.SessaoUsuario.UsuarioId,["@Data"]=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),["@Acao"]=id==0?"CRIACAO":"EDICAO",["@Descricao"]=$"Usuário {login}; perfil {perfil}"});db.Execute("COMMIT;");
        }catch{db.Execute("ROLLBACK;");throw;}
    }

    public void Desativar(int id)
    {
        Autorizacao.ExigirAdministrador();
        if(Models.SessaoUsuario.UsuarioId==id)throw new InvalidOperationException("Não é possível desativar o usuário conectado.");
        using var db=new DatabaseConnection(DatabaseInitializer.DatabasePath);
        db.Execute("BEGIN IMMEDIATE;");
        try
        {
            int ator = Autorizacao.ExigirUsuario(db, administrador: true);
            if (!db.Query("SELECT 1 FROM Usuarios WHERE Id=@Id;", new Dictionary<string,object?> { ["@Id"]=id }).Any())
                throw new InvalidOperationException("Usuário não encontrado.");
            db.Execute("UPDATE Usuarios SET Ativo=0 WHERE Id=@Id;",new Dictionary<string,object?>{["@Id"]=id});
            db.Execute("INSERT INTO Auditoria (UsuarioId,DataHora,Acao,Entidade,Descricao) VALUES (@Usuario,@Data,'DESATIVACAO','USUARIO',@Descricao);",new Dictionary<string,object?>{["@Usuario"]=ator,["@Data"]=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),["@Descricao"]=$"Usuário #{id} desativado."});
            db.Execute("COMMIT;");
        }
        catch { db.Execute("ROLLBACK;"); throw; }
    }

    public void Reativar(int id)
    {
        Autorizacao.ExigirAdministrador();
        using var db=new DatabaseConnection(DatabaseInitializer.DatabasePath);
        db.Execute("BEGIN IMMEDIATE;");
        try
        {
            Autorizacao.ExigirUsuario(db, administrador: true);
            var user=db.Query("SELECT Login,Ativo FROM Usuarios WHERE Id=@Id;",new Dictionary<string,object?>{["@Id"]=id}).SingleOrDefault()??throw new InvalidOperationException("Usuário não encontrado.");
            if(Convert.ToInt32(user["Ativo"])==1)throw new InvalidOperationException("O usuário já está ativo.");
            db.Execute("UPDATE Usuarios SET Ativo=1 WHERE Id=@Id;",new Dictionary<string,object?>{["@Id"]=id});
            db.Execute("INSERT INTO Auditoria (UsuarioId,DataHora,Acao,Entidade,Descricao) VALUES (@Usuario,@Data,'REATIVACAO','USUARIO',@Descricao);",new Dictionary<string,object?>{["@Usuario"]=Models.SessaoUsuario.UsuarioId,["@Data"]=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),["@Descricao"]=$"Usuário {Convert.ToString(user["Login"])} reativado."});
            db.Execute("COMMIT;");
        }
        catch{db.Execute("ROLLBACK;");throw;}
    }

    private static void ValidarNovaSenha(string? senha)
    {
        if (string.IsNullOrWhiteSpace(senha) || senha.Length < 10)
            throw new InvalidOperationException("A senha deve possuir pelo menos 10 caracteres.");
    }

    private static string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string? encoded)
    {
        try
        {
            var parts = encoded?.Split('$');
            if (parts is not { Length: 4 } || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out int iterations) || iterations < 100_000 || iterations > 2_000_000) return false;
            byte[] salt = Convert.FromBase64String(parts[2]), expected = Convert.FromBase64String(parts[3]);
            if (salt.Length < 16 || salt.Length > 64 || expected.Length != 32) return false;
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}

public sealed class UsuarioLinha{public int Id{get;set;}public string Nome{get;set;}="";public string Login{get;set;}="";public string Perfil{get;set;}="OPERADOR";public bool Ativo{get;set;}}
