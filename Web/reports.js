(() => {
  'use strict';
  const { request } = window.MercadoBridge;
  const { element, currency, notify } = window.MercadoUi;
  const $ = id => document.getElementById(id);
  const sections = [
    { key: 'vendas', title: 'Vendas', description: 'Transações concluídas no período', fields: [['id','Venda','id'],['data','Data e hora','dateTime'],['formaPagamento','Pagamento'],['subtotal','Subtotal','money'],['desconto','Desconto','money'],['acrescimo','Acréscimo','money'],['total','Total líquido','money']] },
    { key: 'produtosMaisVendidos', title: 'Mais vendidos', description: 'Produtos com maior quantidade vendida', fields: [['produto','Produto'],['quantidade','Quantidade','number'],['unidade','Unidade'],['total','Total bruto','money']] },
    { key: 'produtosMenosVendidos', title: 'Menos vendidos', description: 'Produtos ativos com menor quantidade vendida', fields: [['produto','Produto'],['quantidade','Quantidade','number'],['unidade','Unidade'],['total','Total bruto','money']] },
    { key: 'pagamentos', title: 'Pagamentos', description: 'Vendas por forma de pagamento', fields: [['formaPagamento','Forma de pagamento'],['quantidade','Vendas','number'],['total','Total','money']] },
    { key: 'despesas', title: 'Despesas', description: 'Despesas registradas no período', fields: [['data','Data e hora','dateTime'],['descricao','Descrição'],['formaPagamento','Pagamento'],['valor','Valor','money']] },
    { key: 'estoqueBaixo', title: 'Estoque baixo', description: 'Produtos no nível mínimo ou abaixo', fields: [['produto','Produto'],['estoque','Estoque','number'],['minimo','Mínimo','number'],['unidade','Unidade']] },
    { key: 'validades', title: 'Validade', description: 'Produtos com vencimento no período', fields: [['produto','Produto'],['validade','Validade','date'],['dias','Dias restantes','number'],['status','Situação']] },
    { key: 'movimentacoesEstoque', title: 'Movimentações', description: 'Entradas, saídas e ajustes de estoque', fields: [['data','Data e hora','dateTime'],['produto','Produto'],['codigoBarras','Código'],['tipo','Tipo'],['quantidade','Quantidade','number'],['unidadeMedida','Unidade'],['motivo','Motivo'],['usuario','Usuário']] },
    { key: 'caixas', title: 'Caixas', description: 'Resumo dos turnos de caixa', fields: [['abertura','Abertura','dateTime'],['fechamento','Fechamento','dateTime'],['saldoInicial','Saldo inicial','money'],['vendas','Vendas','money'],['despesas','Despesas','money'],['valorEsperado','Esperado','money'],['valorContado','Contado','money'],['diferenca','Diferença','money'],['status','Status']] }
  ];
  const state = { report: null, section: 'vendas', start: '', end: '' };
  const dateKey = date => `${date.getFullYear()}-${String(date.getMonth()+1).padStart(2,'0')}-${String(date.getDate()).padStart(2,'0')}`;
  const brDate = value => value ? new Date(value).toLocaleDateString('pt-BR') : '—';
  const format = (value, kind) => {
    if (value === null || value === undefined || value === '') return '—';
    if (kind === 'money') return currency(value);
    if (kind === 'number') return Number(value).toLocaleString('pt-BR', { maximumFractionDigits: 3 });
    if (kind === 'date') return new Date(value).toLocaleDateString('pt-BR');
    if (kind === 'dateTime') return new Date(value).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
    if (kind === 'id') return `#${String(value).padStart(6,'0')}`;
    return String(value);
  };
  function setPeriod() {
    const now = new Date(), today = new Date(now.getFullYear(),now.getMonth(),now.getDate());
    const choice = $('period').value;
    let start = new Date(today), end = new Date(today);
    if (choice === 'yesterday') start = end = new Date(today.getFullYear(),today.getMonth(),today.getDate()-1);
    if (choice === 'week') start.setDate(today.getDate()-6);
    if (choice === 'month') start.setDate(today.getDate()-29);
    if (choice === 'this-month') start = new Date(today.getFullYear(),today.getMonth(),1);
    if (choice === 'previous-month') { start = new Date(today.getFullYear(),today.getMonth()-1,1); end = new Date(today.getFullYear(),today.getMonth(),0); }
    if (choice !== 'custom') { $('start-date').value = dateKey(start); $('end-date').value = dateKey(end); }
    document.querySelectorAll('.custom-date').forEach(label => { label.hidden = choice !== 'custom'; });
  }
  function renderTabs() {
    const host = $('report-tabs'); host.replaceChildren();
    for (const section of sections) {
      const button = element('button', '', section.title); button.type = 'button'; button.role = 'tab'; button.setAttribute('aria-selected', String(state.section === section.key));
      button.addEventListener('click', () => { state.section = section.key; renderTabs(); renderTable(); }); host.append(button);
    }
  }
  function renderTable() {
    const section = sections.find(item => item.key === state.section), rows = state.report?.[section.key] || [];
    $('report-section-title').textContent = section.title; $('report-section-description').textContent = section.description;
    $('report-row-count').textContent = `${rows.length} ${rows.length === 1 ? 'registro' : 'registros'}`;
    const heading = element('tr');
    for (const [,title,kind] of section.fields) heading.append(element('th', kind === 'money' || kind === 'number' ? 'right' : '', title));
    $('report-head').replaceChildren(heading);
    const body = document.createDocumentFragment();
    for (const row of rows) { const tr = element('tr'); for (const [key,,kind] of section.fields) tr.append(element('td', kind === 'money' || kind === 'number' ? 'right' : '', format(row[key], kind))); body.append(tr); }
    $('report-rows').replaceChildren(body); $('report-empty').hidden = rows.length > 0;
  }
  async function loadReport() {
    const started = performance.now();
    const start = $('start-date').value, end = $('end-date').value;
    if (!start || !end || end < start) { $('report-error-text').textContent = 'Informe um período válido: a data final deve ser igual ou posterior à inicial.'; $('report-error').hidden = false; return; }
    try {
      const data = await request('reports.getData', { start, end }); state.report = data.report; state.start = start; state.end = end;
      $('store-name').textContent = data.storeName; $('breadcrumb-store').textContent = data.storeName; document.title = `${data.storeName} · Relatórios`;
      $('user-label').textContent = data.userName; $('user-role').textContent = data.userRole; $('user-avatar').textContent = (data.userName?.[0] || 'M').toLocaleUpperCase('pt-BR');
      $('report-period').textContent = `PERÍODO: ${brDate(`${start}T12:00:00`)} A ${brDate(`${end}T12:00:00`)}`;
      $('metric-revenue').textContent = currency(data.report.totalVendido); $('metric-sales').textContent = String(data.report.quantidadeVendas); $('metric-ticket').textContent = currency(data.report.ticketMedio); $('metric-profit').textContent = currency(data.report.lucroBrutoEstimado);
      $('report-breakdown').textContent = `DESPESAS ${currency(data.report.totalDespesas)}  ·  CUSTO ${currency(data.report.custoEstimado)}  ·  MARGEM ${Number(data.report.margemMediaPercentual).toLocaleString('pt-BR',{maximumFractionDigits:1})}%  ·  DESCONTOS ${currency(data.report.totalDescontos)}  ·  ACRÉSCIMOS ${currency(data.report.totalAcrescimos)}`;
      $('report-error').hidden = true; renderTable();
      window.MercadoBridge.metric('ready', performance.now() - started);
    } catch (error) { $('report-error-text').textContent = error.message; $('report-error').hidden = false; }
  }
  async function exportReport(action) {
    if (!state.report) return;
    try { const result = await request(action, { start: state.start, end: state.end }); if (result.saved) notify($('toast-region'), 'Relatório salvo com sucesso.'); }
    catch (error) { notify($('toast-region'), error.message, true); }
  }
  $('period').addEventListener('change', setPeriod);
  $('report-filters').addEventListener('submit', event => { event.preventDefault(); loadReport(); });
  $('excel-report').addEventListener('click', () => exportReport('reports.exportExcel'));
  $('pdf-report').addEventListener('click', () => exportReport('reports.exportPdf'));
  $('print-report').addEventListener('click', () => exportReport('reports.print'));
  document.querySelectorAll('[data-page]').forEach(button => button.addEventListener('click', () => { window.location.href = `./${button.dataset.page}`; }));
  document.querySelectorAll('[data-module]').forEach(button => button.addEventListener('click', () => { window.location.href = `./modules.html?module=${encodeURIComponent(button.dataset.module)}`; }));
  $('logout-button').addEventListener('click', async () => { try { await request('session.logout'); } catch (error) { notify($('toast-region'), error.message, true); } });
  document.addEventListener('keydown', event => { if (event.key === 'F2' && !event.altKey && !event.ctrlKey && !event.metaKey) { event.preventDefault(); window.location.href = './index.html'; } });
  renderTabs(); setPeriod(); loadReport();
})();
