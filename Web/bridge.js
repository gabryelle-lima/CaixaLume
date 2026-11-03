(() => {
  const pending = new Map();
  const channel = 'caixalume:shell:v1';
  const embedded = window.parent !== window;

  function send(message) {
    if (embedded) window.parent.postMessage({ channel, kind: 'request', message }, location.origin);
    else window.chrome?.webview?.postMessage(message);
  }

  function navigate(target) {
    if (embedded) window.parent.postMessage({ channel, kind: 'navigate', target }, location.origin);
    else window.location.assign(`./${target}`);
  }

  function metric(phase, milliseconds) {
    const page = location.pathname.split('/').pop()?.replace('.html', '');
    if (!['home', 'index', 'products', 'reports', 'modules'].includes(page) || !Number.isFinite(milliseconds)) return;
    send({ version: 1, id: crypto.randomUUID(), action: 'diagnostics.performance', payload: { page, phase, milliseconds: Math.max(0, Math.min(120000, Math.round(milliseconds))) } });
  }

  const readInFlight=new Map();
  const dataReads=new Set(['pdv.getData','products.getData','reports.getData','management.getData','dashboard.getData']);
  function request(action,payload={}){
    if(!dataReads.has(action))return sendRequest(action,payload);
    const key=action+JSON.stringify(payload);
    if(readInFlight.has(key))return readInFlight.get(key);
    const promise=sendRequest(action,payload).finally(()=>{if(readInFlight.get(key)===promise)readInFlight.delete(key);});
    readInFlight.set(key,promise);return promise;
  }
  function sendRequest(action, payload = {}) {
    if (!embedded && !window.chrome?.webview) return Promise.reject(new Error('A ponte com o aplicativo não está disponível.'));
    const id = crypto.randomUUID();
    return new Promise((resolve, reject) => {
      const transactional = action === 'pdv.checkout' || action === 'pdv.openCash' || action === 'stock.invoice.import';
      const timeoutMs = action === 'products.pickImage' || action === 'stock.invoice.select' || action === 'stock.invoice.exportExcel' || action.startsWith('reports.export') || action === 'reports.print' || action === 'management.backup' || action === 'management.restore' ? 300000 : transactional ? 60000 : action === 'products.lookup' ? 25000 : 15000;
      const timeout = window.setTimeout(() => {
        pending.delete(id);
        const message = action === 'pdv.checkout'
          ? 'A venda ainda não teve confirmação. Ela pode ter sido registrada; consulte a mesma operação ou verifique o histórico antes de iniciar outra.'
          : action === 'pdv.openCash'
            ? 'A abertura do caixa ainda não teve confirmação. Verifique o status do caixa antes de tentar novamente.'
            : action === 'stock.invoice.import'
              ? 'A entrada da NF-e ainda não teve confirmação. Confira o histórico de notas antes de tentar novamente.'
            : 'O aplicativo demorou para responder.';
        const error = new Error(message); error.code = transactional ? 'UNKNOWN_OUTCOME' : 'TIMEOUT'; reject(error);
      }, timeoutMs);
      pending.set(id, { resolve, reject, timeout, started: performance.now(), action });
      send({ version: 1, id, action, payload });
    });
  }

  function receive(message) {
    if (!message || message.version !== 1 || typeof message.id !== 'string') return;
    const current = pending.get(message.id);
    if (!current) return;
    pending.delete(message.id); window.clearTimeout(current.timeout);
    if (current.action.endsWith('.getData')) metric('data', performance.now() - current.started);
    if (message.ok) current.resolve(message.data);
    else { const error = new Error(typeof message.error === 'string' ? message.error : 'A operação não foi concluída.'); error.code = 'REJECTED'; current.reject(error); }
  }

  if (embedded) {
    window.addEventListener('message', event => {
      if (event.source !== window.parent || event.origin !== location.origin || event.data?.channel !== channel) return;
      if (event.data.kind === 'response') receive(event.data.message);
      else if (event.data.kind === 'activate') window.dispatchEvent(new CustomEvent('mercado:activate', { detail: { target: event.data.target, first: Boolean(event.data.first), dirty: Boolean(event.data.dirty) } }));
    });
  } else window.chrome?.webview?.addEventListener('message', event => receive(event.data));

  window.addEventListener('load', () => requestAnimationFrame(() => {
    const entry = performance.getEntriesByType('navigation')[0];
    if (entry) metric('navigation', entry.duration || performance.now());
  }));

  window.MercadoBridge = Object.freeze({ request, metric, navigate });
})();
