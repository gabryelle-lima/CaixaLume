(() => {
  const pending = new Map();

  function metric(phase, milliseconds) {
    const page = location.pathname.split('/').pop()?.replace('.html', '');
    if (!['home', 'index', 'products', 'reports', 'modules'].includes(page) || !Number.isFinite(milliseconds)) return;
    window.chrome?.webview?.postMessage({ version: 1, id: crypto.randomUUID(), action: 'diagnostics.performance', payload: { page, phase, milliseconds: Math.max(0, Math.min(120000, Math.round(milliseconds))) } });
  }

  function request(action, payload = {}) {
    if (!window.chrome?.webview) return Promise.reject(new Error('A ponte com o aplicativo não está disponível.'));
    const id = crypto.randomUUID();
    return new Promise((resolve, reject) => {
      const timeoutMs = action === 'products.pickImage' || action.startsWith('reports.export') || action === 'reports.print' || action === 'management.backup' || action === 'management.restore' ? 300000 : action === 'products.lookup' ? 25000 : 15000;
      const timeout = window.setTimeout(() => { pending.delete(id); const error = new Error('O aplicativo demorou para responder.'); error.code = 'TIMEOUT'; reject(error); }, timeoutMs);
      pending.set(id, { resolve, reject, timeout, started: performance.now(), action });
      window.chrome.webview.postMessage({ version: 1, id, action, payload });
    });
  }

  window.chrome?.webview?.addEventListener('message', event => {
    const message = event.data;
    if (!message || message.version !== 1 || typeof message.id !== 'string') return;
    const current = pending.get(message.id);
    if (!current) return;
    pending.delete(message.id); window.clearTimeout(current.timeout);
    if (current.action.endsWith('.getData')) metric('data', performance.now() - current.started);
    if (message.ok) current.resolve(message.data);
    else { const error = new Error(typeof message.error === 'string' ? message.error : 'A operação não foi concluída.'); error.code = 'REJECTED'; current.reject(error); }
  });

  window.addEventListener('load', () => requestAnimationFrame(() => {
    const entry = performance.getEntriesByType('navigation')[0];
    if (entry) metric('navigation', entry.duration || performance.now());
  }));

  window.MercadoBridge = Object.freeze({ request, metric });
})();
