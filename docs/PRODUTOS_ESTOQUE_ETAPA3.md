# Etapa 3 — produtos, estoque e consultas SQLite

Validação em 25/10/2026. Compilação .NET 10 concluída com zero erros e zero avisos.

## Resultado real da execução final

| Bateria | Aprovados | Interrompidos/ignorados |
|---|---:|---:|
| Segurança da etapa 1 | 13/13 | 0 |
| Financeiro da etapa 2 | 20/20 | 0 |
| Integração geral de referência | 73/73 | 0 |
| Produtos, estoque e consultas | 15/15 | 0 |

Total: 121 cenários aprovados. Os resultados vêm de novas execuções, não dos resultados da etapa anterior. Uma execução inicial da bateria nova encontrou um filtro que excluía datas legadas sem horário; o filtro foi corrigido e a bateria inteira foi executada novamente. Essa execução interrompida não foi contabilizada como aprovação.

Logs: [compilação](validation-estoque/build.log), [segurança](validation-estoque/seguranca.log), [financeiro](validation-estoque/financeiro.log), [integração geral](validation-estoque/geral.log), [estoque](validation-estoque/estoque.log), [JavaScript](validation-estoque/javascript.log).

A suíte geral foi executada fora do sandbox para permitir a renomeação de arquivo exigida pelo teste de backup; o diagnóstico dessa restrição está documentado na etapa 2. O teste de backup/restauração voltou a passar. Todas as baterias utilizam bancos temporários; nenhum banco operacional do usuário foi recriado ou alterado para executar testes.

## Relação entre estoque e financeiro auditada antes das alterações

- `VendaRepository.Registrar` baixa estoque, registra itens, movimentos `VENDA`, parcelas de pagamento, movimentos de caixa e auditoria na mesma transação.
- `Cancelar` devolve a quantidade original ao mesmo ProdutoId e cria movimentos `ESTORNO`, preservando itens, pagamentos e venda no histórico. A devolução pode ocorrer depois de um ajuste manual; deve somar a quantidade vendida ao saldo corrente, sem substituir esse saldo pelo saldo antigo.
- `EstoqueService` registra entrada, saída e inventário físico sem gerar vendas ou pagamentos. A entrada de mercadoria modifica custo/fornecedor/validade atuais, sem reescrever os custos e valores das vendas antigas.
- O cadastro de produto também permitia modificar saldo. O risco encontrado era sobrescrever uma venda recente ao salvar uma cópia antiga do cadastro.
- A importação NF-e grava movimentos por item antes de atualizar o saldo agrupado por produto, na mesma transação. Por isso não foi criado um gatilho genérico que inferisse saldos de todo movimento com base na ordem de INSERT.

## Preservação das etapas anteriores

Os serviços `Autorizacao.cs`, `UsuarioService.cs` e `PagamentoService.cs` mantiveram os bytes da entrada desta etapa, verificados por SHA-256. A captura do estado de entrada está em `Tests/stock-stage-baseline.json`.

Os blocos das migrações v6 e v7 foram preservados. A v8 é aditiva, transacional, usa o backup pré-migração existente e não reescreve saldos, códigos, pagamentos ou histórico financeiro. Os caminhos de registrar/cancelar vendas não foram recriados; a alteração em `VendaRepository` foi restrita à consulta do histórico.

Os 13 cenários de segurança e as 20 asserções/casos financeiros foram preservados. Na fixture que reexecuta v7, a remoção do marcador passou de `Version=7` para `Version>=7`, pois deixar o marcador v8 impediria a reexecução da migração. A fixture mínima v2 ganhou as colunas/tabelas necessárias para que os novos índices possam ser criados. A versão esperada no teste geral agora é 8.

## Cadastro, código de barras e edição concorrente

