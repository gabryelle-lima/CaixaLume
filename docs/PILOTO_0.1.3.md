# CaixaLume 0.1.3 — piloto de desempenho e uso diário

Esta versão continua destinada a **um computador**, com vendas e banco locais. O nome inicial da loja é **Mercado** e pode ser alterado pelo administrador. O pacote e o código permanecem no repositório privado.

## O que mudou

- A navegação mantém as telas carregadas durante a sessão. Ao voltar ao PDV, o carrinho em andamento continua disponível; telas sem alterações pendentes são reutilizadas.
- A tela inicial identifica os passos para preparar uma loja nova: produtos, caixa e primeira venda.
- A finalização consulta no banco somente os produtos presentes no carrinho, sem reler todo o catálogo.
- Um código não encontrado pode ser cadastrado e retornar à venda, incluindo o item quando houver estoque.
- A identidade CaixaLume aparece nas telas, com o nome da loja apresentado separadamente.

## Instalação

Feche o aplicativo. Baixe o ZIP privado `CaixaLume-0.1.3-win-x64.zip`, extraia **todos** os arquivos em uma pasta nova e execute `Instalar-CaixaLume.cmd`. O instalador cria uma cópia do banco existente antes de apontar o atalho para a nova versão. O banco não vem no ZIP e permanece em `%LOCALAPPDATA%\MercadinhoPdv\dados\mercadinho.db`.

Para avaliar com dados descartáveis, execute `Iniciar-Piloto.cmd` dentro da pasta instalada. Ele usa uma base separada em `%LOCALAPPDATA%\CaixaLume\piloto\mercado-teste.db`. O atalho **CaixaLume** abre a base normal. Não copie a base real para o piloto.

## O que observar na máquina de uso

1. Abra o programa e registre quanto demora até a tela inicial ficar pronta. Entre em PDV, Produtos, Caixa e Relatórios; volte a cada tela e observe se a segunda abertura é mais rápida.
2. Adicione itens ao carrinho, visite Produtos e volte ao PDV. Confirme que os itens continuam no carrinho e que preços e estoque são atualizados antes de concluir a venda.
3. Cadastre um produto, abra o caixa e faça uma venda de teste. Confira que venda, estoque e caixa foram atualizados uma única vez.
4. Confira a tela inicial em uma loja nova, depois de cadastrar produtos, abrir o caixa e concluir a primeira venda.
5. Repita as tarefas usuais com o leitor de código de barras e a resolução/escala de tela usadas na loja. Confira impressão, PDF/Excel e periféricos aplicáveis.
6. Faça backup e restaure apenas uma **cópia de teste**. Guarde as imagens locais separadamente do arquivo `.db`.

Registre falhas com horário, versão, tela, passos e resultado esperado. O log fica em `%LOCALAPPDATA%\MercadinhoPdv\logs\application.log`. O roteiro detalhado está em `VALIDACAO_MANUAL.md`.

## Limites

A aceitação prática nesta máquina ainda é necessária. Pix e cartões são registros locais: confirme o recebimento no banco ou na maquininha antes de registrar a venda. Não há emissão fiscal NFC-e/NF-e, TEF ou conciliação automática. A consulta ao Open Food Facts é opcional e exige internet.
