using System.Drawing.Printing;
using System.Text;
using CaixaLume.Models;

namespace CaixaLume.Services;

public sealed class ComprovanteService
{
    public string CriarTexto(long vendaId,DateTime data,IReadOnlyCollection<ItemCarrinho> itens,decimal total,string pagamento,decimal recebido,decimal troco,TotaisVenda? totais=null)
    {
        var lines=new List<string>{new ConfiguracaoService().ObterNomeLoja(),$"VENDA #{vendaId:D6}",$"{data:dd/MM/yyyy HH:mm}",$"OPERADOR: {SessaoUsuario.Nome}",new string('-',36)};
        foreach(var item in itens){lines.Add(item.Produto);lines.Add($"{item.Quantidade:0.###} {item.UnidadeMedida} × {item.PrecoUnitario:C2} = {item.Subtotal:C2}");}
        if(totais is not null){lines.Add($"SUBTOTAL: {totais.Subtotal:C2}");lines.Add($"DESCONTO: {totais.Desconto:C2}");lines.Add($"ACRÉSCIMO: {totais.Acrescimo:C2}");}
        lines.Add(new string('-',36));lines.Add($"TOTAL: {total:C2}");lines.Add($"PAGAMENTO: {pagamento}");lines.Add("REGISTRO MANUAL DO OPERADOR");lines.Add("SEM CONFIRMAÇÃO BANCÁRIA PELO PDV");if(pagamento=="DINHEIRO"){lines.Add($"RECEBIDO: {recebido:C2}");lines.Add($"TROCO: {troco:C2}");}lines.Add("");lines.Add("Obrigado pela preferência!");return string.Join(Environment.NewLine,lines);
    }

    public void Imprimir(string text)
    {
        using var document=CreateDocument(text);using var dialog=new PrintDialog{Document=document,UseEXDialog=true};if(dialog.ShowDialog()!=DialogResult.OK)return;document.Print();
    }

    public void SalvarPdf(string text,string destination)
    {
        string? printer=PrinterSettings.InstalledPrinters.Cast<string>().FirstOrDefault(x=>x.Contains("Microsoft Print to PDF",StringComparison.OrdinalIgnoreCase));
        if(printer is null)throw new InvalidOperationException("A impressora Microsoft Print to PDF não está instalada neste Windows.");
        using var document=CreateDocument(text);document.PrinterSettings.PrinterName=printer;document.PrinterSettings.PrintToFile=true;document.PrinterSettings.PrintFileName=Path.GetFullPath(destination);document.Print();
        if(!File.Exists(destination)||new FileInfo(destination).Length==0)throw new InvalidOperationException("O Windows não gerou o arquivo PDF.");
    }

    private static PrintDocument CreateDocument(string text)
    {
        int line=0;string[] lines=text.Split(Environment.NewLine);
        var document=new PrintDocument{DocumentName="Comprovante CaixaLume"};
        document.DefaultPageSettings.Margins=new Margins(40,40,45,45);
        document.PrintPage+=(_,e)=>{if(e.Graphics is null)return;using var font=new Font("Consolas",9);float y=e.MarginBounds.Top;float step=font.GetHeight(e.Graphics)+3;while(line<lines.Length&&y+step<e.MarginBounds.Bottom){e.Graphics.DrawString(lines[line++],font,Brushes.Black,e.MarginBounds.Left,y);y+=step;}e.HasMorePages=line<lines.Length;};
        return document;
    }
}
