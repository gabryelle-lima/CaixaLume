using MercadinhoPdv.Models;
using MercadinhoPdv.Services;

namespace MercadinhoPdv.Forms;

internal sealed class EntradaMercadoriaForm : Form
{
    private readonly ProdutoService _products=new(); private readonly FornecedorService _suppliers=new(); private readonly EstoqueService _stock=new();
    private readonly ComboBox _product=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill,DisplayMember=nameof(Produto.Nome)};
    private readonly TextBox _barcode=new(){Dock=DockStyle.Fill,PlaceholderText="Leia o código de barras"};
    private readonly Label _category=new(){AutoSize=true,Anchor=AnchorStyles.Left};
    private readonly ComboBox _supplier=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill,DisplayMember=nameof(Fornecedor.Nome)};
    private readonly NumericUpDown _quantity=new(){Minimum=.001m,Maximum=999999,DecimalPlaces=3,Increment=.001m,Dock=DockStyle.Fill,Value=1};
    private readonly NumericUpDown _cost=new(){Minimum=0,Maximum=999999,DecimalPlaces=2,Dock=DockStyle.Fill};
    private readonly NumericUpDown _price=new(){Minimum=0,Maximum=999999,DecimalPlaces=2,Dock=DockStyle.Fill};
    private readonly CheckBox _updatePrice=new(){Text="Atualizar preço de venda do produto",AutoSize=true};
    private readonly CheckBox _hasExpiry=new(){Text="Informar validade",AutoSize=true};
    private readonly DateTimePicker _expiry=new(){Format=DateTimePickerFormat.Short,Dock=DockStyle.Fill,Enabled=false};
    private readonly TextBox _invoice=new(){Dock=DockStyle.Fill}; private readonly TextBox _notes=new(){Dock=DockStyle.Fill,Multiline=true};
    private List<Produto> _options=[];

    public EntradaMercadoriaForm(string? initialBarcode=null)
    {
        Text="Entrada de mercadoria";StartPosition=FormStartPosition.CenterParent;MinimumSize=new Size(660,620);Size=new Size(760,700);
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(18),ColumnCount=1,RowCount=2};root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,54));
        var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true};var fields=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,Padding=new Padding(4)};fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,34));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,66));
        Add("Código de barras",_barcode);Add("Produto",_product);Add("Categoria",_category);Add("Fornecedor",_supplier);Add("Unidade",new Label{Text="da ficha do produto",AutoSize=true,Anchor=AnchorStyles.Left});Add("Quantidade",_quantity);Add("Preço de custo",_cost);Add("Preço de venda",_price);Add("",_updatePrice);Add("Validade",_hasExpiry);Add("Data de validade",_expiry);Add("Nota/documento",_invoice);Add("Observação",_notes);
        scroll.Controls.Add(fields);var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false};var save=new Button{Text="Registrar entrada",Width=170,Height=38};var cancel=new Button{Text="Cancelar",Width=110,Height=38,DialogResult=DialogResult.Cancel};var addProduct=new Button{Text="Cadastrar produto",AutoSize=true,Height=38};footer.Controls.AddRange([save,cancel,addProduct]);root.Controls.Add(scroll,0,0);root.Controls.Add(footer,0,1);Controls.Add(root);
        _options=[];LoadLists();if(!string.IsNullOrWhiteSpace(initialBarcode)){_barcode.Text=initialBarcode.Trim();SelectBarcode();}
        _product.SelectedIndexChanged+=(_,_)=>ProductChanged();_barcode.TextChanged+=(_,_)=>SelectBarcode();_hasExpiry.CheckedChanged+=(_,_)=>_expiry.Enabled=_hasExpiry.Checked;
        _updatePrice.CheckedChanged+=(_,_)=>_price.Enabled=_updatePrice.Checked;_price.Enabled=false;
        addProduct.Click+=(_,_)=>{using var form=new ProdutoForm(_products,null,_barcode.Text);if(form.ShowDialog(this)==DialogResult.OK){LoadProducts();SelectBarcode();}};
        cancel.Click+=(_,_)=>Close();save.Click+=(_,_)=>Save();UiTheme.Apply(this);CancelButton=cancel;
        _barcode.KeyDown+=(_,e)=>{if(e.KeyCode!=Keys.Enter)return;e.SuppressKeyPress=true;SelectBarcode();if(_product.SelectedItem is Produto)_quantity.Focus();};

        void Add(string label,Control control){int row=fields.RowCount++;fields.RowStyles.Add(new RowStyle(SizeType.Absolute,control is TextBox{Multiline:true}?76:42));fields.Controls.Add(new Label{Text=label,AutoSize=true,Anchor=AnchorStyles.Left,ForeColor=UiTheme.Text},0,row);control.Margin=new Padding(3,5,3,5);fields.Controls.Add(control,1,row);}
    }
    private void LoadLists(){LoadProducts();var suppliers=_suppliers.Listar();suppliers.Insert(0,new Fornecedor{Id=0,Nome="SEM ALTERAÇÃO"});_supplier.DataSource=suppliers;}
    private void LoadProducts(){_options=_products.Listar(null).Where(x=>x.Ativo).OrderBy(x=>x.Nome).ToList();_product.DataSource=_options;_product.SelectedIndex=-1;}
    private void SelectBarcode(){string code=_barcode.Text.Trim();if(code.Length==0){_product.SelectedIndex=-1;return;}var match=_options.FirstOrDefault(p=>string.Equals(p.CodigoBarras,code,StringComparison.OrdinalIgnoreCase));_product.SelectedItem=match;}
    private void ProductChanged(){if(_product.SelectedItem is not Produto p){_category.Text="Nenhum produto identificado";return;}_category.Text=p.CategoriaNome??"OUTROS";_quantity.DecimalPlaces=p.UnidadeMedida=="UN"?0:3;_quantity.Increment=p.UnidadeMedida=="UN"?1:.001m;if(p.UnidadeMedida=="UN"&&_quantity.Value%1!=0)_quantity.Value=decimal.Ceiling(_quantity.Value);_cost.Value=Math.Clamp(p.PrecoCusto,_cost.Minimum,_cost.Maximum);_price.Value=Math.Clamp(p.PrecoVenda,_price.Minimum,_price.Maximum);_hasExpiry.Checked=p.DataValidade.HasValue;if(p.DataValidade.HasValue)_expiry.Value=p.DataValidade.Value;}
    private void Save(){if(_product.SelectedItem is not Produto p){UiDialog.Info(this,"Produto obrigatório","Leia um código cadastrado ou selecione o produto antes de registrar a entrada.");_barcode.Focus();return;}if(!string.IsNullOrWhiteSpace(_barcode.Text)&&!string.Equals(_barcode.Text.Trim(),p.CodigoBarras,StringComparison.OrdinalIgnoreCase)){UiDialog.Warning(this,"Produto divergente","O código lido não corresponde ao produto selecionado. Confira a mercadoria antes de registrar.");_barcode.Focus();return;}try{int? supplier=_supplier.SelectedItem is Fornecedor{Id:>0} selected?selected.Id:null;_stock.RegistrarEntradaMercadoria(p.Id,_quantity.Value,_cost.Value,_updatePrice.Checked?_price.Value:null,_hasExpiry.Checked?_expiry.Value.Date:null,supplier,_invoice.Text,_notes.Text);UiDialog.Success(this,"Entrada registrada","Estoque, custo e histórico foram atualizados.");DialogResult=DialogResult.OK;Close();}catch(Exception ex){UiDialog.Warning(this,"Não foi possível registrar",ex.Message);}}
}