- Código continua texto: zeros à esquerda e códigos internos foram preservados; não foi imposta validação EAN que rejeite códigos internos existentes.
- Cadastro e busca normalizam espaços externos. A consulta por código agora retorna validade, categoria e fornecedor corretamente, sem tentar ler colunas que não estavam no SELECT.
- A verificação de duplicidade ocorre dentro da transação e é reforçada por gatilhos SQLite para INSERT/UPDATE, usando `TRIM(CodigoBarras)`. Assim duas gravações concorrentes não criam o mesmo código. Produto inativo continua reservando o código; exclusão lógica libera o código conforme a regra existente.
- Duplicidades legadas por espaços são preservadas na migração. Uma busca ambígua falha explicitamente, em vez de escolher uma conta/produto arbitrariamente. A correção do código é uma edição explícita pelo administrador, sem exclusão automática de registros.
- `Produtos.Versao` fornece controle de concorrência otimista. Um gatilho incrementa a versão em qualquer atualização, inclusive vendas, cancelamentos e importações. O cadastro verifica a versão dentro da transação. Salvar uma tela antiga depois de uma venda/entrada é bloqueado e exige recarga.
- WinForms mantém a versão no modelo; o editor Web envia a versão que realmente exibiu, sem substituir pela versão lida no momento de salvar. A versão após gravação é lida ainda dentro da transação, evitando outra janela de concorrência.
- Categoria e fornecedor são conferidos antes de salvar, inclusive para bancos antigos cuja tabela de produtos não possua todas as FKs declaradas. Os cadastros existentes e referências a registros inativos não foram removidos.
- A unidade só pode mudar quando não há saldo nem histórico de movimentos/itens. Isso impede que uma venda antiga em UN seja estornada posteriormente em um cadastro convertido para KG. Produto sem saldo e sem histórico ainda pode trocar de unidade.
- Exclusão permanece lógica, com permissão e auditoria existentes; um cancelamento continua referenciando o produto original mesmo se outro produto reutilizar seu código.

## Movimentações e alertas

- Novas entradas, saídas, ajustes manuais e diferenças explícitas pelo cadastro registram saldo anterior/posterior, além de usuário, data, quantidade e motivo. Ajuste negativo deixa de ser ambíguo: a variação é obtida dos saldos.
- O histórico expõe IDs de produto, usuário e venda, além de saldo e variação. Registros antigos não recebem saldos inventados; movimentos direcionais de venda/estorno/entrada/saída permitem exibir a variação pelo tipo.
- Quantidades continuam inteiras em UN e têm no máximo três casas em KG. Os movimentos revalidam o administrador dentro da transação e normalizam o saldo a três casas. Unidade desconhecida é rejeitada. A importação NF-e usa o mesmo arredondamento do saldo, sem mudar o contrato fiscal/financeiro.
- Erro na auditoria reverte cadastro/saldo/movimento. Ajuste manual não altera pagamentos nem caixa. Há teste de cancelamento misto depois de ajuste e de estorno depois da exclusão lógica.
- O filtro de histórico inclui datas legadas sem horário e deixou de cortar registros após 1.000 movimentos. Predicados SQL são montados apenas para os filtros utilizados, mantendo valores parametrizados e permitindo uso de índices.
- Receber mercadoria com validade posterior não apaga a validade mais próxima de estoque que já existia. Se o saldo anterior é zero, a validade do novo recebimento pode substituir a anterior. Recebimento com validade anterior conserva essa data mais conservadora.
- Alertas distinguem vencido, vence hoje e próximo de vencer; produtos excluídos ficam fora dos alertas. A tela Web de estoque permite filtrar vencidos, vencendo hoje, próximos 30 dias e sem validade.
- Estoque baixo mantém a regra inclusiva `EstoqueAtual <= EstoqueMinimo`, com precisão compatível com UN/KG. O filtro de estoque baixo pode ser combinado com unidade e validade.

## Paginação e consultas

`VendaService.ListarHistoricoPaginado(inicio, fim, status, pagina, tamanho, limiteId)` retorna itens, página, tamanho, total e teto de ID. Tamanho permitido: 1 a 500; telas utilizam 100. Ordenação determinística por Id decrescente. A primeira consulta captura o teto de ID; páginas seguintes conservam esse teto para que novas vendas não desloquem a navegação. Contagem e linhas da página são consultadas na mesma transação de leitura.

