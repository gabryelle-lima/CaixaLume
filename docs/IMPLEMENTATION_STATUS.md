# Estado do CaixaLume

Atualizado em 2026-10-24 para a versão 0.1.2, destinada ao primeiro piloto controlado.

## Pronto no código

- Aplicativo Windows x64 com interface local WebView2 e SQLite local. Após o login, visão geral, PDV, produtos, estoque, caixa, vendas, despesas, relatórios, fornecedores, configurações e usuários compartilham a mesma navegação.
- Venda, itens, baixa de estoque, movimentação do caixa e auditoria são gravados em transação. O identificador da operação protege o checkout contra confirmação duplicada.
- Abertura e fechamento de caixa, suprimento, sangria, despesas, histórico, cancelamento autorizado de venda, cadastro e movimentação de estoque.
- Relatórios por período com impressão e exportação PDF/Excel, sujeitos aos recursos instalados no Windows.
- Backup consistente do SQLite, verificação de integridade, cópia de segurança antes da restauração e snapshot antes das migrações de esquema. As imagens locais ficam fora do `.db` e precisam de cópia separada para recuperação em outro computador.
- Banco de dados fora da pasta do programa, em `%LOCALAPPDATA%\MercadinhoPdv\dados\mercadinho.db`. Atualizar o executável não substitui o banco.
- Código e pacotes em repositório GitHub privado. Publicação versionada em ZIP, com instalador por usuário e lançador separado para piloto.

## Limites da primeira versão de teste

- O piloto é para um computador e operação offline. Download e atualização do pacote ainda exigem acesso ao repositório privado; não há atualização automática.
- Pix, débito e crédito registram a forma informada pelo operador; não há verificação bancária ou integração com maquininha.
- Não há emissão fiscal NFC-e/NF-e, integração SEFAZ, SAT/MFE ou TEF. A liberação para operação comercial depende de revisão fiscal e do ambiente da loja.
- Consulta ao Open Food Facts é opcional e depende de internet e dos dados do catálogo. O cadastro manual funciona sem essa consulta.
- Ainda falta a aceitação prática na máquina da loja: abrir e reiniciar, navegar entre módulos, vender, fechar caixa, conferir relatórios, restaurar uma cópia de teste e verificar impressora/leitor/balança, se usados.

O roteiro e os critérios para aprovar o piloto estão em [PILOTO_0.1.2.md](PILOTO_0.1.2.md). Não use a base real para ensaiar restauração ou limpeza de dados.
