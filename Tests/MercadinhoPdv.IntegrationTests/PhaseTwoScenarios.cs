using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using MercadinhoPdv.Data;
using MercadinhoPdv.Models;
using MercadinhoPdv.Services;

internal static class PhaseTwoScenarios
{
    internal static void Run(Action<string, Action> check, string root)
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        var admin = db.Query("SELECT Id,Nome FROM Usuarios WHERE Login='admin';").Single();
        var op = db.Query("SELECT Id,Nome FROM Usuarios WHERE Login='operador';").Single();
        void Session(bool isAdmin) { var u = isAdmin ? admin : op; SessaoUsuario.Iniciar(Convert.ToInt32(u["Id"]), Convert.ToString(u["Nome"])!, isAdmin ? "ADMINISTRADOR" : "OPERADOR"); }
        Session(true);
        var products = new ProdutoService();
        var sales = new VendaService();
        var policyService = new AjustesVendaService();
        var product = new Produto { Nome = "PESO EXATO REGRESSÃO", CodigoBarras = "7890000000017", UnidadeMedida = "KG", EstoqueAtual = 30, PrecoVenda = 19.90m, PrecoCusto = 10 };
        products.Salvar(product);
        List<ItemCarrinho> Cart(decimal qtd) => [new() { ProdutoId = product.Id, Produto = product.Nome, UnidadeMedida = "KG", PrecoUnitario = product.PrecoVenda, Quantidade = qtd }];
        long Count(string table) => Convert.ToInt64(db.Query($"SELECT COUNT(*) FROM {table};").Single().Values.Single());