A ponte `management.getData` mantém `data.sales` e acrescenta `data.pagination`; aceita `page` e `snapshotId`. A tela Web e o formulário WinForms possuem anterior/próxima e indicação do total. Métricas calculadas somente com as linhas da página são identificadas como tal. Atualização/filtro novo inicia nova navegação.

O método antigo `ListarHistorico` permanece compatível e retorna todos os resultados filtrados, sem teto silencioso de 2.000. Chamadores interativos devem preferir a versão paginada.

Índices aditivos da v8:

- código normalizado por `TRIM(CodigoBarras)`;
- vendas por data/ID e status/data/ID;
- itens por produto (consulta de histórico antes da mudança de unidade);
- movimentos por produto/ID, data/ID e documento;
- índices parciais para validade e estoque baixo de produtos ativos não excluídos.

`EXPLAIN QUERY PLAN` foi verificado automaticamente para busca exata por código e intervalo de vendas com status. Nenhum histórico foi removido para obter desempenho.

## Limites e validações ainda necessárias

- O modelo possui uma validade por produto, não estoque por lote. A política conserva a data mais próxima ao misturar recebimentos; não identifica automaticamente o esgotamento de cada lote. Não houve criação de lotes fictícios nem modificação de quantidades para resolver validade.
- Saldos anteriores/posteriores históricos desconhecidos continuam nulos. Não foi reconstruído artificialmente o saldo de movimentos legados ou de importações que registram movimentos antes do UPDATE agrupado.
- O teto de ID estabiliza inclusões novas, mas não congela alterações de status entre páginas; filtros refletem os cancelamentos correntes. Uma fotografia histórica imutável exigiria um mecanismo diferente.
- Consultas de compatibilidade sem paginação e pesquisas por trecho (`LIKE %texto%`) podem ser custosas em bases muito grandes. Não houve benchmark com uma cópia de produção; a validação usou mais de 2.000 vendas, mais de 1.000 movimentos e planos SQL.
- JavaScript passou em validação sintática e os formulários foram construídos pelos testes existentes. Interação humana completa no editor Web, navegação e filtros ainda deve ser validada em uso real.

## Arquivos desta etapa

- `Data/DatabaseInitializer.cs`, `Data/ProdutoRepository.cs`, `Data/VendaRepository.cs`.
- `Services/ProdutoService.cs`, `Services/EstoqueService.cs`, `Services/NfeImportService.cs`, `Services/DashboardService.cs`, `Services/VendaService.cs`, `Services/ManagementWebBridge.cs`.
- `Models/Entities.cs`, `Models/MovimentoEstoqueLinha.cs`, `Models/ResumoDashboard.cs`.
- `Forms/AppWebViewForm.cs`, `Forms/VendasForm.cs`, `Web/products.js`, `Web/modules.js`.
- `Tests/CaixaLume.IntegrationTests/StockScenarios.cs`, `Program.cs`, `PhaseTwoScenarios.cs`, `FinancialScenarios.cs` (fixture de migração), `Tests/stock-stage-baseline.json`.
- Este relatório e os logs em `docs/validation-estoque/`.

Alterações de outras etapas já existentes no workspace foram preservadas.

## Repetir os testes

```powershell
dotnet build Tests/CaixaLume.IntegrationTests/CaixaLume.IntegrationTests.csproj --no-restore
dotnet run --project Tests/CaixaLume.IntegrationTests/CaixaLume.IntegrationTests.csproj --no-build -- --security
dotnet run --project Tests/CaixaLume.IntegrationTests/CaixaLume.IntegrationTests.csproj --no-build -- --financial
dotnet run --project Tests/CaixaLume.IntegrationTests/CaixaLume.IntegrationTests.csproj --no-build -- --stock
# Fora da restrição de renomeação de arquivos do sandbox:
dotnet run --project Tests/CaixaLume.IntegrationTests/CaixaLume.IntegrationTests.csproj --no-build
```
