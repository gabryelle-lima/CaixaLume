(() => {
  'use strict';

  const origin = location.origin;
  const channel = 'caixalume:shell:v1';
  const allowedPages = new Set(['home.html', 'index.html', 'products.html', 'reports.html', 'modules.html']);
  const modules = new Set(['stock', 'cash', 'sales', 'expenses', 'suppliers', 'settings', 'users']);
  const frames = new Map();
  const requests = new Map();
  const screens = document.getElementById('screens');
  const loading = document.getElementById('screen-loading');
  const printHost = document.getElementById('print-host');
  let activePage = null;
  let startupReported = false;

  function route(target) {
    if (typeof target !== 'string') return null;
    let url;
    try { url = new URL(target, location.href); } catch { return null; }
    if (url.origin !== origin || url.hash) return null;
    const page = url.pathname.split('/').pop();
    if (!allowedPages.has(page)) return null;
    if (page === 'modules.html') {
      const module = url.searchParams.get('module');
      if (!modules.has(module) || [...url.searchParams.keys()].some(key => key !== 'module')) return null;
      return { page, target: `${page}?module=${encodeURIComponent(module)}`, module };
    }
    if (page === 'products.html' && url.searchParams.has('barcode')) {
      const barcode = url.searchParams.get('barcode');
      const returnToPdv = url.searchParams.get('return') === 'pdv';
      if (!/^\d{8,14}$/.test(barcode) || [...url.searchParams.keys()].some(key => key !== 'barcode' && key !== 'return') || (url.searchParams.has('return') && !returnToPdv)) return null;
      return { page, target: `${page}?barcode=${barcode}${returnToPdv ? '&return=pdv' : ''}`, barcode };
    }
    if (url.search) return null;
    return { page, target: page };
  }

  function frameFor(source) {
    for (const [page, record] of frames) {
      if (record.frame.contentWindow === source) return { page, record };
    }
    return null;
  }

  function send(record, kind, details = {}) {
    if (!record.loaded || !record.frame.contentWindow) return;
    record.frame.contentWindow.postMessage({ channel, kind, ...details }, origin);
  }

  function activate(rawTarget, first = false) {
    const next = route(rawTarget);
    if (!next) return;
    const previousPage = activePage;
    let record = frames.get(next.page);
    if (previousPage === next.page && record?.loaded && record.target === next.target) return;
    if (!record) {
      const frame = document.createElement('iframe');
      frame.className = 'screen';
      frame.title = next.page === 'index.html' ? 'Ponto de venda' : next.page === 'modules.html' ? 'Gestão' : next.page === 'reports.html' ? 'Relatórios' : next.page === 'products.html' ? 'Produtos' : 'Visão geral';
      frame.setAttribute('referrerpolicy', 'no-referrer');
      record = { frame, loaded: false, target: next.target, initialTarget: next.target, dirty: false };
      frames.set(next.page, record);
      frame.addEventListener('load', () => {
        try { if (frame.contentWindow?.location.pathname.split('/').pop() !== next.page) return; }
        catch { return; }
        record.loaded = true;
        if (activePage !== next.page) return;
        loading.hidden = true;
        frame.contentWindow?.focus();
        if (next.page === 'home.html' && !startupReported) {
          startupReported = true;
          window.chrome?.webview?.postMessage({ version: 1, id: crypto.randomUUID(), action: 'shell.ready', payload: {} });
        }
        send(record, 'activate', { first: record.target === record.initialTarget, target: record.target, dirty: record.dirty });
        record.dirty = false;
      });
      frame.src = `./${next.target}`;
      screens.append(frame);
    }
    record.target = next.target;
    if (previousPage === 'reports.html' && next.page !== previousPage) printHost.replaceChildren();
    if (previousPage && previousPage !== next.page) frames.get(previousPage)?.frame.classList.remove('active');
    activePage = next.page;
    record.frame.classList.add('active');
    document.title = `${record.frame.title} · CaixaLume`;
    if (!record.loaded) { loading.hidden = false; return; }
    loading.hidden = true;
    record.frame.contentWindow?.focus();
    if (!first) {
      send(record, 'activate', { first: false, target: next.target, dirty: record.dirty });
      record.dirty = false;
    }
  }

  function prepareReportPrint(page) {
    if (page !== 'reports.html') return false;
    const view = frames.get(page)?.frame.contentDocument?.querySelector('.reports-view');
    const content = view?.querySelector('#report-print-all');
    if (!view || !content || !content.children.length) return false;
    printHost.replaceChildren(view.cloneNode(true));
    return true;
  }

  function invalidateOthers(sourcePage) {
    for (const [page, record] of frames) {
      if (page !== sourcePage) record.dirty = true;
    }
  }

  window.addEventListener('message', event => {
    if (event.origin !== origin || event.data?.channel !== channel) return;
    const source = frameFor(event.source);
    if (!source) return;
    const { page, record } = source;
    if (event.data.kind === 'navigate') {
      const next = route(event.data.target);
      if (next) activate(next.target);
      return;
    }
    if (event.data.kind !== 'request') return;
    const message = event.data.message;
    if (!message || message.version !== 1 || typeof message.id !== 'string' || !/^[0-9a-f-]{36}$/i.test(message.id) || typeof message.action !== 'string' || requests.has(message.id)) return;
    if (!window.chrome?.webview) {
      send(record, 'response', { message: { version: 1, id: message.id, ok: false, error: 'A ponte com o aplicativo não está disponível.' } });
      return;
    }
    if (message.action === 'reports.print' || message.action === 'reports.exportPdf') {
      if (!prepareReportPrint(page)) {
        send(record, 'response', { message: { version: 1, id: message.id, ok: false, error: 'Abra o relatório antes de imprimir.' } });
        return;
      }
    }
    const timer = window.setTimeout(() => requests.delete(message.id), 360000);
    requests.set(message.id, { page, source: event.source, action: message.action, timer });
    window.chrome.webview.postMessage(message);
  });

  window.chrome?.webview?.addEventListener('message', event => {
    const message = event.data;
    if (!message || message.version !== 1 || typeof message.id !== 'string') return;
    const entry = requests.get(message.id);
    if (!entry) return;
    window.clearTimeout(entry.timer);
    requests.delete(message.id);
    if (message.ok && /^(products\.(save|setActive|delete)|pdv\.(checkout|openCash)|management\.(execute|restore))$/.test(entry.action)) invalidateOthers(entry.page);
    if (message.ok && entry.action === 'reports.getData') printHost.replaceChildren();
    const record = frames.get(entry.page);
    if (record?.frame.contentWindow === entry.source) entry.source.postMessage({ channel, kind: 'response', message }, origin);
  });

  activate('home.html', true);
})();
