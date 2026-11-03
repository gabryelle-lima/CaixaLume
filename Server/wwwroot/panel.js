(() => {
'use strict';
const $=id=>document.getElementById(id);
let csrf='',challenge='',resetFactor=false;
function notice(message){$('status').textContent=message;}
function screen(id){for(const name of ['setup','factor','recovery','login','dashboard'])$(name).hidden=name!==id;$('account-bar').hidden=id!=='dashboard';}
function clearSecrets(){for(const id of ['bootstrap-token','setup-password','setup-repeat','login-password','login-code','factor-code','current-password','new-password','new-repeat','password-code','reset-password','factor-secret'])$(id).value='';$('recovery-codes').textContent='';$('invite').textContent='';$('invite').hidden=true;challenge='';}
async function api(path,method='GET',body){
 const headers={'Content-Type':'application/json'};
 if(method!=='GET'){headers['X-CaixaLume-Request']='1';if(csrf)headers['X-CSRF-Token']=csrf;}
 let r;try{r=await fetch(path,{method,headers,body:body===undefined?undefined:JSON.stringify(body),cache:'no-store',credentials:'same-origin'});}catch{throw new Error('Não foi possível conectar. Confira se o servidor e o túnel SSH estão ativos.');}
 if(!r.ok){let data;try{data=await r.json();}catch{}if(r.status===401&&path.startsWith('/admin')){clearSecrets();$('stores').replaceChildren();csrf='';screen('login');}
 const error=new Error(data?.message||(r.status===401?'Acesso recusado. Confira os dados e o código. Após várias tentativas, aguarde 5 minutos.':r.status===429?'Muitas tentativas. Aguarde um minuto.':r.status===403?'Acesso recusado. Abra o painel pelo túnel SSH e atualize a página.':`Não foi possível concluir (HTTP ${r.status}).`));error.status=r.status;throw error;}
 return r.status===204?null:r.json();
}
async function guarded(fn,form){const buttons=form?[...form.querySelectorAll('button')]:[];buttons.forEach(b=>b.disabled=true);notice('Aguarde…');try{await fn();if($('status').textContent==='Aguarde…')notice('Concluído.');}catch(e){notice(e.message||'Não foi possível conectar ao servidor.');}finally{buttons.forEach(b=>b.disabled=false);}}
function bind(id,fn){$(id).addEventListener('submit',event=>{event.preventDefault();if($(id).dataset.busy)return;$(id).dataset.busy='1';void guarded(fn,$(id)).finally(()=>delete $(id).dataset.busy);});}
async function session(){const data=await api('/auth/session');csrf=data.csrf;$('account-name').textContent=data.user;return data;}
async function showDashboard(){await session();screen('dashboard');await load();}
function showFactor(data,reset){challenge=data.challenge;resetFactor=reset;$('factor-secret').value=data.secret;$('factor-label').value='CaixaLume';$('factor-code').value='';screen('factor');$('factor-code').focus();notice('Adicione a chave ao seu autenticador e confirme o código.');}
function recovery(codes){clearSecrets();$('recovery-codes').textContent=codes.join('\n');screen('recovery');notice('Acesso configurado. Guarde os códigos antes de continuar.');}
bind('setup-form',async()=>{if($('setup-password').value!==$('setup-repeat').value)throw new Error('As senhas não coincidem.');const data=await api('/auth/setup/start','POST',{bootstrapToken:$('bootstrap-token').value,login:$('setup-login').value,password:$('setup-password').value});$('bootstrap-token').value='';$('setup-password').value='';$('setup-repeat').value='';showFactor(data,false);});
bind('factor-form',async()=>{const data=await api(resetFactor?'/auth/security/totp/finish':'/auth/setup/finish','POST',{challenge,code:$('factor-code').value.trim()});await session();recovery(data.recoveryCodes);});
$('factor-cancel').onclick=()=>{clearSecrets();void guarded(init);};
$('recovery-done').onclick=()=>{$('recovery-codes').textContent='';void guarded(showDashboard);};
bind('login-form',async()=>{const data=await api('/auth/login','POST',{login:$('login-name').value,password:$('login-password').value,code:$('login-code').value.trim(),remember:$('remember').checked});csrf=data.csrf;clearSecrets();await showDashboard();});
async function logout(all=false){await api(all?'/auth/security/logout-all':'/auth/logout','POST',{});csrf='';clearSecrets();$('stores').replaceChildren();screen('login');notice('Você saiu do painel.');}
$('logout').onclick=()=>guarded(()=>logout());$('logout-all').onclick=()=>{if(confirm('Encerrar todas as sessões do painel, incluindo esta?'))void guarded(()=>logout(true));};
async function load(){const rows=await api('/admin/stores');$('stores').replaceChildren();if(!rows.length){const p=document.createElement('p');p.textContent='Nenhum mercado cadastrado. Use o formulário acima para cadastrar o primeiro.';$('stores').append(p);return;}
 const groups=new Map();for(const row of rows){if(!groups.has(row.establishmentId))groups.set(row.establishmentId,[]);groups.get(row.establishmentId).push(row);}
 for(const [id,devices] of groups){const article=document.createElement('article'),title=document.createElement('h3'),info=document.createElement('p');title.textContent=devices[0].name;info.textContent='Identificação: '+id;info.className='muted';article.append(title,info);
 const invite=document.createElement('button');invite.textContent='Gerar código de vínculo';invite.onclick=()=>guarded(async()=>{invite.disabled=true;try{const r=await api(`/admin/stores/${id}/invites`,'POST',{});$('invite').hidden=false;$('invite').textContent=`Código de uso único: ${r.code}\nVálido até ${new Date(r.expires).toLocaleString('pt-BR')}. Informe ao administrador deste mercado e use em até 30 minutos.`;}finally{invite.disabled=false;}});article.append(invite);
 for(const row of devices){const detail=document.createElement('div');detail.className='installation';const p=document.createElement('p');p.textContent=row.installationId?`Instalação: ${row.installationId} · Versão: ${row.version||'Ainda não informada'} · Último contato: ${row.lastSeen?new Date(row.lastSeen).toLocaleString('pt-BR'):'Aguardando primeiro contato'} · ${row.revoked?'Revogada':'Vínculo habilitado'}`:'Nenhuma instalação vinculada.';detail.append(p);
 if(row.installationId&&!row.revoked){const revoke=document.createElement('button');revoke.textContent='Revogar instalação';revoke.className='danger';revoke.onclick=()=>{if(confirm('Revogar a comunicação desta instalação? O caixa continuará localmente.'))void guarded(async()=>{await api(`/admin/installations/${row.installationId}/revoke`,'POST',{});await load();});};detail.append(revoke);}article.append(detail);}$('stores').append(article);}
}
$('load').onclick=()=>guarded(load);
bind('store-form',async()=>{await api('/admin/stores','POST',{name:$('name').value});$('name').value='';await load();});
bind('password-form',async()=>{if($('new-password').value!==$('new-repeat').value)throw new Error('As senhas não coincidem.');await api('/auth/security/password','POST',{currentPassword:$('current-password').value,newPassword:$('new-password').value,code:$('password-code').value.trim()});clearSecrets();await session();notice('Senha alterada. As outras sessões foram encerradas.');});
bind('reset-form',async()=>{const data=await api('/auth/security/totp/start','POST',{password:$('reset-password').value});$('reset-password').value='';showFactor(data,true);});
async function init(){const status=await api('/auth/status');if(!status.configured){screen('setup');notice('Configure seu acesso uma única vez.');return;}try{await showDashboard();notice('Sessão restaurada.');}catch(e){if(e.status!==401)throw e;csrf='';screen('login');notice('Entre com sua conta para continuar.');}}
void guarded(init);
})();
