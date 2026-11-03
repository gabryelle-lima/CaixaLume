(function(root){
  'use strict';
  function normalizeSearch(value) { return String(value ?? '').normalize('NFD').replace(/[\u0300-\u036f]/g,'').toLocaleLowerCase('pt-BR').trim(); }
  function decimal(value, places=2, maximum=1000000000) {
    let text=String(value ?? '').trim().replace(/^R\$\s*/, '');
    if (places===0 && /[.,]/.test(text)) throw new Error('Informe uma quantidade inteira.');
    if (text.includes(',') && text.includes('.')) {
      if (!new RegExp('^\\d{1,3}(?:\\.\\d{3})+,\\d{1,'+places+'}$').test(text)) throw new Error('Use vírgula ou ponto para decimais, sem separadores ambíguos.');
      text=text.replace(/\./g,'');
    }
    const pattern=places===0 ? /^\d+$/ : new RegExp('^\\d+(?:[.,]\\d{1,'+places+'})?$');
    if (!pattern.test(text)) throw new Error(places===0 ? 'Informe uma quantidade inteira.' : 'Informe um valor com até '+places+' casas decimais. Exemplo: '+(places===3?'0,315':'14,27')+'.');
    const number=Number(text.replace(',','.'));
    if (!Number.isFinite(number) || number>maximum) throw new Error('Valor acima do limite permitido.');
    return number;
  }
  const api=Object.freeze({ decimal, normalizeSearch });root.MercadoInput=api;
  if(typeof module!=='undefined'&&module.exports)module.exports=api;
})(typeof window==='undefined'?globalThis:window);
