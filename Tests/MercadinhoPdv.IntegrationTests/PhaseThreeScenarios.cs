using System.Globalization;
using System.Text.Json;
using MercadinhoPdv.Data;
using MercadinhoPdv.Models;
using MercadinhoPdv.Services;
using MercadinhoPdv.Forms;

internal static class PhaseThreeScenarios
{
    internal static void Run(Action<string, Action> check)
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        check("Parser nativo de peso aceita vírgula e ponto e rejeita fração de UN", () =>
        {
            foreach (var (text, expected) in new[] { ("0,125", .125m), ("0.315", .315m), ("0,750", .750m), ("1.235", 1.235m), ("2,500", 2.500m) })
                Equal(expected, QuantidadeService.Interpretar(text, "KG", 5));
            Blocked(() => QuantidadeService.Interpretar("0", "KG", 5));
            Blocked(() => QuantidadeService.Interpretar("1,2345", "KG", 5));
            Blocked(() => QuantidadeService.Interpretar("0,315", "UN", 5));
            Blocked(() => QuantidadeService.Interpretar("6", "UN", 5));
        });
        var products = new ProdutoService();
        var product = new Produto { Nome = "PONTE REGRESSÃO", CodigoBarras = "7891122334455", EstoqueAtual = 20, PrecoVenda = 3.50m };
        products.Salvar(product);
        check("Total divergente exibido no carrinho bloqueia venda antes da baixa", () =>
        {
            long before = Convert.ToInt64(db.Query("SELECT COUNT(*) FROM Vendas;").Single().Values.Single());
            Blocked(() => new VendaService().Registrar([new ItemCarrinho { ProdutoId = product.Id, Produto = product.Nome, PrecoUnitario = 3.50m, Quantidade = 1 }], "PIX", totalEsperado: 3));
            Equal(before, Convert.ToInt64(db.Query("SELECT COUNT(*) FROM Vendas;").Single().Values.Single()));
            Equal(20m, products.BuscarPorId(product.Id)!.EstoqueAtual);
        });
        check("Checkout real pela ponte retorna contrato, total e mesma venda ao repetir", () =>
        {
            string message = JsonSerializer.Serialize(new { version = 1, id = "checkout-contract", action = "pdv.checkout", payload = new { items = new[] { new { productId = product.Id, quantity = 2 } }, paymentMethod = "DINHEIRO", amountReceived = 10, operationId = Guid.NewGuid().ToString(), expectedTotal = 7 } });
            var bridge = new PdvWebBridge();
            using var first = JsonDocument.Parse(JsonSerializer.Serialize(bridge.Handle(message), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            using var repeat = JsonDocument.Parse(JsonSerializer.Serialize(bridge.Handle(message), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            var result = first.RootElement.GetProperty("data");
            Equal(7m, result.GetProperty("total").GetDecimal()); Equal(3m, result.GetProperty("change").GetDecimal());
            Equal(result.GetProperty("saleId").GetInt64(), repeat.RootElement.GetProperty("data").GetProperty("saleId").GetInt64());
            Equal(18m, products.BuscarPorId(product.Id)!.EstoqueAtual);
            db.Execute("UPDATE Produtos SET PrecoVenda=4,Ativo=0 WHERE Id=@Id;",new Dictionary<string,object?> { ["@Id"]=product.Id });
            try
            {
                using var changedCatalog = JsonDocument.Parse(JsonSerializer.Serialize(bridge.Handle(message),new JsonSerializerOptions { PropertyNamingPolicy=JsonNamingPolicy.CamelCase }));
                Equal(result.GetProperty("saleId").GetInt64(), changedCatalog.RootElement.GetProperty("data").GetProperty("saleId").GetInt64());
                Equal(18m, products.BuscarPorId(product.Id)!.EstoqueAtual);
            }
            finally { db.Execute("UPDATE Produtos SET PrecoVenda=3.50,Ativo=1 WHERE Id=@Id;",new Dictionary<string,object?> { ["@Id"]=product.Id }); }
        });
        var cash = new CaixaService();
        check("Movimentos e fechamento de caixa rejeitam frações menores que centavos", () =>
        {
            Blocked(() => cash.RegistrarMovimentacao("SUPRIMENTO",1.001m,"Inválido"));
            Blocked(() => cash.Fechar(1.001m));
        });
        cash.Fechar(cash.ObterAberto()!.ValorEsperadoDinheiro);
        var expenseService = new DespesasService();
        var expense = new DespesaLinha { Descricao = "DESPESA SEM CAIXA", FormaPagamento = "PIX", Valor = 10 };
        expenseService.Salvar(expense);
        expense.Id = Convert.ToInt64(db.Query("SELECT MAX(Id) FROM Despesas;").Single().Values.Single());
        cash.Abrir(100);
        check("Despesa alterada de Pix para dinheiro passa a compor o caixa aberto", () =>
        {
            expense.FormaPagamento = "DINHEIRO"; expenseService.Salvar(expense);
            Equal(90m, cash.ObterAberto()!.ValorEsperadoDinheiro);
        });
        check("Despesa sem dinheiro disponível é bloqueada sem alterar o saldo", () =>
        {
            Blocked(() => expenseService.Salvar(new DespesaLinha { Descricao = "MAIOR QUE O SALDO", Valor = 101, FormaPagamento = "DINHEIRO" }));
            Equal(90m, cash.ObterAberto()!.ValorEsperadoDinheiro);
        });
        cash.Fechar(90);
        check("Despesa de caixa fechado não permite editar ou cancelar valores históricos", () =>
        {
            expense.Valor = 11;
            Blocked(() => expenseService.Salvar(expense)); Blocked(() => expenseService.Cancelar(expense.Id, "Teste"));
            Equal(10m, expenseService.Listar(DateTime.Today, DateTime.Today).Single(x => x.Id == expense.Id).Valor);
        });
        check("Formulários nativos constroem seus controles nas cinco dimensões solicitadas", () =>
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    Func<Form>[] factories = [() => new MainForm(), () => new PdvForm(), () => new ProdutosForm(), () => new ProdutoForm(products, null), () => new EstoqueForm(), () => new CaixaForm(), () => new VendasForm(), () => new DespesasForm(), () => new RelatoriosForm(), () => new ConfiguracoesForm(), () => new FornecedoresForm(), () => new UsuariosForm(), () => new EntradaMercadoriaForm("")];
                    foreach (var factory in factories)
                        using (var form = factory())
                        {
                            foreach (var size in new[] { new Size(1024,768), new Size(1280,720), new Size(1366,768), new Size(1440,900), new Size(1920,1080) })
                            { form.Size = size; form.CreateControl(); form.PerformLayout(); if (form.Controls.Count == 0) throw new Exception("Formulário sem controles."); }
                            if (form is ConfiguracoesForm && Descendants(form).Any(c => c is Button && c.Text.Contains("Limpar histórico"))) throw new Exception("Ferramenta de limpeza exposta no ambiente normal.");
                        }
                }
                catch (Exception exception) { failure = exception; }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
            if (failure is not null) throw failure;
        });
    }
    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected,actual)) throw new Exception($"Esperado {expected}; recebido {actual}."); }
    private static void Blocked(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Era esperado bloqueio."); }
}
