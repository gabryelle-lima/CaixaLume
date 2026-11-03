# Correção: Notas fiscais desaparecia ao abrir Produtos

29/10/2026.

A causa era a existência de menus separados: `modules.js` construía um menu incluindo Notas fiscais, enquanto `home.html`, `index.html`, `products.html` e `reports.html` possuíam menus estáticos sem esse item. Ao abrir Produtos, o aplicativo trocava de iframe e mostrava o menu incompleto.

Correção:

- Inserido Notas fiscais após Vendas nas quatro páginas estáticas, na mesma ordem do menu de gestão.
- Compartilhada a atualização de visibilidade em `MercadoUi.updateInvoiceNavigation(role)`, chamada após carregar o perfil nas cinco telas.
- Item inicia oculto e desabilitado; aparece somente com perfil ADMINISTRADOR confirmado. Operador ou perfil desconhecido mantém o item oculto/desabilitado.
- Autorização do backend, regras de importação, financeiro e banco permaneceram inalterados.
- Acrescentado rótulo acessível para identificar o ícone quando a barra lateral está compacta.

Validação: compilação sem erros/avisos, 16 testes JavaScript (quatro novos de navegação), sintaxe de todos os scripts Web e 13 cenários de segurança aprovados. Logs em `docs/validation-menu/`. As outras baterias C# não foram repetidas nesta correção de menu.

Os novos testes comparam todos os destinos dos menus estáticos com o menu de gestão e verificam ocultação inicial, alternância entre telas para administrador e revogação visual para operador/perfil desconhecido. Não substituem inspeção visual no Windows, que continua pendente.

Arquivos alterados: `Web/home.html`, `index.html`, `products.html`, `reports.html`, `home.js`, `app.js`, `products.js`, `reports.js`, `modules.js`, `ui.js` e `Tests/Web/navigation.test.js`.

A compilação copiou os recursos corrigidos para `bin/Debug/net10.0-windows/Web`. Feche e reabra o executável dessa pasta para descartar páginas antigas que já estejam carregadas. O instalador anterior não é atualizado automaticamente.
