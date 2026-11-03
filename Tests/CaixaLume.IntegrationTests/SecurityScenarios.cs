using CaixaLume.Data;
using CaixaLume.Models;
using CaixaLume.Services;

internal static class SecurityScenarios
{
    internal static int Run()
    {
        int passed = 0;
        void Check(string name, Action action) { action(); Console.WriteLine($"PASSOU segurança {++passed}: {name}"); }
        var users = new UsuarioService();
        DatabaseInitializer.Initialize();
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        int Id(string login) => Convert.ToInt32(db.Query("SELECT Id FROM Usuarios WHERE Login=@Login;", new Dictionary<string, object?> { ["@Login"] = login }).Single()["Id"]);
        void Login(string login) { var u = users.Autenticar(login, "senha-segura-123")!.Value; SessaoUsuario.Iniciar(u.Id, u.Nome, u.Perfil); }
        Check("Bootstrap exige senha forte e não armazena texto puro", () =>
        {
            Blocked(() => users.CriarAdministradorInicial("ADMIN", "admin", "curta"));
            users.CriarAdministradorInicial("ADMIN", "admin", "senha-segura-123");
            string hash = Convert.ToString(db.Query("SELECT SenhaHash FROM Usuarios;").Single()["SenhaHash"])!;
            Assert(hash.StartsWith("pbkdf2-sha256$210000$") && !hash.Contains("senha-segura-123"));
            Assert(users.Autenticar(" ADMIN ", "senha-segura-123") is not null);
            Assert(users.Autenticar("admin", "errada") is null);
            Assert(users.Autenticar("' OR 1=1 --", "senha-segura-123") is null);
            Assert(users.Autenticar(null!, null!) is null);
        });
        int admin = Id("admin");
        Check("Sem sessão não administra usuários nem permissões", () =>
        {
            SessaoUsuario.Encerrar();
            Blocked(() => users.Listar()); Blocked(() => users.Salvar(0,"INVASOR","invasor","ADMINISTRADOR","senha-segura-123"));
            Blocked(() => users.Desativar(admin)); Blocked(() => users.Reativar(admin));
            Blocked(() => Autorizacao.ConfigurarPermissaoUsuario(admin, true));
        });
        Login("admin");
        Check("Último administrador não pode ser rebaixado, desativado ou excluído", () =>
        {
            Blocked(() => users.Salvar(admin,"ADMIN","admin","OPERADOR"));
            Blocked(() => users.Desativar(admin));
            Blocked(() => db.Execute("DELETE FROM Usuarios WHERE Login='admin';"));
            Blocked(() => db.Execute("UPDATE Usuarios SET Ativo=0 WHERE Login='admin';"));
            Blocked(() => db.Execute("UPDATE Usuarios SET Perfil='OPERADOR' WHERE Login='admin';"));
            Blocked(() => db.Execute("UPDATE Usuarios SET SenhaHash='' WHERE Login='admin';"));
            Assert(users.Autenticar("admin","senha-segura-123") is not null);
        });
        users.Salvar(0,"GABRYELLE","operador","OPERADOR","senha-segura-123");
        int operador = Id("operador");
        Check("Operador e perfil forjado na sessão não concedem acesso", () =>
        {
            SessaoUsuario.Iniciar(operador,"ADMIN FORJADO","ADMINISTRADOR");
            Blocked(() => users.Listar()); Blocked(() => users.Salvar(admin,"ADMIN","admin","OPERADOR"));
            Blocked(() => users.Desativar(admin)); Blocked(() => users.Reativar(admin));
            Blocked(() => Autorizacao.ConfigurarPermissaoPerfil("ADMINISTRADOR",true));
            Blocked(Autorizacao.ExigirExclusaoProduto); Assert(!Autorizacao.PodeExcluirProduto());
        });
        Login("admin");
        users.Salvar(0,"OUTRO ADMIN","segundo","ADMINISTRADOR","senha-segura-123");
        int segundo = Id("segundo");
        Check("Permissão por perfil e exceção individual independem de nomes", () =>
        {
            Autorizacao.ConfigurarPermissaoPerfil("ADMINISTRADOR",false);
            Assert(!Autorizacao.PodeExcluirProduto());
            Autorizacao.ConfigurarPermissaoUsuario(admin,true); Assert(Autorizacao.PodeExcluirProduto());
            users.Salvar(admin,"NOVO NOME","admin","ADMINISTRADOR"); Assert(Autorizacao.PodeExcluirProduto());
            Autorizacao.ConfigurarPermissaoUsuario(admin,null); Assert(!Autorizacao.PodeExcluirProduto());
            Autorizacao.ConfigurarPermissaoPerfil("ADMINISTRADOR",true); Assert(Autorizacao.PodeExcluirProduto());
            Autorizacao.ConfigurarPermissaoUsuario(admin,false); Assert(!Autorizacao.PodeExcluirProduto());
            Autorizacao.ConfigurarPermissaoUsuario(admin,null);
            Autorizacao.ConfigurarPermissaoUsuario(operador,true); Login("operador"); Assert(!Autorizacao.PodeExcluirProduto());
            Login("admin");
        });
        Check("Desativação e reativação auditadas; sessão desativada perde acesso", () =>
        {
            users.Desativar(segundo); Assert(users.Autenticar("segundo","senha-segura-123") is null);
            SessaoUsuario.Iniciar(segundo,"OUTRO ADMIN","ADMINISTRADOR");
            Blocked(() => users.Reativar(segundo)); Blocked(() => users.Listar()); Assert(!Autorizacao.PodeExcluirProduto());
            Login("admin"); users.Reativar(segundo); Assert(users.Autenticar("segundo","senha-segura-123") is not null);
            Assert(db.Query("SELECT 1 FROM Auditoria WHERE Acao='REATIVACAO';").Any());
        });
        Check("Rebaixamento revoga acesso imediatamente; administrador inativo não conta", () =>
        {
            users.Salvar(segundo,"OUTRO ADMIN","segundo","OPERADOR");
            SessaoUsuario.Iniciar(segundo,"OUTRO ADMIN","ADMINISTRADOR"); Blocked(() => users.Listar());
            Login("admin");
            users.Salvar(segundo,"OUTRO ADMIN","segundo","ADMINISTRADOR"); users.Desativar(segundo);
            Blocked(() => users.Salvar(admin,"ADMIN","admin","OPERADOR"));
            // Um administrador inativo pode ser rebaixado sem afetar o último ativo.
            users.Salvar(segundo,"OUTRO ADMIN","segundo","OPERADOR");
        });
        Check("Falha de auditoria reverte alteração de usuário e permissão", () =>
        {
            db.ExecuteScript("CREATE TRIGGER FalharAuditoria BEFORE INSERT ON Auditoria BEGIN SELECT RAISE(ABORT,'falha simulada'); END;");
            try
            {
                Blocked(() => users.Desativar(operador));
                Blocked(() => Autorizacao.ConfigurarPermissaoUsuario(admin,false));
                Assert(users.Autenticar("operador","errada") is null);
                Assert(Convert.ToInt32(db.Query("SELECT Ativo FROM Usuarios WHERE Login='operador';").Single()["Ativo"])==1);
                Assert(Autorizacao.PodeExcluirProduto());
            }
            finally { db.ExecuteScript("DROP TRIGGER FalharAuditoria;"); }
        });
        Check("Hash malformado ou com custo abusivo falha fechado", () =>
        {
            foreach (string hash in new[] { "texto-puro", "pbkdf2-sha256$210000$!!!$!!!", "pbkdf2-sha256$2147483647$AAAA$AAAA", "pbkdf2-sha256$210000$AAAA$" })
            {
                db.Execute("UPDATE Usuarios SET SenhaHash=@Hash WHERE Login='operador';",new Dictionary<string,object?> { ["@Hash"]=hash });
                Assert(users.Autenticar("operador","senha-segura-123") is null);
            }
        });
        Check("Configuração inicial não reabre quando administradores ficam indisponíveis", () =>
        {
            db.ExecuteScript("DROP TRIGGER Usuarios_ProtegerUltimoAdmin_Update;");
            try
            {
                db.Execute("UPDATE Usuarios SET Ativo=0 WHERE Login='admin';");
                Assert(!users.PrecisaAdministradorInicial());
                Blocked(() => users.CriarAdministradorInicial("INVASOR","invasor","senha-segura-123"));
            }
            finally { db.Execute("UPDATE Usuarios SET Ativo=1 WHERE Login='admin';"); }
        });
        Check("Migração preserva hashes, usuários e concessão legada por ID", () =>
        {
            string hash = Convert.ToString(db.Query("SELECT SenhaHash FROM Usuarios WHERE Login='admin';").Single()["SenhaHash"])!;
            db.Execute("DELETE FROM SchemaMigrations WHERE Version>=6;");
            db.Execute("DELETE FROM Configuracoes WHERE Chave LIKE 'Permissao:%';");
            db.Execute("INSERT INTO Configuracoes (Chave,Valor) VALUES ('ProdutoExcluirUsuarioId',@Id);",new Dictionary<string,object?> { ["@Id"]=admin.ToString() });
            DatabaseInitializer.Initialize(); DatabaseInitializer.Initialize();
            Assert(hash == Convert.ToString(db.Query("SELECT SenhaHash FROM Usuarios WHERE Login='admin';").Single()["SenhaHash"]));
            Assert(Autorizacao.PodeExcluirProduto());
            users.Salvar(segundo,"GABRYELLE","segundo","ADMINISTRADOR"); users.Reativar(segundo); Login("segundo");
            Assert(!Autorizacao.PodeExcluirProduto());
            Login("admin");
        });
        Check("Backend bloqueia exclusão e configuração pela ponte sem autorização", () =>
        {
            db.Execute("INSERT INTO Produtos (Nome) VALUES ('TESTE DE AUTORIZACAO');");
            int produto = (int)db.LastInsertRowId;
            Autorizacao.ConfigurarPermissaoUsuario(admin,false);
            Blocked(() => new ProdutoService().Excluir(produto));
            Blocked(() => new ProdutoRepository().Excluir(produto));
            Assert(db.Query("SELECT 1 FROM Produtos WHERE Id=@Id AND ExcluidoEm IS NULL;",new Dictionary<string,object?> { ["@Id"]=produto }).Any());
            Autorizacao.ConfigurarPermissaoUsuario(admin,true);
            new ProdutoService().Excluir(produto);
            Assert(db.Query("SELECT 1 FROM Produtos WHERE Id=@Id AND ExcluidoEm IS NOT NULL;",new Dictionary<string,object?> { ["@Id"]=produto }).Any());
            // Restaura a senha do operador para testar a fronteira JSON do backend.
            users.Salvar(operador,"OPERADOR","operador","OPERADOR","senha-segura-123");
            Login("operador");
            using var payload = System.Text.Json.JsonDocument.Parse("{\"operation\":\"users.permission.user\",\"id\":" + operador + ",\"allow\":true}");
            Blocked(() => new ManagementWebBridge().Execute(payload.RootElement));
            Login("admin");
        });
        Check("Transações concorrentes não removem os dois administradores", () =>
        {
            using var inicio = new ManualResetEventSlim(false);
            int bloqueadas = 0;
            Task Remover(int id) => Task.Run(() =>
            {
                using var connection = new DatabaseConnection(DatabaseInitializer.DatabasePath);
                inicio.Wait();
                connection.Execute("BEGIN IMMEDIATE;");
                try
                {
                    connection.Execute("UPDATE Usuarios SET Ativo=0 WHERE Id=@Id;",new Dictionary<string,object?> { ["@Id"]=id });
                    connection.Execute("COMMIT;");
                }
                catch (InvalidOperationException)
                {
                    connection.Execute("ROLLBACK;"); Interlocked.Increment(ref bloqueadas);
                }
            });
            Task a = Remover(admin), b = Remover(segundo);
            inicio.Set(); Task.WaitAll(a,b);
            Assert(bloqueadas == 1);
            Assert(Convert.ToInt32(db.Query("SELECT COUNT(*) FROM Usuarios WHERE Ativo=1 AND Perfil='ADMINISTRADOR';").Single().Values.Single())==1);
        });
        Console.WriteLine($"SUCESSO: {passed} cenários de segurança.");
        SessaoUsuario.Encerrar();
        return 0;
    }
    private static void Assert(bool condition) { if (!condition) throw new Exception("Asserção de segurança falhou."); }
    private static void Blocked(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("A operação deveria ter sido bloqueada.");
    }
}