        check("Migração v2 para v3 preserva vendas e cria snapshot antes de atualizar", () =>
        {
            string path = Path.Combine(root, "v2-migration.db");
            using (var fixture = new DatabaseConnection(path))
                fixture.ExecuteScript("CREATE TABLE SchemaMigrations (Version INTEGER PRIMARY KEY,AppliedAt TEXT NOT NULL); INSERT INTO SchemaMigrations VALUES (1,'2026-01-01'),(2,'2026-01-01'); CREATE TABLE Vendas (Id INTEGER PRIMARY KEY,ValorTotal NUMERIC NOT NULL); INSERT INTO Vendas VALUES (123,7.50);");
            DatabaseInitializer.Initialize(path);
            DatabaseInitializer.Initialize(path);
            using var migrated = new DatabaseConnection(path);
            var row = migrated.Query("SELECT * FROM Vendas WHERE Id=123;").Single();
            Equal(7.50m, Money(row["ValorTotal"])); Equal(7.50m, Money(row["Subtotal"])); Equal(0m, Money(row["ValorDesconto"]));
            Equal(1, Directory.GetFiles(root, "v2-migration.db.pre-migration-v2-*.bak").Length);
            Equal(1L, Convert.ToInt64(migrated.Query("SELECT COUNT(*) FROM SchemaMigrations WHERE Version=3;").Single().Values.Single()));
        });
        check("Ajustes começam desabilitados e não persistem venda nem estoque", () =>
        {
            Equal(false, policyService.ObterPolitica().Habilitada);
            long count = Count("Vendas"); decimal stock = products.BuscarPorId(product.Id)!.EstoqueAtual;
            Blocked(() => sales.Registrar(Cart(.315m), "DINHEIRO", 10, new AjusteVenda { Desconto = 1 }));
            Equal(count, Count("Vendas")); Equal(stock, products.BuscarPorId(product.Id)!.EstoqueAtual);
        });
        var policy = new PoliticaAjustesVenda { Habilitada = true, MaxDescontoPercentual = 20, MaxAcrescimoValor = 5 };
        policyService.SalvarPolitica(policy);
        long percentSale = 0;
        check("Peso 0,315 KG, percentual, acréscimo e troco persistem em centavos", () =>
        {
            percentSale = sales.Registrar(Cart(.315m), "DINHEIRO", 10, new AjusteVenda { TipoDesconto = "PERCENTUAL", Desconto = 10, Acrescimo = .50m });
            var totals = sales.ObterTotais(percentSale);
            Equal(new TotaisVenda(6.27m, .63m, .50m, 6.14m), totals);
            var row = db.Query("SELECT Troco FROM Vendas WHERE Id=@Id;", new Dictionary<string, object?> { ["@Id"] = percentSale }).Single();
            Equal(3.86m, Money(row["Troco"]));
            Equal(29.685m, products.BuscarPorId(product.Id)!.EstoqueAtual);
        });
        check("Desconto monetário é aplicado uma vez e aparece no histórico", () =>
        {
            long id = sales.Registrar(Cart(.125m), "PIX", ajuste: new AjusteVenda { Desconto = .25m });
            Equal(new TotaisVenda(2.49m, .25m, 0, 2.24m), sales.ObterTotais(id));
            var history = sales.ListarHistorico(DateTime.Today, DateTime.Today).Single(s => s.Id == id);
            Equal(2.24m, history.Total); Equal(.25m, history.Desconto);
        });
        check("Limites, total negativo, dinheiro fracionado e quantidade inválida são rejeitados", () =>
        {
            long before = Count("Vendas");
            Blocked(() => sales.Registrar(Cart(.315m), "PIX", ajuste: new AjusteVenda { TipoDesconto = "PERCENTUAL", Desconto = 21 }));
            Blocked(() => sales.Registrar(Cart(.315m), "PIX", ajuste: new AjusteVenda { Desconto = 100 }));
            Blocked(() => sales.Registrar(Cart(.315m), "PIX", ajuste: new AjusteVenda { Acrescimo = 5.01m }));
            Blocked(() => sales.Registrar(Cart(.315m), "PIX", ajuste: new AjusteVenda { Desconto = -.01m }));
            Blocked(() => sales.Registrar(Cart(.315m), "DINHEIRO", 10.001m));
            Blocked(() => sales.Registrar(Cart(.3151m), "PIX"));
            Equal(before, Count("Vendas"));
        });
        check("Operador não concede ajustes sem regra explícita", () =>
        {
            Session(false);
            try { Blocked(() => sales.Registrar(Cart(.315m), "PIX", ajuste: new AjusteVenda { Desconto = .20m })); Blocked(() => policyService.SalvarPolitica(policy)); }
            finally { Session(true); }
        });
        check("Política configurada permite ajuste ao operador dentro do limite", () =>
        {
            policyService.SalvarPolitica(policy with { PermitirOperador = true });
            Session(false);
            try
            {
                long id = sales.Registrar(Cart(.750m), "PIX", ajuste: new AjusteVenda { TipoDesconto = "PERCENTUAL", Desconto = 5 });
                Equal(new TotaisVenda(14.93m, .75m, 0, 14.18m), sales.ObterTotais(id));
            }
            finally { Session(true); policyService.SalvarPolitica(policy); }
        });
        string operationId = Guid.NewGuid().ToString();
        long replayId = 0;
        check("Mesma operação repetida baixa estoque e movimenta caixa somente uma vez", () =>
        {
            decimal before = products.BuscarPorId(product.Id)!.EstoqueAtual; long count = Count("Vendas");
            replayId = sales.Registrar(Cart(1.235m), "PIX", operacaoId: operationId);
            Equal(replayId, sales.Registrar(Cart(1.235m), "PIX", operacaoId: operationId));
            Equal(count + 1, Count("Vendas")); Equal(before - 1.235m, products.BuscarPorId(product.Id)!.EstoqueAtual);
            Equal(1L, Convert.ToInt64(db.Query("SELECT COUNT(*) FROM MovimentacoesCaixa WHERE VendaId=@Id;", new Dictionary<string, object?> { ["@Id"] = replayId }).Single().Values.Single()));
        });
        check("Reuso do identificador com dados diferentes é bloqueado", () => Blocked(() => sales.Registrar(Cart(2.500m), "PIX", operacaoId: operationId)));
        check("Cancelamento estorna valor líquido e não pode ser repetido", () =>
        {
            decimal before = products.BuscarPorId(product.Id)!.EstoqueAtual;
            sales.Cancelar(replayId, "Teste de cancelamento");
            Equal(before + 1.235m, products.BuscarPorId(product.Id)!.EstoqueAtual);
            long movements = Count("MovimentacoesCaixa");
            Blocked(() => sales.Cancelar(replayId, "Cancelamento repetido"));
            Blocked(() => sales.Registrar(Cart(1.235m), "PIX", operacaoId: operationId));
            Equal(movements, Count("MovimentacoesCaixa"));
        });
        check("Relatórios conciliam bruto menos desconto mais acréscimo e excluem canceladas", () =>
        {
            var report = new RelatoriosService().Gerar(DateTime.Today, DateTime.Today);
            Equal(report.SubtotalVendas - report.TotalDescontos + report.TotalAcrescimos, report.TotalVendido);
            Equal(report.TotalVendido, report.Pagamentos.Sum(x => x.Total));
            Equal(false, report.Vendas.Any(x => x.Id == replayId));
            Equal(6.14m, report.Vendas.Single(x => x.Id == percentSale).Total);
        });
        check("Preço monetário e peso usam a mesma regra de arredondamento exibida", () =>
        {
            Equal(28.54m, new ItemCarrinho { Quantidade = 2, PrecoUnitario = 14.273m }.Subtotal);
            Equal(24.58m, new ItemCarrinho { Quantidade = 1.235m, PrecoUnitario = 19.90m }.Subtotal);
        });
        check("Dashboard separa vencidos, próximos e itens dentro da validade", () =>
        {
            foreach (var (name, days) in new[] { ("VENCIDO TESTE", -1), ("PRÓXIMO TESTE", 7), ("VÁLIDO TESTE", 120) })
                products.Salvar(new Produto { Nome = name, DataValidade = DateTime.Today.AddDays(days) });
            var summary = new DashboardService().ObterResumo();
            Equal(true, summary.ProdutosVencidos.Any(x => x.Produto == "VENCIDO TESTE" && x.Status == "VENCIDO"));
            Equal(true, summary.ProdutosValidadeProxima.Any(x => x.Produto == "PRÓXIMO TESTE"));
            Equal(false, summary.ProdutosValidadeProxima.Any(x => x.Produto is "VENCIDO TESTE" or "VÁLIDO TESTE"));
        });
        check("Ponte C# entrega contrato serializável com política e catálogo reais", () =>
        {
            var data = new PdvWebBridge().Handle("""{"version":1,"id":"get-data","action":"pdv.getData","payload":{}}""");
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            Equal(true, json.RootElement.GetProperty("ok").GetBoolean());
            Equal(true, json.RootElement.GetProperty("data").GetProperty("products").EnumerateArray().Any(p => p.GetProperty("barcode").GetString() == product.CodigoBarras));
            Equal(true, json.RootElement.GetProperty("data").GetProperty("adjustmentsPolicy").GetProperty("habilitada").GetBoolean());
        });
        check("Ponte rejeita versão, produto inexistente e inativo", () =>
        {
            var bridge = new PdvWebBridge();
            Blocked(() => bridge.Handle("""{"version":2,"id":"bad","action":"pdv.getData","payload":{}}"""));
            Blocked(() => bridge.Handle("""{"version":1,"id":"bad","action":"pdv.checkout","payload":{"items":[{"productId":999999,"quantity":1}],"paymentMethod":"PIX","amountReceived":0}}"""));
            products.Desativar(product.Id);
            Blocked(() => bridge.Handle(JsonSerializer.Serialize(new { version = 1, id = "inactive", action = "pdv.checkout", payload = new { items = new[] { new { productId = product.Id, quantity = 1 } }, paymentMethod = "PIX", amountReceived = 0 } })));
        });
        ApiScenarios.Run(check);
        policyService.SalvarPolitica(new PoliticaAjustesVenda());
    }

    private static decimal Money(object? value) => Convert.ToDecimal(value, CultureInfo.InvariantCulture);
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Esperado {expected}; recebido {actual}."); }
    private static void Blocked(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Operação inválida foi aceita."); }
}

