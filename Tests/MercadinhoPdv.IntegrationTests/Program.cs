using System.Globalization;
using MercadinhoPdv.Data;
using MercadinhoPdv.Models;
using MercadinhoPdv.Services;

if (args.Length >= 2 && args[0] == "--interface")
    return UiFixture.Run(args);

string root = Path.Combine(Path.GetTempPath(), "MercadinhoPdvIntegration", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("MERCADINHOPDV_DATABASE", Path.Combine(root, "legacy.db"));
int passed = 0;

try
{
    CreateLegacyDatabase();
    DatabaseInitializer.Initialize();
    DatabaseInitializer.Initialize();
    Check("Migração preserva dados e unidade padrão", () =>
    {
        var legacy = new ProdutoService().BuscarPorId(1) ?? throw new Exception("Produto legado não encontrado.");
        Equal("CAFÉ LEGADO", legacy.Nome);
        Equal("UN", legacy.UnidadeMedida);
        Equal(0m, legacy.PrecoCusto);
        Equal(1, new ProdutoService().ListarCategorias().Count == 0 ? 0 : 1);
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        Equal(true, db.Query("PRAGMA table_info(Produtos);").Any(column => Convert.ToString(column["name"]) == "LocalImagePath"));
        Equal(3L, Convert.ToInt64(db.Query("SELECT MAX(Version) AS Version FROM SchemaMigrations;").Single()["Version"], CultureInfo.InvariantCulture));
        Equal(true, db.Query("PRAGMA table_info(ItensVenda);").Any(column => Convert.ToString(column["name"]) == "PrecoCustoUnitario"));
        Equal(true, Directory.GetFiles(root, "*.pre-migration-v1-*.bak").Length == 1);
    });
    Check("Categorias padrão são idempotentes e duplicatas consolidadas", () =>
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        Equal(12L, Convert.ToInt64(db.Query("SELECT COUNT(*) AS Total FROM Categorias WHERE Ativa = 1;").Single()["Total"], CultureInfo.InvariantCulture));
        Equal(1L, Convert.ToInt64(db.Query("SELECT COUNT(*) AS Total FROM Categorias WHERE Nome = 'MERCEARIA' AND Ativa = 1;").Single()["Total"], CultureInfo.InvariantCulture));
        Equal(1L, Convert.ToInt64(db.Query("SELECT CategoriaId FROM Produtos WHERE Id = 1;").Single()["CategoriaId"], CultureInfo.InvariantCulture));
    });

    var users = new UsuarioService();
    Check("Primeiro acesso cria administrador com senha derivada e autentica", () =>
    {
        Equal(true, users.PrecisaAdministradorInicial());
        ThrowsWithMessage<InvalidOperationException>(() => users.CriarAdministradorInicial("Gabryelle", "admin", "senha12345", "senha456"), "As senhas não coincidem.");
        ThrowsWithMessage<InvalidOperationException>(() => users.CriarAdministradorInicial("Gabryelle", "ad", "senha12345", "senha12345"), "O login deve possuir pelo menos 3 caracteres.");
        ThrowsWithMessage<InvalidOperationException>(() => users.CriarAdministradorInicial("Gabryelle", "admin", "12345", "12345"), "A senha deve possuir pelo menos 10 caracteres.");
        users.CriarAdministradorInicial("Gabryelle", "admin", "senha12345", "senha12345");
        Equal(false, users.PrecisaAdministradorInicial());
        Equal(true, users.Autenticar("admin", "senha-incorreta") is null);
        var user = users.Autenticar("ADMIN", "senha12345") ?? throw new Exception("Autenticação válida falhou.");
        Equal("ADMINISTRADOR", user.Perfil);
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        string stored = Convert.ToString(db.Query("SELECT SenhaHash FROM Usuarios WHERE Login='admin';").Single()["SenhaHash"])!;
        Equal(false, stored.Contains("senha-super-segura-123", StringComparison.Ordinal));
        SessaoUsuario.Iniciar(user.Id, user.Nome, user.Perfil);
        users.Salvar(0,"Operador de Teste","operador","OPERADOR","senha-operador-123");
        Equal("OPERADOR",users.Autenticar("operador","senha-operador-123")!.Value.Perfil);
    });
    var operatorLogin = users.Autenticar("operador", "senha-operador-123") ?? throw new Exception("Operador de teste não autenticou.");

    var products = new ProdutoService();
    var categories = products.ListarCategorias();
    int categoryId = categories.Single(option => option.Nome == "MERCEARIA").Id!.Value;
    long supplierId;
    using (var db = new DatabaseConnection(DatabaseInitializer.DatabasePath))
    {
        db.Execute("INSERT INTO Fornecedores (Nome, Telefone, Email) VALUES ('FORNECEDOR DE TESTE', '11999990000', 'Contato@exemplo.com');");
        supplierId = db.LastInsertRowId;
    }
    var unitProduct = new Produto { Nome = " produto unidade ", CodigoBarras = "1110001", CategoriaId = categoryId, FornecedorId = checked((int)supplierId), PrecoCusto = 2.25m, PrecoVenda = 4.50m, UnidadeMedida = "UN", EstoqueAtual = 10, EstoqueMinimo = 2, DataValidade = DateTime.Today.AddDays(120) };
    products.Salvar(unitProduct);
    Check("Cadastro, categoria, fornecedor, custo, validade e caixa alta", () =>
    {
        var saved = products.BuscarPorId(unitProduct.Id) ?? throw new Exception("Produto salvo não retornou.");
        Equal("PRODUTO UNIDADE", saved.Nome);
        Equal(categoryId, saved.CategoriaId);
        Equal(checked((int)supplierId), saved.FornecedorId);
        Equal(2.25m, saved.PrecoCusto);
        Equal("UN", saved.UnidadeMedida);
        Equal(DateTime.Today.AddDays(120), saved.DataValidade);
    });

    var kgProduct = new Produto { Nome = "carne bovina", CodigoBarras = "2220001", CategoriaId = categoryId, PrecoCusto = 24m, PrecoVenda = 35m, UnidadeMedida = "KG", EstoqueAtual = 5m, EstoqueMinimo = .5m };
    products.Salvar(kgProduct);
    Check("Cadastro de produto por KG com quantidade fracionada", () =>
    {
        var saved = products.BuscarPorId(kgProduct.Id) ?? throw new Exception("Produto KG não encontrado.");
        Equal("CARNE BOVINA", saved.Nome);
        Equal("KG", saved.UnidadeMedida);
        Equal(5m, saved.EstoqueAtual);
    });
    Check("Produto UN rejeita estoque fracionado", () => Throws<ProdutoValidationException>(() => products.Salvar(new Produto { Nome = "Inválido", UnidadeMedida = "UN", EstoqueAtual = .5m })));

    unitProduct.Nome = "produto unidade editado";
    products.Salvar(unitProduct);
    Check("Edição de produto persiste mudanças", () => Equal("PRODUTO UNIDADE EDITADO", products.BuscarPorId(unitProduct.Id)!.Nome));
    Check("Código de barras duplicado é rejeitado", () => Throws<ProdutoValidationException>(() => products.Salvar(new Produto { Nome = "Duplicado", CodigoBarras = "1110001" })));

    var inventory = new EstoqueService();
    inventory.RegistrarMovimentacao(unitProduct.Id, "Entrada", 2, "Reposição");
    inventory.RegistrarMovimentacao(unitProduct.Id, "Saída", 3, "Perda");
    inventory.AjustarSaldo(unitProduct.Id, 8, "Inventário físico");
    inventory.RegistrarMovimentacao(kgProduct.Id, "Entrada", .25m, "Recebimento");
    Check("Entrada, saída, ajuste e histórico de estoque respeitam unidade", () =>
    {
        Equal(8m, products.BuscarPorId(unitProduct.Id)!.EstoqueAtual);
        Equal(5.25m, products.BuscarPorId(kgProduct.Id)!.EstoqueAtual);
        Equal(4, inventory.ListarMovimentacoes(unitProduct.Id).Count);
        Equal("UN", inventory.ListarMovimentacoes(unitProduct.Id).First().UnidadeMedida);
        Throws<InvalidOperationException>(() => inventory.RegistrarMovimentacao(unitProduct.Id, "Saída", .5m, "Teste"));
        Throws<InvalidOperationException>(() => inventory.RegistrarMovimentacao(unitProduct.Id, "Saída", 99, "Teste"));
    });
    var receiptProduct = new Produto { Nome="FEIJÃO PARA ENTRADA", CodigoBarras="3330001", CategoriaId=categoryId, PrecoCusto=0, PrecoVenda=8, UnidadeMedida="UN", EstoqueAtual=0, EstoqueMinimo=0 };
    products.Salvar(receiptProduct);
    inventory.RegistrarEntradaMercadoria(receiptProduct.Id, 12, 5.25m, 9.50m, DateTime.Today.AddMonths(8), checked((int)supplierId), "NF 123", "LOTE RECEBIDO");
    Check("Entrada de mercadoria atualiza estoque, custos, fornecedor, validade e documento", () =>
    {
        var saved=products.BuscarPorId(receiptProduct.Id)!; Equal(12m,saved.EstoqueAtual); Equal(5.25m,saved.PrecoCusto); Equal(9.50m,saved.PrecoVenda); Equal(checked((int)supplierId),saved.FornecedorId); Equal(DateTime.Today.AddMonths(8),saved.DataValidade);
        var movement=inventory.ListarMovimentacoes(receiptProduct.Id).Single(); Equal("NF 123",movement.Documento); Equal(true,movement.Motivo!.Contains("LOTE RECEBIDO"));
    });

    var sales = new VendaService();
    Check("Venda sem caixa aberto é bloqueada sem persistência", () =>
    {
        Throws<InvalidOperationException>(() => sales.Registrar([Cart(unitProduct, 1)], "DINHEIRO"));
        Equal(0L, Scalar("SELECT COUNT(*) FROM Vendas;"));
    });
    var cash = new CaixaService();
    cash.Abrir(200m);
    sales.Registrar([Cart(unitProduct, 2)], "dinheiro");
    sales.Registrar([Cart(kgProduct, .75m)], "pix");
    sales.Registrar([Cart(unitProduct, 1)], "débito");
    sales.Registrar([Cart(unitProduct, 1)], "crédito");
    Check("Vendas UN, KG, pagamentos, baixa e movimento atômicos", () =>
    {
        Equal(4m, products.BuscarPorId(unitProduct.Id)!.EstoqueAtual);
        Equal(4.5m, products.BuscarPorId(kgProduct.Id)!.EstoqueAtual);
        Equal(4L, Scalar("SELECT COUNT(*) FROM Vendas WHERE Status = 'Concluida';"));
        Equal(4L, Scalar("SELECT COUNT(*) FROM ItensVenda;"));
        Equal(4L, Scalar("SELECT COUNT(*) FROM MovimentacoesEstoque WHERE Motivo = 'Venda';"));
        Equal(44.25m, DecimalScalar("SELECT SUM(ValorTotal) FROM Vendas;"));
        Equal(1L, Scalar("SELECT COUNT(*) FROM ItensVenda WHERE UnidadeMedida = 'KG' AND Quantidade = 0.75;"));
        Equal(3, inventory.ListarMovimentacoes(unitProduct.Id, DateTime.Today, DateTime.Today, "VENDA").Count);
    });
    Check("Venda sem estoque reverte transação", () =>
    {
        Throws<InvalidOperationException>(() => sales.Registrar([Cart(unitProduct, 99)], "DINHEIRO"));
        Equal(4L, Scalar("SELECT COUNT(*) FROM Vendas;"));
        Equal(4m, products.BuscarPorId(unitProduct.Id)!.EstoqueAtual);
    });
    Check("Cálculo de troco", () =>
    {
        Equal(7.25m, PagamentoService.CalcularTroco(52.75m, 60m));
        Throws<InvalidOperationException>(() => PagamentoService.CalcularTroco(10m, 9m));
        Equal(true, PagamentoService.TryParseValor("14,30", out var comma) && comma == 14.30m);
        Equal(true, PagamentoService.TryParseValor("14.30", out var dot) && dot == 14.30m);
        Equal(true, PagamentoService.TryParseValor("1.234,50", out var grouped) && grouped == 1234.50m);
    });

    cash.RegistrarMovimentacao("SUPRIMENTO", 30m, "Troco adicional");
    cash.RegistrarMovimentacao("SANGRIA", 20m, "Retirada segura");
    cash.RegistrarDespesa("Compra de sacolas", 10m, "DINHEIRO");
    cash.RegistrarDespesa("Conta de internet", 5m, "PIX");
    Check("Caixa, vendas, suprimento, sangria e despesas", () =>
    {
        var summary = cash.ObterAberto() ?? throw new Exception("Caixa deveria estar aberto.");
        Equal(4, summary.QuantidadeVendas);
        Equal(44.25m, summary.TotalVendas);
        Equal(209m, summary.ValorEsperadoDinheiro);
        Equal(8, cash.ListarMovimentacoes(summary.Id).Count);
    });
    Check("Relatório de período e dashboard", () =>
    {
        var report = new RelatoriosService().Gerar(DateTime.Today, DateTime.Today);
        Equal(4, report.QuantidadeVendas);
        Equal(44.25m, report.TotalVendido);
        Equal(11.0625m, report.TicketMedio);
        Equal(15m, report.TotalDespesas);
        Equal(27m,report.CustoEstimado); Equal(17.25m,report.LucroBrutoEstimado);
        Equal(4, report.Pagamentos.Count);
        var dashboard = new DashboardService().ObterResumo();
        Equal(4, dashboard.VendasHoje);
        Equal(44.25m, dashboard.FaturamentoHoje);
        Equal("Mercado", dashboard.NomeLoja);
        Equal(true, dashboard.CaixaAberto);
    });
    using (var storeSettings = new DatabaseConnection(DatabaseInitializer.DatabasePath))
        storeSettings.Execute("INSERT INTO Configuracoes (Chave,Valor) VALUES ('NomeLoja',' Mercado Gabryelle ') ON CONFLICT(Chave) DO UPDATE SET Valor=excluded.Valor;");
    DatabaseInitializer.Initialize();
    Check("Nome do mercado é editável e preserva acentuação de caixa/espaços normalizados", () => Equal("Mercado Gabryelle", new DashboardService().ObterResumo().NomeLoja));
    Check("Cadastro e cancelamento de despesas mantém trilha de auditoria", () =>
    {
        var expenses=new DespesasService();
        expenses.Salvar(new DespesaLinha{Descricao="Despesa de teste",Categoria="Operacional",Valor=3.50m,Data=DateTime.Now,FormaPagamento="PIX"});
        var item=expenses.Listar(DateTime.Today,DateTime.Today,"teste").Single();
        Equal("DESPESA DE TESTE",item.Descricao); expenses.Cancelar(item.Id,"REGISTRO DE TESTE");
        Equal("Cancelada",expenses.Listar(DateTime.Today,DateTime.Today,"teste").Single().Status);
    });
    Check("Histórico e cancelamento restauram estoque sem apagar a venda", () =>
    {
        decimal before = products.BuscarPorId(unitProduct.Id)!.EstoqueAtual;
        long id = sales.Registrar([Cart(unitProduct, 1)], "DINHEIRO", 10m);
        Equal(before - 1, products.BuscarPorId(unitProduct.Id)!.EstoqueAtual);
        Equal(true, sales.ListarHistorico(DateTime.Today, DateTime.Today).Any(x => x.Id == id));
        sales.Cancelar(id, "LANÇAMENTO DE TESTE");
        Equal(before, products.BuscarPorId(unitProduct.Id)!.EstoqueAtual);
        Equal("Cancelada", sales.ListarHistorico(DateTime.Today, DateTime.Today, "Cancelada").Single(x => x.Id == id).Status);
        Equal(1L, Scalar($"SELECT COUNT(*) FROM Vendas WHERE Id={id};"));
        Equal(1L, Scalar($"SELECT COUNT(*) FROM MovimentacoesEstoque WHERE VendaId={id} AND Tipo='ESTORNO';"));
        Equal(1L, Scalar($"SELECT COUNT(*) FROM MovimentacoesCaixa WHERE VendaId={id} AND Tipo='ESTORNO_VENDA_DINHEIRO' AND Valor=4.50;"));
    });
    var close = cash.Fechar(207m);
    Check("Fechamento calcula falta/sobra e encerra caixa", () =>
    {
        Equal(209m, close.ValorEsperado);
        Equal(-2m, close.Diferenca);
        Equal(null, cash.ObterAberto());
        var closedCash = cash.ListarHistorico().Single(item => item.Status == "Fechado");
        Equal(44.25m, closedCash.Vendas);
        Equal(30m, closedCash.Suprimentos);
        Equal(20m, closedCash.Sangrias);
        Equal(15m, closedCash.Despesas);
        Equal(207m, closedCash.ValorContado);
        Equal(-2m, closedCash.Diferenca);
    });
    SessaoUsuario.Iniciar(operatorLogin.Id, operatorLogin.Nome, operatorLogin.Perfil);
    cash.Abrir(50m);
    decimal stockBeforeOperatorSale = products.BuscarPorId(kgProduct.Id)!.EstoqueAtual;
    long operatorSaleId = sales.Registrar([Cart(kgProduct, .25m)], "DINHEIRO", 20m);
    Check("Operador registra venda em dinheiro com caixa, usuário, estoque e troco vinculados", () =>
    {
        Equal(stockBeforeOperatorSale - .25m, products.BuscarPorId(kgProduct.Id)!.EstoqueAtual);
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        var sale = db.Query("SELECT UsuarioId,CaixaId,ValorRecebido,Troco,Status FROM Vendas WHERE Id=@Id;", new Dictionary<string, object?> { ["@Id"] = operatorSaleId }).Single();
        Equal(operatorLogin.Id, Convert.ToInt32(sale["UsuarioId"], CultureInfo.InvariantCulture));
        Equal(true, sale["CaixaId"] is not null);
        Equal(20m, Convert.ToDecimal(sale["ValorRecebido"], CultureInfo.InvariantCulture));
        Equal(11.25m, Convert.ToDecimal(sale["Troco"], CultureInfo.InvariantCulture));
        Equal("Concluida", Convert.ToString(sale["Status"]));
        Equal(1L, Convert.ToInt64(db.Query("SELECT COUNT(*) AS Total FROM ItensVenda WHERE VendaId=@Id;", new Dictionary<string, object?> { ["@Id"] = operatorSaleId }).Single()["Total"], CultureInfo.InvariantCulture));
        Equal(1L, Convert.ToInt64(db.Query("SELECT COUNT(*) AS Total FROM MovimentacoesEstoque WHERE VendaId=@Id AND Tipo='VENDA';", new Dictionary<string, object?> { ["@Id"] = operatorSaleId }).Single()["Total"], CultureInfo.InvariantCulture));
    });
    Check("Venda de operador aparece imediatamente no dashboard e histórico filtrável de caixa", () =>
    {
        var dashboard = new DashboardService().ObterResumo();
        Equal(5, dashboard.VendasHoje);
        Equal(53m, dashboard.FaturamentoHoje);
        var reportAdmin = users.Autenticar("admin", "senha12345")!.Value;
        SessaoUsuario.Iniciar(reportAdmin.Id, reportAdmin.Nome, reportAdmin.Perfil);
        ResumoRelatorio currentReport;
        try { currentReport = new RelatoriosService().Gerar(DateTime.Today, DateTime.Today); }
        finally { SessaoUsuario.Iniciar(operatorLogin.Id, operatorLogin.Nome, operatorLogin.Perfil); }
        Equal(5, currentReport.QuantidadeVendas);
        Equal(53m, currentReport.TotalVendido);
        var todayCash = cash.ListarHistorico(DateTime.Today, DateTime.Today);
        Equal(2, todayCash.Count);
        Equal("OPERADOR DE TESTE", todayCash.Single(item => item.Status == "Aberto").UsuarioAbertura);
        Equal("OPERADOR DE TESTE", cash.ListarMovimentacoes(todayCash.Single(item => item.Status == "Aberto").Id).Single(item => item.Tipo.StartsWith("VENDA", StringComparison.Ordinal)).Usuario);
    });
    var operatorClose = cash.Fechar(58.75m);
    Check("Fechamento do caixa do operador inclui o dinheiro recebido da venda", () =>
    {
        Equal(58.75m, operatorClose.ValorEsperado);
        Equal(58.75m, operatorClose.ValorContado);
        Equal(0m, operatorClose.Diferenca);
    });
    var cafe = new Produto { Nome="CAFÉ REGRESSÃO", CodigoBarras="4440001", CategoriaId=categoryId, PrecoVenda=14.27m, UnidadeMedida="UN", EstoqueAtual=8, EstoqueMinimo=0 };
    var admin = users.Autenticar("admin", "senha12345") ?? throw new Exception("Administrador de teste não autenticou.");
    SessaoUsuario.Iniciar(admin.Id, admin.Nome, admin.Perfil);
    products.Salvar(cafe);
    // Simula preço legado com precisão maior que centavos, causa da divergência no total exibido.
    using (var db = new DatabaseConnection(DatabaseInitializer.DatabasePath)) db.Execute("UPDATE Produtos SET PrecoVenda=14.273 WHERE Id=@Id;", new Dictionary<string, object?> { ["@Id"] = cafe.Id });
    cafe.PrecoVenda = 14.273m;
    cash.Abrir(0m);
    Check("Venda em dinheiro conclui no PDV para administrador", () =>
    {
        long id = sales.Registrar([Cart(cafe, 1)], "DINHEIRO", 14.27m);
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        var sale = db.Query("SELECT ValorTotal,Status,FormaPagamento FROM Vendas WHERE Id=@Id;", new Dictionary<string, object?> { ["@Id"] = id }).Single();
        Equal(14.27m, Convert.ToDecimal(sale["ValorTotal"], CultureInfo.InvariantCulture)); Equal("Concluida", Convert.ToString(sale["Status"]));
        Equal(1L, Scalar($"SELECT COUNT(*) FROM ItensVenda WHERE VendaId={id};"));
        Equal(1L, Scalar($"SELECT COUNT(*) FROM MovimentacoesEstoque WHERE VendaId={id} AND Tipo='VENDA';"));
        Equal(1L, Scalar($"SELECT COUNT(*) FROM MovimentacoesCaixa WHERE VendaId={id} AND Tipo='VENDA_DINHEIRO' AND Valor=14.27;"));
        Equal(7m, products.BuscarPorId(cafe.Id)!.EstoqueAtual);
    });
    Check("Venda em dinheiro com troco conclui e movimenta somente o total", () =>
    {
        long id = sales.Registrar([Cart(cafe, 1)], "DINHEIRO", 20m);
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        var sale = db.Query("SELECT ValorTotal,ValorRecebido,Troco FROM Vendas WHERE Id=@Id;", new Dictionary<string, object?> { ["@Id"] = id }).Single();
        Equal(14.27m, Convert.ToDecimal(sale["ValorTotal"], CultureInfo.InvariantCulture)); Equal(20m, Convert.ToDecimal(sale["ValorRecebido"], CultureInfo.InvariantCulture)); Equal(5.73m, Convert.ToDecimal(sale["Troco"], CultureInfo.InvariantCulture));
        Equal(1L, Scalar($"SELECT COUNT(*) FROM MovimentacoesCaixa WHERE VendaId={id} AND Tipo='VENDA_DINHEIRO' AND Valor=14.27;"));
    });
    Check("Venda em dinheiro com total exato conclui no PDV para operador", () =>
    {
        SessaoUsuario.Iniciar(operatorLogin.Id, operatorLogin.Nome, operatorLogin.Perfil);
        long id = sales.Registrar([Cart(cafe, 1)], "DINHEIRO", 14.27m);
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        Equal("OPERADOR", Convert.ToString(db.Query("SELECT u.Perfil FROM Vendas v JOIN Usuarios u ON u.Id=v.UsuarioId WHERE v.Id=@Id;", new Dictionary<string, object?> { ["@Id"] = id }).Single()["Perfil"]));
        Equal(1L, Scalar($"SELECT COUNT(*) FROM MovimentacoesCaixa WHERE VendaId={id} AND Tipo='VENDA_DINHEIRO';"));
    });
    Check("Pagamento insuficiente não persiste venda nem baixa estoque", () =>
    {
        long count = Scalar("SELECT COUNT(*) FROM Vendas;"); decimal stock = products.BuscarPorId(cafe.Id)!.EstoqueAtual;
        Throws<InvalidOperationException>(() => sales.Registrar([Cart(cafe, 1)], "DINHEIRO", 10m));
        Equal(count, Scalar("SELECT COUNT(*) FROM Vendas;")); Equal(stock, products.BuscarPorId(cafe.Id)!.EstoqueAtual);
    });
    Check("Falha intermediária faz rollback de venda, itens, estoque e movimentos", () =>
    {
        using (var db = new DatabaseConnection(DatabaseInitializer.DatabasePath)) db.ExecuteScript("CREATE TRIGGER falhar_mov_caixa BEFORE INSERT ON MovimentacoesCaixa WHEN NEW.Tipo LIKE 'VENDA_%' BEGIN SELECT RAISE(ABORT, 'falha simulada'); END;");
        long salesBefore = Scalar("SELECT COUNT(*) FROM Vendas;"), itemsBefore = Scalar("SELECT COUNT(*) FROM ItensVenda;"), inventoryMovesBefore = Scalar("SELECT COUNT(*) FROM MovimentacoesEstoque;");
        long cashMovesBefore = Scalar("SELECT COUNT(*) FROM MovimentacoesCaixa;"); decimal stock = products.BuscarPorId(cafe.Id)!.EstoqueAtual;
        try { Throws<Exception>(() => sales.Registrar([Cart(cafe, 1)], "PIX")); }
        finally { using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath); db.ExecuteScript("DROP TRIGGER IF EXISTS falhar_mov_caixa;"); }
        Equal(salesBefore, Scalar("SELECT COUNT(*) FROM Vendas;")); Equal(itemsBefore, Scalar("SELECT COUNT(*) FROM ItensVenda;"));
        Equal(inventoryMovesBefore, Scalar("SELECT COUNT(*) FROM MovimentacoesEstoque;")); Equal(cashMovesBefore, Scalar("SELECT COUNT(*) FROM MovimentacoesCaixa;"));
        Equal(stock, products.BuscarPorId(cafe.Id)!.EstoqueAtual);
    });
    Check("Venda PIX grava histórico e movimento financeiro", () =>
    {
        long id = sales.Registrar([Cart(cafe, 1)], "PIX");
        Equal(1L, Scalar($"SELECT COUNT(*) FROM Vendas WHERE Id={id} AND Status='Concluida' AND FormaPagamento='PIX' AND ValorTotal=14.27;"));
        Equal(1L, Scalar($"SELECT COUNT(*) FROM MovimentacoesCaixa WHERE VendaId={id} AND Tipo='VENDA_PIX' AND Valor=14.27;"));
        Equal(true, sales.ListarHistorico(DateTime.Today, DateTime.Today).Any(row => row.Id == id));
    });
    Check("Vendas em débito e crédito geram movimentação financeira por forma", () =>
    {
        long debit = sales.Registrar([Cart(cafe, 1)], "DÉBITO"), credit = sales.Registrar([Cart(cafe, 1)], "CRÉDITO");
        Equal(1L, Scalar($"SELECT COUNT(*) FROM MovimentacoesCaixa WHERE VendaId={debit} AND Tipo='VENDA_DEBITO' AND Valor=14.27;"));
        Equal(1L, Scalar($"SELECT COUNT(*) FROM MovimentacoesCaixa WHERE VendaId={credit} AND Tipo='VENDA_CREDITO' AND Valor=14.27;"));
        Equal(1L, Scalar($"SELECT COUNT(*) FROM Auditoria WHERE Entidade='VENDA' AND Descricao LIKE '%Venda #{debit}%';"));
    });
    SessaoUsuario.Iniciar(admin.Id, admin.Nome, admin.Perfil);
    Check("Relatório mantém custo da venda após alteração do cadastro", () =>
    {
        decimal historicalCost = new RelatoriosService().Gerar(DateTime.Today, DateTime.Today).CustoEstimado;
        using (var db = new DatabaseConnection(DatabaseInitializer.DatabasePath)) db.Execute("UPDATE Produtos SET PrecoCusto=99 WHERE Id=@Id;", new Dictionary<string, object?> { ["@Id"] = cafe.Id });
        Equal(historicalCost, new RelatoriosService().Gerar(DateTime.Today, DateTime.Today).CustoEstimado);
    });
    products.Desativar(unitProduct.Id);
    Check("Desativação de produto", () => Equal(false, products.BuscarPorId(unitProduct.Id)!.Ativo));
    Check("Backup e restauração preservam cópia do banco anterior", () =>
    {
        string backup = Path.Combine(root, "backup.db");
        var service = new BackupService(); service.Criar(backup);
        using (var db = new DatabaseConnection(DatabaseInitializer.DatabasePath)) db.Execute("INSERT INTO Configuracoes (Chave, Valor) VALUES ('TESTE_RESTAURACAO','TEMPORARIO');");
        service.Restaurar(backup);
        using var restored = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        Equal(0L, Convert.ToInt64(restored.Query("SELECT COUNT(*) AS Total FROM Configuracoes WHERE Chave='TESTE_RESTAURACAO';").Single()["Total"], CultureInfo.InvariantCulture));
        Equal(true, Directory.GetFiles(root, "*.antes-restauracao-*.bak").Length == 1);
    });

    PhaseOneScenarios.Run(Check);
    PhaseTwoScenarios.Run(Check, root);
    PhaseThreeScenarios.Run(Check);
    Console.WriteLine($"SUCESSO: {passed} cenários de integração passaram.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"FALHA após {passed} cenários: {exception}");
    return 1;
}
finally
{
    Environment.SetEnvironmentVariable("MERCADINHOPDV_DATABASE", null);
    try { Directory.Delete(root, recursive: true); } catch { }
}

