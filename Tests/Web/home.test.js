const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const web=path.join(__dirname,'../../Web');
class Node {
  constructor(){this.textContent='';this.hidden=false;this.disabled=false;this.children=[];this.listeners={};this.classList={toggle(){}};this.firstChild={textContent:''};}
  append(...nodes){this.children.push(...nodes);} replaceChildren(...nodes){this.children=nodes;}
  setAttribute(key,value){this[key]=value;} addEventListener(event,fn){this.listeners[event]=fn;}
  querySelector(){return new Node();}
}
function fixture(){
  const nodes=new Map(),pending=[],calls=[],events={};
  const invoice=new Node(),restricted=[new Node(),new Node()];invoice.hidden=true;invoice.disabled=true;
  const document={title:'',addEventListener(){},createElement(){return new Node();},createTextNode(text){const n=new Node();n.textContent=text;return n;},
    getElementById(id){if(!nodes.has(id))nodes.set(id,new Node());return nodes.get(id);},
    querySelector(){return new Node();},
    querySelectorAll(selector){if(selector.includes('invoices'))return [invoice];if(selector.includes('expenses'))return restricted;return [];}};
  document.getElementById('user-label').textContent='Operador';document.getElementById('user-role').textContent='OPERADOR';
  const window={addEventListener(name,fn){events[name]=fn;},MercadoBridge:{request(action){calls.push(action);return new Promise((resolve,reject)=>pending.push({resolve,reject}));},metric(){},navigate(){}}};
  const context=vm.createContext({window,document,performance:{now:()=>0}});
  vm.runInContext(fs.readFileSync(path.join(web,'ui.js'),'utf8'),context);
  vm.runInContext(fs.readFileSync(path.join(web,'home.js'),'utf8'),context);
  return {nodes,invoice,restricted,pending,calls,events};
}
const data=(name='Gabryelle',role='ADMINISTRADOR')=>({userName:name,userRole:role,storeName:'LOJA TESTE',revenue:25,salesToday:2,averageTicket:12.5,lowStockCount:0,cashOpen:false,totalSales:2,recentSales:[],expired:[],lowStock:[],expiring:[]});
const settle=async()=>{await Promise.resolve();await Promise.resolve();};
test('Visão geral aguarda a resposta antes de ler perfil e completa a carga inicial',async()=>{
 const f=fixture();assert.deepEqual(f.calls,['dashboard.getData']);assert.equal(f.invoice.hidden,true);
 f.pending.shift().resolve(data());await settle();
 assert.equal(f.nodes.get('dashboard-error').hidden,true);assert.equal(f.nodes.get('user-label').textContent,'Gabryelle');assert.equal(f.nodes.get('user-role').textContent,'ADMINISTRADOR');assert.equal(f.nodes.get('sales-today').textContent,'2');assert.equal(f.invoice.hidden,false);
});
test('Falha de carregamento permite tentar novamente sem erro de variável não inicializada',async()=>{
 const f=fixture();assert.equal(f.pending.length,1);f.pending.shift().reject(new Error('Falha de comunicação de teste'));await settle();
 assert.equal(f.nodes.get('dashboard-error-text').textContent,'Falha de comunicação de teste');
 const retry=f.nodes.get('retry-dashboard').listeners.click();assert.equal(f.calls.length,2);f.pending.shift().resolve(data());await retry;
 assert.equal(f.nodes.get('dashboard-error').hidden,true);assert.equal(f.invoice.hidden,false);
});
test('Novas telas de login não reutilizam nome e permissões do usuário anterior',async()=>{
 const admin=fixture();assert.equal(admin.pending.length,1);admin.pending.shift().resolve(data());await settle();assert.equal(admin.invoice.hidden,false);
 const operator=fixture();assert.equal(operator.invoice.hidden,true);operator.pending.shift().resolve(data('Mercado','OPERADOR'));await settle();assert.equal(operator.nodes.get('user-label').textContent,'Mercado');assert.equal(operator.invoice.hidden,true);assert.equal(operator.invoice.disabled,true);
 const again=fixture();again.pending.shift().resolve(data('Gabryelle','ADMINISTRADOR'));await settle();assert.equal(again.nodes.get('user-label').textContent,'Gabryelle');assert.equal(again.invoice.hidden,false);
});
test('Reativar uma tela já carregada atualiza dados e permissões recebidas',async()=>{
 const f=fixture();assert.equal(f.pending.length,1);f.pending.shift().resolve(data());await settle();
 f.events['mercado:activate']({detail:{dirty:true}});assert.equal(f.calls.length,2);f.pending.shift().resolve(data('Outro operador','OPERADOR'));await settle();
 assert.equal(f.nodes.get('user-label').textContent,'Outro operador');assert.equal(f.nodes.get('user-role').textContent,'OPERADOR');assert.equal(f.invoice.hidden,true);assert.equal(f.restricted.every(b=>b.disabled),true);
});
