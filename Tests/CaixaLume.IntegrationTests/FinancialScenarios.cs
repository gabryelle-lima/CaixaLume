using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using CaixaLume.Data;
using CaixaLume.Models;
using CaixaLume.Services;

internal static class FinancialScenarios
{
    internal static int Run(string root)
    {
        int passed = 0;
        void Check(string name, Action action) { action(); Console.WriteLine($"PASSOU financeiro {++passed:00}: {name}"); }
        DatabaseInitializer.Initialize();
        var users = new UsuarioService(); users.CriarAdministradorInicial("ADMIN FINANCEIRO","financeiro","senha-financeira-123");
        var user = users.Autenticar("financeiro","senha-financeira-123")!.Value;
        SessaoUsuario.Iniciar(user.Id,user.Nome,user.Perfil);
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        var products = new ProdutoService(); var sales = new VendaService(); var cash = new CaixaService();
        var unit = new Produto { Nome="UN FINANCEIRO", PrecoVenda=10, PrecoCusto=3, EstoqueAtual=1000, UnidadeMedida="UN" }; products.Salvar(unit);
        var kg = new Produto { Nome="KG FINANCEIRO", PrecoVenda=19.90m, PrecoCusto=5, EstoqueAtual=100, UnidadeMedida="KG" }; products.Salvar(kg);
        cash.Abrir(100);
        List<(int ProdutoId, decimal Quantidade)> Cart(decimal q=1) => [(unit.Id,q)];
        string Op() => Guid.NewGuid().ToString("D");
        decimal Stock(int id) => products.BuscarPorId(id)!.EstoqueAtual;
        long Count(string table) => Convert.ToInt64(db.Query($"SELECT COUNT(*) FROM {table};").Single().Values.Single());
        decimal Money(string sql) => Convert.ToDecimal(db.Query(sql).Single().Values.Single(),CultureInfo.InvariantCulture);
        string Snapshot() => string.Join("|",new[]{"Vendas","ItensVenda","MovimentacoesEstoque","MovimentacoesCaixa","PagamentosVenda","Auditoria"}.Select(Count)) + $"|{Stock(unit.Id)}|{Stock(kg.Id)}|{cash.ObterAberto()?.ValorEsperadoDinheiro}";
        long normal=0;
        Check("Venda normal grava itens, estoque, financeiro e pagamento manual em centavos", () =>
        {
            normal=sales.RegistrarPorProdutoIds(Cart(),"DINHEIRO",20,operacaoId:Op(),totalEsperado:10);
            Equal(999m,Stock(unit.Id)); Equal(110m,cash.ObterAberto()!.ValorEsperadoDinheiro);
            Equal(10m,Money($"SELECT Troco FROM Vendas WHERE Id={normal};"));
            Equal(1000m,Money($"SELECT ValorCentavos FROM PagamentosVenda WHERE VendaId={normal};"));
            Equal(2000m,Money($"SELECT RecebidoCentavos FROM PagamentosVenda WHERE VendaId={normal};"));
            Assert(db.Query($"SELECT 1 FROM PagamentosVenda WHERE VendaId={normal} AND Origem='MANUAL' AND Status='REGISTRADO_MANUALMENTE';").Any());
        });
        string replay=Op(); long repeated=0;
        Check("Duplo clique concorrente registra somente uma venda", () =>
        {
            long before=Count("Vendas"); decimal stock=Stock(unit.Id);
            var a=Task.Run(()=>sales.RegistrarPorProdutoIds(Cart(),"PIX",operacaoId:replay,totalEsperado:10));
            var b=Task.Run(()=>sales.RegistrarPorProdutoIds(Cart(),"PIX",operacaoId:replay,totalEsperado:10)); Task.WaitAll(a,b);
            repeated=a.Result; Equal(a.Result,b.Result); Equal(before+1,Count("Vendas")); Equal(stock-1,Stock(unit.Id));
            Equal(1m,Money($"SELECT COUNT(*) FROM MovimentacoesCaixa WHERE VendaId={repeated};"));
        });
        Check("Reenvio após timeout retorna venda mesmo com produto inativo ou preço alterado", () =>
        {
            products.Desativar(unit.Id); db.Execute($"UPDATE Produtos SET PrecoVenda=11 WHERE Id={unit.Id};");
            string before=Snapshot();
            Equal(repeated,sales.RegistrarPorProdutoIds(Cart(),"PIX",operacaoId:replay,totalEsperado:10)); Equal(before,Snapshot());
            Blocked(()=>sales.RegistrarPorProdutoIds(Cart(2),"PIX",operacaoId:replay,totalEsperado:20));
            products.Reativar(unit.Id); db.Execute($"UPDATE Produtos SET PrecoVenda=10 WHERE Id={unit.Id};");
        });
        Check("Outro usuário não consulta nem reutiliza operação alheia", () =>
        {
            users.Salvar(0,"OPERADOR FIN","operador-fin","OPERADOR","senha-financeira-123");
            var other=users.Autenticar("operador-fin","senha-financeira-123")!.Value;
            SessaoUsuario.Iniciar(other.Id,other.Nome,other.Perfil);
            try { Assert(sales.ObterPorOperacao(replay) is null); Blocked(()=>sales.RegistrarPorProdutoIds(Cart(),"PIX",operacaoId:replay,totalEsperado:10)); }
            finally { SessaoUsuario.Iniciar(user.Id,user.Nome,user.Perfil); }
        });
        Check("Estoque insuficiente e linhas repetidas não deixam gravações parciais", () =>
        {
            string before=Snapshot();
            Blocked(()=>sales.RegistrarPorProdutoIds(Cart(2000),"PIX",operacaoId:Op()));
            Blocked(()=>sales.RegistrarPorProdutoIds([(unit.Id,600),(unit.Id,600)],"PIX",operacaoId:Op())); Equal(before,Snapshot());
        });
        Check("Quantidade decimal por KG, desconto percentual e acréscimo arredondam no backend", () =>
        {
            new AjustesVendaService().SalvarPolitica(new PoliticaAjustesVenda { Habilitada=true, MaxDescontoPercentual=100,MaxAcrescimoValor=100 });
            long id=sales.RegistrarPorProdutoIds([(kg.Id,.315m)],"DINHEIRO",10,new AjusteVenda { TipoDesconto="PERCENTUAL",Desconto=10,Acrescimo=.50m },Op(),6.14m);
            Equal(new TotaisVenda(6.27m,.63m,.50m,6.14m),sales.ObterTotais(id)); Equal(99.685m,Stock(kg.Id));
            Equal(3.86m,Money($"SELECT Troco FROM Vendas WHERE Id={id};"));
        });
        Check("UN fracionária, KG com mais de três casas, centavos inválidos e total divergente são bloqueados", () =>
        {
            string before=Snapshot();
            Blocked(()=>sales.RegistrarPorProdutoIds(Cart(.5m),"PIX",operacaoId:Op()));
            Blocked(()=>sales.RegistrarPorProdutoIds([(kg.Id,.0001m)],"PIX",operacaoId:Op()));
            Blocked(()=>sales.RegistrarPorProdutoIds(Cart(),"DINHEIRO",10.001m,operacaoId:Op()));
            Blocked(()=>sales.RegistrarPorProdutoIds(Cart(),"DINHEIRO",9,operacaoId:Op()));
            Blocked(()=>sales.RegistrarPorProdutoIds(Cart(),"PIX",operacaoId:Op(),totalEsperado:9)); Equal(before,Snapshot());
        });
        Check("Repetições do mesmo produto são somadas antes de arredondar", () =>
        {
            long id=sales.RegistrarPorProdutoIds([(kg.Id,.001m),(kg.Id,.001m)],"PIX",operacaoId:Op(),totalEsperado:.04m);
            Equal(.04m,sales.ObterTotais(id).Total); Equal(1,sales.Detalhes(id).Count);
        });
        long mixed=0; string mixedOp=Op();
        Check("Pagamento misto com troco movimenta somente o dinheiro líquido", () =>
        {
            decimal before=cash.ObterAberto()!.ValorEsperadoDinheiro;
            mixed=sales.RegistrarPorProdutoIds(Cart(),"MISTO",operacaoId:mixedOp,totalEsperado:10,pagamentos:[new("DINHEIRO",20),new("PIX",4)]);
            Equal(before+6,cash.ObterAberto()!.ValorEsperadoDinheiro);
            Equal(14m,Money($"SELECT Troco FROM Vendas WHERE Id={mixed};"));
            Equal(1000m,Money($"SELECT SUM(ValorCentavos) FROM PagamentosVenda WHERE VendaId={mixed};"));
            Equal(6m,Money($"SELECT Valor FROM MovimentacoesCaixa WHERE VendaId={mixed} AND Tipo='VENDA_DINHEIRO';"));
            Equal(4m,Money($"SELECT Valor FROM MovimentacoesCaixa WHERE VendaId={mixed} AND Tipo='VENDA_PIX';"));
        });
        Check("Relatório discrimina parcelas mistas e concilia o total líquido", () =>
        {
            var report=new RelatoriosService().Gerar(DateTime.Today,DateTime.Today);
            Equal(report.TotalVendido,report.Pagamentos.Sum(p=>p.Total));
            Assert(report.Pagamentos.All(p=>p.FormaPagamento!="MISTO"));
            Equal(report.SubtotalVendas-report.TotalDescontos+report.TotalAcrescimos,report.TotalVendido);
        });
        Check("Reenvio misto é idempotente; mudar composição é rejeitado", () =>
        {
            string before=Snapshot();
            Equal(mixed,sales.RegistrarPorProdutoIds(Cart(),"MISTO",operacaoId:mixedOp,totalEsperado:10,pagamentos:[new("DINHEIRO",20),new("PIX",4)]));
            Blocked(()=>sales.RegistrarPorProdutoIds(Cart(),"MISTO",operacaoId:mixedOp,totalEsperado:10,pagamentos:[new("DINHEIRO",20),new("PIX",3)])); Equal(before,Snapshot());
        });
        Check("Misto sem cobertura, troco eletrônico, forma repetida e valor negativo são rejeitados", () =>
        {
            string before=Snapshot();
            foreach(var partes in new List<PagamentoVenda>[] { [new("DINHEIRO",1),new("PIX",2)], [new("PIX",8),new("DÉBITO",5)], [new("PIX",5),new("PIX",5)], [new("DINHEIRO",-1),new("PIX",11)] })
                Blocked(()=>sales.RegistrarPorProdutoIds(Cart(),"MISTO",operacaoId:Op(),pagamentos:partes));
            Equal(before,Snapshot());
        });
        Check("Falhas em cada etapa revertem venda, itens, estoque, caixa, pagamentos e auditoria", () =>
        {
            foreach(string table in new[]{"ItensVenda","MovimentacoesEstoque","PagamentosVenda","MovimentacoesCaixa","Auditoria"})
            {
                string before=Snapshot(); string operation=Op();
                db.ExecuteScript($"CREATE TRIGGER financial_failure BEFORE INSERT ON {table} BEGIN SELECT RAISE(ABORT,'interrupcao simulada'); END;");
                try { Blocked(()=>sales.RegistrarPorProdutoIds(Cart(),"PIX",operacaoId:operation)); }
                finally { db.ExecuteScript("DROP TRIGGER financial_failure;"); }
                Equal(before,Snapshot()); Assert(sales.ObterPorOperacao(operation) is null);
            }
        });
        Check("Cancelamento misto restaura estoque e estorna cada meio uma única vez", () =>
        {
            decimal stock=Stock(unit.Id), before=cash.ObterAberto()!.ValorEsperadoDinheiro;
            sales.Cancelar(mixed,"teste financeiro"); Equal(stock+1,Stock(unit.Id)); Equal(before-6,cash.ObterAberto()!.ValorEsperadoDinheiro);
            Equal(6m,Money($"SELECT Valor FROM MovimentacoesCaixa WHERE VendaId={mixed} AND Tipo='ESTORNO_VENDA_DINHEIRO';"));
            Equal(4m,Money($"SELECT Valor FROM MovimentacoesCaixa WHERE VendaId={mixed} AND Tipo='ESTORNO_VENDA_PIX';"));
            string snapshot=Snapshot(); Blocked(()=>sales.Cancelar(mixed,"repetido"));
            Blocked(()=>sales.RegistrarPorProdutoIds(Cart(),"MISTO",operacaoId:mixedOp,totalEsperado:10,pagamentos:[new("DINHEIRO",20),new("PIX",4)])); Equal(snapshot,Snapshot());
        });
        Check("Falha durante cancelamento reverte status, estoque e estornos", () =>
        {
            string before=Snapshot();
            db.ExecuteScript("CREATE TRIGGER financial_cancel_failure BEFORE INSERT ON Auditoria WHEN NEW.Acao='CANCELAMENTO' BEGIN SELECT RAISE(ABORT,'falha cancelamento'); END;");
            try { Blocked(()=>sales.Cancelar(normal,"teste de rollback")); }
            finally { db.ExecuteScript("DROP TRIGGER financial_cancel_failure;"); }
            Equal(before,Snapshot()); Equal("Concluida",sales.ObterPorOperacao(replay)!.Status);
            Equal("Concluida",Convert.ToString(db.Query($"SELECT Status FROM Vendas WHERE Id={normal};").Single()["Status"]));
        });
        Check("Ponte exige ID e total e não aceita confirmação bancária fictícia", () =>
        {
            object Payload(string? op,decimal? total,string? status=null) => new { items=new[]{new { productId=unit.Id,quantity=1 }},paymentMethod="PIX",amountReceived=10,operationId=op,expectedTotal=total,paymentStatus=status };
            object Send(object payload) => new PdvWebBridge().Handle(JsonSerializer.Serialize(new { version=1,id="financial",action="pdv.checkout",payload }));
            string before=Snapshot();
            Blocked(()=>Send(Payload(null,10))); Blocked(()=>Send(Payload(Guid.Empty.ToString(),10))); Blocked(()=>Send(Payload(Op(),null))); Blocked(()=>Send(Payload(Op(),10,"CONFIRMADO_EXTERNAMENTE"))); Equal(before,Snapshot());
            string operation=Op(); object payload=Payload(operation,10);
            string first=JsonSerializer.Serialize(Send(payload)); Equal(first,JsonSerializer.Serialize(Send(payload)));
            Assert(first.Contains("REGISTRADO_MANUALMENTE"));
            object mixedPayload=new { items=new[]{new { productId=unit.Id,quantity=1 }},paymentMethod="MISTO",amountReceived=0,operationId=Op(),expectedTotal=10,payments=new[]{new { forma="DINHEIRO",valor=20 },new { forma="PIX",valor=4 }} };
            using var json=JsonDocument.Parse(JsonSerializer.Serialize(Send(mixedPayload),new JsonSerializerOptions { PropertyNamingPolicy=JsonNamingPolicy.CamelCase }));
            Equal(14m,json.RootElement.GetProperty("data").GetProperty("change").GetDecimal());
            Equal(24m,json.RootElement.GetProperty("data").GetProperty("amountReceived").GetDecimal());
        });
        Check("Fechamento concilia dinheiro e impede cancelamento de caixa fechado", () =>
        {
            decimal saldo=cash.ObterAberto()!.ValorEsperadoDinheiro; var fechamento=cash.Fechar(saldo);
            Equal(0m,fechamento.Diferenca); Equal(saldo,cash.ListarHistorico().First().ValorEsperado);
            Blocked(()=>sales.Cancelar(normal,"caixa fechado"));
            Equal(repeated,sales.RegistrarPorProdutoIds(Cart(),"PIX",operacaoId:replay,totalEsperado:10)); cash.Abrir(100);
        });
        Check("Estorno sem dinheiro disponível é bloqueado atomicamente", () =>
        {
            long id=sales.RegistrarPorProdutoIds(Cart(),"DINHEIRO",10,operacaoId:Op());
            cash.RegistrarMovimentacao("SANGRIA",cash.ObterAberto()!.ValorEsperadoDinheiro,"retirada de teste");
            string before=Snapshot(); Blocked(()=>sales.Cancelar(id,"sem saldo")); Equal(before,Snapshot());
            cash.RegistrarMovimentacao("SUPRIMENTO",10,"devolucao de teste"); sales.Cancelar(id,"com saldo");
        });
        Check("Encerramento forçado durante a venda recupera journal sem transação parcial", () =>
        {
            string before=Snapshot(); string operation=Op(); string marker=Path.Combine(root,"crash-ready");
            db.ExecuteScript("CREATE TRIGGER financial_process_stop AFTER INSERT ON ItensVenda BEGIN SELECT crash_probe(); END;");
            var psi=new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true };
            if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath),"dotnet",StringComparison.OrdinalIgnoreCase)) psi.ArgumentList.Add(typeof(FinancialScenarios).Assembly.Location);
            psi.ArgumentList.Add("--crash-financial");
            psi.Environment["FIN_CRASH_MARKER"]=marker; psi.Environment["FIN_CRASH_OPERATION"]=operation;
            psi.Environment["FIN_CRASH_USER"]=user.Id.ToString(); psi.Environment["FIN_CRASH_PRODUCT"]=unit.Id.ToString();
            using var child=Process.Start(psi)!;
            try
            {
                var wait=Stopwatch.StartNew();
                while(!File.Exists(marker) && !child.HasExited && wait.Elapsed<TimeSpan.FromSeconds(15)) Thread.Sleep(50);
                Assert(File.Exists(marker)); child.Kill(entireProcessTree:true); Assert(child.WaitForExit(5000));
            }
            finally { if(!child.HasExited) { child.Kill(entireProcessTree:true);child.WaitForExit(5000); } db.ExecuteScript("DROP TRIGGER financial_process_stop;"); }
            Equal(before,Snapshot()); Assert(sales.ObterPorOperacao(operation) is null);
            Equal("ok",Convert.ToString(db.Query("PRAGMA integrity_check;").Single().Values.Single()));
            Assert(db.Query("PRAGMA foreign_key_check;").Count==0);
            long id=sales.RegistrarPorProdutoIds(Cart(),"PIX",operacaoId:operation,totalEsperado:10);
            Equal(id,sales.RegistrarPorProdutoIds(Cart(),"PIX",operacaoId:operation,totalEsperado:10));
        });
        Check("Migração v7 preserva dados, proteção v6 e classifica pagamentos antigos como manuais", () =>
        {
            string path=Path.Combine(root,"finance-v6.db"); db.BackupTo(path);
            using(var fixture=new DatabaseConnection(path)) { fixture.Execute("DELETE FROM SchemaMigrations WHERE Version>=7;");fixture.ExecuteScript("DROP TABLE PagamentosVenda;"); }
            DatabaseInitializer.Initialize(path); DatabaseInitializer.Initialize(path);
            using var migrated=new DatabaseConnection(path);
            Equal(Count("Vendas"),Convert.ToInt64(migrated.Query("SELECT COUNT(*) FROM Vendas;").Single().Values.Single()));
            Equal(2,migrated.Query("SELECT 1 FROM sqlite_master WHERE type='trigger' AND name LIKE 'Usuarios_ProtegerUltimoAdmin_%';").Count);
            Equal(Convert.ToString(db.Query("SELECT SenhaHash FROM Usuarios WHERE Login='financeiro';").Single()["SenhaHash"]),Convert.ToString(migrated.Query("SELECT SenhaHash FROM Usuarios WHERE Login='financeiro';").Single()["SenhaHash"]));
            Assert(migrated.Query("SELECT 1 FROM PagamentosVenda WHERE Origem<>'MANUAL' OR Status<>'REGISTRADO_MANUALMENTE';").Count==0);
        });
        Console.WriteLine($"SUCESSO: {passed} cenários financeiros."); return 0;
    }

    // Extensão SQLite registrada somente neste processo de teste. A venda usa o repositório real.
    private static readonly SqlFunction Probe=(_,_,_) => { File.WriteAllText(Environment.GetEnvironmentVariable("FIN_CRASH_MARKER")!,"ItensVenda inserido; COMMIT ainda não executado"); Thread.Sleep(Timeout.Infinite); };
    private static readonly AutoExtension Extension=(db,_,_)=>sqlite3_create_function_v2(db,"crash_probe",0,1,IntPtr.Zero,Probe,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
    internal static int CrashChild()
    {
        if(sqlite3_auto_extension(Extension)!=0) return 2;
        SessaoUsuario.Iniciar(int.Parse(Environment.GetEnvironmentVariable("FIN_CRASH_USER")!),"TESTE","ADMINISTRADOR");
        new VendaService().RegistrarPorProdutoIds([(int.Parse(Environment.GetEnvironmentVariable("FIN_CRASH_PRODUCT")!),1)],"PIX",operacaoId:Environment.GetEnvironmentVariable("FIN_CRASH_OPERATION"),totalEsperado:10);
        return 3; // O teste exige encerramento antes do retorno.
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SqlFunction(IntPtr context,int argc,IntPtr argv);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AutoExtension(IntPtr db,IntPtr error,IntPtr api);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_auto_extension(AutoExtension extension);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_create_function_v2(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)]string name,int argc,int flags,IntPtr app,SqlFunction function,IntPtr step,IntPtr final,IntPtr destroy);
    private static void Assert(bool value) { if(!value)throw new Exception("Asserção financeira falhou."); }
    private static void Equal<T>(T expected,T actual) { if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception($"Esperado {expected}; recebido {actual}."); }
    private static void Blocked(Action action) { try { action(); }catch(InvalidOperationException){return;}throw new Exception("Operação indevida foi aceita."); }
}
