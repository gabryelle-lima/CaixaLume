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
    host.append(toast); window.setTimeout(() => toast.remove(), 3600);
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
  window.MercadoUi = Object.freeze({ element, currency, notify });
})();
