using System.Reflection;
using System.Text.Json;
using System.Drawing.Imaging;
using CaixaLume.Data;
using CaixaLume.Forms;
using CaixaLume.Models;
using CaixaLume.Services;

internal static class UxScenarios
{
    internal static int Run(string root)
    {
        int count=0;void Check(string name,Action action){action();Console.WriteLine($"PASSOU UX {++count:00}: {name}");}
        DatabaseInitializer.Initialize();var users=new UsuarioService();users.CriarAdministradorInicial("UX TESTE","ux-teste","senha-ux-teste-123");
        var user=users.Autenticar("ux-teste","senha-ux-teste-123")!.Value;SessaoUsuario.Iniciar(user.Id,user.Nome,user.Perfil);
        var bridge=new PdvWebBridge();var products=new ProdutoService();
        string path=Path.Combine(root,"thumbnail.png");using(var bitmap=new Bitmap(250,250)){using var g=Graphics.FromImage(bitmap);g.Clear(Color.Green);bitmap.Save(path,ImageFormat.Png);}
        var product=new Produto { Nome="CAFÉ UX",CodigoBarras="0012345",EstoqueAtual=10,PrecoVenda=5,LocalImagePath=path };products.Salvar(product);
        var corrupt=new Produto { Nome="FOTO CORROMPIDA UX",EstoqueAtual=1,PrecoVenda=1,LocalImagePath=Path.Combine(root,"corrupt.png") };File.WriteAllText(corrupt.LocalImagePath,"not an image");products.Salvar(corrupt);
        JsonDocument Send(string action,object payload)=>JsonDocument.Parse(JsonSerializer.Serialize(bridge.Handle(JsonSerializer.Serialize(new {version=1,id="ux-test",action,payload})),new JsonSerializerOptions {PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        Check("GetPdvData não decodifica nem transporta imagens do catálogo inteiro",()=>{
            using var json=Send("pdv.getData",new{});var items=json.RootElement.GetProperty("data").GetProperty("products");Equal(2,items.GetArrayLength());
            foreach(var p in items.EnumerateArray()){Assert(p.GetProperty("imageData").ValueKind==JsonValueKind.Null);Assert(p.GetProperty("hasImage").GetBoolean());Assert(p.TryGetProperty("imageVersion",out _));}
            Assert(!json.RootElement.GetRawText().Contains("data:image"));
        });
        Check("Miniaturas são carregadas em lote e fotos inválidas não derrubam o PDV",()=>{
            using var json=Send("pdv.getImages",new {ids=new[]{product.Id,corrupt.Id}});var data=json.RootElement.GetProperty("data");
            Assert(data.GetProperty(product.Id.ToString()).GetString()!.StartsWith("data:image/png;base64,"));Assert(data.GetProperty(corrupt.Id.ToString()).ValueKind==JsonValueKind.Null);
            using var again=Send("pdv.getImages",new {ids=new[]{product.Id}});Equal(data.GetProperty(product.Id.ToString()).GetString(),again.RootElement.GetProperty("data").GetProperty(product.Id.ToString()).GetString());
        });
        Check("Lotes de imagem são limitados e não expõem produtos inativos",()=>{
            Blocked(()=>Send("pdv.getImages",new {ids=Enumerable.Repeat(product.Id,33).ToArray()}));Blocked(()=>Send("pdv.getImages",new {ids=new[]{-1}}));
            products.Desativar(product.Id);using(var json=Send("pdv.getImages",new {ids=new[]{product.Id}}))Assert(!json.RootElement.GetProperty("data").TryGetProperty(product.Id.ToString(),out _));products.Reativar(product.Id);
        });
        Check("Catálogo e miniaturas exigem sessão ativa",()=>{
            SessaoUsuario.Encerrar();try{Blocked(()=>Send("pdv.getData",new{}));Blocked(()=>Send("pdv.getImages",new {ids=new[]{product.Id}}));}finally{SessaoUsuario.Iniciar(user.Id,user.Nome,user.Perfil);}
        });
        Check("Histórico de caixa aceita filtro de período na ponte de gestão",()=>{
            var cash=new CaixaService();cash.Abrir(0);cash.Fechar(0);
            using var payload=JsonDocument.Parse("{\"module\":\"cash\",\"tab\":\"history\",\"start\":\"1999-01-01\",\"end\":\"1999-01-02\"}");
            using var response=JsonDocument.Parse(JsonSerializer.Serialize(new ManagementWebBridge().GetData(payload.RootElement),new JsonSerializerOptions { PropertyNamingPolicy=JsonNamingPolicy.CamelCase }));
            Equal(0,response.RootElement.GetProperty("data").GetProperty("history").GetArrayLength());
        });
        Check("Campos monetários WinForms rejeitam colagens ambíguas e aceitam os dois separadores decimais",()=>{
            foreach(var text in new[]{"14,27","14.27","R$ 14,27"}){Assert(EntradaNumerica.TryParseDinheiro(text,out var value));Equal(14.27m,value);}
            foreach(var text in new[]{"1.234","1,234","12.34,56","1e3","-1","","1000000001"})Assert(!EntradaNumerica.TryParseDinheiro(text,out _));
        });
        Check("Pagamento mantém confirmação explícita e bloqueia texto inválido mesmo em venda zero",()=>RunSta(()=>{
            using var form=new PagamentoForm(0);form.CreateControl();
            var received=(TextBox)typeof(PagamentoForm).GetField("_received",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!;
            var confirm=(Button)typeof(PagamentoForm).GetField("_confirm",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!;
            Assert(form.FormaPagamento=="" && !confirm.Enabled);
            typeof(PagamentoForm).GetMethod("SelectMethod",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(form,new object[]{"DINHEIRO"});
            received.Text="texto";Assert(!confirm.Enabled);received.Text="0,00";Assert(confirm.Enabled);Assert(form.DialogResult!=DialogResult.OK);Assert(form.AcceptButton==confirm);
        }));
        Check("Pagamento mantém botão principal dentro da área útil e telas de gestão constroem nas cinco resoluções",()=>RunSta(()=>{
            Func<Form>[] factories=[()=>new PagamentoForm(10),()=>new EstoqueForm(),()=>new VendasForm(),()=>new DespesasForm(),()=>new RelatoriosForm(),()=>new CaixaForm()];
            foreach(var factory in factories)using(var form=factory())foreach(var size in new[]{new Size(1024,768),new Size(1280,720),new Size(1366,768),new Size(1440,900),new Size(1920,1080)})
            {form.Size=size;form.CreateControl();form.PerformLayout();Assert(form.Controls.Count>0);
                if(form is PagamentoForm){
                    var confirm=(Button)typeof(PagamentoForm).GetField("_confirm",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!;
                    var origin=Point.Empty;for(Control? c=confirm;c is not null&&c!=form;c=c.Parent)origin.Offset(c.Left,c.Top);
                    Assert(confirm.Height>=30 && form.ClientRectangle.Contains(new Rectangle(origin,confirm.Size)));
                }}
        }));
        Console.WriteLine($"SUCESSO: {count} cenários UX e desempenho.");return 0;
    }
    private static void RunSta(Action action){Exception? error=null;var t=new Thread(()=>{try{action();}catch(Exception ex){error=ex;}});t.SetApartmentState(ApartmentState.STA);t.Start();t.Join();if(error is not null)throw error;}
    private static void Assert(bool result){if(!result)throw new Exception("Asserção UX falhou.");}
    private static void Equal<T>(T expected,T actual){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception($"Esperado {expected}; recebido {actual}.");}
    private static void Blocked(Action action){try{action();}catch(InvalidOperationException){return;}throw new Exception("Acesso deveria ser bloqueado.");}
}