void CreateLegacyDatabase()
{
    using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
    db.ExecuteScript("""
        CREATE TABLE Categorias (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nome TEXT NOT NULL UNIQUE, Ativa INTEGER NOT NULL DEFAULT 1, CriadoEm TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
        INSERT INTO Categorias (Nome) VALUES ('mercearia'), ('MERCEARIA');
        CREATE TABLE Fornecedores (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nome TEXT NOT NULL, Documento TEXT, Telefone TEXT, Email TEXT, Ativo INTEGER NOT NULL DEFAULT 1, CriadoEm TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
        CREATE TABLE Produtos (Id INTEGER PRIMARY KEY AUTOINCREMENT, CodigoBarras TEXT UNIQUE, Nome TEXT NOT NULL, CategoriaId INTEGER, FornecedorId INTEGER, PrecoVenda NUMERIC NOT NULL DEFAULT 0, EstoqueAtual NUMERIC NOT NULL DEFAULT 0, EstoqueMinimo NUMERIC NOT NULL DEFAULT 0, DataValidade TEXT, Ativo INTEGER NOT NULL DEFAULT 1, CriadoEm TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
        INSERT INTO Produtos (CodigoBarras, Nome, CategoriaId, PrecoVenda, EstoqueAtual, EstoqueMinimo) VALUES ('9990001', 'café legado', 2, 6.50, 10, 2);
        CREATE TABLE ItensVenda (Id INTEGER PRIMARY KEY AUTOINCREMENT, VendaId INTEGER NOT NULL, ProdutoId INTEGER NOT NULL, Quantidade NUMERIC NOT NULL, ValorUnitario NUMERIC NOT NULL, Subtotal NUMERIC NOT NULL);
        CREATE TABLE MovimentacoesEstoque (Id INTEGER PRIMARY KEY AUTOINCREMENT, ProdutoId INTEGER NOT NULL, Tipo TEXT NOT NULL, Quantidade NUMERIC NOT NULL, DataMovimentacao TEXT NOT NULL, Motivo TEXT, VendaId INTEGER, UsuarioId INTEGER);
        """);
}

ItemCarrinho Cart(Produto product, decimal quantity) => new() { ProdutoId = product.Id, Produto = product.Nome, UnidadeMedida = product.UnidadeMedida, Quantidade = quantity, PrecoUnitario = product.PrecoVenda };

long Scalar(string sql)
{
    using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
    return Convert.ToInt64(db.Query(sql).Single().Values.Single(), CultureInfo.InvariantCulture);
}

decimal DecimalScalar(string sql)
{
    using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
    return Convert.ToDecimal(db.Query(sql).Single().Values.Single(), CultureInfo.InvariantCulture);
}

void Check(string name, Action action)
{
    action();
    passed++;
    Console.WriteLine($"PASSOU {passed:00}: {name}");
}

void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Esperado: {expected}; recebido: {actual}.");
}

void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Era esperado {typeof(T).Name}.");
}

void ThrowsWithMessage<T>(Action action, string message) where T : Exception
{
    try { action(); }
    catch (T exception) when (exception.Message == message) { return; }
    throw new Exception($"Era esperado {typeof(T).Name} com a mensagem '{message}'.");
}
