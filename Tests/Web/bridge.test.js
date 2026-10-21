const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const source = fs.readFileSync(require('node:path').join(__dirname, '../../Web/bridge.js'), 'utf8');
function fixture() {
  let receive, sent, timeout;
  const window = { chrome: { webview: { postMessage: message => { sent = message; }, addEventListener: (_, handler) => { receive = handler; } } },
    setTimeout: callback => { timeout = callback; return 1; }, clearTimeout: () => {} };
  vm.runInNewContext(source, { window, crypto: { randomUUID: () => 'contract-1' }, Map, Promise, Error });
  return { request: window.MercadoBridge.request, sent: () => sent, receive: value => receive({ data: value }), timeout: () => timeout() };
}
test('Ponte envia envelope versionado e resolve resposta correspondente', async () => {
  const f = fixture(); const response = f.request('pdv.getData', { productId: 1 });
  assert.equal(f.sent().version, 1); assert.equal(f.sent().id, 'contract-1'); assert.equal(f.sent().action, 'pdv.getData');
  f.receive({ version: 1, id: 'contract-1', ok: true, data: { cashOpen: true } });
  assert.equal((await response).cashOpen, true);
});
test('Ponte distingue rejeição do serviço de timeout com resultado desconhecido', async () => {
  let f = fixture(); let response = f.request('pdv.checkout');
  f.receive({ version: 1, id: 'contract-1', ok: false, error: 'Estoque insuficiente' });
  await assert.rejects(response, error => error.code === 'REJECTED' && error.message === 'Estoque insuficiente');
  f = fixture(); response = f.request('pdv.checkout'); f.timeout();
  await assert.rejects(response, error => error.code === 'TIMEOUT');
});
