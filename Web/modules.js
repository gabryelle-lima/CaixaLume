(() => {
  const { request } = window.MercadoBridge;
  const { element, currency, notify } = window.MercadoUi;
  const $ = id => document.getElementById(id);
  const names = { stock: 'Estoque', cash: 'Caixa', sales: 'Vendas', expenses: 'Despesas', suppliers: 'Fornecedores', settings: 'Configurações', users: 'Usuários' };
  const descriptions = { stock: 'Saldos, entradas e movimentações dos produtos.', cash: 'Acompanhe o turno e as movimentações do caixa.', sales: 'Consulte vendas e seus detalhes.', expenses: 'Registre e acompanhe as despesas da loja.', suppliers: 'Gerencie fornecedores e categorias.', settings: 'Ajuste os dados e as regras da loja.', users: 'Controle o acesso dos operadores.' };
  const adminModules = new Set(['expenses', 'suppliers', 'settings', 'users']);
  const nav = [ ['home.html','⌂','Visão geral'],['index.html','▦','Ponto de venda'],['products.html','▤','Produtos'],['stock','◫','Estoque'],['cash','▣','Caixa'],['sales','↗','Vendas'],['expenses','▧','Despesas'],['reports.html','▥','Relatórios'],['suppliers','♧','Fornecedores'],['settings','⚙','Configurações'],['users','♙','Usuários'] ];
  const state = { module: '', data: {}, role: '', tab: '', selected: null, rows: [], pending: false, sequence: 0 };
  const localDate = d => `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;
  const today = () => localDate(new Date());
  const prior = () => { const d = new Date(); d.setDate(d.getDate() - 29); return localDate(d); };
  const date = v => v ? new Date(v).toLocaleString('pt-BR') : '—';
  const num = v => Number(v ?? 0).toLocaleString('pt-BR', { maximumFractionDigits: 3 });
  const button = (label, fn, primary = false, danger = false) => { const b = element('button', `button ${danger ? 'button-danger' : primary ? 'button-primary' : 'button-secondary'}`, label); b.type = 'button'; b.addEventListener('click', fn); return b; };
  function buildNavigation() {
    nav.forEach(([target, icon, label], index) => {
      const b = element('button', 'nav-item'); b.type = 'button'; b.dataset.target = target;
      b.append(element('span', 'nav-icon', icon), element('span', '', label));
      if (target === 'index.html') b.append(element('kbd', '', 'F2'));
      b.addEventListener('click', () => target.endsWith('.html') ? location.assign(`./${target}`) : navigate(target));
      $(index < 6 ? 'primary-nav' : 'secondary-nav').append(b);
    });
    const logout = element('button', 'nav-item'); logout.type = 'button'; logout.append(element('span', 'nav-icon', '⇥'), element('span', '', 'Sair'));
    logout.addEventListener('click', async () => { try { await request('session.logout'); } catch (e) { alertError(e); } }); $('secondary-nav').append(logout);
    const offline = element('div', 'offline-status'); offline.append(element('span', 'connection-dot'), element('span', '', 'Aplicativo local · offline')); $('secondary-nav').append(offline);
  }
  function alertError(error) { notify($('toast-region'), error.message || String(error), true); }
  function navigate(module, push = true) {
    if (!names[module]) module = 'stock';
    if (push) history.pushState({ module }, '', `./modules.html?module=${module}`);
    state.module = module; state.tab = ''; state.selected = null; state.data = {};
    document.querySelectorAll('[data-target]').forEach(b => { b.classList.toggle('active', b.dataset.target === module); b.disabled = adminModules.has(b.dataset.target) && Boolean(state.role) && state.role !== 'ADMINISTRADOR'; });
    $('module-title').textContent = names[module]; $('breadcrumb-module').textContent = names[module]; $('module-subtitle').textContent = descriptions[module]; document.title = `Mercado · ${names[module]}`;
    $('module-error').hidden = true; $('module-metrics').replaceChildren(); $('module-actions').replaceChildren(); $('module-toolbar').replaceChildren(); $('module-tabs').replaceChildren(); $('module-head').replaceChildren(); $('module-rows').replaceChildren(); $('module-row-actions').replaceChildren(); $('module-count').textContent = 'Carregando…';
    renderToolbar(); load();
  }
  function filterField(type, id, placeholder, initial) { const input = element('input'); input.type = type; input.id = id; input.placeholder = placeholder; if (initial) input.value = initial; return input; }
  function renderToolbar() {
    const host = $('module-toolbar'), module = state.module;
    if (['stock','expenses','suppliers'].includes(module)) {
      const search = filterField('search', 'filter-search', 'Pesquisar registros'); search.addEventListener('input', () => { clearTimeout(search._timer); search._timer = setTimeout(() => module === 'expenses' || (module === 'stock' && state.tab === 'movements') ? load() : render(), 250); }); host.append(search);
    }
    if (module === 'stock') {
      const status = element('select'); status.id = 'filter-stock-status'; [['','Todos os saldos'],['low','Estoque baixo'],['normal','Estoque normal']].forEach(([value,label]) => { const option = element('option','',label); option.value=value; status.append(option); }); status.addEventListener('change',render); host.append(status);
      const unit = element('select'); unit.id = 'filter-stock-unit'; [['','Todas as unidades'],['UN','Unidade'],['KG','Quilo']].forEach(([value,label]) => { const option = element('option','',label); option.value=value; unit.append(option); }); unit.addEventListener('change',render); host.append(unit);
    }
    if (['stock','sales','expenses'].includes(module)) {
      host.append(element('span', '', 'De'), filterField('date', 'filter-start', '', prior()), element('span', '', 'até'), filterField('date', 'filter-end', '', today()));
      if (module === 'sales') { const select = element('select'); select.id = 'filter-status'; [['','Todas'],['Concluida','Concluídas'],['Cancelada','Canceladas']].forEach(([v,l]) => { const o = element('option', '', l); o.value = v; select.append(o); }); host.append(select); }
      host.append(button('Aplicar filtro', load));
    }
    host.append(button('↻ Atualizar', load));
  }
  async function load() {
    const started = performance.now();
    const module = state.module, sequence = ++state.sequence;
    $('module-error').hidden = true; $('module-count').textContent = 'Carregando…';
    try {
      const result = await request('management.getData', { module, tab: state.tab, start: $('filter-start')?.value || prior(), end: $('filter-end')?.value || today(), search: $('filter-search')?.value || '', status: $('filter-status')?.value || '' });
      if (sequence !== state.sequence || module !== state.module) return;
      state.data = result.data || {}; state.role = result.userRole; state.selected = null;
      $('store-name').textContent = $('breadcrumb-store').textContent = result.storeName || 'Mercado'; $('user-label').textContent = result.userName || 'Operador'; $('user-role').textContent = result.userRole || 'OPERADOR'; $('user-avatar').textContent = (result.userName || 'M').slice(0,1);
      document.querySelectorAll('[data-target]').forEach(b => b.disabled = adminModules.has(b.dataset.target) && state.role !== 'ADMINISTRADOR');
      render();
      window.MercadoBridge.metric('ready', performance.now() - started);
    } catch (error) { if (sequence !== state.sequence) return; $('module-error').textContent = error.message; $('module-error').hidden = false; $('module-count').textContent = 'Não foi possível carregar'; }
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
      columns = selectedTab === 'products' ? [['codigoBarras','CÓDIGO'],['nome','PRODUTO'],['categoriaNome','CATEGORIA'],['unidadeMedida','UNID.'],['estoqueAtual','SALDO',num],['estoqueMinimo','MÍNIMO',num],['dataValidade','VALIDADE',v => v ? new Date(v).toLocaleDateString('pt-BR') : '—']] : [['data','DATA',date],['produto','PRODUTO'],['tipo','TIPO'],['quantidade','QUANTIDADE',num],['motivo','MOTIVO'],['usuario','USUÁRIO']];
      if (state.role === 'ADMINISTRADOR') { actions.append(action('＋ Movimentar', 'stock.move', [productField(),field('type','Tipo','select',true,{options:[['Entrada','Entrada'],['Saída','Saída'],['Ajustar saldo','Ajustar saldo']]}),quantity('quantity','Quantidade ou novo saldo'),field('reason','Motivo','textarea',true)], {}, { primary:true })); actions.append(action('Receber mercadoria','stock.receive',[productField(),quantity('quantity','Quantidade'),money('cost','Custo unitário'),money('price','Novo preço de venda',false),field('expiry','Validade','date'),field('supplierId','Fornecedor','select',false,{options:[['','Não informado'],...(d.suppliers||[]).map(s=>[s.id,s.nome])]}),field('document','Documento'),field('note','Observação','textarea')])); }
      if (state.role === 'ADMINISTRADOR' && /^\d{8,14}$/.test(search) && !products.some(p=>p.codigoBarras===search)) actions.append(button('Cadastrar este código',()=>location.assign(`./products.html?barcode=${encodeURIComponent(search)}`)));
      metric('Produtos ativos', products.length); metric('Estoque baixo', products.filter(p => Number(p.estoqueAtual) <= Number(p.estoqueMinimo)).length);
    } else if (m === 'cash') {
      tabs = [['movements','Movimentações'],['history','Histórico de caixas']]; selectedTab ||= 'movements'; const c = d.current;
      rows = selectedTab === 'movements' ? d.movements || [] : d.history || [];
      columns = selectedTab === 'movements' ? [['data','DATA',date],['tipo','MOVIMENTAÇÃO'],['valor','VALOR',currency],['observacao','DESCRIÇÃO'],['usuario','USUÁRIO']] : [['id','CAIXA',v=>`#${v}`],['abertura','ABERTO EM',date],['fechamento','FECHADO EM',date],['saldoInicial','INICIAL',currency],['valorEsperado','ESPERADO',currency],['valorContado','CONTADO',currency],['status','STATUS']];
      metric('Status', c ? 'Aberto' : 'Fechado'); metric('Saldo inicial', currency(c?.saldoInicial)); metric('Vendas', currency(c?.totalVendas)); metric('Dinheiro esperado', currency(c?.valorEsperadoDinheiro));
      if (!c) actions.append(action('＋ Abrir caixa','cash.open',[money('amount','Saldo inicial')],{}, {primary:true}));
      else { actions.append(action('Suprimento','cash.move',[money('amount','Valor'),field('reason','Motivo','textarea',true)],{type:'SUPRIMENTO'})); actions.append(action('Sangria','cash.move',[money('amount','Valor'),field('reason','Motivo','textarea',true)],{type:'SANGRIA'})); actions.append(action('Registrar despesa','cash.expense',[field('description','Descrição','text',true),money('amount','Valor'),field('payment','Pagamento','select',true,{options:[['DINHEIRO','Dinheiro'],['PIX','Pix'],['DÉBITO','Débito'],['CRÉDITO','Crédito']]})])); actions.append(action('Fechar caixa','cash.close',[money('amount','Valor contado')],{}, {danger:true, confirm:'Confirmar o fechamento do caixa?'})); }
    } else if (m === 'sales') {
      rows = d.sales || []; columns = [['id','VENDA',v=>`#${v}`],['data','DATA',date],['operador','OPERADOR'],['pagamento','PAGAMENTO'],['subtotal','SUBTOTAL',currency],['desconto','DESCONTO',currency],['acrescimo','ACRÉSCIMO',currency],['total','TOTAL',currency],['status','STATUS']];
      metric('Vendas no período', rows.filter(x=>x.status === 'Concluida').length); metric('Faturamento', currency(rows.filter(x=>x.status === 'Concluida').reduce((a,x)=>a+Number(x.total||0),0)));
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
      actions.append(action('Editar nome','settings.name',[field('name','Nome da loja','text',true)],{name:d.storeName},{primary:true}));
      const p=d.policy||{}; actions.append(action('Política de descontos','settings.policy',[field('enabled','Permitir ajustes','checkbox'),field('operators','Permitir operadores','checkbox'),money('discount','Desconto máximo (%)'),money('surcharge','Acréscimo máximo (R$)')],{enabled:p.habilitada,operators:p.permitirOperador,discount:p.maxDescontoPercentual,surcharge:p.maxAcrescimoValor}));
      actions.append(button('Salvar backup',async()=>{try{const r=await request('management.backup'); if(r.saved) notify($('toast-region'),'Cópia de segurança salva.');}catch(e){alertError(e);}}));
      actions.append(button('Restaurar backup',async()=>{try{const r=await request('management.restore'); if(r.restored) notify($('toast-region'),'Backup restaurado. Faça login novamente.');}catch(e){alertError(e);}}));
      if(d.testTools) actions.append(button('Limpar caixas de teste',async()=>{if(!confirm('Excluir o histórico dos caixas de teste?'))return;try{await request('management.execute',{operation:'settings.clearCash'});notify($('toast-region'),'Histórico limpo.');load();}catch(e){alertError(e);}},false,true));
    } else if (m === 'users') {
      rows = d.users || []; columns = [['nome','NOME'],['login','LOGIN'],['perfil','PERFIL'],['ativo','STATUS',v=>v?'Ativo':'Inativo']]; metric('Usuários',rows.length);
      actions.append(action('＋ Novo usuário','users.save',userFields(),{}, {primary:true}));
    }
    state.tab = selectedTab; state.rows = rows; renderTabs(tabs); renderTable(columns,rows); renderRowActions();
  }
  function metric(label,value) { const card=element('div','module-metric'); card.append(element('span','',label),element('strong','',String(value))); $('module-metrics').append(card); }
  function renderTabs(tabs) { const host=$('module-tabs'); host.replaceChildren(); tabs.forEach(([id,label])=>{const b=element('button',id===state.tab?'active':'',label);b.type='button';b.addEventListener('click',()=>{if(state.tab===id)return;state.tab=id;state.selected=null;load();});host.append(b);}); }
  function renderTable(columns,rows) {
    const header=element('tr'); columns.forEach(([,label])=>header.append(element('th','',label))); $('module-head').replaceChildren(header); const body=$('module-rows'); body.replaceChildren();
    rows.forEach((item,index)=>{const tr=element('tr');tr.tabIndex=0;columns.forEach(([key,,format])=>{const raw=item[key];tr.append(element('td','',format?format(raw):raw===null||raw===undefined||raw===''?'—':String(raw)));});tr.addEventListener('click',()=>{state.selected=index;body.querySelectorAll('tr').forEach((r,i)=>r.classList.toggle('selected',i===index));renderRowActions();});tr.addEventListener('keydown',e=>{if(e.key==='Enter')tr.click();});body.append(tr);});
    $('module-count').textContent=`${rows.length} registro${rows.length===1?'':'s'}`; $('module-empty').hidden=rows.length>0;
  }
  function expenseFields() { return [field('description','Descrição','text',true),field('category','Categoria'),money('amount','Valor'),field('date','Data','date',true),field('payment','Forma de pagamento','select',true,{options:[['DINHEIRO','Dinheiro'],['PIX','Pix'],['DÉBITO','Débito'],['CRÉDITO','Crédito']]}),field('note','Observação','textarea')]; }
  function supplierFields() { return [field('name','Nome','text',true),field('document','Documento'),field('phone','Telefone'),field('email','E-mail','email'),field('address','Endereço','text',false,{wide:true}),field('note','Observação','textarea',false,{wide:true})]; }
  function userFields(edit=false) { return [field('name','Nome','text',true),field('login','Login','text',true),field('role','Perfil','select',true,{options:[['OPERADOR','Operador'],['ADMINISTRADOR','Administrador']]}),field('password',edit?'Nova senha (opcional)':'Senha','password',!edit)]; }
  function renderRowActions() {
    const host=$('module-row-actions'); host.replaceChildren(); const item=state.rows[state.selected]; if(!item)return; const m=state.module;
    if(m==='sales') {host.append(button('Ver detalhes',()=>showDetails('sales',item.id)));if(state.role==='ADMINISTRADOR'&&item.status==='Concluida')host.append(action('Cancelar venda','sales.cancel',[field('reason','Motivo do cancelamento','textarea',true)],{id:item.id},{danger:true,confirm:'Cancelar esta venda e estornar os itens?'}));}
    if(m==='cash'&&state.tab==='history')host.append(button('Ver movimentações',()=>showDetails('cash',item.id)));
    if(m==='expenses'&&item.status!=='Cancelada'){host.append(action('Editar','expenses.save',expenseFields(),{id:item.id,description:item.descricao,category:item.categoria,amount:item.valor,date:item.data?.slice(0,10),payment:item.formaPagamento,note:item.observacao}));host.append(action('Cancelar','expenses.cancel',[field('reason','Motivo','textarea',true)],{id:item.id},{danger:true,confirm:'Cancelar esta despesa?'}));}
    if(m==='suppliers') { if(state.tab==='suppliers'){host.append(action('Editar','suppliers.save',supplierFields(),{id:item.id,name:item.nome,document:item.documento,phone:item.telefone,email:item.email,address:item.endereco,note:item.observacao}));if(item.ativo)host.append(button('Desativar',()=>simpleAction('suppliers.deactivate',item.id),false,true));}else {host.append(action('Editar','categories.save',[field('name','Nome','text',true)],{id:item.id,name:item.nome}));if(item.ativa)host.append(button('Desativar',()=>simpleAction('categories.deactivate',item.id),false,true));} }
    if(m==='users') {host.append(action('Editar','users.save',userFields(true),{id:item.id,name:item.nome,login:item.login,role:item.perfil}));host.append(button(item.ativo?'Desativar':'Reativar',()=>simpleAction(item.ativo?'users.deactivate':'users.reactivate',item.id),false,item.ativo));}
  }
  async function simpleAction(operation,id){if(!confirm('Confirmar esta alteração?'))return;try{await request('management.execute',{operation,id});notify($('toast-region'),'Alteração salva.');load();}catch(e){alertError(e);}}
  let formOperation='', formItem={}, formOptions={};
  function openForm(title,operation,fields,item={},options={}){
    formOperation=operation;formItem=item;formOptions=options;$('dialog-title').textContent=title;$('dialog-error').textContent='';const host=$('dialog-fields');host.replaceChildren();
    fields.forEach(spec=>{const wrapper=element('label',spec.wide?'wide':'');wrapper.append(element('span','',spec.label));let input;if(spec.type==='textarea')input=element('textarea');else if(spec.type==='select'){input=element('select');(spec.options||[]).forEach(([value,label])=>{const opt=element('option','',label);opt.value=value;input.append(opt);});}else{input=element('input');input.type=spec.type;}
      input.name=spec.key;input.required=Boolean(spec.required);if(spec.step)input.step=spec.step;if(spec.min)input.min=spec.min;if(spec.type==='checkbox'){input.checked=Boolean(item[spec.key]);}else if(item[spec.key]!==undefined&&item[spec.key]!==null)input.value=String(item[spec.key]);else if(spec.key==='date')input.value=today();wrapper.append(input);host.append(wrapper);});$('module-dialog').showModal();host.querySelector('input,select,textarea')?.focus();
  }
  $('module-form').addEventListener('submit',async event=>{event.preventDefault();const form=event.currentTarget;if(!form.reportValidity())return;if(formOptions.confirm&&!confirm(formOptions.confirm))return;const payload={operation:formOperation,...formItem};for(const input of form.elements){if(!input.name)continue;if(input.type==='checkbox')payload[input.name]=input.checked;else if(input.type==='number')payload[input.name]=input.value===''?null:Number(input.value);else if(input.type==='select-one'&&['productId','supplierId'].includes(input.name))payload[input.name]=input.value===''?null:Number(input.value);else payload[input.name]=input.value;}
    $('dialog-submit').disabled=true;$('dialog-error').textContent='';try{const result=await request('management.execute',payload);$('module-dialog').close();notify($('toast-region'),result.closed?`Caixa fechado. Diferença: ${currency(result.closed.diferenca)}`:'Alteração salva.');load();}catch(error){$('dialog-error').textContent=error.message;}finally{$('dialog-submit').disabled=false;}
  });
  async function showDetails(module,id){try{const result=await request('management.details',{module,id});$('details-title').textContent=module==='sales'?`Venda #${id}`:`Caixa #${id}`;const host=$('details-content');host.replaceChildren();if(module==='sales'&&result.totals){const totals=element('p','',`Subtotal: ${currency(result.totals.subtotal)} · Total: ${currency(result.totals.total)}`);host.append(totals);}const table=element('table'),head=element('tr'),body=element('tbody');const columns=module==='sales'?[['produto','Produto'],['quantidade','Quantidade'],['precoUnitario','Preço'],['subtotal','Subtotal']]:[['data','Data'],['tipo','Movimento'],['valor','Valor'],['observacao','Descrição']];columns.forEach(([,title])=>head.append(element('th','',title)));table.append(head);(result.rows||[]).forEach(row=>{const tr=element('tr');columns.forEach(([key])=>tr.append(element('td','',String(row[key]??'—'))));body.append(tr);});table.append(body);host.append(table);$('details-dialog').showModal();}catch(e){alertError(e);}}
  $('dialog-close').onclick=$('dialog-cancel').onclick=()=>$('module-dialog').close();$('details-close').onclick=$('details-done').onclick=()=>$('details-dialog').close();
  document.addEventListener('keydown',e=>{if(e.key==='F2'&&!e.altKey&&!e.ctrlKey&&!e.metaKey){e.preventDefault();location.assign('./index.html');}});
  window.addEventListener('popstate',()=>navigate(new URLSearchParams(location.search).get('module'),false));window.addEventListener('mercado:refresh',load);
  buildNavigation();navigate(new URLSearchParams(location.search).get('module'),false);
})();
