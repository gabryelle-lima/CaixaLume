const test=require('node:test');
const assert=require('node:assert/strict');
const input=require('../../Web/ui-input.js');
const domain=require('../../Web/pdv-domain.js');
test('Valores monetários aceitam vírgula, ponto e agrupamento brasileiro explícito',()=>{
  for(const [text,value] of [['14,27',14.27],['14.27',14.27],['1.234,56',1234.56],['R$ 14,27',14.27],['0',0]])assert.equal(input.decimal(text),value);
});
test('Valores ambíguos ou inválidos não são reinterpretados silenciosamente',()=>{
  for(const text of ['1.234','1,234','12.34,56','1,234.56','1..2','1e3','-10','14,','14.','', '1 234,56','Infinity','1000000001'])assert.throws(()=>input.decimal(text),text);
});
test('Precisão de quantidade e campo inteiro são explícitos',()=>{
  assert.equal(input.decimal('0,315',3),.315);assert.equal(input.decimal('0.315',3),.315);assert.equal(input.decimal('3',0),3);
  assert.throws(()=>input.decimal('3,5',0));assert.throws(()=>input.decimal('0,3151',3));
});
test('Busca ignora acento e caixa sem confundir código de barras com outro produto',()=>{
  const products=[{id:1,name:'CAFÉ TORRADO',barcode:'0012345'},{id:2,name:'Cafe solúvel',barcode:'9988776'}];
  assert.equal(domain.resolveProduct(products,'cafe torrado').product.id,1);
  assert.equal(domain.resolveProduct(products,'CAFE').kind,'ambiguous');
  assert.equal(domain.resolveProduct(products,'0012345').product.id,1);
  assert.equal(domain.resolveProduct(products,'0012346').kind,'notFound');
  assert.equal(input.normalizeSearch(' AÇÚCAR '),'acucar');
});
