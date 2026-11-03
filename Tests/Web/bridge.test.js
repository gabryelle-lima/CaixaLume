const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const source = fs.readFileSync(require('node:path').join(__dirname, '../../Web/bridge.js'), 'utf8');
function fixture() {
  let receive, sent, timeout, nextId=0;
  const window = { chrome: { webview: { postMessage: message => { sent = message; }, addEventListener: (_, handler) => { receive = handler; } } },
    setTimeout: callback => { timeout = callback; return 1; }, clearTimeout: () => {} };
  window.parent=window;window.addEventListener=()=>{};
  vm.runInNewContext(source, { window, performance:{now:()=>0},location:{pathname:'/index.html',origin:'https://test.local'},requestAnimationFrame:()=>{},crypto: { randomUUID: () => 'contract-'+(++nextId) }, Map, Promise, Error });
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
  await assert.rejects(response, error => error.code === 'UNKNOWN_OUTCOME');
});

test('Consultas de dados iguais em andamento compartilham uma requisição e podem atualizar depois',async()=>{
  const f=fixture(),first=f.request('products.getData'),second=f.request('products.getData');
  assert.equal(first,second);assert.equal(f.sent().id,'contract-1');
  f.receive({version:1,id:'contract-1',ok:true,data:{}});await first;
  const next=f.request('products.getData');assert.notEqual(first,next);
  const id=f.sent().id;f.receive({version:1,id,ok:true,data:{}});await next;
});
test('Operações de venda não são agrupadas pelo cache de leitura',async()=>{
  const f=fixture();const first=f.request('pdv.checkout');const id1=f.sent().id;const second=f.request('pdv.checkout');const id2=f.sent().id;
  assert.notEqual(first,second);assert.notEqual(id1,id2);
  f.receive({version:1,id:id1,ok:true,data:{saleId:1}});f.receive({version:1,id:id2,ok:true,data:{saleId:1}});await Promise.all([first,second]);
});
