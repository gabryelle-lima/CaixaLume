using System.Globalization;
using System.Security.Cryptography;
using MercadinhoPdv.Data;
using MercadinhoPdv.Models;
using MercadinhoPdv.Services;

internal static class PhaseOneScenarios
{
    internal static void Run(Action<string, Action> check)
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        var admin = db.Query("SELECT Id,Nome FROM Usuarios WHERE Login='admin';").Single();
        var op = db.Query("SELECT Id,Nome FROM Usuarios WHERE Login='operador';").Single();
        void Admin() => SessaoUsuario.Iniciar(Convert.ToInt32(admin["Id"]), Convert.ToString(admin["Nome"])!, "ADMINISTRADOR");
        Admin();

        check("Limpeza de caixas é bloqueada no ambiente normal sem mudar histórico", () =>
        {
            string? original = Environment.GetEnvironmentVariable("MERCADINHOPDV_AMBIENTE");
            try
            {
                Environment.SetEnvironmentVariable("MERCADINHOPDV_AMBIENTE", null);
                long before = Count(db, "Caixa");
                Blocked(() => new CaixaService().LimparHistoricoParaTestes());
                Assert(before == Count(db, "Caixa"));
                Environment.SetEnvironmentVariable("MERCADINHOPDV_AMBIENTE", "DESENVOLVIMENTO");
                Blocked(() => new CaixaService().LimparHistoricoParaTestes());
                Assert(before == Count(db, "Caixa"));
            }
            finally { Environment.SetEnvironmentVariable("MERCADINHOPDV_AMBIENTE", original); }
        });
        check("Toda conexão SQLite ativa foreign_keys e rejeita referência inexistente", () =>
        {
            using var other = new DatabaseConnection(DatabaseInitializer.DatabasePath);
            Assert(Convert.ToInt64(other.Query("PRAGMA foreign_keys;").Single().Values.Single()) == 1);
            Blocked(() => other.Execute("INSERT INTO Auditoria (UsuarioId,DataHora,Acao) VALUES (-999,'2026-10-17','INVALIDO');"));
            Assert(other.Query("PRAGMA foreign_key_check;").Count == 0);
        });
        check("Falha de integridade referencial permite rollback integral", () =>
        {
            db.Execute("BEGIN IMMEDIATE;");
            try
            {
                db.Execute("INSERT INTO Configuracoes (Chave,Valor) VALUES ('FK_ROLLBACK','TESTE');");
                Blocked(() => db.Execute("INSERT INTO Auditoria (UsuarioId,DataHora,Acao) VALUES (-999,'2026-10-17','INVALIDO');"));
            }
            finally { db.Execute("ROLLBACK;"); }
            Assert(db.Query("SELECT 1 FROM Configuracoes WHERE Chave='FK_ROLLBACK';").Count == 0);
        });

