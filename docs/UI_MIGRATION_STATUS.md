# Interface do CaixaLume

Atualizado em 2026-10-24.

## Padrão atual

O aplicativo mantém uma janela principal WebView2 com navegação integrada. Visão geral, ponto de venda, produtos, estoque, caixa, vendas, despesas, relatórios, fornecedores, configurações e usuários usam HTML/CSS/JavaScript local e a mesma ponte C# para os serviços e o SQLite. A janela de login é WinForms. Formulários nativos ainda existem no código para compatibilidade e manutenção, mas a navegação principal não os abre como módulos paralelos.

Os recursos Web são carregados da subpasta `Web` da publicação. A falha de WebView2 ou de carregamento da página é exibida ao usuário e registrada no log; o programa não troca silenciosamente para um PDV antigo. O carrinho e a operação de pagamento devem ser conferidos após qualquer interrupção durante uma gravação.

## O que a interface cobre

- Produtos: cadastro, busca, filtros, prévia e remoção de imagem, estoque e código de barras.
- PDV: busca, carrinho, UN/KG, descontos e acréscimos autorizados, caixa e pagamento em dinheiro/Pix/débito/crédito.
- Gestão: estoque, caixa, vendas, despesas, fornecedores, configurações, usuários e relatórios na navegação principal.
- Relatórios: leitura por período, impressão e exportação PDF/Excel.

As páginas são locais e funcionam sem rede. A consulta de catálogo online e o download de versões privadas precisam de internet. A aceitação visual em resoluções, escalas e equipamentos reais continua no [roteiro do piloto](PILOTO_0.1.2.md).
