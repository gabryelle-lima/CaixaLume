using MercadinhoPdv.Models;
using MercadinhoPdv.Services;

namespace MercadinhoPdv.Forms;

internal sealed class VendasForm : Form
{
    private readonly VendaService _service = new();
    private readonly DataGridView _grid = new() { Dock=DockStyle.Fill, ReadOnly=true, AutoGenerateColumns=true, AllowUserToAddRows=false, SelectionMode=DataGridViewSelectionMode.FullRowSelect, MultiSelect=false, AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill };
    private readonly DateTimePicker _start = new() { Format=DateTimePickerFormat.Short, Value=DateTime.Today.AddDays(-29), Width=125 };
    private readonly DateTimePicker _end = new() { Format=DateTimePickerFormat.Short, Width=125 };
    private readonly ComboBox _status = new() { DropDownStyle=ComboBoxStyle.DropDownList, Width=140 };
    private readonly Button _cancel = new() { Text="Cancelar venda", AutoSize=true, Enabled=false };

    public VendasForm()
    {
        Text="Vendas"; StartPosition=FormStartPosition.CenterParent; MinimumSize=new Size(920,580); Size=new Size(1120,680);
        var root=new TableLayoutPanel { Dock=DockStyle.Fill, Padding=new Padding(16), ColumnCount=1, RowCount=2 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,52)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var filters=new FlowLayoutPanel { Dock=DockStyle.Fill, WrapContents=false, AutoScroll=true };
        var presets=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList, Width=160 };
        presets.Items.AddRange(["Hoje","Ontem","Últimos 7 dias","Últimos 30 dias","Este mês","Mês anterior","Personalizado"]); presets.SelectedIndex=3;
        _status.Items.AddRange(["Todos","Concluída","Cancelada"]); _status.SelectedIndex=0;
        var details=new Button { Text="Ver detalhes", AutoSize=true };
        filters.Controls.AddRange([presets,new Label { Text="De",AutoSize=true,Margin=new Padding(4,8,2,0) },_start,new Label { Text="Até",AutoSize=true,Margin=new Padding(4,8,2,0) },_end,_status,details,_cancel]);
        _grid.CellDoubleClick+=(_,e)=>{if(e.RowIndex>=0)ShowDetails();}; _grid.SelectionChanged+=(_,_)=>_cancel.Enabled=Models.SessaoUsuario.Administrador && Selected()?.Status=="Concluida";
        details.Click+=(_,_)=>ShowDetails(); _cancel.Click+=(_,_)=>CancelSale();
        presets.SelectedIndexChanged+=(_,_)=>{if(presets.SelectedIndex==6){_start.Enabled=_end.Enabled=true;return;} _start.Enabled=_end.Enabled=false; var today=DateTime.Today; switch(presets.SelectedIndex){case 0:_start.Value=_end.Value=today;break;case 1:_start.Value=_end.Value=today.AddDays(-1);break;case 2:_start.Value=today.AddDays(-6);_end.Value=today;break;case 3:_start.Value=today.AddDays(-29);_end.Value=today;break;case 4:_start.Value=new DateTime(today.Year,today.Month,1);_end.Value=today;break;case 5:_start.Value=new DateTime(today.Year,today.Month,1).AddMonths(-1);_end.Value=new DateTime(today.Year,today.Month,1).AddDays(-1);break;} RefreshRows();};
        _start.ValueChanged+=(_,_)=>RefreshRows(); _end.ValueChanged+=(_,_)=>RefreshRows(); _status.SelectedIndexChanged+=(_,_)=>RefreshRows();
        root.Controls.Add(filters,0,0); root.Controls.Add(_grid,0,1); Controls.Add(root); UiTheme.Apply(this); RefreshRows();
    }

    private VendaHistoricoLinha? Selected()=>_grid.CurrentRow?.DataBoundItem as VendaHistoricoLinha;
    private void RefreshRows(){if(_end.Value.Date<_start.Value.Date)return;_grid.DataSource=_service.ListarHistorico(_start.Value,_end.Value,_status.SelectedIndex switch{1=>"Concluida",2=>"Cancelada",_=>null});foreach(DataGridViewColumn c in _grid.Columns){if(c.DataPropertyName==nameof(VendaHistoricoLinha.Data))c.DefaultCellStyle.Format="dd/MM/yyyy HH:mm";if(c.DataPropertyName is nameof(VendaHistoricoLinha.Total) or nameof(VendaHistoricoLinha.Subtotal) or nameof(VendaHistoricoLinha.Desconto) or nameof(VendaHistoricoLinha.Acrescimo))c.DefaultCellStyle.Format="C2";}}
    private void ShowDetails(){if(Selected() is not { } sale)return;var grid=new DataGridView{Dock=DockStyle.Fill,ReadOnly=true,AutoGenerateColumns=true,AllowUserToAddRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill};UiTheme.StyleGrid(grid);grid.DataSource=_service.Detalhes(sale.Id);foreach(DataGridViewColumn c in grid.Columns){if(c.DataPropertyName==nameof(VendaItemDetalhe.PrecoUnitario)||c.DataPropertyName==nameof(VendaItemDetalhe.Subtotal))c.DefaultCellStyle.Format="C2";if(c.DataPropertyName==nameof(VendaItemDetalhe.Quantidade))c.DefaultCellStyle.Format="0.###";}using var form=new Form{Text=$"Venda #{sale.Id} · {sale.Status} · {sale.Total:C2}",StartPosition=FormStartPosition.CenterParent,Size=new Size(760,460),MinimumSize=new Size(580,340)};form.Controls.Add(grid);UiTheme.Apply(form);form.ShowDialog(this);}
    private void CancelSale(){if(Selected() is not { } sale)return;using var reason=new CadastroDialog($"Cancelar venda #{sale.Id}","Motivo");if(reason.ShowDialog(this)!=DialogResult.OK)return;if(!UiDialog.Confirm(this,"Confirmar cancelamento",$"A venda #{sale.Id} será mantida no histórico, o estoque será devolvido e o registro marcado como cancelado. Continuar?",true))return;try{_service.Cancelar(sale.Id,reason.Values["Motivo"]);RefreshRows();UiDialog.Success(this,"Venda cancelada","A venda foi estornada e preservada no histórico.");}catch(Exception ex){UiDialog.Warning(this,"Não foi possível cancelar",ex.Message);}}
}
