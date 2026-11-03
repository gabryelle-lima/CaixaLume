(() => {
  'use strict';
  const { request } = window.MercadoBridge;
  const { element, currency, notify } = window.MercadoUi;
  const toast = document.getElementById('toast-region');

  function setCash(open) {
    const pill = document.getElementById('cash-status');
    pill.classList.toggle('open', open); pill.classList.toggle('closed', !open);
    pill.replaceChildren(element('span'), document.createTextNode(open ? 'Caixa aberto' : 'Caixa fechado'));
    const state = document.getElementById('cash-card-state'); state.replaceChildren(
      element('strong', '', open ? 'Caixa em operação' : 'Caixa fechado'),
      element('span', '', open ? 'O ponto de venda está pronto para registrar vendas.' : 'Abra o caixa para iniciar as vendas do turno.')
    );
    document.getElementById('cash-action').firstChild.textContent = open ? 'Conferir caixa ' : 'Abrir caixa ';
  }

  function showRecentSales(sales) {
    const host = document.getElementById('recent-sales'); host.replaceChildren();
    document.getElementById('sales-empty').hidden = sales.length > 0;
    if (!sales.length) return;
    sales.slice(0, 10).forEach(sale => {
      const row = element('tr');
      const date = new Date(sale.date); const dateText = Number.isNaN(date.getTime()) ? '—' : new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }).format(date);
      const idCell = element('td', '', `#${String(sale.id).padStart(5, '0')}`);
      const dateCell = element('td', '', dateText); const operatorCell = element('td', '', sale.operatorName || '—');
      const paymentCell = element('td'); paymentCell.append(element('span', 'payment-badge', sale.payment || '—'));
      const totalCell = element('td', 'right', currency(sale.total));
      row.append(idCell, dateCell, operatorCell, paymentCell, totalCell); host.append(row);
    });
  }

  function showGettingStarted(data) {
    const totalSales = Number(data.totalSales || 0);
    const section = document.getElementById('getting-started');
    section.hidden = totalSales > 0;
    if (section.hidden) return;

    const completed = [Number(data.productCount || 0) > 0, Boolean(data.cashOpen), false];
    const current = completed.indexOf(false);
    ['products', 'cash', 'sale'].forEach((name, index) => {
      const step = document.getElementById(`setup-step-${name}`);
      const done = completed[index];
      step.classList.toggle('done', done);
      step.classList.toggle('current', index === current);
      const label = step.querySelector('.setup-step-state');
      label.textContent = done ? 'Concluído' : index === current ? 'Próximo passo' : 'Pendente';
      label.setAttribute('aria-label', label.textContent);
    });
    document.getElementById('getting-started-progress').textContent =
      `${completed.filter(Boolean).length} de 3 concluídos`;
  }

  function renderAlerts(targetId, emptyId, items, makeDetail) {
    const host = document.getElementById(targetId); host.replaceChildren();
    document.getElementById(emptyId).hidden = items.length > 0;
    items.slice(0, 5).forEach(item => {
      const row = element('div', 'alert-row'); row.append(element('span', 'alert-name', item.product), element('span', 'alert-detail', makeDetail(item))); host.append(row);
    });
  }

  async function loadDashboard() {
    const started = performance.now();
    try {
      const data = await request('dashboard.getData');
      window.MercadoUi.updateInvoiceNavigation(data.userRole);
      const firstName = (data.userName || 'Olá').trim().split(/\s+/)[0];
      document.title = `${data.storeName || 'Mercado'} · Visão geral`;
      document.getElementById('store-name').textContent = data.storeName || 'Mercado';
      document.querySelector('.breadcrumbs>span').textContent = data.storeName || 'Mercado';
      document.getElementById('user-label').textContent = data.userName || 'Operador';
      document.getElementById('user-role').textContent = data.userRole || 'OPERADOR';
      document.querySelectorAll('[data-module="expenses"],[data-page="reports.html"],[data-module="suppliers"],[data-module="settings"],[data-module="users"]').forEach(button => {
        button.disabled = data.userRole !== 'ADMINISTRADOR';
        button.title = button.disabled ? 'Disponível para administradores' : '';
      });
      document.getElementById('user-avatar').textContent = firstName.slice(0, 1).toLocaleUpperCase('pt-BR');
      document.getElementById('greeting').textContent = `Bom dia, ${firstName}`;
      document.getElementById('today-label').textContent = new Intl.DateTimeFormat('pt-BR', { weekday: 'long', day: 'numeric', month: 'long' }).format(new Date()).toLocaleUpperCase('pt-BR');
      document.getElementById('revenue').textContent = currency(data.revenue);
      document.getElementById('sales-today').textContent = String(data.salesToday ?? 0);
      document.getElementById('average-ticket').textContent = currency(data.averageTicket);
      document.getElementById('low-stock-count').textContent = String(data.lowStockCount ?? 0);
      setCash(Boolean(data.cashOpen));
      showGettingStarted(data);
      showRecentSales(data.recentSales || []);
      renderAlerts('expired-alerts', 'expired-empty', data.expired || [], item => 'Vencido há ' + Math.abs(item.days) + ' dia(s) · ' + new Date(item.expiry).toLocaleDateString('pt-BR'));
      renderAlerts('stock-alerts', 'stock-empty', data.lowStock || [], item => `${item.stock.toLocaleString('pt-BR')} ${item.unit} · mín. ${item.minimum.toLocaleString('pt-BR')}`);
      renderAlerts('expiry-alerts', 'expiry-empty', data.expiring || [], item => `${item.days} ${item.days === 1 ? 'dia' : 'dias'} · ${new Date(item.expiry).toLocaleDateString('pt-BR')}`);
      document.getElementById('dashboard-error').hidden = true;
      window.MercadoBridge.metric('ready', performance.now() - started);
    } catch (error) {
      document.getElementById('dashboard-error-text').textContent = error.message;
      document.getElementById('dashboard-error').hidden = false;
    }
  }

  function navigatePage(page) { window.MercadoBridge.navigate(page); }
  function openModule(module) { navigatePage(`modules.html?module=${encodeURIComponent(module)}`); }

  document.querySelectorAll('[data-page]').forEach(button => button.addEventListener('click', () => navigatePage(button.dataset.page)));
  document.querySelectorAll('[data-module]').forEach(button => button.addEventListener('click', () => openModule(button.dataset.module)));
  document.getElementById('logout-button').addEventListener('click', async () => {
    try { await request('session.logout'); }
    catch (error) { notify(toast, error.message, true); }
  });
  document.getElementById('retry-dashboard').addEventListener('click', loadDashboard);
  document.addEventListener('keydown', event => { if (event.key === 'F2' && !event.altKey && !event.ctrlKey && !event.metaKey) { event.preventDefault(); navigatePage('index.html'); } });
  window.addEventListener('mercado:refresh', loadDashboard);
  window.addEventListener('mercado:activate', event => { if (event.detail.dirty) loadDashboard(); });
  loadDashboard();
})();
