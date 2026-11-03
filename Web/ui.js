(() => {
  const element = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  };
  const currencyFormatter = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });
  const currency = value => currencyFormatter.format(value || 0);
  const notify = (host, message, error = false) => {
    const toast = element('div', `toast${error ? ' error' : ''}`, message);
    toast.setAttribute('role',error?'alert':'status');host.append(toast);while(host.children.length>4)host.firstElementChild.remove();window.setTimeout(() => toast.remove(), error?10000:4500);
  };
  document.addEventListener('input', event => {
    const input = event.target;
    if (!(input instanceof HTMLInputElement || input instanceof HTMLTextAreaElement)) return;
    if (input instanceof HTMLInputElement && (input.type !== 'text' && input.type !== 'search')) return;
    if (input.inputMode === 'numeric' || input.inputMode === 'decimal' || input.dataset.preserveCase === 'true') return;
    const upper = input.value.toLocaleUpperCase('pt-BR');
    if (upper === input.value) return;
    const start = input.selectionStart, end = input.selectionEnd;
    input.value = upper;
    if (start !== null && end !== null) input.setSelectionRange(start, end);
  }, true);
  function updateInvoiceNavigation(role){
    const allowed=role==='ADMINISTRADOR';
    document.querySelectorAll('[data-module="invoices"],[data-target="invoices"]').forEach(button=>{
      button.hidden=!allowed;button.disabled=!allowed;
      button.title=allowed?'Notas fiscais':'Disponível para administradores';
      button.setAttribute('aria-label','Notas fiscais');
    });
  }
  let confirming=false;
  function confirm(message,{title='Confirmar operação',destructive=false,accept='Confirmar'}={}) {
    if(confirming)return Promise.resolve(false);
    confirming=true;const previous=document.activeElement;
    return new Promise(resolve=>{
      const dialog=element('dialog','modal ui-confirm');dialog.setAttribute('aria-labelledby','ui-confirm-title');
      const card=element('section','modal-card'),heading=element('h2','',title);heading.id='ui-confirm-title';
      const body=element('p','modal-description',message),actions=element('div','ui-confirm-actions');
      const cancel=element('button','button button-secondary','Voltar'),ok=element('button',destructive?'button button-danger':'button button-primary',accept);
      cancel.type=ok.type='button';actions.append(cancel,ok);card.append(heading,body,actions);dialog.append(card);document.body.append(dialog);
      let accepted=false;cancel.onclick=()=>dialog.close();ok.onclick=()=>{accepted=true;dialog.close();};
      dialog.addEventListener('close',()=>{dialog.remove();confirming=false;previous?.isConnected&&previous.focus();resolve(accepted);},{once:true});
      dialog.showModal();cancel.focus();
    });
  }
  document.addEventListener('close',event=>{
    if(event.target instanceof HTMLDialogElement && event.target.id==='payment-modal')document.getElementById('product-search')?.focus();
  },true);
  document.addEventListener('keydown',event=>{
    if(event.key==='F2' && document.querySelector('dialog[open]'))event.stopImmediatePropagation();
    if(event.key==='/' && !event.ctrlKey && !event.altKey && !event.metaKey && !document.querySelector('dialog[open]') && !event.target.closest?.('input,textarea,select,[contenteditable]')){
      const search=document.querySelector('input[type="search"]');if(search){event.preventDefault();search.focus();search.select();}
    }
  },true);
  window.MercadoUi = Object.freeze({ element, currency, notify, confirm, updateInvoiceNavigation });
})();
