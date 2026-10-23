(() => {
  'use strict';

  const state = { products: [], categories: ['TODOS'], category: 'TODOS', cart: new Map(), cashOpen: false, cashStatusUncertain: false, busy: false, policy: {}, role: 'OPERADOR', adjustment: {}, checkoutId: null, checkoutOutcomeUncertain: false, checkoutCancelledId: null, draftKey: null, draftRestored: false, pendingWeight: null, visibleLimit: 250 };
  const elements = Object.fromEntries(['product-search','product-grid','category-list','result-count','cart-list','cart-count','subtotal','total','charge-total','charge-button','clear-cart','cash-status','user-label','user-role','user-avatar','payment-modal','payment-form','payment-total','amount-received','received-label','change-value','confirm-payment','payment-error','success-modal','success-detail','new-sale','cash-modal','cash-form','opening-balance','open-cash','cash-error','toast-region'].map(id => [id, document.getElementById(id)]));
  const request = window.MercadoBridge.request;
  const domain = window.MercadoPdv;
  const { element, currency, notify: showToast } = window.MercadoUi;
  const cartItems = () => [...state.cart.values()];
  const moneyRound = value => Math.round((value + Number.EPSILON) * 100) / 100;
  const formatMoneyInput = value => moneyRound(value).toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  const parseMoneyInput = value => {
    const raw = String(value ?? '').trim().replace(/^R\$\s*/, '').replace(/\s/g, '');
    const normalized = raw.includes(',') ? raw.replace(/\./g, '').replace(',', '.') : raw;
    if (!/^\d+(?:\.\d{1,2})?$/.test(normalized)) return NaN;
    const amount = Number(normalized);
    return Number.isFinite(amount) && amount <= 1000000000 ? amount : NaN;
  };
  const itemTotal = item => domain.itemCents(item) / 100;
  const pricing = () => domain.totals(cartItems(), state.adjustment, state.policy, state.role);
  const total = () => pricing().total;
  const count = () => state.cart.size;
  function notify(message, error = false) {
    showToast(elements['toast-region'], message, error);
  }

  function saveDraft() {
    if (!state.draftKey) return;
    try {
      if (!state.cart.size && !state.checkoutId) { localStorage.removeItem(state.draftKey); return; }
      const items = cartItems().map(({ product, quantity }) => ({ product: { id: product.id, name: product.name, barcode: product.barcode, category: product.category, price: product.price, unit: product.unit, stock: product.stock, minimumStock: product.minimumStock }, quantity }));
      localStorage.setItem(state.draftKey, JSON.stringify({ items, adjustment: state.adjustment, checkoutId: state.checkoutId,
        paymentMethod: elements['payment-form'].elements['payment-method'].value, amountReceived: elements['amount-received'].value }));
    } catch (error) { notify('Não foi possível preservar esta venda no computador: ' + error.message, true); }
  }

  function restoreDraft() {
    if (state.draftRestored || !state.draftKey) return;
    state.draftRestored = true;
    try {
      const draft = JSON.parse(localStorage.getItem(state.draftKey) || 'null');
      if (!draft || !Array.isArray(draft.items)) return;
      let unavailable = 0;
      for (const item of draft.items) {
        const current = state.products.find(product => product.id === item.product?.id);
        const product = draft.checkoutId ? item.product : current;
        if (!product || !Number.isFinite(item.quantity) || item.quantity <= 0) { unavailable++; continue; }
        state.cart.set(product.id, { product, quantity: item.quantity });
      }
      state.adjustment = draft.adjustment || {};
      state.checkoutId = typeof draft.checkoutId === 'string' ? draft.checkoutId : null;
      state.checkoutOutcomeUncertain = Boolean(state.checkoutId);
      if (draft.paymentMethod) {
        const option = [...elements['payment-form'].elements['payment-method']].find(radio => radio.value === draft.paymentMethod);
        if (option) option.checked = true;
      }
      if (draft.amountReceived) elements['amount-received'].value = draft.amountReceived;
      if (state.checkoutId) notify('Há uma venda com resultado pendente. Consulte a mesma operação antes de iniciar outra.', true);
      else if (state.cart.size) notify('Venda em andamento recuperada neste computador.');
      if (unavailable) notify(`${unavailable} item(ns) do rascunho não estão mais disponíveis. Revise o carrinho.`, true);
    } catch (error) { notify('O rascunho da venda não pôde ser recuperado: ' + error.message, true); }
  }

  function showCheckoutStatus() {
    document.getElementById('checkout-status').hidden = !state.checkoutId;
    const cancelled = state.checkoutCancelledId !== null;
    document.getElementById('checkout-status-title').textContent = cancelled ? `Venda #${state.checkoutCancelledId} cancelada` : 'Resultado do pagamento pendente';
    document.getElementById('checkout-status-description').textContent = cancelled
      ? 'A operação foi localizada como cancelada. Confira o histórico antes de limpar esta tentativa.'
      : 'Reveja a mesma operação antes de iniciar outra venda.';
    document.getElementById('retry-checkout').hidden = cancelled;
    document.getElementById('discard-cancelled-checkout').hidden = !cancelled;
    if (cancelled) elements['charge-button'].disabled = true;
  }
  function setCashStatusUncertain(uncertain) {
    state.cashStatusUncertain = uncertain;
    elements['open-cash'].firstChild.textContent = uncertain ? 'Verificar caixa ' : 'Abrir caixa ';
  }

  function setCash(open) {
    state.cashOpen = open;
    const pill = elements['cash-status'];
    pill.classList.toggle('open', open); pill.classList.toggle('closed', !open);
    pill.replaceChildren(element('span'), document.createTextNode(open ? 'Caixa aberto' : 'Caixa fechado · abrir'));
    pill.setAttribute('aria-label', open ? 'Caixa aberto' : 'Abrir caixa');
    elements['charge-button'].disabled = !open || count() === 0 || state.busy;
  }

  function renderCategories() {
    const host = elements['category-list']; host.replaceChildren();
    state.categories.forEach(category => {
      const chip = element('button', `category-chip${state.category === category ? ' selected' : ''}`, category === 'TODOS' ? 'Todos' : category);
      chip.type = 'button'; chip.setAttribute('role', 'tab'); chip.setAttribute('aria-selected', String(state.category === category));
      chip.addEventListener('click', () => { state.category = category; state.visibleLimit = 250; renderCategories(); renderProducts(); }); host.append(chip);
    });
  }

  function renderProducts() {
    const host = elements['product-grid']; host.replaceChildren();
    const query = elements['product-search'].value.trim().toLocaleLowerCase('pt-BR');
    const filtered = state.products.filter(product => (state.category === 'TODOS' || product.category.toLocaleLowerCase('pt-BR') === state.category.toLocaleLowerCase('pt-BR')) && (!query || product.name.toLocaleLowerCase('pt-BR').includes(query) || product.barcode.includes(query)));
    elements['result-count'].textContent = `${filtered.length} ${filtered.length === 1 ? 'produto' : 'produtos'}`;
    if (!filtered.length) {
      const empty = element('div', 'empty-state'); empty.append(element('span', 'empty-icon', '⌕'), element('strong', '', state.products.length ? 'Nenhum produto encontrado' : 'Catálogo vazio'), element('small', '', state.products.length ? 'Tente outra busca ou categoria.' : 'Cadastre produtos para começar a vender.')); host.append(empty); return;
    }
    const fragment = document.createDocumentFragment();
    filtered.slice(0, state.visibleLimit).forEach(product => {
      const card = element('button', 'product-card'); card.type = 'button'; card.setAttribute('aria-label', `Adicionar ${product.name}, ${currency(product.price)} por ${product.unit}`);
      const top = element('span', 'product-top');
      const visual = product.imageData ? document.createElement('img') : element('span', 'product-visual', '◇');
      if (product.imageData) { visual.className = 'product-visual'; visual.alt = ''; visual.src = product.imageData; }
      const category = element('span', 'product-category', product.category);
      top.append(visual, category);
      const name = element('span', 'product-name', product.name);
      const meta = element('span', 'product-meta'); meta.append(element('span', 'product-price', currency(product.price)), element('span', 'product-unit', `/${product.unit}`));
      const low = product.stock <= product.minimumStock;
      const stock = element('span', `product-stock${low ? ' low' : ''}`); stock.append(element('span', 'stock-dot'), document.createTextNode(`${product.stock.toLocaleString('pt-BR')} ${product.unit} em estoque`));
      card.append(top, name, meta, stock, element('span', 'add-mark', '+'));
      card.disabled = product.stock <= 0; card.title = product.stock <= 0 ? 'Produto sem estoque' : 'Adicionar ao carrinho';
      card.addEventListener('click', () => addProduct(product)); fragment.append(card);
    });
    if (filtered.length > state.visibleLimit) {
      const more = element('button', 'button button-secondary', `Mostrar mais (${filtered.length - state.visibleLimit} restantes)`);
      more.type = 'button'; more.addEventListener('click', () => { state.visibleLimit += 250; renderProducts(); }); fragment.append(more);
    }
    host.append(fragment);
  }

  function addProduct(product) {
    if (!canEdit() || product.active === false) return false;
    if (product.unit === 'KG') {
      state.pendingWeight = product;
      document.getElementById('weight-product').textContent = product.name;
      document.getElementById('weight-price').textContent = `${currency(product.price)}/kg · disponível: ${product.stock.toLocaleString('pt-BR')} kg`;
      document.getElementById('weight-value').value = '';
      document.getElementById('weight-error').textContent = '';
      document.getElementById('weight-modal').showModal();
      document.getElementById('weight-value').focus();
      return true;
    }
    const current = state.cart.get(product.id)?.quantity || 0;
    const increment = 1;
    const quantity = current + increment;
    try { domain.quantity(quantity.toFixed(product.unit === 'KG' ? 3 : 0), product.unit, product.stock); }
    catch (error) { notify(error.message, true); return false; }
    if (increment <= 0) { notify('Produto sem estoque disponível.', true); return false; }
    state.cart.set(product.id, { product, quantity }); renderCart(); return true;
  }

  function changeQuantity(id, delta) {
    if (!canEdit()) return;
    const item = state.cart.get(id); if (!item) return;
    const increment = item.product.unit === 'KG' ? .25 : 1;
    const next = item.quantity + increment * delta;
    if (next <= 0) state.cart.delete(id);
    else if (next > item.product.stock) { notify(`Estoque disponível: ${item.product.stock} ${item.product.unit}.`, true); return; }
    else item.quantity = next;
    renderCart();
  }

  function renderCart() {
    const host = elements['cart-list']; host.replaceChildren();
    if (!state.cart.size) {
      const empty = element('div', 'cart-empty'); empty.append(element('div', 'cart-empty-icon', '▱'), element('strong', '', 'Seu carrinho está vazio'), element('span', '', 'Selecione um item para iniciar a venda.')); host.append(empty);
    } else {
      state.cart.forEach(({ product, quantity }) => {
        const row = element('article', 'cart-row'); row.append(element('div', 'cart-row-heading'));
        const head = row.firstChild; head.append(element('span', 'cart-row-name', product.name), element('span', 'cart-row-price', `${currency(product.price)}/${product.unit}`));
        const controls = element('div', 'cart-row-controls'); const quantityControl = element('div', 'quantity-control');
        const minus = element('button', '', '−'); minus.type = 'button'; minus.setAttribute('aria-label', `Diminuir ${product.name}${product.unit === 'KG' ? ' em 250 gramas' : ''}`); minus.addEventListener('click', () => changeQuantity(product.id, -1));
        const shown = product.unit === 'KG' ? quantity.toLocaleString('pt-BR', { minimumFractionDigits: 3, maximumFractionDigits: 3 }) : String(quantity);
        const input = element('input', 'quantity-value'); input.type = 'text'; input.inputMode = 'decimal'; input.value = shown; input.dataset.productId = String(product.id); input.setAttribute('aria-label', 'Quantidade em ' + product.unit + ' de ' + product.name);
        const commit = () => {
          if (!canEdit()) { input.value = shown; return; }
          try { const next = domain.quantity(input.value, product.unit, product.stock); state.cart.get(product.id).quantity = next; renderCart(); const replacement = [...host.querySelectorAll('input[data-product-id]')].find(field => field.dataset.productId === String(product.id)); replacement?.focus(); }
          catch (error) { notify(error.message, true); input.value = shown; }
        };
        input.addEventListener('change', commit);
        input.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); input.blur(); } });
        quantityControl.append(minus, input, element('span', 'quantity-unit', product.unit === 'KG' ? 'kg' : 'un'));
        const plus = element('button', '', '+'); plus.type = 'button'; plus.setAttribute('aria-label', `Aumentar ${product.name}${product.unit === 'KG' ? ' em 250 gramas' : ''}`); plus.addEventListener('click', () => changeQuantity(product.id, 1)); quantityControl.append(plus);
        const rowTotal = element('span', 'cart-row-total', currency(itemTotal({ quantity, product })));
        const remove = element('button', 'remove-item', '×'); remove.type = 'button'; remove.setAttribute('aria-label', `Remover ${product.name}`); remove.addEventListener('click', () => { if (canEdit()) { state.cart.delete(product.id); renderCart(); } });
        controls.append(quantityControl, rowTotal, remove); row.append(controls); host.append(row);
      });
    }
    let values;
    try { values = pricing(); }
    catch (error) { state.adjustment = {}; values = pricing(); notify('O ajuste foi removido após alteração do carrinho: ' + error.message, true); }
    const value = values.total; elements['cart-count'].textContent = String(count()); elements['subtotal'].textContent = currency(values.subtotal); elements['total'].textContent = currency(value); elements['charge-total'].textContent = currency(value);
    document.getElementById('discount-total').textContent = currency(values.discount);
    document.getElementById('surcharge-total').textContent = currency(values.surcharge);
    document.getElementById('edit-adjustments').disabled = !state.cart.size || !state.policy.habilitada || (state.role !== 'ADMINISTRADOR' && !state.policy.permitirOperador);
    elements['charge-button'].disabled = !state.cashOpen || state.cart.size === 0 || state.busy;
    elements['clear-cart'].disabled = state.cart.size === 0;
    showCheckoutStatus(); saveDraft();
  }

  function refreshCashLabel() {
    const user = window.__pdvUser;
    if (user) { elements['user-label'].textContent = user.name; elements['user-role'].textContent = user.role; elements['user-avatar'].textContent = (user.name.trim()[0] || 'M').toLocaleUpperCase('pt-BR'); }
  }

  async function loadData() {
    const started = performance.now();
    try {
      const data = await request('pdv.getData'); state.products = data.products || []; state.categories = data.categories || ['TODOS'];
      state.policy = data.adjustmentsPolicy || {}; state.role = data.userRole || 'OPERADOR';
      state.draftKey = Number.isInteger(data.userId) && data.userId > 0 ? `mercado-pdv-draft-${data.userId}` : null;
      restoreDraft();
      state.categories = ['TODOS', ...state.categories.filter(category => category !== 'TODOS')];
      window.__pdvUser = { name: data.userName || 'Operador', role: data.userRole || 'OPERADOR' }; refreshCashLabel();
      document.querySelectorAll('[data-module="expenses"],[data-page="reports.html"],[data-module="suppliers"],[data-module="settings"],[data-module="users"]').forEach(button => {
        button.disabled = state.role !== 'ADMINISTRADOR';
        button.title = button.disabled ? 'Disponível para administradores' : '';
      });
      document.getElementById('store-name').textContent = data.storeName || 'Mercado';
      renderCategories(); renderProducts(); renderCart(); setCash(Boolean(data.cashOpen)); setCashStatusUncertain(false);
      document.getElementById('load-error').hidden = true;
      window.MercadoBridge.metric('ready', performance.now() - started);
      return true;
    } catch (error) {
      document.getElementById('load-error-text').textContent = error.message;
      document.getElementById('load-error').hidden = false;
      elements['cash-status'].textContent = 'Status do caixa indisponível';
      elements['charge-button'].disabled = true;
      return false;
    }
  }

  function openPayment() {
    if (state.busy || document.querySelector('dialog[open]')) return;
    if (state.checkoutCancelledId !== null) { showCheckoutStatus(); return; }
    if (!state.cart.size) return;
    if (!state.cashOpen && !state.checkoutId) { elements['cash-modal'].showModal(); return; }
    elements['payment-error'].textContent = ''; elements['payment-total'].textContent = currency(total());
    if (!state.checkoutId) {
      elements['amount-received'].value = formatMoneyInput(total());
      elements['payment-form'].querySelectorAll('input[name="payment-method"]').forEach(radio => { radio.checked = false; });
    }
    elements['payment-modal'].showModal();
    updatePaymentMethod();
    if (!state.checkoutId) elements['payment-form'].querySelector('input[name="payment-method"]').focus();
    else { elements['payment-error'].textContent = 'Consulte a mesma operação para conhecer o resultado antes de iniciar outra venda.'; elements['confirm-payment'].focus(); }
  }

  function updatePaymentMethod() {
    const method = elements['payment-form'].elements['payment-method'].value;
    elements['payment-form'].querySelectorAll('.payment-option').forEach(option => option.classList.toggle('selected', option.querySelector('input').checked));
    const isCash = method === 'DINHEIRO'; elements['received-label'].hidden = !isCash; elements['amount-received'].closest('.money-input').hidden = !isCash; elements['change-value'].closest('.change-row').hidden = !isCash; document.getElementById('money-help').hidden = !isCash;
    const amount = parseMoneyInput(elements['amount-received'].value); elements['change-value'].textContent = currency(Math.max(0, amount - total()));
    elements['confirm-payment'].firstElementChild.textContent = state.checkoutId ? 'Consultar a mesma venda' : method ? `Registrar ${currency(total())} em ${method.toLocaleLowerCase('pt-BR')}` : 'Selecione a forma de pagamento';
  }

  async function finishCheckout(response) {
    state.checkoutId = null; state.checkoutOutcomeUncertain = false; state.checkoutCancelledId = null; state.adjustment = {};
    state.cart.clear(); renderCart(); elements['payment-modal'].close();
    const change = response.change > 0 ? ` · Troco ${currency(response.change)}` : '';
    elements['success-detail'].textContent = `Venda #${String(response.saleId).padStart(5, '0')} · ${currency(response.total)} · ${response.paymentMethod}${change}`;
    elements['success-modal'].showModal(); await loadData();
  }
  async function submitPayment(event) {
    event.preventDefault(); if (state.busy || !state.cart.size) return;
    const method = elements['payment-form'].elements['payment-method'].value; const amount = parseMoneyInput(elements['amount-received'].value);
    if (!method) { elements['payment-error'].textContent = 'Selecione a forma de pagamento desta venda.'; elements['payment-form'].querySelector('input[name="payment-method"]').focus(); return; }
    if (method === 'DINHEIRO' && !Number.isFinite(amount)) { elements['payment-error'].textContent = 'Informe o valor recebido em reais. Exemplo: 14,27.'; elements['amount-received'].focus(); return; }
    if (method === 'DINHEIRO' && amount < total()) { elements['payment-error'].textContent = 'O valor recebido deve cobrir o total da venda.'; elements['amount-received'].focus(); return; }
    setBusy(true); elements['payment-error'].textContent = '';
    let checkoutSent = false;
    try {
      const pendingCheckout = Boolean(state.checkoutId);
      if (!state.checkoutId) { state.checkoutId = crypto.randomUUID(); state.checkoutOutcomeUncertain = false; }
      saveDraft(); showCheckoutStatus();
      if (pendingCheckout) {
        const previous = await request('pdv.operationStatus', { operationId: state.checkoutId });
        if (previous.found && previous.status === 'Concluida') { await finishCheckout(previous); return; }
        if (previous.found) { elements['payment-error'].textContent = `A venda #${previous.saleId} já consta como ${previous.status}. Consulte o histórico antes de iniciar outra.`; return; }
      }
      const checkoutPayload = { items: cartItems().map(item => ({ productId: item.product.id, quantity: item.quantity })), paymentMethod: method, amountReceived: method === 'DINHEIRO' ? amount : total(), adjustments: state.adjustment, operationId: state.checkoutId, expectedTotal: total() };
      checkoutSent = true;
      const response = await request('pdv.checkout', checkoutPayload);
      await finishCheckout(response);
    } catch (error) {
      if (checkoutSent && error.code === 'UNKNOWN_OUTCOME') state.checkoutOutcomeUncertain = true;
      let status = null;
      if (checkoutSent && state.checkoutId) {
        try { status = await request('pdv.operationStatus', { operationId: state.checkoutId }); }
        catch { /* Preserve a operação até que seja possível confirmar o resultado. */ }
      }
      if (status?.found && status.status === 'Concluida') { await finishCheckout(status); return; }
      if (status?.found && status.status === 'Cancelada') {
        state.checkoutCancelledId = status.saleId;
        saveDraft(); showCheckoutStatus(); elements['payment-modal'].close();
        notify(`A venda #${status.saleId} está cancelada. Confira o histórico antes de encerrar esta tentativa.`, true);
        return;
      }
      const rejectedBeforeRegistration = error.code === 'REJECTED' && [
        'Dados de pagamento inválidos.', 'Forma de pagamento inválida.',
        'Produto ou quantidade inválidos.', 'Um dos produtos não está mais disponível.'
      ].includes(error.message);
      if (!state.checkoutOutcomeUncertain && (rejectedBeforeRegistration || (error.code === 'REJECTED' && error.message.startsWith('A venda precisa conter de ')) || (error.code === 'REJECTED' && status?.found === false))) state.checkoutId = null;
      saveDraft(); showCheckoutStatus();
      elements['payment-error'].textContent = error.message + (state.checkoutId && error.code !== 'UNKNOWN_OUTCOME' ? ' Use “Consultar a mesma venda” para verificar o resultado com o mesmo identificador.' : '');
    }
    finally { setBusy(false); }
  }

  function setBusy(busy) {
    elements['payment-form'].querySelectorAll('input').forEach(input => { input.disabled = busy || Boolean(state.checkoutId); });
    state.busy = busy; elements['confirm-payment'].disabled = busy; if (busy) elements['confirm-payment'].firstElementChild.textContent = 'Processando venda…'; else updatePaymentMethod();
    elements['charge-button'].disabled = busy || !state.cashOpen || !state.cart.size;
  }

  async function submitCash(event) {
    event.preventDefault();
    if (state.cashStatusUncertain) {
      elements['open-cash'].disabled = true; elements['cash-error'].textContent = '';
      try {
        const refreshed = await loadData();
        if (refreshed && state.cashOpen) { elements['cash-modal'].close(); notify('Caixa aberto confirmado. O PDV foi atualizado.'); }
        else if (refreshed) elements['cash-error'].textContent = 'O caixa aparece fechado. O status foi atualizado; revise o saldo antes de abrir.';
        else elements['cash-error'].textContent = 'Ainda não foi possível verificar o caixa. Use “Verificar caixa” novamente antes de abrir.';
      } finally { elements['open-cash'].disabled = false; }
      return;
    }
    const amount = parseMoneyInput(elements['opening-balance'].value);
    if (!Number.isFinite(amount) || amount < 0) { elements['cash-error'].textContent = 'Informe o saldo em reais. Exemplo: 50,00.'; elements['opening-balance'].focus(); return; }
    elements['open-cash'].disabled = true; elements['cash-error'].textContent = '';
    try { await request('pdv.openCash', { openingBalance: amount }); elements['cash-modal'].close(); setCash(true); setCashStatusUncertain(false); notify('Caixa aberto.'); }
    catch (error) {
      if (error.code === 'UNKNOWN_OUTCOME' || String(error.message).toLocaleLowerCase('pt-BR').includes('já existe um caixa aberto')) {
        setCashStatusUncertain(true);
        const refreshed = await loadData();
        if (refreshed && state.cashOpen) {
          elements['cash-modal'].close();
          notify('Caixa aberto confirmado. O PDV foi atualizado.');
          return;
        }
        setCashStatusUncertain(true);
        elements['cash-error'].textContent = refreshed ? 'A abertura pode ainda estar em andamento. Clique em “Verificar caixa” antes de tentar novamente.' : error.message;
        return;
      }
      elements['cash-error'].textContent = error.message;
    }
    finally { elements['open-cash'].disabled = false; }
  }

  elements['product-search'].addEventListener('input', () => { state.visibleLimit = 250; renderProducts(); });
  elements['product-search'].addEventListener('keydown', event => {
    if (event.key !== 'Enter') return;
    event.preventDefault(); if (event.repeat || !canEdit()) return;
    const query = elements['product-search'].value.trim(); const result = domain.resolveProduct(state.products, query);
    if (result.kind === 'found') {
      if (addProduct(result.product)) { elements['product-search'].value = ''; renderProducts(); elements['product-search'].focus(); }
    } else if (result.kind === 'ambiguous') notify('Há mais de um produto. Selecione o item correto na grade.', true);
    else if (result.kind === 'notFound') { document.getElementById('unknown-code').textContent = query; document.getElementById('unknown-modal').showModal(); }
  });
  elements['clear-cart'].addEventListener('click', () => {
    if (!canEdit() || !state.cart.size) return;
    const previous = cartItems().map(item => ({ product: item.product, quantity: item.quantity }));
    const adjustment = state.adjustment;
    state.cart.clear(); state.adjustment = {}; renderCart();
    const notice = element('div', 'toast', 'Carrinho limpo. ');
    const undo = element('button', 'undo-button', 'Desfazer'); undo.type = 'button';
    undo.addEventListener('click', () => {
      if (state.cart.size || !canEdit()) return;
      previous.forEach(item => state.cart.set(item.product.id, item)); state.adjustment = adjustment;
      renderCart(); notice.remove();
    });
    notice.append(undo); elements['toast-region'].append(notice);
    window.setTimeout(() => notice.remove(), 10000);
  });
  document.getElementById('retry-checkout').addEventListener('click', openPayment);
  document.getElementById('discard-cancelled-checkout').addEventListener('click', async () => {
    if (state.checkoutCancelledId === null || !window.confirm(`A venda #${state.checkoutCancelledId} está cancelada. Você conferiu o histórico e deseja limpar esta tentativa?`)) return;
    state.checkoutId = null; state.checkoutOutcomeUncertain = false; state.checkoutCancelledId = null; state.adjustment = {};
    state.cart.clear(); renderCart(); await loadData();
    notify('Tentativa cancelada encerrada. Você pode iniciar outra venda.');
  });
  document.getElementById('retry-load').addEventListener('click', loadData);
  elements['charge-button'].addEventListener('click', openPayment);
  elements['cash-status'].addEventListener('click', () => { if (!state.cashOpen) elements['cash-modal'].showModal(); });
  elements['payment-form'].addEventListener('change', updatePaymentMethod);
  elements['amount-received'].addEventListener('input', () => { updatePaymentMethod(); if (state.checkoutId) saveDraft(); });
  for (const input of [elements['amount-received'], elements['opening-balance']]) input.addEventListener('blur', () => {
    const amount = parseMoneyInput(input.value);
    if (Number.isFinite(amount)) input.value = formatMoneyInput(amount);
    if (state.checkoutId) saveDraft();
  });
  document.getElementById('weight-form').addEventListener('submit', event => {
    event.preventDefault();
    const product = state.pendingWeight;
    if (!product || !canEdit()) return;
    const current = state.cart.get(product.id)?.quantity || 0;
    try {
      const amount = domain.quantity(document.getElementById('weight-value').value, 'KG', product.stock - current);
      const next = current + amount;
      state.cart.set(product.id, { product, quantity: next });
      state.pendingWeight = null;
      document.getElementById('weight-modal').close(); renderCart(); elements['product-search'].focus();
    } catch (error) { document.getElementById('weight-error').textContent = error.message; document.getElementById('weight-value').focus(); }
  });
  document.getElementById('weight-modal').addEventListener('close', () => { state.pendingWeight = null; });
  document.querySelectorAll('[data-close]').forEach(button => button.addEventListener('click', () => button.closest('dialog').close()));
  elements['payment-modal'].addEventListener('cancel', event => { if (state.busy) event.preventDefault(); });
  document.getElementById('lookup-product').addEventListener('click', async () => {
    const barcode = document.getElementById('unknown-code').textContent;
    document.getElementById('unknown-modal').close();
    if (state.checkoutId) { showCheckoutStatus(); return; }
    saveDraft();
    window.location.href = `./products.html?barcode=${encodeURIComponent(barcode)}`;
  });
  document.getElementById('edit-adjustments').addEventListener('click', () => {
    if (!canEdit()) return;
    document.getElementById('discount-type').value = state.adjustment.tipoDesconto || 'VALOR';
    document.getElementById('discount-value').value = String(state.adjustment.desconto || 0).replace('.', ',');
    document.getElementById('surcharge-value').value = String(state.adjustment.acrescimo || 0).replace('.', ',');
    document.getElementById('adjustment-error').textContent = '';
    document.getElementById('adjustment-preview').textContent = currency(total());
    document.getElementById('adjustments-modal').showModal();
  });
  function readAdjustment() {
    const number = id => { const text = document.getElementById(id).value.trim() || '0'; if (!/^\d+(?:[.,]\d{1,2})?$/.test(text)) throw new Error('Informe valores positivos com até duas casas.'); return Number(text.replace(',', '.')); };
    return { tipoDesconto: document.getElementById('discount-type').value, desconto: number('discount-value'), acrescimo: number('surcharge-value') };
  }
  document.getElementById('adjustments-form').addEventListener('input', () => {
    try { const values = domain.totals(cartItems(), readAdjustment(), state.policy, state.role); document.getElementById('adjustment-preview').textContent = currency(values.total); document.getElementById('adjustment-error').textContent = ''; }
    catch (error) { document.getElementById('adjustment-error').textContent = error.message; }
  });
  document.getElementById('adjustments-form').addEventListener('submit', event => {
    event.preventDefault();
    try { const adjustment = readAdjustment(); domain.totals(cartItems(), adjustment, state.policy, state.role); state.adjustment = adjustment; renderCart(); document.getElementById('adjustments-modal').close(); }
    catch (error) { document.getElementById('adjustment-error').textContent = error.message; }
  });
  function canEdit() {
    if (state.busy) return false;
    if (state.checkoutId) { notify('Confirme novamente o pagamento pendente antes de alterar o carrinho.', true); return false; }
    return true;
  }
  window.addEventListener('mercado:refresh', loadData);
  elements['payment-form'].addEventListener('submit', submitPayment);
  elements['cash-form'].addEventListener('submit', submitCash);
  elements['new-sale'].addEventListener('click', () => { elements['success-modal'].close(); elements['product-search'].focus(); });
  document.querySelectorAll('[data-page]').forEach(button => button.addEventListener('click', () => {
    if (state.checkoutId) { notify('Consulte a venda pendente antes de sair do PDV.', true); showCheckoutStatus(); return; }
    saveDraft(); window.location.href = `./${button.dataset.page}`;
  }));
  document.querySelectorAll('[data-module]').forEach(button => button.addEventListener('click', () => {
    if (state.checkoutId) { notify('Consulte a venda pendente antes de sair do PDV.', true); showCheckoutStatus(); return; }
    saveDraft(); window.location.href = `./modules.html?module=${encodeURIComponent(button.dataset.module)}`;
  }));
  document.getElementById('logout-button')?.addEventListener('click', async () => {
    if (state.checkoutId) { notify('Consulte a venda pendente antes de encerrar a sessão.', true); return; }
    if (state.cart.size && !window.confirm('Descartar a venda em andamento e sair?')) return;
    try { await request('session.logout'); if (state.draftKey) localStorage.removeItem(state.draftKey); }
    catch (error) { notify(error.message, true); }
  });
  document.addEventListener('keydown', event => { if (event.altKey || event.ctrlKey || event.metaKey || event.repeat) return; if (event.key === 'F2' && !document.querySelector('dialog[open]')) { event.preventDefault(); elements['product-search'].focus(); elements['product-search'].select(); } if (event.key === 'F4' && !document.querySelector('dialog[open]')) { event.preventDefault(); openPayment(); } });

  renderCart(); loadData();
})();
