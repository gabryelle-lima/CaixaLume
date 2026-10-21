const test = require('node:test');
const assert = require('node:assert/strict');
const domain = require('../../Web/pdv-domain.js');
const products = [
  { id: 1, name: 'Leite', barcode: '7891234567890', unit: 'UN', stock: 5, price: 4.50 },
  { id: 2, name: 'Leite desnatado', barcode: '7891234567891', unit: 'UN', stock: 3, price: 5 },
  { id: 3, name: 'Inativo', barcode: '12345678', active: false, stock: 1 },
  { id: 4, name: 'Carne', barcode: '7899876543210', unit: 'KG', stock: 5, price: 19.90 }
];
test('Código exato tem prioridade; ausente e ambíguo não selecionam outro produto', () => {
  assert.equal(domain.resolveProduct(products, '7891234567890').product.id, 1);
  assert.equal(domain.resolveProduct(products, '7891234567899').kind, 'notFound');
  assert.equal(domain.resolveProduct(products, '12345678').kind, 'notFound');
  assert.equal(domain.resolveProduct(products, 'leite').product.id, 1);
  assert.equal(domain.resolveProduct(products, 'leit').kind, 'ambiguous');
  assert.equal(domain.resolveProduct([...products, { ...products[0], id: 5 }], '7891234567890').kind, 'ambiguous');
});
test('Quantidade aceita vírgula e ponto, precisão de 1 g e rejeita UN fracionada', () => {
  for (const [text, value] of [['0,125', .125], ['0.315', .315], ['0,750', .750], ['1.235', 1.235], ['2,500', 2.500]]) {
    assert.equal(domain.quantity(text, 'KG', 5), value);
  }
  for (const text of ['0', '-1', '1,2345', '1.2.3', 'NaN']) assert.throws(() => domain.quantity(text, 'KG', 5));
  assert.throws(() => domain.quantity('1,5', 'UN', 5));
  assert.throws(() => domain.quantity('6', 'UN', 5));
});
test('Subtotal, desconto percentual e acréscimo usam centavos iguais ao contrato C#', () => {
  const totals = domain.totals([{ product: products[3], quantity: .315 }], { tipoDesconto: 'PERCENTUAL', desconto: 10, acrescimo: .50 },
    { habilitada: true, maxDescontoPercentual: 20, maxAcrescimoValor: 5 }, 'ADMINISTRADOR');
  assert.deepEqual(totals, { subtotal: 6.27, discount: .63, surcharge: .50, total: 6.14 });
  assert.equal(domain.itemCents({ product: products[3], quantity: 1.235 }), 2458);
  assert.equal(domain.itemCents({ product: { unit: 'UN', price: 14.273 }, quantity: 2 }), 2854);
});
test('Política indefinida, limite e autorização bloqueiam ajustes', () => {
  const items = [{ product: products[0], quantity: 2 }];
  assert.throws(() => domain.totals(items, { desconto: 1 }));
  assert.throws(() => domain.totals(items, { desconto: 1 }, { habilitada: true, maxDescontoPercentual: 20 }, 'OPERADOR'));
  assert.throws(() => domain.totals(items, { desconto: 10 }, { habilitada: true, maxDescontoPercentual: 100 }, 'ADMINISTRADOR'));
  assert.equal(domain.totals(items).total, 9);
});
