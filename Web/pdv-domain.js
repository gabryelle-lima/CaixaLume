(function (root) {
  'use strict';
  function cents(value) {
    const result = Math.round((Number(value) + Number.EPSILON) * 100);
    if (!Number.isSafeInteger(result) || result < 0) throw new Error('Valor monetário inválido.');
    return result;
  }
  function quantity(text, unit, stock) {
    const value = String(text).trim();
    if (!/^\d+(?:[.,]\d{1,3})?$/.test(value)) throw new Error('Informe a quantidade com até três casas decimais.');
    const number = Number(value.replace(',', '.'));
    if (!Number.isFinite(number) || number <= 0 || number > 100000) throw new Error('Informe uma quantidade positiva de até 100.000.');
    const mills = Math.round(number * 1000);
    if (unit === 'UN' && mills % 1000 !== 0) throw new Error('Produtos UN exigem quantidade inteira.');
    if (stock !== undefined && mills > Math.round(stock * 1000)) throw new Error('Estoque disponível: ' + stock + ' ' + unit + '.');
    return mills / 1000;
  }
  function itemCents(item) {
    const mills = Math.round(quantity(item.quantity, item.product.unit) * 1000);
    return Number((BigInt(cents(item.product.price)) * BigInt(mills) + 500n) / 1000n);
  }
  function resolveProduct(products, query) {
    const text = String(query).trim();
    if (!text) return { kind: 'empty' };
    const active = products.filter(p => p.active !== false);
    const byCode = active.filter(p => String(p.barcode || '') === text);
    let matches = byCode.length ? byCode : active.filter(p => p.name.toLocaleLowerCase('pt-BR') === text.toLocaleLowerCase('pt-BR'));
    if (!matches.length && /^\d+$/.test(text)) matches = active.filter(p => String(p.id) === text);
    if (!matches.length && !/^\d+$/.test(text)) matches = active.filter(p => p.name.toLocaleLowerCase('pt-BR').includes(text.toLocaleLowerCase('pt-BR')));
    return matches.length === 1 ? { kind: 'found', product: matches[0] } : { kind: matches.length ? 'ambiguous' : 'notFound' };
  }
  function totals(items, adjustment = {}, policy = {}, role = 'OPERADOR') {
    const subtotal = items.reduce((sum, item) => sum + itemCents(item), 0);
    if (!Number.isSafeInteger(subtotal)) throw new Error('O total excede o limite seguro.');
    const value = Number(adjustment.desconto || 0), extra = cents(adjustment.acrescimo || 0);
    const type = adjustment.tipoDesconto || 'VALOR';
    if (!Number.isFinite(value) || value < 0 || !['VALOR', 'PERCENTUAL'].includes(type)) throw new Error('Ajuste inválido.');
    if (type === 'PERCENTUAL' && (value > 100 || Math.abs(value * 100 - Math.round(value * 100)) > 1e-7)) throw new Error('Percentual inválido.');
    const percent = (amount, rate) => Number((BigInt(amount) * BigInt(Math.round(rate * 100)) + 5000n) / 10000n);
    const discount = type === 'PERCENTUAL' ? percent(subtotal, value) : cents(value);
    if ((discount || extra) && (!policy.habilitada || (role !== 'ADMINISTRADOR' && !policy.permitirOperador))) throw new Error('Ajustes desabilitados. O administrador deve definir a política.');
    if (discount > subtotal) throw new Error('O desconto supera o subtotal.');
    if (discount > percent(subtotal, Number(policy.maxDescontoPercentual || 0)) || extra > cents(policy.maxAcrescimoValor || 0)) throw new Error('O ajuste ultrapassa o limite definido pelo administrador.');
    return { subtotal: subtotal / 100, discount: discount / 100, surcharge: extra / 100, total: (subtotal - discount + extra) / 100 };
  }
  const api = Object.freeze({ cents, quantity, itemCents, resolveProduct, totals });
  root.MercadoPdv = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof window === 'undefined' ? globalThis : window);
