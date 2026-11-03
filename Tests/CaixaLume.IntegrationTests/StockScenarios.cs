using System.Globalization;
using System.Text.Json;
using CaixaLume.Data;
using CaixaLume.Models;
using CaixaLume.Services;

internal static class StockScenarios
{
    internal static int Run(string root)
    {
        int passed=0;
        void Check(string name,Action action){action();Console.WriteLine($"PASSOU estoque {++passed:00}: {name}");}
        DatabaseInitializer.Initialize();
        var users=new UsuarioService(); users.CriarAdministradorInicial("ADMIN ESTOQUE","estoque","senha-estoque-123");
        var admin=users.Autenticar("estoque","senha-estoque-123")!.Value;SessaoUsuario.Iniciar(admin.Id,admin.Nome,admin.Perfil);
        using var db=new DatabaseConnection(DatabaseInitializer.DatabasePath);
        var products=new ProdutoService();var stock=new EstoqueService();var sales=new VendaService();var cash=new CaixaService();cash.Abrir(100);
        long Count(string table)=>Convert.ToInt64(db.Query($"SELECT COUNT(*) FROM {table};").Single().Values.Single());
        decimal Value(string sql)=>Convert.ToDecimal(db.Query(sql).Single().Values.Single(),CultureInfo.InvariantCulture);
        Produto Create(string name,string? barcode=null,string unit="UN",decimal balance=10){var p=new Produto{Nome=name,CodigoBarras=barcode,UnidadeMedida=unit,EstoqueAtual=balance,PrecoVenda=10};products.Salvar(p);return p;}
        string Finance()=>string.Join("|",new[]{"Vendas","ItensVenda","PagamentosVenda","MovimentacoesCaixa"}.Select(Count));
        var item=Create("PRODUTO DE TESTE","0012345");
        Check("Código preserva zeros, normaliza espaços e busca contém dados completos",()=>{
            Equal(item.Id,products.BuscarPorCodigoBarras(" 0012345 ")!.Id);
            Blocked(()=>Create("DUPLICADO"," 0012345 "));
            products.Desativar(item.Id);Blocked(()=>Create("DUPLICADO INATIVO","0012345"));products.Reativar(item.Id);
            var p=products.BuscarPorId(item.Id)!;p.DataValidade=DateTime.Today.AddDays(10);products.Salvar(p);
            Equal(p.DataValidade,products.BuscarPorCodigoBarras("0012345")!.DataValidade);
        });
        Check("Concorrência não cria duplicidade de código nem saldo parcial",()=>{
            int successes=0,blocked=0;
            Task Insert()=>Task.Run(()=>{try{Create("CONCORRENTE","990001");Interlocked.Increment(ref successes);}catch(InvalidOperationException){Interlocked.Increment(ref blocked);}catch(ProdutoValidationException){Interlocked.Increment(ref blocked);}});
            Task.WaitAll(Insert(),Insert());Equal(1,successes);Equal(1,blocked);Equal(1m,Value("SELECT COUNT(*) FROM Produtos WHERE CodigoBarras='990001';"));
        });
        Check("Saldo inicial e edição explícita são auditados com anterior, posterior e usuário",()=>{
            var p=products.BuscarPorId(item.Id)!;p.EstoqueAtual=8;products.Salvar(p);
            var move=stock.ListarMovimentacoes(item.Id).First();Equal(10m,move.SaldoAnterior);Equal(8m,move.SaldoPosterior);Equal(-2m,move.Variacao);Equal(admin.Id,move.UsuarioId);Assert(!string.IsNullOrWhiteSpace(move.Motivo));
            Equal(10m,stock.ListarMovimentacoes(item.Id).Last().Variacao);
        });
        Check("Cadastro desatualizado não sobrescreve estoque alterado por venda",()=>{
            var stale=products.BuscarPorId(item.Id)!;
            long sale=sales.RegistrarPorProdutoIds([(item.Id,1)],"PIX",operacaoId:Guid.NewGuid().ToString(),totalEsperado:10);
            stale.Nome="EDICAO ANTIGA";Blocked(()=>products.Salvar(stale));Equal(7m,products.BuscarPorId(item.Id)!.EstoqueAtual);
            sales.Cancelar(sale,"reversao do teste");Equal(8m,products.BuscarPorId(item.Id)!.EstoqueAtual);
        });
        Check("Histórico impede trocar unidade mesmo com saldo zerado",()=>{
            var p=Create("UNIDADE HISTORICA",balance:1);
            long sale=sales.RegistrarPorProdutoIds([(p.Id,1)],"PIX",operacaoId:Guid.NewGuid().ToString(),totalEsperado:10);
            p=products.BuscarPorId(p.Id)!;p.UnidadeMedida="KG";Blocked(()=>products.Salvar(p));sales.Cancelar(sale,"unidade preservada");
            Equal("UN",products.BuscarPorId(p.Id)!.UnidadeMedida);Equal(1m,products.BuscarPorId(p.Id)!.EstoqueAtual);
            var clean=Create("SEM HISTORICO",balance:0);clean.UnidadeMedida="KG";products.Salvar(clean);Equal("KG",products.BuscarPorId(clean.Id)!.UnidadeMedida);
        });
        Check("Entradas, saídas e ajuste negativo têm saldo preciso e não alteram financeiro",()=>{
            var p=Create("PESO",unit:"KG",balance:1);string finance=Finance();
            stock.Ajustar(p.Id,.125m,true,"recebimento");stock.Ajustar(p.Id,.315m,false,"perda identificada");stock.AjustarSaldo(p.Id,.500m,"inventario");
            Equal(.5m,products.BuscarPorId(p.Id)!.EstoqueAtual);var move=stock.ListarMovimentacoes(p.Id).First();Equal(.810m,move.SaldoAnterior);Equal(.500m,move.SaldoPosterior);Equal(-.310m,move.Variacao);Equal(finance,Finance());
            Blocked(()=>stock.Ajustar(p.Id,.0011m,true,"precisao invalida"));Blocked(()=>stock.Ajustar(p.Id,1,false,"saldo insuficiente"));Blocked(()=>stock.Ajustar(item.Id,.5m,true,"UN fracionaria"));Blocked(()=>stock.Ajustar(p.Id,1,true," "));
        });
        Check("Falha da auditoria reverte cadastro, movimento e alteração de estoque",()=>{
            long before=Count("Produtos"),moves=Count("MovimentacoesEstoque");decimal balance=products.BuscarPorId(item.Id)!.EstoqueAtual;
            db.ExecuteScript("CREATE TRIGGER StockFail BEFORE INSERT ON Auditoria WHEN NEW.Entidade IN ('PRODUTO','ESTOQUE') BEGIN SELECT RAISE(ABORT,'falha estoque'); END;");
            try{Blocked(()=>Create("NAO PERSISTIR","990099"));Blocked(()=>stock.AjustarSaldo(item.Id,1,"falha proposital"));}
            finally{db.ExecuteScript("DROP TRIGGER StockFail;");}
            Equal(before,Count("Produtos"));Equal(moves,Count("MovimentacoesEstoque"));Equal(balance,products.BuscarPorId(item.Id)!.EstoqueAtual);
        });
        Check("Cancelamento após ajuste manual preserva composição financeira e histórico de estoque",()=>{
            var p=Create("AJUSTE E VENDA");long sale=sales.RegistrarPorProdutoIds([(p.Id,3)],"MISTO",operacaoId:Guid.NewGuid().ToString(),totalEsperado:30,pagamentos:[new("DINHEIRO",10),new("PIX",20)]);
            stock.Ajustar(p.Id,1,true,"acrescimo de inventario");sales.Cancelar(sale,"cancelamento misto apos inventario");Equal(11m,products.BuscarPorId(p.Id)!.EstoqueAtual);
            Equal(30m,Value($"SELECT SUM(Valor) FROM MovimentacoesCaixa WHERE VendaId={sale} AND Tipo LIKE 'ESTORNO_%';"));
            var history=stock.ListarMovimentacoes(p.Id);Assert(history.Any(m=>m.VendaId==sale&&m.Tipo=="VENDA"));Assert(history.Any(m=>m.VendaId==sale&&m.Tipo=="ESTORNO"));Equal(11m,history.Sum(m=>m.Variacao??0));
        });
        Check("Exclusão lógica libera código e mantém estorno ligado ao produto original",()=>{
            var p=Create("EXCLUIR COM HISTORICO","990002");long sale=sales.RegistrarPorProdutoIds([(p.Id,1)],"PIX",operacaoId:Guid.NewGuid().ToString(),totalEsperado:10);
            products.Excluir(p.Id);Assert(products.BuscarPorId(p.Id)is null);var replacement=Create("SUBSTITUTO","990002",balance:2);
            sales.Cancelar(sale,"estorno do original excluido");Equal(10m,Value($"SELECT EstoqueAtual FROM Produtos WHERE Id={p.Id};"));Equal(2m,products.BuscarPorId(replacement.Id)!.EstoqueAtual);Assert(stock.ListarMovimentacoes(p.Id).Count>=3);
            Blocked(()=>products.Reativar(p.Id));
        });
        Check("Validade de reposição não esconde lote anterior; limites e mínimo são respeitados",()=>{
            var p=Create("VALIDADE",balance:3);p.DataValidade=DateTime.Today.AddDays(2);p.EstoqueMinimo=3;products.Salvar(p);
            stock.RegistrarEntradaMercadoria(p.Id,1,2,null,DateTime.Today.AddDays(60),null,"RECEB-1","compra");Equal(DateTime.Today.AddDays(2),products.BuscarPorId(p.Id)!.DataValidade);
            stock.RegistrarEntradaMercadoria(p.Id,1,2,null,DateTime.Today.AddDays(-1),null,"RECEB-2","compra anterior");Equal(DateTime.Today.AddDays(-1),products.BuscarPorId(p.Id)!.DataValidade);
            var today=Create("VALIDADE HOJE");today.DataValidade=DateTime.Today;products.Salvar(today);
            var limit=Create("VALIDADE LIMITE");limit.DataValidade=DateTime.Today.AddDays(30);products.Salvar(limit);
            var beyond=Create("VALIDADE ALEM");beyond.DataValidade=DateTime.Today.AddDays(31);products.Salvar(beyond);
            stock.AjustarSaldo(p.Id,3,"limite minimo");var dash=new DashboardService().ObterResumo();Assert(dash.ProdutosEstoqueBaixo.Any(x=>x.Produto==p.Nome));Assert(dash.ProdutosVencidos.Any(x=>x.Produto==p.Nome));Equal("VENCE HOJE",dash.ProdutosValidadeProxima.Single(x=>x.Produto==today.Nome).Status);Assert(dash.ProdutosValidadeProxima.Any(x=>x.Produto==limit.Nome));Assert(!dash.ProdutosValidadeProxima.Any(x=>x.Produto==beyond.Nome));
        });
        Check("Paginação acessa mais de 2000 vendas sem perda, duplicidade ou inclusão de novas vendas",()=>{
            db.ExecuteScript("WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<2105) INSERT INTO Vendas (DataVenda,ValorTotal,FormaPagamento,Status,Subtotal) SELECT '2000-01-01 10:00:00',1,'PIX',CASE WHEN n%3=0 THEN 'Cancelada' ELSE 'Concluida' END,1 FROM seq;");
            DateTime day=new(2000,1,1);var first=sales.ListarHistoricoPaginado(day,day,tamanho:100);Equal(2105L,first.Total);Assert(first.TemProxima);
            db.Execute("INSERT INTO Vendas (DataVenda,ValorTotal,FormaPagamento,Status,Subtotal) VALUES ('2000-01-01 10:00:00',1,'PIX','Concluida',1);");
            var ids=new List<long>();for(int page=1;page<=22;page++)ids.AddRange(sales.ListarHistoricoPaginado(day,day,pagina:page,tamanho:100,limiteId:first.LimiteId).Itens.Select(x=>x.Id));
            Equal(2105,ids.Count);Equal(2105,ids.Distinct().Count());Assert(ids.SequenceEqual(ids.OrderDescending()));Equal(2106,sales.ListarHistorico(day,day).Count);
            var filtered=sales.ListarHistoricoPaginado(day,day,"Cancelada");Equal(701L,filtered.Total);Assert(filtered.Itens.All(x=>x.Status=="Cancelada"));
            Blocked(()=>sales.ListarHistoricoPaginado(day,day,pagina:0));Blocked(()=>sales.ListarHistoricoPaginado(day,day,tamanho:501));Blocked(()=>sales.ListarHistoricoPaginado(day.AddDays(1),day));
            using var payload=JsonDocument.Parse("{\"module\":\"sales\",\"start\":\"2000-01-01\",\"end\":\"2000-01-01\",\"page\":22}");
            using var result=JsonDocument.Parse(JsonSerializer.Serialize(new ManagementWebBridge().GetData(payload.RootElement),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));Equal(6,result.RootElement.GetProperty("data").GetProperty("sales").GetArrayLength());
        });
        Check("Histórico de estoque não oculta registros além do antigo limite de 1000",()=>{
            db.ExecuteScript($"WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<1005) INSERT INTO MovimentacoesEstoque (ProdutoId,Tipo,Quantidade,UnidadeMedida,DataMovimentacao,Motivo,UsuarioId) SELECT {item.Id},'LEGADO_TESTE',0,'UN','2000-01-01','fixture de historico',{admin.Id} FROM seq;");Equal(1005,stock.ListarMovimentacoes(item.Id,new DateTime(2000,1,1),new DateTime(2000,1,1)).Count);
        });
        Check("Índices são usados em código de barras e período de vendas",()=>{
            string Plan(string sql)=>string.Join(" ",db.Query("EXPLAIN QUERY PLAN "+sql).Select(r=>Convert.ToString(r["detail"])));
            Assert(Plan("SELECT Id FROM Produtos WHERE TRIM(CodigoBarras)='0012345';").Contains("IX_Produtos_CodigoNormalizado"));Assert(Plan("SELECT Id FROM Vendas WHERE Status='Concluida' AND DataVenda>='2000-01-01' AND DataVenda<'2000-01-02';").Contains("IX_Vendas_Status_Data_Id"));Assert(db.Query("PRAGMA foreign_key_check;").Count==0);
        });
        Check("Duplicidades legadas são preservadas, sinalizadas e podem ser corrigidas explicitamente",()=>{
            db.ExecuteScript("DROP TRIGGER Produtos_Codigo_Insert; DROP TRIGGER Produtos_Codigo_Update; INSERT INTO Produtos (Nome,CodigoBarras) VALUES ('LEGADO DUP A','778899'),('LEGADO DUP B',' 778899 '); DELETE FROM SchemaMigrations WHERE Version=8;");
            DatabaseInitializer.Initialize();Blocked(()=>products.BuscarPorCodigoBarras("778899"));Equal(2m,Value("SELECT COUNT(*) FROM Produtos WHERE TRIM(CodigoBarras)='778899';"));
            var duplicate=products.Listar("LEGADO DUP B").Single();duplicate.CodigoBarras="778899-REVISTO";products.Salvar(duplicate);Assert(products.BuscarPorCodigoBarras("778899")is not null);
        });
        Check("Migração v8 é idempotente e preserva fornecedores, categorias, estoque e pagamentos",()=>{
            var category=new Categoria{Nome="CATEGORIA PRESERVADA"};new CategoriaService().Salvar(category);var supplier=new Fornecedor{Nome="FORNECEDOR PRESERVADO"};new FornecedorService().Salvar(supplier);
            string path=Path.Combine(root,"stock-v7.db");db.BackupTo(path);long moves=Count("MovimentacoesEstoque");
            using(var fixture=new DatabaseConnection(path)){fixture.Execute("DELETE FROM SchemaMigrations WHERE Version>=8;");
                fixture.ExecuteScript("DROP TRIGGER Produtos_Versao; ALTER TABLE Produtos DROP COLUMN Versao; ALTER TABLE MovimentacoesEstoque DROP COLUMN SaldoAnterior; ALTER TABLE MovimentacoesEstoque DROP COLUMN SaldoPosterior;");}
            DatabaseInitializer.Initialize(path);DatabaseInitializer.Initialize(path);
            using var migrated=new DatabaseConnection(path);Equal(moves,Convert.ToInt64(migrated.Query("SELECT COUNT(*) FROM MovimentacoesEstoque;").Single().Values.Single()));Equal(Count("PagamentosVenda"),Convert.ToInt64(migrated.Query("SELECT COUNT(*) FROM PagamentosVenda;").Single().Values.Single()));Assert(migrated.Query("SELECT 1 FROM Categorias WHERE Nome='CATEGORIA PRESERVADA';").Any());Assert(migrated.Query("SELECT 1 FROM Fornecedores WHERE Nome='FORNECEDOR PRESERVADO';").Any());Equal(2,migrated.Query("SELECT 1 FROM sqlite_master WHERE name LIKE 'Usuarios_ProtegerUltimoAdmin_%';").Count);Assert(migrated.Query("PRAGMA foreign_key_check;").Count==0);
        });
        Console.WriteLine($"SUCESSO: {passed} cenários de estoque e consultas.");return 0;
    }
    static void Assert(bool value){if(!value)throw new Exception("Asserção de estoque falhou.");}
    static void Equal<T>(T expected,T actual){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception($"Esperado {expected}; recebido {actual}.");}
    static void Blocked(Action action){try{action();}catch(InvalidOperationException){return;}catch(ProdutoValidationException){return;}throw new Exception("Operação indevida foi aceita.");}
}
