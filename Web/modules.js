(() => {
  const { request } = window.MercadoBridge;
  const { element, currency, notify } = window.MercadoUi;
  const $ = id => document.getElementById(id);
  const names = { stock: 'Estoque', invoices: 'Notas fiscais', cash: 'Caixa', sales: 'Vendas', expenses: 'Despesas', suppliers: 'Fornecedores', settings: 'Configurações', users: 'Usuários' };
  const descriptions = { stock: 'Saldos, entradas e movimentações dos produtos.', invoices: 'Notas de compra importadas de XML · situação fiscal atual não consultada.', cash: 'Acompanhe o turno e as movimentações do caixa.', sales: 'Consulte vendas e seus detalhes.', expenses: 'Registre e acompanhe as despesas da loja.', suppliers: 'Gerencie fornecedores e categorias.', settings: 'Ajuste os dados e as regras da loja.', users: 'Controle o acesso dos operadores.' };
  const adminModules = new Set(['invoices', 'expenses', 'suppliers', 'settings', 'users']);
  const nav = [ ['home.html','⌂','Visão geral'],['index.html','▦','Ponto de venda'],['products.html','▤','Produtos'],['stock','◫','Estoque'],['cash','▣','Caixa'],['sales','↗','Vendas'],['invoices','▣','Notas fiscais'],['expenses','▧','Despesas'],['reports.html','▥','Relatórios'],['suppliers','♧','Fornecedores'],['settings','⚙','Configurações'],['users','♙','Usuários'] ];
  const state = { module: '', data: {}, role: '', tab: '', selected: null, selectedRow: null, rows: [], columns: [], displayedRows: 0, loading: false, searchTimer: null, sequence: 0, invoiceToken: null, invoicePreview: null, invoiceProducts: [], invoiceBusy: false };
  const tablePageSize = 150;
  const localDate = d => `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;
  const today = () => localDate(new Date());
  const prior = () => { const d = new Date(); d.setDate(d.getDate() - 29); return localDate(d); };
  const date = v => v ? new Date(v).toLocaleString('pt-BR') : '—';
  const num = v => Number(v ?? 0).toLocaleString('pt-BR', { maximumFractionDigits: 3 });
  const invoiceNum = v => Number(v ?? 0).toLocaleString('pt-BR', { maximumFractionDigits: 6 });
  const button = (label, fn, primary = false, danger = false) => { const b = element('button', `button ${danger ? 'button-danger' : primary ? 'button-primary' : 'button-secondary'}`, label); b.type = 'button'; b.addEventListener('click', fn); return b; };
  function buildNavigation() {
    nav.forEach(([target, icon, label], index) => {
      const b = element('button', 'nav-item'); b.type = 'button'; b.dataset.target = target;
      b.append(element('span', 'nav-icon', icon), element('span', '', label));
      if (target === 'index.html') b.append(element('kbd', '', 'F2'));
      b.addEventListener('click', () => target.endsWith('.html') ? window.MercadoBridge.navigate(target) : navigate(target));
      $(index < 6 || target === 'invoices' ? 'primary-nav' : 'secondary-nav').append(b);
    });
    const logout = element('button', 'nav-item'); logout.type = 'button'; logout.append(element('span', 'nav-icon', '⇥'), element('span', '', 'Sair'));
    logout.addEventListener('click', async () => { try { await request('session.logout'); } catch (e) { alertError(e); } }); $('secondary-nav').append(logout);
    const offline = element('div', 'offline-status'); offline.append(element('span', 'connection-dot'), element('span', '', 'Aplicativo local · offline')); $('secondary-nav').append(offline);
  }
  function alertError(error) { notify($('toast-region'), error.message || String(error), true); }
  function updateAdminNavigation() {
    document.querySelectorAll('[data-target]').forEach(b => {
      const restricted = adminModules.has(b.dataset.target);
      b.disabled = restricted && state.role !== 'ADMINISTRADOR';
    });
    window.MercadoUi.updateInvoiceNavigation(state.role);
  }
  function navigate(module, push = true) {
    if (!names[module]) module = 'stock';
    if (push && state.module === module) return;
    if (push) history.pushState({ module }, '', `./modules.html?module=${module}`);
    clearTimeout(state.searchTimer);
    state.module = module; state.tab = ''; state.selected = null; state.selectedRow = null; state.rows = []; state.data = {};
    document.querySelectorAll('[data-target]').forEach(b => b.classList.toggle('active', b.dataset.target === module));
    updateAdminNavigation();
    $('module-title').textContent = names[module]; $('breadcrumb-module').textContent = names[module]; $('module-subtitle').textContent = descriptions[module]; $('module-eyebrow').textContent = module === 'invoices' ? 'RECEBIMENTO DE MERCADORIAS' : 'GESTÃO DA LOJA'; document.title = `Mercado · ${names[module]}`;
    $('module-error').hidden = true; $('module-empty').hidden = true; $('module-load-more').hidden = true; $('module-metrics').replaceChildren(); $('module-actions').replaceChildren(); $('module-toolbar').replaceChildren(); $('module-tabs').replaceChildren(); $('module-head').replaceChildren(); $('module-rows').replaceChildren(); $('module-row-actions').replaceChildren(); $('module-count').textContent = 'Carregando…';
    renderToolbar(); load();
  }
  function filterField(type, id, placeholder, initial) { const input = element('input'); input.type = type; input.id = id; input.placeholder = placeholder; if (initial) input.value = initial; return input; }
  function renderToolbar() {
    const host = $('module-toolbar'), module = state.module;
    if (['stock','expenses','suppliers'].includes(module)) {
      const search = filterField('search', 'filter-search', 'Pesquisar registros'); search.addEventListener('input', () => { clearTimeout(state.searchTimer); state.searchTimer = setTimeout(() => module === 'expenses' || (module === 'stock' && state.tab === 'movements') ? load() : render(), 250); }); host.append(search);
    }
    if (module === 'stock') {
      const status = element('select'); status.id = 'filter-stock-status'; [['','Todos os saldos'],['low','Estoque baixo'],['normal','Estoque normal']].forEach(([value,label]) => { const option = element('option','',label); option.value=value; status.append(option); }); status.addEventListener('change',render); host.append(status);
      const unit = element('select'); unit.id = 'filter-stock-unit'; [['','Todas as unidades'],['UN','Unidade'],['KG','Quilo']].forEach(([value,label]) => { const option = element('option','',label); option.value=value; unit.append(option); }); unit.addEventListener('change',render); host.append(unit);
    }
    if (module === 'stock') {
      const expiry = element('select'); expiry.id = 'filter-expiry';
      [['','Todas as validades'],['expired','Vencidos'],['today','Vencem hoje'],['soon','Vencem em até 30 dias'],['none','Sem validade']].forEach(([value,label])=>{const o=element('option','',label);o.value=value;expiry.append(o);});
      expiry.addEventListener('change',render);host.append(expiry);
    }
    if (['stock','cash','sales','expenses','invoices'].includes(module)) {
      host.append(element('span', '', 'De'), filterField('date', 'filter-start', '', prior()), element('span', '', 'até'), filterField('date', 'filter-end', '', today()));
      if (module === 'sales') { const select = element('select'); select.id = 'filter-status'; [['','Todas'],['Concluida','Concluídas'],['Cancelada','Canceladas']].forEach(([v,l]) => { const o = element('option', '', l); o.value = v; select.append(o); }); host.append(select); }
      host.append(button('Aplicar filtro', load));
    }
    host.append(button('↻ Atualizar', load));
  }
  async function load(page = 1) {
    if (!Number.isInteger(page)) page = 1;
    clearTimeout(state.searchTimer); state.searchTimer = null;
    const started = performance.now();
    const module = state.module, sequence = ++state.sequence;
    state.loading = true;
    $('module-error').hidden = true; $('module-count').textContent = 'Carregando…';
    try {
      const result = await request('management.getData', { module, page, snapshotId: module === 'sales' && page > 1 ? state.data.pagination?.snapshotId : null, tab: state.tab, start: $('filter-start')?.value || prior(), end: $('filter-end')?.value || today(), search: $('filter-search')?.value || '', status: $('filter-status')?.value || '' });
      if (sequence !== state.sequence || module !== state.module) return;
      state.data = result.data || {}; state.role = result.userRole; state.selected = null;
      $('store-name').textContent = $('breadcrumb-store').textContent = result.storeName || 'Mercado'; $('user-label').textContent = result.userName || 'Operador'; $('user-role').textContent = result.userRole || 'OPERADOR'; $('user-avatar').textContent = (result.userName || 'M').slice(0,1);
      updateAdminNavigation();
      render();
      window.MercadoBridge.metric('ready', performance.now() - started);
    } catch (error) { if (sequence !== state.sequence) return; $('module-error').textContent = error.message; $('module-error').hidden = false; $('module-count').textContent = 'Não foi possível carregar'; }
    finally { if (sequence === state.sequence) state.loading = false; }
  }
  function action(label, operation, fields, item = {}, options = {}) { return button(label, () => openForm(label, operation, fields, item, options), options.primary, options.danger); }
  const field = (key, label, type = 'text', required = false, extra = {}) => ({ key, label, type, required, ...extra });
  const money = (key, label, required = true) => field(key, label, 'number', required, { step: '0.01', min: '0' });
  const quantity = (key, label) => field(key, label, 'number', true, { step: '0.001', min: '0.001' });
  function productField() { return field('productId', 'Produto', 'select', true, { options: (state.data.products || []).map(p => [p.id, p.nome]) }); }
  function render() {
    const d = state.data, m = state.module, actions = $('module-actions'), metrics = $('module-metrics'); actions.replaceChildren(); metrics.replaceChildren(); $('module-row-actions').replaceChildren();
    let columns = [], rows = [], tabs = [], selectedTab = state.tab;
    if (m === 'stock') {
      tabs = [['products','Produtos'],['movements','Movimentações']]; selectedTab ||= 'products';
      const products = d.products || []; const search = ($('filter-search')?.value || '').toLocaleUpperCase('pt-BR');
      rows = selectedTab === 'products' ? products.filter(p => (!search || `${p.nome} ${p.codigoBarras || ''}`.toLocaleUpperCase('pt-BR').includes(search)) && (!$('filter-stock-unit')?.value || p.unidadeMedida === $('filter-stock-unit').value) && (!$('filter-stock-status')?.value || ($('filter-stock-status').value === 'low') === (Number(p.estoqueAtual) <= Number(p.estoqueMinimo)))) : d.movements || [];
      if (selectedTab === 'products' && $('filter-expiry')?.value) {
        const f=$('filter-expiry').value, now=today(), limit=new Date();limit.setDate(limit.getDate()+30);const until=localDate(limit);
        rows=rows.filter(p=>{const d=p.dataValidade?.slice(0,10);return f==='none'?!d:!!d&&(f==='expired'?d<now:f==='today'?d===now:d>=now&&d<=until);});
      }
      columns = selectedTab === 'products' ? [['codigoBarras','CÓDIGO'],['nome','PRODUTO'],['categoriaNome','CATEGORIA'],['unidadeMedida','UNID.'],['estoqueAtual','SALDO',num],['estoqueMinimo','MÍNIMO',num],['dataValidade','VALIDADE',v => v ? new Date(v).toLocaleDateString('pt-BR') : '—']] : [['data','DATA',date],['produto','PRODUTO'],['tipo','TIPO'],['quantidade','QUANTIDADE',num],['variacao','VARIAÇÃO',v=>v==null?'—':num(v)],['saldoAnterior','ANTERIOR',v=>v==null?'—':num(v)],['saldoPosterior','POSTERIOR',v=>v==null?'—':num(v)],['motivo','MOTIVO'],['usuario','USUÁRIO']];
      if (state.role === 'ADMINISTRADOR') { actions.append(action('＋ Movimentar', 'stock.move', [productField(),field('type','Tipo','select',true,{options:[['Entrada','Entrada'],['Saída','Saída'],['Ajustar saldo','Ajustar saldo']]}),quantity('quantity','Quantidade ou novo saldo'),field('reason','Motivo','textarea',true)], {}, { primary:true })); actions.append(action('Receber mercadoria','stock.receive',[productField(),quantity('quantity','Quantidade'),money('cost','Custo unitário'),money('price','Novo preço de venda',false),field('expiry','Validade','date'),field('supplierId','Fornecedor','select',false,{options:[['','Não informado'],...(d.suppliers||[]).map(s=>[s.id,s.nome])]}),field('document','Documento'),field('note','Observação','textarea')])); }
      if (state.role === 'ADMINISTRADOR' && /^\d{8,14}$/.test(search) && !products.some(p=>p.codigoBarras===search)) actions.append(button('Cadastrar este código',()=>window.MercadoBridge.navigate(`products.html?barcode=${encodeURIComponent(search)}`)));
      metric('Produtos ativos', products.length); metric('Estoque baixo', products.filter(p => Number(p.estoqueAtual) <= Number(p.estoqueMinimo)).length);
    } else if (m === 'invoices') {
      tabs = [['invoices','Notas importadas'],['entries','Entradas por item']]; selectedTab ||= 'invoices';
      rows = selectedTab === 'invoices' ? d.invoices || [] : d.entries || [];
      columns = selectedTab === 'invoices'
        ? [['number','NOTA',v=>v ? `#${v}` : '—'],['series','SÉRIE'],['issueDate','EMISSÃO',date],['date','ENTRADA EM',date],['supplierName','FORNECEDOR'],['supplierCnpj','CNPJ FORNECEDOR'],['invoiceTotal','TOTAL DA NOTA',currency],['itemCount','ITENS',num]]
        : [['number','NOTA',v=>v ? `#${v}` : '—'],['date','ENTRADA EM',date],['supplierName','FORNECEDOR'],['productName','PRODUTO'],['quantity','ENTRADA',num],['unit','UNID.'],['unitCost','CUSTO UNIT.',currency],['totalCost','CUSTO TOTAL',currency]];
      metric('Notas no período', (d.invoices || []).length); metric('Entradas por item', (d.entries || []).length); metric('Custo dos itens recebidos', currency((d.entries || []).reduce((sum, entry) => sum + Number(entry.totalCost || 0), 0)));
      $('module-hint').textContent = selectedTab === 'invoices' ? 'Selecione uma nota para consultar os itens recebidos' : 'Entradas confirmadas a partir dos XMLs';
      $('module-empty').textContent = selectedTab === 'invoices' ? 'Nenhuma NF-e de compra importada neste período.' : 'Nenhuma entrada de item neste período.';
      if (state.role === 'ADMINISTRADOR') actions.append(button('＋ Importar XML', selectInvoiceXml, true));
      if (state.role === 'ADMINISTRADOR') actions.append(button('Exportar Excel', exportInvoiceReport));
    } else if (m === 'cash') {
      tabs = [['movements','Movimentações'],['history','Histórico de caixas']]; selectedTab ||= 'movements'; const c = d.current;
      rows = selectedTab === 'movements' ? d.movements || [] : d.history || [];
      columns = selectedTab === 'movements' ? [['data','DATA',date],['tipo','MOVIMENTAÇÃO'],['valor','VALOR',currency],['observacao','DESCRIÇÃO'],['usuario','USUÁRIO']] : [['id','CAIXA',v=>`#${v}`],['abertura','ABERTO EM',date],['fechamento','FECHADO EM',date],['saldoInicial','INICIAL',currency],['valorEsperado','ESPERADO',currency],['valorContado','CONTADO',currency],['status','STATUS']];
      metric('Status', c ? 'Aberto' : 'Fechado'); metric('Saldo inicial', currency(c?.saldoInicial)); metric('Vendas', currency(c?.totalVendas)); metric('Dinheiro esperado', currency(c?.valorEsperadoDinheiro));
      if (!c) actions.append(action('＋ Abrir caixa','cash.open',[money('amount','Saldo inicial')],{}, {primary:true}));
      else { actions.append(action('Suprimento','cash.move',[money('amount','Valor'),field('reason','Motivo','textarea',true)],{type:'SUPRIMENTO'})); actions.append(action('Sangria','cash.move',[money('amount','Valor'),field('reason','Motivo','textarea',true)],{type:'SANGRIA'})); actions.append(action('Registrar despesa','cash.expense',[field('description','Descrição','text',true),money('amount','Valor'),field('payment','Pagamento','select',true,{options:[['DINHEIRO','Dinheiro'],['PIX','Pix'],['DÉBITO','Débito'],['CRÉDITO','Crédito']]})])); actions.append(action('Fechar caixa','cash.close',[money('amount','Valor contado')],{}, {danger:true, confirm:'Confirmar o fechamento do caixa?'})); }
    } else if (m === 'sales') {
      rows = d.sales || []; columns = [['id','VENDA',v=>`#${v}`],['data','DATA',date],['operador','OPERADOR'],['pagamento','PAGAMENTO'],['subtotal','SUBTOTAL',currency],['desconto','DESCONTO',currency],['acrescimo','ACRÉSCIMO',currency],['total','TOTAL',currency],['status','STATUS']];
      metric('Registros no período', d.pagination?.total ?? rows.length); metric('Total concluído nesta página', currency(rows.filter(x=>x.status === 'Concluida').reduce((a,x)=>a+Number(x.total||0),0)));
      const p = d.pagination;
      if (p) { const previous = button('Página anterior', () => load(p.page-1)); previous.disabled = p.page<=1; const next = button('Próxima página', () => load(p.page+1)); next.disabled = !p.hasNext; actions.append(previous,element('span','',`Página ${p.page} · ${p.total} registros`),next); }
    } else if (m === 'expenses') {
      rows = d.expenses || []; columns = [['data','DATA',date],['descricao','DESCRIÇÃO'],['categoria','CATEGORIA'],['formaPagamento','PAGAMENTO'],['valor','VALOR',currency],['status','STATUS']];
      metric('Despesas no período', rows.filter(x=>x.status !== 'Cancelada').length); metric('Total',currency(rows.filter(x=>x.status !== 'Cancelada').reduce((a,x)=>a+Number(x.valor||0),0)));
      actions.append(action('＋ Nova despesa','expenses.save',expenseFields(),{}, {primary:true}));
    } else if (m === 'suppliers') {
      tabs = [['suppliers','Fornecedores'],['categories','Categorias']]; selectedTab ||= 'suppliers'; rows = (selectedTab === 'suppliers' ? d.suppliers : d.categories) || [];
      const search = ($('filter-search')?.value || '').toLocaleUpperCase('pt-BR'); rows = rows.filter(x=>!search||x.nome.toLocaleUpperCase('pt-BR').includes(search));
      columns = selectedTab === 'suppliers' ? [['nome','FORNECEDOR'],['documento','DOCUMENTO'],['telefone','TELEFONE'],['email','EMAIL'],['ativo','STATUS',v=>v?'Ativo':'Inativo']] : [['nome','CATEGORIA'],['ativa','STATUS',v=>v?'Ativa':'Inativa']];
      actions.append(action(selectedTab === 'suppliers' ? '＋ Novo fornecedor' : '＋ Nova categoria',selectedTab === 'suppliers' ? 'suppliers.save' : 'categories.save',selectedTab === 'suppliers' ? supplierFields() : [field('name','Nome','text',true)],{}, {primary:true}));
      metric(selectedTab === 'suppliers' ? 'Fornecedores' : 'Categorias', rows.length);
    } else if (m === 'settings') {
      rows = d.audit || []; columns = [['Data','DATA',date],['Usuario','USUÁRIO'],['Acao','AÇÃO'],['Entidade','ÁREA'],['Descricao','DESCRIÇÃO']];
      metric('Nome da loja', d.storeName || 'Mercado');
      metric('CNPJ da loja', d.storeCnpj || 'Não configurado');
      actions.append(action('Editar nome','settings.name',[field('name','Nome da loja','text',true)],{name:d.storeName},{primary:true}));
      const central=d.central||{};
      metric('Gestão central',central.enabled ? central.status : 'Desconectada');
      if(central.enabled){
        actions.append(action('Verificar conexão','settings.central.check',[]));
        actions.append(action('Desconectar suporte','settings.central.disconnect',[],{},{danger:true,confirm:'Desconectar a gestão central? As vendas e os dados locais serão preservados.'}));
      }else actions.append(action('Conectar gestão central','settings.central.connect',[field('server','Servidor HTTPS','url',true),field('code','Código temporário','password',true)],{},{confirm:'Permitir o envio da versão do programa, versão do banco e sinal de funcionamento ao servidor informado? Vendas, CPFs e senhas não serão enviados.'}));
      actions.append(action('Configurar CNPJ','settings.cnpj',[field('cnpj','CNPJ da loja','text',true)],{cnpj:d.storeCnpj || ''}));
      const p=d.policy||{}; actions.append(action('Política de descontos','settings.policy',[field('enabled','Permitir ajustes','checkbox'),field('operators','Permitir operadores','checkbox'),money('discount','Desconto máximo (%)'),money('surcharge','Acréscimo máximo (R$)')],{enabled:p.habilitada,operators:p.permitirOperador,discount:p.maxDescontoPercentual,surcharge:p.maxAcrescimoValor}));
      actions.append(button('Salvar backup',async()=>{try{const r=await request('management.backup'); if(r.saved) notify($('toast-region'),'Cópia de segurança salva.');}catch(e){alertError(e);}}));
      actions.append(button('Restaurar backup',async()=>{try{const r=await request('management.restore'); if(r.restored) notify($('toast-region'),'Backup restaurado. Faça login novamente.');}catch(e){alertError(e);}}));
      if(d.testTools) actions.append(button('Limpar caixas de teste',async()=>{if(!await window.MercadoUi.confirm('Excluir o histórico dos caixas de teste?'))return;try{await request('management.execute',{operation:'settings.clearCash'});notify($('toast-region'),'Histórico limpo.');load();}catch(e){alertError(e);}},false,true));
    } else if (m === 'users') {
      rows = d.users || []; columns = [['nome','NOME'],['login','LOGIN'],['perfil','PERFIL'],['ativo','STATUS',v=>v?'Ativo':'Inativo']]; metric('Usuários',rows.length);
      actions.append(action('＋ Novo usuário','users.save',userFields(),{}, {primary:true}));
    }
    if (m !== 'invoices') { $('module-hint').textContent = 'Selecione uma linha para ver as ações'; $('module-empty').textContent = 'Nenhum registro encontrado.'; }
    if(m==='cash'&&selectedTab==='history')$('module-hint').textContent='Selecione um caixa para ver movimentos · até 200 registros por período; refine as datas se necessário.';
    state.tab = selectedTab; state.rows = rows; renderTabs(tabs); renderTable(columns,rows); renderRowActions();
  }
  function metric(label,value) { const card=element('div','module-metric'); card.append(element('span','',label),element('strong','',String(value))); $('module-metrics').append(card); }
  function renderTabs(tabs) { const host=$('module-tabs'); host.replaceChildren(); tabs.forEach(([id,label])=>{const b=element('button',id===state.tab?'active':'',label);b.type='button';b.addEventListener('click',()=>{if(state.tab===id)return;clearTimeout(state.searchTimer);state.searchTimer=null;state.tab=id;state.selected=null;const loadedLocally=!state.loading&&((state.module==='suppliers'&&Array.isArray(state.data.suppliers)&&Array.isArray(state.data.categories))||(state.module==='stock'&&id==='products'&&Array.isArray(state.data.products))||(state.module==='invoices'&&Array.isArray(state.data.invoices)&&Array.isArray(state.data.entries)));if(loadedLocally)render();else load();});host.append(b);}); }
  function renderTable(columns,rows) {
    const header=element('tr'); columns.forEach(([,label])=>header.append(element('th','',label))); $('module-head').replaceChildren(header);
    state.columns=columns; state.rows=rows; state.selected=null; state.selectedRow=null; state.displayedRows=0;
    $('module-rows').replaceChildren(); $('module-empty').hidden=rows.length>0; appendTableRows();
  }
  function appendTableRows() {
    const rows=state.rows, columns=state.columns, body=$('module-rows'), end=Math.min(rows.length,state.displayedRows+tablePageSize), fragment=document.createDocumentFragment();
    for(let index=state.displayedRows;index<end;index++){
      const item=rows[index],tr=element('tr');tr.tabIndex=0;
      columns.forEach(([key,,format])=>{const raw=item[key],text=format?format(raw):raw===null||raw===undefined||raw===''?'—':String(raw);const cell=element('td','',text);cell.title=text;tr.append(cell);});
      tr.addEventListener('click',()=>{state.selectedRow?.classList.remove('selected');state.selected=index;state.selectedRow=tr;tr.classList.add('selected');renderRowActions();});
      tr.addEventListener('keydown',e=>{if(e.key==='Enter')tr.click();});fragment.append(tr);
    }
    body.append(fragment);state.displayedRows=end;
    $('module-count').textContent=end<rows.length?`${end.toLocaleString('pt-BR')} de ${rows.length.toLocaleString('pt-BR')} registros`:`${rows.length.toLocaleString('pt-BR')} registro${rows.length===1?'':'s'}`;
    $('module-load-more').hidden=end>=rows.length;
    if(end<rows.length)$('module-load-more-button').textContent=`Mostrar mais ${Math.min(tablePageSize,rows.length-end).toLocaleString('pt-BR')} registros`;
  }
  function expenseFields() { return [field('description','Descrição','text',true),field('category','Categoria'),money('amount','Valor'),field('date','Data','date',true),field('payment','Forma de pagamento','select',true,{options:[['DINHEIRO','Dinheiro'],['PIX','Pix'],['DÉBITO','Débito'],['CRÉDITO','Crédito']]}),field('note','Observação','textarea')]; }
  function supplierFields() { return [field('name','Nome','text',true),field('document','Documento'),field('phone','Telefone'),field('email','E-mail','email'),field('address','Endereço','text',false,{wide:true}),field('note','Observação','textarea',false,{wide:true})]; }
  function userFields(edit=false) { return [field('name','Nome','text',true),field('login','Login','text',true),field('role','Perfil','select',true,{options:[['OPERADOR','Operador'],['ADMINISTRADOR','Administrador']]}),field('password',edit?'Nova senha (opcional)':'Senha','password',!edit)]; }
  function renderRowActions() {
    const host=$('module-row-actions'); host.replaceChildren(); const item=state.rows[state.selected]; if(!item)return; const m=state.module;
    if(m==='sales') {host.append(button('Ver detalhes',()=>showDetails('sales',item.id)));if(state.role==='ADMINISTRADOR'&&item.status==='Concluida')host.append(action('Cancelar venda','sales.cancel',[field('reason','Motivo do cancelamento','textarea',true)],{id:item.id},{danger:true,confirm:'Cancelar esta venda e estornar os itens?'}));}
    if(m==='invoices') host.append(button('Ver itens da nota',()=>showDetails('invoices',item.invoiceId ?? item.id)));
    if(m==='cash'&&state.tab==='history')host.append(button('Ver movimentações',()=>showDetails('cash',item.id)));
    if(m==='expenses'&&item.status!=='Cancelada'){host.append(action('Editar','expenses.save',expenseFields(),{id:item.id,description:item.descricao,category:item.categoria,amount:item.valor,date:item.data?.slice(0,10),payment:item.formaPagamento,note:item.observacao}));host.append(action('Cancelar','expenses.cancel',[field('reason','Motivo','textarea',true)],{id:item.id},{danger:true,confirm:'Cancelar esta despesa?'}));}
    if(m==='suppliers') { if(state.tab==='suppliers'){host.append(action('Editar','suppliers.save',supplierFields(),{id:item.id,name:item.nome,document:item.documento,phone:item.telefone,email:item.email,address:item.endereco,note:item.observacao}));if(item.ativo)host.append(button('Desativar',()=>simpleAction('suppliers.deactivate',item.id),false,true));}else {host.append(action('Editar','categories.save',[field('name','Nome','text',true)],{id:item.id,name:item.nome}));if(item.ativa)host.append(button('Desativar',()=>simpleAction('categories.deactivate',item.id),false,true));} }
    if(m==='users') {host.append(action('Editar','users.save',userFields(true),{id:item.id,name:item.nome,login:item.login,role:item.perfil}));host.append(button(item.ativo?'Desativar':'Reativar',()=>simpleAction(item.ativo?'users.deactivate':'users.reactivate',item.id),false,item.ativo));}
  }
  function normalizedCnpj(value) { return String(value || '').toLocaleUpperCase('pt-BR').replace(/[.\-/\s]/g, ''); }
  async function exportInvoiceReport() {
    try {
      const result = await request('stock.invoice.exportExcel', { start: $('filter-start').value, end: $('filter-end').value });
      if (result.saved) notify($('toast-region'), 'Relatório de entradas salvo no Excel.');
    } catch (error) { alertError(error); }
  }
  function invoiceSummary(label, value) {
    const item = element('div','invoice-summary-item');
    item.append(element('span','',label),element('strong','',String(value ?? '—')));
    return item;
  }
  function invoiceProductOption(product) {
    const label = `${product.nome || `Produto #${product.id}`} · ${product.unidadeMedida || 'UN'}${product.codigoBarras ? ` · ${product.codigoBarras}` : ''}`;
    const option = element('option','',label); option.value = String(product.id); return option;
  }
  function fillInvoiceConversion(row, item, product) {
    const unit = String(item.unit || '').trim().toLocaleUpperCase('pt-BR');
    const internalUnit = String(product?.unidadeMedida || '').trim().toLocaleUpperCase('pt-BR');
    const compatible = Boolean(product && (unit === 'UN' || unit === 'KG') && unit === internalUnit);
    row.querySelector('[data-invoice-quantity]').value = compatible && Number(item.quantity) > 0 ? String(item.quantity) : '';
    row.querySelector('[data-invoice-cost]').value = compatible && item.unitPrice !== null && item.unitPrice !== undefined && Number.isFinite(Number(item.unitPrice)) && Number(item.unitPrice) >= 0 ? String(item.unitPrice) : '';
    row.querySelector('[data-invoice-help]').textContent = compatible
      ? `Unidade da nota e do produto: ${internalUnit}. Confira a quantidade física e o custo.`
      : 'Informe a conversão para a unidade do produto e o custo por unidade. Não use automaticamente a quantidade da embalagem.';
  }
  function renderInvoiceItem(item, products) {
    const row = element('section','invoice-item'); row.dataset.lineNumber = String(item.lineNumber);
    const top = element('div','invoice-item-top');
    const title = element('div'); title.append(element('strong','',`${item.lineNumber}. ${item.name || 'Item sem descrição'}`),element('small','',`Código do fornecedor: ${item.supplierCode || '—'} · GTIN: ${item.gtin || '—'}`));
    top.append(title,element('span','invoice-item-total',currency(item.total))); row.append(top);
    const xml = element('p','invoice-item-xml',`No XML: ${invoiceNum(item.quantity)} ${item.unit || '—'} · ${currency(item.unitPrice)} por ${item.unit || 'unidade'}`); row.append(xml);
    const fields = element('div','invoice-item-fields');
    const productLabel = element('label'); productLabel.append(element('span','', 'Produto no CaixaLume'));
    const select = element('select'); select.required = true; select.dataset.invoiceProduct = 'true';
    const placeholder = element('option','','Selecione o produto'); placeholder.value = ''; select.append(placeholder);
    products.forEach(product => select.append(invoiceProductOption(product)));
    const suggested = products.find(product => Number(product.id) === Number(item.suggestedProductId));
    if (suggested) select.value = String(suggested.id);
    select.addEventListener('change', () => fillInvoiceConversion(row,item,products.find(product => String(product.id) === select.value)));
    productLabel.append(select); fields.append(productLabel);
    const quantityLabel = element('label'); quantityLabel.append(element('span','', 'Quantidade recebida no estoque'));
    const quantityInput = element('input'); quantityInput.type='text';quantityInput.inputMode='decimal'; quantityInput.step = '0.001'; quantityInput.min = '0.001'; quantityInput.required = true; quantityInput.dataset.invoiceQuantity = 'true'; quantityLabel.append(quantityInput); fields.append(quantityLabel);
    const costLabel = element('label'); costLabel.append(element('span','', 'Custo por unidade de venda'));
    const costInput = element('input'); costInput.type='text';costInput.inputMode='decimal'; costInput.step = 'any'; costInput.min = '0'; costInput.required = true; costInput.dataset.invoiceCost = 'true'; costLabel.append(costInput); fields.append(costLabel);
    row.append(fields,element('p','invoice-item-help','')); row.querySelector('.invoice-item-help').dataset.invoiceHelp = 'true';
    fillInvoiceConversion(row,item,suggested);
    return row;
  }
  function closeInvoiceDialog() {
    if (state.invoiceBusy) return;
    $('invoice-dialog').close(); state.invoiceToken = null; state.invoicePreview = null; state.invoiceProducts = [];
  }
  async function selectInvoiceXml(event) {
    if (state.invoiceBusy || state.role !== 'ADMINISTRADOR') return;
    const trigger = event.currentTarget; trigger.disabled = true;
    try {
      const result = await request('stock.invoice.select');
      if (result?.cancelled) return;
      if (!result?.token || !result?.preview || !Array.isArray(result.preview.items)) throw new Error('Não foi possível ler a prévia do XML.');
      state.invoiceToken = result.token; state.invoicePreview = result.preview; state.invoiceProducts = Array.isArray(result.products) ? result.products : [];
      const preview = result.preview, summary = $('invoice-summary'); summary.replaceChildren();
      summary.append(invoiceSummary('NF-e',`${preview.number || '—'} / série ${preview.series || '—'}`),invoiceSummary('Emissão',date(preview.issueDate)),invoiceSummary('Fornecedor',`${preview.supplierName || '—'} · ${preview.supplierCnpj || '—'}`),invoiceSummary('Destinatário',`${preview.recipientName || '—'} · ${preview.recipientCnpj || '—'}`),invoiceSummary('Total da nota',currency(preview.invoiceTotal)),invoiceSummary('Chave de acesso',preview.key || '—'));
      const products = state.invoiceProducts, host = $('invoice-items'); host.replaceChildren();
      preview.items.forEach(item => host.append(renderInvoiceItem(item,products)));
      const storeCnpj = normalizedCnpj(state.data.storeCnpj);
      $('invoice-recipient-cnpj').textContent = storeCnpj || 'Não configurado'; $('invoice-error').textContent = '';
      $('invoice-submit').disabled = Boolean(preview.alreadyImported) || preview.items.length === 0 || !storeCnpj || storeCnpj !== normalizedCnpj(preview.recipientCnpj);
      if (preview.alreadyImported) $('invoice-error').textContent = 'Esta NF-e já foi importada. Nenhuma nova entrada será registrada.';
      else if (!preview.items.length) $('invoice-error').textContent = 'O XML não possui itens para entrada.';
      else if (!storeCnpj) $('invoice-error').textContent = 'Configure o CNPJ da loja em Configurações antes de importar.';
      else if (storeCnpj !== normalizedCnpj(preview.recipientCnpj)) $('invoice-error').textContent = 'Esta NF-e foi emitida para outro destinatário. Confira o CNPJ configurado da loja.';
      $('invoice-dialog').showModal();
    } catch (error) { alertError(error); }
    finally { trigger.disabled = false; }
  }
  $('invoice-form').addEventListener('submit',async event => {
    event.preventDefault();
    if (state.invoiceBusy || !state.invoiceToken || !state.invoicePreview || state.invoicePreview.alreadyImported) return;
    const form = event.currentTarget;
    if (!form.reportValidity()) return;
    const recipient = normalizedCnpj(state.data.storeCnpj);
    const xmlRecipient = normalizedCnpj(state.invoicePreview.recipientCnpj);
    const error = $('invoice-error'); error.textContent = '';
    if (!/^[0-9]{14}$/.test(recipient)) { error.textContent = 'Configure o CNPJ da loja antes de importar.'; return; }
    if (recipient !== xmlRecipient) { error.textContent = 'O destinatário da NF-e não corresponde ao CNPJ configurado da loja.'; return; }
    const selections = [];
    for (const row of $('invoice-items').children) {
      const productId = Number(row.querySelector('[data-invoice-product]').value);
      let quantity,unitCost;try{quantity=window.MercadoInput.decimal(row.querySelector('[data-invoice-quantity]').value,3);unitCost=window.MercadoInput.decimal(row.querySelector('[data-invoice-cost]').value,6);}catch(e){error.textContent=e.message;row.querySelector('input')?.focus();return;}
      if (!Number.isInteger(productId) || productId < 1 || !Number.isFinite(quantity) || quantity <= 0 || !Number.isFinite(unitCost) || unitCost < 0) {
        error.textContent = 'Vincule todos os itens e confira quantidade e custo antes de confirmar.'; row.querySelector('select,input')?.focus(); return;
      }
      const product = state.invoiceProducts.find(p => Number(p.id) === productId);
      if ((product?.unidadeMedida || '').toLocaleUpperCase('pt-BR') === 'UN' && !Number.isInteger(quantity)) {
        error.textContent = 'Produtos vendidos por unidade precisam de quantidade inteira.'; row.querySelector('[data-invoice-quantity]').focus(); return;
      }
      selections.push({ lineNumber: Number(row.dataset.lineNumber), productId, quantity, unitCost });
    }
    if (!selections.length || selections.length !== state.invoicePreview.items.length) { error.textContent = 'Confira todos os itens da nota.'; return; }
    if (!await window.MercadoUi.confirm(`Confirmar a entrada de ${selections.length} item(ns) da NF-e ${state.invoicePreview.number || ''}? Confira a mercadoria física antes de prosseguir.`)) return;
    state.invoiceBusy = true; $('invoice-submit').disabled = true; $('invoice-submit').textContent = 'Registrando entrada…'; $('invoice-cancel').disabled = true; $('invoice-close').disabled = true;
    try {
      await request('stock.invoice.import',{ token:state.invoiceToken, selections });
      state.invoiceBusy = false; closeInvoiceDialog(); notify($('toast-region'),'Entrada da NF-e registrada no estoque.'); await load();
    } catch (failure) { error.textContent = `${failure.message || String(failure)} Confira o histórico de notas antes de tentar novamente.`; }
    finally { state.invoiceBusy = false; $('invoice-cancel').disabled = false; $('invoice-close').disabled = false; $('invoice-submit').textContent = 'Confirmar entrada'; $('invoice-submit').disabled = Boolean(state.invoicePreview?.alreadyImported) || !state.data.storeCnpj || normalizedCnpj(state.data.storeCnpj) !== normalizedCnpj(state.invoicePreview?.recipientCnpj); }
  });
  $('invoice-close').onclick = $('invoice-cancel').onclick = closeInvoiceDialog;
  $('invoice-create-product').onclick = () => { if (state.invoiceBusy) return; closeInvoiceDialog(); window.MercadoBridge.navigate('products.html'); };
  $('invoice-dialog').addEventListener('cancel',event => { if (state.invoiceBusy) event.preventDefault(); else { state.invoiceToken = null; state.invoicePreview = null; state.invoiceProducts = []; } });
  async function simpleAction(operation,id){if(!await window.MercadoUi.confirm('Confirmar esta alteração?'))return;try{await request('management.execute',{operation,id});notify($('toast-region'),'Alteração salva.');load();}catch(e){alertError(e);}}
  let formOperation='', formItem={}, formOptions={};
  function openForm(title,operation,fields,item={},options={}){
    formOperation=operation;formItem=item;formOptions=options;$('dialog-title').textContent=title;$('dialog-error').textContent='';const host=$('dialog-fields');host.replaceChildren();
    fields.forEach(spec=>{const wrapper=element('label',spec.wide?'wide':'');wrapper.append(element('span','',spec.label));let input;if(spec.type==='textarea')input=element('textarea');else if(spec.type==='select'){input=element('select');(spec.options||[]).forEach(([value,label])=>{const opt=element('option','',label);opt.value=value;input.append(opt);});}else{input=element('input');input.type=spec.type==='number'?'text':spec.type;if(spec.type==='number'){input.inputMode='decimal';input.dataset.places=spec.step==='0.001'?'3':'2';input.placeholder=spec.step==='0.001'?'Ex.: 0,315':'Ex.: 14,27';}}
      input.name=spec.key;input.required=Boolean(spec.required);if(spec.key==='login')input.dataset.preserveCase='true';if(spec.step)input.step=spec.step;if(spec.min)input.min=spec.min;if(spec.type==='checkbox'){input.checked=Boolean(item[spec.key]);}else if(item[spec.key]!==undefined&&item[spec.key]!==null)input.value=input.dataset.places?String(item[spec.key]).replace('.',','):String(item[spec.key]);else if(spec.key==='date')input.value=today();wrapper.append(input);host.append(wrapper);});$('module-dialog').showModal();host.querySelector('input,select,textarea')?.focus();
  }
  let savingForm=false;
  $('module-form').addEventListener('submit',async event=>{
    event.preventDefault();if(savingForm)return;const form=event.currentTarget;if(!form.reportValidity())return;
    savingForm=true;$('dialog-submit').disabled=true;$('dialog-error').textContent='';
    try{
      if(formOptions.confirm&&!await window.MercadoUi.confirm(formOptions.confirm,{title:$('dialog-title').textContent,destructive:!!formOptions.danger}))return;
      const payload={operation:formOperation,...formItem};
      for(const input of form.elements){
        if(!input.name)continue;
        if(input.type==='checkbox')payload[input.name]=input.checked;
        else if(input.dataset.places){try{payload[input.name]=input.value===''&&!input.required?null:window.MercadoInput.decimal(input.value,Number(input.dataset.places));}catch(error){input.focus();throw new Error(`${input.closest('label').querySelector('span').textContent}: ${error.message}`);}}
        else if(input.type==='number')payload[input.name]=input.value===''?null:Number(input.value);
        else if(input.type==='select-one'&&['productId','supplierId'].includes(input.name))payload[input.name]=input.value===''?null:Number(input.value);
        else payload[input.name]=input.value;
      }
      const result=await request('management.execute',payload);if(formOperation==='settings.central.connect')form.reset();$('module-dialog').close();notify($('toast-region'),result.closed?`Caixa fechado. Diferença: ${currency(result.closed.diferenca)}`:'Alteração salva.');load();
    }catch(error){$('dialog-error').textContent=error.message;}
    finally{savingForm=false;$('dialog-submit').disabled=false;}
  });
  async function showDetails(module,id){try{const result=await request('management.details',{module,id});$('details-dialog').classList.toggle('invoice-details-dialog',module==='invoices');$('details-title').textContent=module==='sales'?`Venda #${id}`:module==='invoices'?`Itens da nota #${id}`:`Caixa #${id}`;const host=$('details-content');host.replaceChildren();if(module==='sales'&&result.totals){const totals=element('p','',`Subtotal: ${currency(result.totals.subtotal)} · Total: ${currency(result.totals.total)}`);host.append(totals);}if(module==='invoices')host.append(element('p','invoice-notice','Situação fiscal atual não consultada. Este histórico registra a entrada de estoque, não uma nova consulta à SEFAZ.'));const table=element('table'),head=element('tr'),body=element('tbody');const columns=module==='sales'?[['produto','Produto'],['quantidade','Quantidade',num],['precoUnitario','Preço',currency],['subtotal','Subtotal',currency]]:module==='invoices'?[['lineNumber','ITEM'],['xmlName','DESCRIÇÃO NO XML'],['productName','PRODUTO NO ESTOQUE'],['xmlQuantity','QTD. XML',num],['xmlUnit','UNID. XML'],['quantity','ENTRADA',num],['unit','UNID.'],['unitCost','CUSTO UNIT.',currency],['totalCost','CUSTO TOTAL',currency]]:[['data','Data',date],['tipo','Movimento'],['valor','Valor',currency],['observacao','Descrição']];columns.forEach(([,title])=>head.append(element('th','',title)));table.append(head);(result.rows||[]).forEach(row=>{const tr=element('tr');columns.forEach(([key,,format])=>{const raw=row[key];tr.append(element('td','',raw===null||raw===undefined||raw===''?'—':format?format(raw):String(raw)));});body.append(tr);});table.append(body);host.append(table);$('details-dialog').showModal();}catch(e){alertError(e);}}
  $('module-dialog').addEventListener('cancel',event=>{if(savingForm)event.preventDefault();});$('dialog-close').onclick=$('dialog-cancel').onclick=()=>{if(!savingForm)$('module-dialog').close();};$('details-close').onclick=$('details-done').onclick=()=>$('details-dialog').close();
  $('module-load-more-button').addEventListener('click',appendTableRows);
  document.addEventListener('keydown',e=>{if(document.querySelector('dialog[open]'))return;if(e.key==='F3'){e.preventDefault();$('filter-search')?.focus();return;}if(e.key==='F2'&&!e.altKey&&!e.ctrlKey&&!e.metaKey){e.preventDefault();window.MercadoBridge.navigate('index.html');}});
  window.addEventListener('popstate',()=>navigate(new URLSearchParams(location.search).get('module'),false));window.addEventListener('mercado:refresh',load);
  window.addEventListener('mercado:activate', event => {
    if (event.detail.first || document.querySelector('dialog[open]')) return;
    const module = new URL(event.detail.target, location.href).searchParams.get('module');
    if (module && module !== state.module) navigate(module);
    else if (event.detail.dirty) load();
  });
  buildNavigation();navigate(new URLSearchParams(location.search).get('module'),false);
})();
