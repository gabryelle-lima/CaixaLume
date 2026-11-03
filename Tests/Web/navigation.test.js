const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const web=path.join(__dirname,'../../Web');
const pages=['home','index','products','reports'];
function menu(page){const html=fs.readFileSync(path.join(web,page+'.html'),'utf8');return html.match(/<aside\b[\s\S]*?<\/aside>/)[0];}
function fixture(){
 const buttons=pages.map(page=>({page,hidden:true,disabled:true,title:'',setAttribute(name,value){this[name]=value;}}));
 const document={addEventListener(){},querySelectorAll(selector){assert.match(selector,/invoices/);return buttons;}};
 const window={};vm.runInNewContext(fs.readFileSync(path.join(web,'ui.js'),'utf8'),{window,document});
 return {buttons,update:window.MercadoUi.updateInvoiceNavigation};
}
test('Todas as telas estáticas possuem Notas fiscais na mesma posição do menu de gestão',()=>{
 const source=fs.readFileSync(path.join(web,'modules.js'),'utf8');
 const nav=vm.runInNewContext(source.match(/const nav = (\[[^\n]+);/)[1]);
 const expected=Array.from(nav,entry=>entry[0]);
 for(const page of pages){const html=menu(page);const entries=Array.from(html.matchAll(/data-(?:page|module)="([^"]+)"/g),m=>m[1]);assert.deepEqual(entries,expected,page);}
});
test('Item de Notas fiscais inicia oculto e desabilitado antes de conhecer o perfil',()=>{
 for(const page of pages){const tag=menu(page).match(/<button\b[^>]*data-module="invoices"[^>]*>/)[0];assert.match(tag,/\bhidden\b/);assert.match(tag,/\bdisabled\b/);assert.match(tag,/aria-label="Notas fiscais"/);}
});
test('Administrador mantém Notas fiscais visível ao alternar gestão, produtos e outras telas',()=>{
 const {buttons,update}=fixture();for(let cycle=0;cycle<3;cycle++)for(const page of pages){update('ADMINISTRADOR');assert.equal(buttons.find(b=>b.page===page).hidden,false);assert.equal(buttons.find(b=>b.page===page).disabled,false);}
});
test('Operador e perfil desconhecido não recebem acesso ao trocar de tela',()=>{
 const {buttons,update}=fixture();update('ADMINISTRADOR');
 for(const role of ['OPERADOR',undefined,'']){update(role);for(const button of buttons){assert.equal(button.hidden,true);assert.equal(button.disabled,true);}}
 update('ADMINISTRADOR');for(const button of buttons)assert.equal(button.hidden,false);
});