        var product = new Produto { Nome = "AJUSTE AUDITADO", EstoqueAtual = 5, PrecoVenda = 2, UnidadeMedida = "UN" };
        new ProdutoService().Salvar(product);
        check("Ajuste registra usuário, anterior, posterior, diferença e motivo", () =>
        {
            new EstoqueService().AjustarSaldo(product.Id, 3, "Inventário completo");
            var audit = db.Query("SELECT UsuarioId,Descricao FROM Auditoria WHERE Acao='AJUSTE' ORDER BY Id DESC LIMIT 1;").Single();
            string text = Convert.ToString(audit["Descricao"])!;
            Assert(Convert.ToInt32(audit["UsuarioId"]) == SessaoUsuario.UsuarioId);
            Assert(text.Contains($"Produto #{product.Id}") && text.Contains("anterior 5 UN") && text.Contains("posterior 3 UN")
                && text.Contains("diferença -2") && text.Contains("INVENTÁRIO COMPLETO"));
        });
        check("Falha da auditoria reverte saldo e movimentação do ajuste", () =>
        {
            long moves = Count(db, "MovimentacoesEstoque");
            db.ExecuteScript("CREATE TRIGGER TestFailAdjustment BEFORE INSERT ON Auditoria WHEN NEW.Acao='AJUSTE' BEGIN SELECT RAISE(ABORT,'Falha de auditoria simulada'); END;");
            try { Blocked(() => new EstoqueService().AjustarSaldo(product.Id, 8, "Ajuste não confirmado")); }
            finally { db.ExecuteScript("DROP TRIGGER TestFailAdjustment;"); }
            Assert(new ProdutoService().BuscarPorId(product.Id)!.EstoqueAtual == 3);
            Assert(moves == Count(db, "MovimentacoesEstoque"));
        });
        check("Política de 10 caracteres vale para primeiro administrador e novos usuários", () =>
        {
            var users = new UsuarioService();
            Blocked(() => users.CriarAdministradorInicial("Teste", "teste", "123456789", "123456789"));
            Blocked(() => users.Salvar(0, "Teste", "senha-curta", "OPERADOR", "123456789"));
            Blocked(() => users.Salvar(0, "Teste", "senha-vazia", "OPERADOR", "          "));
        });
        check("Hashes de senhas antigas continuam autenticando sem redefinição", () =>
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            string oldHash = "pbkdf2-sha256$210000$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2("123456", salt, 210000, HashAlgorithmName.SHA256, 32));
            db.Execute("INSERT INTO Usuarios (Nome,Login,Perfil,SenhaHash) VALUES ('LEGADO','senha-legada','OPERADOR',@Hash);", new Dictionary<string, object?> { ["@Hash"] = oldHash });
            Assert(new UsuarioService().Autenticar("senha-legada", "123456") is not null);
        });
        check("Operador não altera cadastros, estoque, usuários, backup ou venda por chamada direta", () =>
        {
            SessaoUsuario.Iniciar(Convert.ToInt32(op["Id"]), Convert.ToString(op["Nome"])!, "ADMINISTRADOR");
            try
            {
                Blocked(() => new ProdutoService().Salvar(new Produto { Nome = "BLOQUEADO" }));
                Blocked(() => new EstoqueService().AjustarSaldo(product.Id, 9, "Sem permissão"));
                Blocked(() => new CategoriaService().Salvar(new Categoria { Nome = "BLOQUEADA" }));
                Blocked(() => new FornecedorService().Salvar(new Fornecedor { Nome = "BLOQUEADO" }));
                Blocked(() => new ConfiguracaoService().SalvarNomeLoja("BLOQUEADO"));
                Blocked(() => new UsuarioService().Salvar(0, "Intruso", "intruso", "ADMINISTRADOR", "senha-segura123"));
                Blocked(() => new DespesasService().Salvar(new DespesaLinha { Descricao = "BLOQUEADA", Valor = 1 }));
                Blocked(() => new BackupService().Criar(Path.Combine(Path.GetTempPath(), "bloqueado.db")));
                Blocked(() => new VendaService().Cancelar(1, "Sem permissão"));
                Blocked(() => new RelatoriosService().Gerar(DateTime.Today,DateTime.Today));
                Assert(new ProdutoService().BuscarPorId(product.Id)!.EstoqueAtual == 3);
            }
            finally { Admin(); }
        });
        check("Usuário desativado não mantém autorização administrativa pela sessão", () =>
        {
            db.Execute("UPDATE Usuarios SET Ativo=0 WHERE Id=@Id;", new Dictionary<string, object?> { ["@Id"] = SessaoUsuario.UsuarioId });
            try { Blocked(Autorizacao.ExigirAdministrador); }
            finally { db.Execute("UPDATE Usuarios SET Ativo=1 WHERE Id=@Id;", new Dictionary<string, object?> { ["@Id"] = SessaoUsuario.UsuarioId }); }
        });
        check("Último administrador não pode remover o próprio perfil administrativo", () =>
        {
            Blocked(() => new UsuarioService().Salvar(Convert.ToInt32(admin["Id"]),Convert.ToString(admin["Nome"])!,"admin","OPERADOR"));
            Autorizacao.ExigirAdministrador();
        });
        check("Inicialização Web bem-sucedida não abre a alternativa", () =>
        {
            int native = 0;
            InicializacaoInterface.Executar(() => DialogResult.OK, () => { native++; return DialogResult.OK; });
            Assert(native == 0);
        });
        check("Falha Web abre WinForms uma vez e preserva sessão", () =>
        {
            int web = 0, native = 0;
            int? user = SessaoUsuario.UsuarioId;
            InicializacaoInterface.Executar(() => { web++; return DialogResult.Retry; }, () => { native++; return DialogResult.OK; });
            Assert(web == 1 && native == 1 && SessaoUsuario.UsuarioId == user);
        });
    }

    private static long Count(DatabaseConnection db, string table) => Convert.ToInt64(db.Query($"SELECT COUNT(*) FROM {table};").Single().Values.Single(), CultureInfo.InvariantCulture);
    private static void Assert(bool result) { if (!result) throw new Exception("A regressão não apresentou o resultado esperado."); }
    private static void Blocked(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Era esperado bloqueio da operação.");
    }
}
