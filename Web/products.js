(() => {
  'use strict';
  const { request } = window.MercadoBridge;
  const { element, currency, notify } = window.MercadoUi;
  const state = { products: [], categories: [], suppliers: [], selectedId: null, editorId: 0, imageToken: null, removeImage: false, saving: false, category: 'Todas', query: '', inactive: false, admin: false, canDelete: false, initialBarcodeHandled: false, visibleLimit: 250 };
  const $ = id => document.getElementById(id);
  const toast = $('toast-region');

  function dateKey(value) { return value ? String(value).slice(0, 10) : ''; }
  function todayKey() {
    const now = new Date();
    return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
  }
  function displayDate(value) {
    const parts = dateKey(value).split('-');
    return parts.length === 3 ? `${parts[2]}/${parts[1]}/${parts[0]}` : '—';
  }
  function selected() { return state.products.find(product => product.id === state.selectedId) || null; }
  function status(product) {
    if (!product.ativo) return { text: 'Inativo', tone: 'inactive' };
    const expires = dateKey(product.dataValidade);
    const low = Number(product.estoqueAtual) <= Number(product.estoqueMinimo);
    if (expires && expires < todayKey()) return { text: low ? 'Vencido · estoque baixo' : 'Vencido', tone: 'error' };
    const limit = new Date(); limit.setDate(limit.getDate() + 30);
    const limitKey = `${limit.getFullYear()}-${String(limit.getMonth() + 1).padStart(2, '0')}-${String(limit.getDate()).padStart(2, '0')}`;
    if (expires && expires <= limitKey) return { text: low ? 'Validade próxima · estoque baixo' : 'Validade próxima', tone: 'warn' };
    if (low) return { text: 'Estoque baixo', tone: 'warn' };
    if (!expires) return { text: 'Validade não informada', tone: 'inactive' };
    return { text: 'Em dia', tone: '' };
  }

  function renderCategories() {
    const host = $('category-filter'); host.replaceChildren();
    const categories = ['Todas', ...[...new Set(state.products.map(product => product.categoriaNome || 'Sem categoria'))].sort((a, b) => a.localeCompare(b, 'pt-BR'))];
    for (const name of categories) {
      const button = element('button', state.category === name ? 'active' : '', name);
      button.type = 'button'; button.setAttribute('aria-pressed', String(state.category === name));
      button.addEventListener('click', () => { state.category = name; state.visibleLimit = 250; renderCategories(); renderRows(); });
      host.append(button);
    }
  }

  function renderRows() {
    const host = $('product-rows'); host.replaceChildren();
    const query = state.query.toLocaleLowerCase('pt-BR');
    const visible = state.products.filter(product =>
      (state.inactive || product.ativo) &&
      (state.category === 'Todas' || (product.categoriaNome || 'Sem categoria') === state.category) &&
      (!query || product.nome.toLocaleLowerCase('pt-BR').includes(query) || String(product.codigoBarras || '').includes(query)));
    $('product-count').textContent = `${visible.length} ${visible.length === 1 ? 'produto' : 'produtos'}`;
    $('products-empty').hidden = visible.length > 0;
    const fragment = document.createDocumentFragment();
    for (const product of visible.slice(0, state.visibleLimit)) {
      const row = element('tr', product.id === state.selectedId ? 'selected' : '');
      row.tabIndex = 0; row.setAttribute('aria-selected', String(product.id === state.selectedId));
      const cells = [
        element('td', 'muted', product.codigoBarras || '—'),
        element('td', 'name', product.nome),
        element('td', 'muted', product.categoriaNome || '—'),
        element('td', 'muted', product.unidadeMedida),
        element('td', 'right', currency(product.precoVenda)),
        element('td', 'right', `${Number(product.estoqueAtual).toLocaleString('pt-BR', { maximumFractionDigits: 3 })} ${product.unidadeMedida}`),
        element('td', 'muted', displayDate(product.dataValidade))
      ];
      const stateCell = element('td'); const itemStatus = status(product);
      const badge = element('span', `status-tag ${itemStatus.tone}`, itemStatus.text); badge.title = itemStatus.text;
      stateCell.append(badge); row.append(...cells, stateCell);
      row.addEventListener('click', () => { state.selectedId = product.id; host.querySelectorAll('tr.selected').forEach(previous => { previous.classList.remove('selected'); previous.setAttribute('aria-selected', 'false'); }); row.classList.add('selected'); row.setAttribute('aria-selected', 'true'); updateActions(); });
      row.addEventListener('keydown', event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); row.click(); $('edit-product').focus(); } });
      row.addEventListener('dblclick', () => { if (state.admin) openEditor(product.id); });
      fragment.append(row);
    }
    if (visible.length > state.visibleLimit) {
      const more = element('tr'); const cell = element('td'); cell.colSpan = 8;
      const show = element('button', 'button button-secondary', `Mostrar mais produtos (${visible.length - state.visibleLimit} restantes)`); show.type = 'button';
      show.addEventListener('click', () => { state.visibleLimit += 250; renderRows(); }); cell.append(show); more.append(cell); fragment.append(more);
    }
    host.append(fragment);
    updateActions();
  }

  function updateActions() {
    const product = selected();
    $('selected-product').textContent = product ? `${product.nome} · #${product.id}` : 'Nenhum produto selecionado';
    $('edit-product').disabled = !state.admin || !product;
    $('toggle-product').disabled = !state.admin || !product;
    $('toggle-product').textContent = product && !product.ativo ? 'Reativar' : 'Desativar';
    $('delete-product').hidden = !state.canDelete;
    $('delete-product').disabled = !state.canDelete || !product;
    $('new-product').disabled = !state.admin;
  }

  async function loadData() {
    const started = performance.now();
    try {
      const data = await request('products.getData');
      state.products = data.products || [];
      state.categories = data.categories || []; state.suppliers = data.suppliers || [];
      state.admin = data.userRole === 'ADMINISTRADOR'; state.canDelete = Boolean(data.canDelete);
      if (!selected()) state.selectedId = null;
      const categories = new Set(state.products.map(product => product.categoriaNome || 'Sem categoria'));
      if (state.category !== 'Todas' && !categories.has(state.category)) state.category = 'Todas';
      $('store-name').textContent = data.storeName || 'Mercado'; $('breadcrumb-store').textContent = data.storeName || 'Mercado';
      document.title = `${data.storeName || 'Mercado'} · Produtos`;
      $('user-label').textContent = data.userName || 'Operador'; $('user-role').textContent = data.userRole || 'OPERADOR';
      $('user-avatar').textContent = (data.userName?.trim()?.[0] || 'M').toLocaleUpperCase('pt-BR');
      document.querySelectorAll('[data-module="expenses"],[data-page="reports.html"],[data-module="suppliers"],[data-module="settings"],[data-module="users"]').forEach(button => {
        button.disabled = !state.admin; button.title = button.disabled ? 'Disponível para administradores' : '';
      });
      renderCategories(); renderRows(); $('products-error').hidden = true;
      window.MercadoBridge.metric('ready', performance.now() - started);
      if (!state.initialBarcodeHandled) {
        state.initialBarcodeHandled = true;
        const barcode = new URLSearchParams(window.location.search).get('barcode');
        if (barcode && /^\d{8,14}$/.test(barcode)) {
          window.history.replaceState(null, '', './products.html');
          if (state.admin) { openEditor(); $('field-barcode').value = barcode; }
          else { state.query = barcode; $('product-search').value = barcode; renderRows(); }
        }
      }
    } catch (error) { $('products-error-text').textContent = error.message; $('products-error').hidden = false; }
  }

  function confirmAction(title, description, destructive = false) {
    return new Promise(resolve => {
      const dialog = $('confirm-modal'), accept = $('confirm-accept'), cancel = $('confirm-cancel');
      $('confirm-title').textContent = title; $('confirm-description').textContent = description;
      accept.classList.toggle('button-danger', destructive); accept.classList.toggle('button-primary', !destructive);
      let done = false;
      const finish = result => { if (done) return; done = true; dialog.close(); resolve(result); };
      accept.onclick = () => finish(true); cancel.onclick = () => finish(false);
      dialog.oncancel = event => { event.preventDefault(); finish(false); };
      dialog.showModal(); cancel.focus();
    });
  }

  function setOptions(id, options, selectedId) {
    const select = $(id); select.replaceChildren();
    const unknown = element('option', '', 'Não informado'); unknown.value = ''; select.append(unknown);
    for (const option of options) {
      const node = element('option', '', option.nome); node.value = String(option.id); select.append(node);
    }
    select.value = selectedId ? String(selectedId) : '';
  }

  function showImage(data) {
    const preview = $('product-image-preview');
    preview.hidden = !data; $('image-preview-empty').hidden = Boolean(data);
    preview.src = data || '';
  }

  async function loadImagePreview(productId) {
    if (!productId) { showImage(null); return; }
    try { const image = await request('products.getImage', { productId }); if (state.editorId === productId && !state.removeImage && !state.imageToken) showImage(image.imageData); }
    catch { showImage(null); }
  }

  function openEditor(productId = 0) {
    if (!state.admin) return;
    const product = productId ? state.products.find(item => item.id === productId) : null;
    if (productId && !product) { notify(toast, 'Produto não encontrado. Atualize a lista.', true); return; }
    state.editorId = productId; state.imageToken = null; state.removeImage = false; showImage(null);
    $('product-modal-title').textContent = product ? `Editar ${product.nome}` : 'Novo produto';
    $('field-barcode').value = product?.codigoBarras || '';
    $('field-name').value = product?.nome || '';
    $('field-price').value = product ? formatDecimal(product.precoVenda, 2) : '';
    $('field-unit').value = product?.unidadeMedida || 'UN';
    $('field-stock').value = product ? formatDecimal(product.estoqueAtual, product.unidadeMedida === 'KG' ? 3 : 0) : '0';
    $('field-brand').value = product?.marca || '';
    $('field-cost').value = product ? formatDecimal(product.precoCusto, 2) : '0,00';
    $('field-min-stock').value = product ? formatDecimal(product.estoqueMinimo, product.unidadeMedida === 'KG' ? 3 : 0) : '0';
    $('field-expiry').value = dateKey(product?.dataValidade);
    $('field-image-url').value = product?.imagemUrl || '';
    $('image-file-label').textContent = product?.hasLocalImage ? 'Imagem local já cadastrada' : 'Nenhum arquivo novo selecionado';
    setOptions('field-category', state.categories, product?.categoriaId);
    setOptions('field-supplier', state.suppliers, product?.fornecedorId);
    $('stock-unit').textContent = `(${ $('field-unit').value })`;
    $('product-form-error').textContent = '';
    $('product-modal').showModal(); $('field-barcode').focus(); loadImagePreview(productId);
  }

  function formatDecimal(value, places) { return Number(value || 0).toLocaleString('pt-BR', { minimumFractionDigits: places, maximumFractionDigits: places }); }
  function parseDecimal(value, places, label) {
    const raw = String(value || '').trim();
    const normalized = raw.includes(',') ? raw.replace(/\./g, '').replace(',', '.') : raw;
    const pattern = places === 0 ? /^\d+$/ : new RegExp(`^\\d+(?:\\.\\d{1,${places}})?$`);
    if (!pattern.test(normalized)) throw new Error(`${label}: informe um número válido com até ${places} casas decimais.`);
    const number = Number(normalized);
    if (!Number.isFinite(number) || number < 0 || number > 999999) throw new Error(`${label}: valor fora do limite permitido.`);
    return number;
  }

  async function saveProduct(event) {
    event.preventDefault(); if (state.saving) return;
    const error = $('product-form-error'); error.textContent = '';
    try {
      const unit = $('field-unit').value;
      const draft = {
        id: state.editorId,
        codigoBarras: $('field-barcode').value.trim() || null,
        nome: $('field-name').value.trim(),
        precoVenda: parseDecimal($('field-price').value, 2, 'Preço de venda'),
        unidadeMedida: unit,
        estoqueAtual: parseDecimal($('field-stock').value, unit === 'KG' ? 3 : 0, 'Estoque atual'),
        categoriaId: Number($('field-category').value) || null,
        fornecedorId: Number($('field-supplier').value) || null,
        marca: $('field-brand').value.trim() || null,
        precoCusto: parseDecimal($('field-cost').value, 2, 'Preço de custo'),
        estoqueMinimo: parseDecimal($('field-min-stock').value, unit === 'KG' ? 3 : 0, 'Estoque mínimo'),
        dataValidade: $('field-expiry').value ? `${$('field-expiry').value}T12:00:00` : null,
        imagemUrl: $('field-image-url').value.trim() || null,
        imageToken: state.imageToken,
        removeImage: state.removeImage
      };
      if (!draft.nome) { $('field-name').focus(); throw new Error('Informe o nome do produto.'); }
      state.saving = true; $('save-product').disabled = true; $('save-product').textContent = 'Salvando…';
      const saved = await request('products.save', draft);
      $('product-modal').close(); state.editorId = 0; state.imageToken = null; state.removeImage = false;
      state.selectedId = saved.productId; await loadData(); notify(toast, 'Produto salvo.');
    } catch (exception) { error.textContent = exception.message; }
    finally { state.saving = false; $('save-product').disabled = false; $('save-product').textContent = 'Salvar produto'; }
  }

  async function lookupProduct() {
    const barcode = $('field-barcode').value.trim();
    if (!/^\d{8,14}$/.test(barcode)) { $('product-form-error').textContent = 'Informe um código de barras com 8 a 14 dígitos.'; $('field-barcode').focus(); return; }
    const button = $('lookup-product'); button.disabled = true; button.textContent = 'Consultando…'; $('product-form-error').textContent = '';
    try {
      const response = await request('products.lookup', { barcode });
      const found = response.product;
      if (!found) { $('product-form-error').textContent = 'Produto não encontrado no catálogo. Continue o cadastro manualmente.'; return; }
      if (!$('field-name').value.trim()) $('field-name').value = (found.nome || '').toLocaleUpperCase('pt-BR');
      if (!$('field-brand').value.trim()) $('field-brand').value = (found.marca || '').toLocaleUpperCase('pt-BR');
      if (!$('field-image-url').value.trim() && found.imagemUrl) { $('field-image-url').value = found.imagemUrl; previewUrl(); }
      const categoryName = found.categoria?.split(',').at(-1)?.trim();
      if (!$('field-category').value && categoryName) {
        const match = state.categories.find(option => option.nome.toLocaleLowerCase('pt-BR') === categoryName.toLocaleLowerCase('pt-BR'));
        if (match) $('field-category').value = String(match.id);
      }
      notify(toast, 'Dados públicos encontrados. Revise antes de salvar.');
    } catch (exception) { $('product-form-error').textContent = exception.message; }
    finally { button.disabled = false; button.textContent = 'Buscar catálogo'; }
  }

  async function pickImage() {
    try {
      const result = await request('products.pickImage');
      if (result.token) { state.imageToken = result.token; state.removeImage = false; $('image-file-label').textContent = result.fileName; showImage(result.imageData); }
    } catch (exception) { $('product-form-error').textContent = exception.message; }
  }

  async function previewUrl() {
    const url = $('field-image-url').value.trim();
    if (!url || state.imageToken || state.removeImage) { if (!url && !state.imageToken) showImage(null); return; }
    try { const result = await request('products.previewImage', { url }); if ($('field-image-url').value.trim() === url && !state.imageToken && !state.removeImage) showImage(result.imageData); }
    catch { showImage(null); }
  }

  function removeImage() {
    state.removeImage = true; state.imageToken = null;
    $('field-image-url').value = ''; $('image-file-label').textContent = 'Imagem removida ao salvar'; showImage(null);
  }

  async function changeActive() {
    const product = selected(); if (!product || !state.admin) return;
    const active = !product.ativo;
    if (!await confirmAction(active ? 'Reativar produto' : 'Desativar produto', `${active ? 'Reativar' : 'Desativar'} ${product.nome}? O histórico será preservado.`, !active)) return;
    try { await request('products.setActive', { productId: product.id, active }); await loadData(); notify(toast, active ? 'Produto reativado.' : 'Produto desativado.'); }
    catch (error) { notify(toast, error.message, true); }
  }

  async function deleteProduct() {
    const product = selected(); if (!product || !state.canDelete) return;
    if (!await confirmAction('Excluir produto', `Excluir ${product.nome} do catálogo? O código de barras poderá ser reutilizado. Vendas e movimentações antigas continuarão no histórico.`, true)) return;
    try { await request('products.delete', { productId: product.id }); state.selectedId = null; await loadData(); notify(toast, 'Produto excluído do catálogo.'); }
    catch (error) { notify(toast, error.message, true); }
  }

  $('product-search').addEventListener('input', event => { state.query = event.target.value.trim(); state.visibleLimit = 250; renderRows(); });
  $('inactive-toggle').addEventListener('click', () => { state.inactive = !state.inactive; state.visibleLimit = 250; $('inactive-toggle').setAttribute('aria-pressed', String(state.inactive)); renderRows(); });
  $('new-product').addEventListener('click', () => openEditor());
  $('edit-product').addEventListener('click', () => { if (selected()) openEditor(selected().id); });
  $('toggle-product').addEventListener('click', changeActive);
  $('delete-product').addEventListener('click', deleteProduct);
  $('retry-products').addEventListener('click', loadData);
  $('product-form').addEventListener('submit', saveProduct);
  $('lookup-product').addEventListener('click', lookupProduct);
  $('pick-image').addEventListener('click', pickImage);
  $('remove-image').addEventListener('click', removeImage);
  $('field-image-url').addEventListener('change', () => { state.removeImage = false; state.imageToken = null; previewUrl(); });
  $('field-unit').addEventListener('change', () => { $('stock-unit').textContent = `(${ $('field-unit').value })`; });
  $('close-product').addEventListener('click', () => { if (!state.saving) $('product-modal').close(); });
  $('cancel-product').addEventListener('click', () => { if (!state.saving) $('product-modal').close(); });
  $('product-modal').addEventListener('cancel', event => { if (state.saving) event.preventDefault(); });
  document.querySelectorAll('[data-page]').forEach(button => button.addEventListener('click', () => { window.location.href = `./${button.dataset.page}`; }));
  document.querySelectorAll('[data-module]').forEach(button => button.addEventListener('click', () => { window.location.href = `./modules.html?module=${encodeURIComponent(button.dataset.module)}`; }));
  $('logout-button').addEventListener('click', async () => { try { await request('session.logout'); } catch (error) { notify(toast, error.message, true); } });
  document.addEventListener('keydown', event => { if (event.key === 'F2' && !event.altKey && !event.ctrlKey && !event.metaKey) { event.preventDefault(); window.location.href = './index.html'; } });
  window.addEventListener('mercado:refresh', loadData);
  loadData();
})();