internal static class ApiScenarios
{
    internal static void Run(Action<string, Action> check)
    {
        ProdutoCatalogoPublico? Query(string json, HttpStatusCode status = HttpStatusCode.OK, Action<HttpRequestMessage>? inspect = null)
        {
            using var client = new HttpClient(new Handler((request, _) => { inspect?.Invoke(request); return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") }); }));
            return new OpenFoodFactsService(client).BuscarPorCodigoAsync("3017620425035").GetAwaiter().GetResult();
        }
        check("Catálogo interpreta sucesso v3 e monta URL localizada correta", () =>
        {
            var result = Query("""{"status":"success","product":{"product_name_pt":"Creme de avelã","brands":"Marca","categories":"Doces"}}""",
                inspect: request => { if (!request.RequestUri!.AbsoluteUri.Contains("/api/v3/product/3017620425035.json") || !request.RequestUri.Query.Contains("lc=pt")) throw new Exception("URL inesperada."); });
            if (result?.Nome != "Creme de avelã") throw new Exception("Nome localizado ausente.");
        });
        check("Catálogo aceita status legado numérico e success_with_errors", () =>
        {
            if (Query("""{"status":1,"product":{"product_name":"Produto"}}""")?.Nome != "Produto"
                || Query("""{"status":"success_with_errors","product":{"generic_name":"Produto"}}""")?.Nome != "Produto") throw new Exception("Formato de sucesso não interpretado.");
        });
        check("Produto ausente e HTTP 404 retornam ausência, sem erro de programação", () =>
        {
            if (Query("""{"status":"failure"}""") is not null || Query("{}", HttpStatusCode.NotFound) is not null) throw new Exception("Ausência não interpretada.");
        });
        check("HTTP 503 e JSON inválido geram falhas claras", () =>
        {
            Expect<HttpRequestException>(() => Query("{}", HttpStatusCode.ServiceUnavailable));
            Expect<InvalidOperationException>(() => Query("{invalido"));
        });
        check("Timeout do catálogo e cancelamento do usuário têm comportamentos distintos", () =>
        {
            using var client = new HttpClient(new Handler((_, _) => Task.FromException<HttpResponseMessage>(new TaskCanceledException())));
            var service = new OpenFoodFactsService(client);
            Expect<TimeoutException>(() => service.BuscarPorCodigoAsync("3017620425035").GetAwaiter().GetResult());
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            Expect<OperationCanceledException>(() => service.BuscarPorCodigoAsync("3017620425035", cancel.Token).GetAwaiter().GetResult());
        });
    }
    private static void Expect<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception($"Era esperado {typeof(T).Name}."); }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
