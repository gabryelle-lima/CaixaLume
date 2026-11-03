# CaixaLume — interface e desempenho

Validação final em 29/10/2026. Escopo: Windows Forms, WebView2 e interface HTML/CSS/JavaScript. Não foram acrescentadas migrações ou integrações financeiras.

## Preservação e resultado

Os arquivos de autenticação/permissões, `PagamentoService`, `VendaService`, `VendaRepository`, `EstoqueService` e `DatabaseInitializer` permaneceram idênticos ao estado de entrada, verificados por SHA-256 em `Tests/ui-stage-baseline.json`. As migrações v6/v7/v8, transações, composição mista, assinatura de operação e proteção contra duplicidade foram preservadas.

A ponte do PDV ganhou uma operação de leitura de imagens. A ponte de gestão passou a encaminhar filtros de data para a consulta já existente de histórico de caixa. Os cálculos e regras de gravação financeira não foram alterados. Os parsers acrescentados tratam a digitação na interface; a validação definitiva continua no backend.

Resultado final automatizado:

| Bateria | Resultado |
|---|---:|
| Compilação | Zero erros e zero avisos |
| Segurança | 13/13 |
| Financeiro | 20/20 |
| Estoque e consultas | 15/15 |
| Integração geral | 73/73 |
| Novos cenários C# de UX/desempenho | 8/8 |
| JavaScript | 12/12 |
| Sintaxe JavaScript | Todos os arquivos `Web/*.js` |

Total: 129 cenários C# e 12 testes JavaScript. Nenhum interrompido ou ignorado na execução final. O teste geral de backup/restauração foi executado fora do sandbox, devido à restrição de renomeação já diagnosticada nas etapas anteriores. Bancos de testes são isolados; o banco operacional não foi recriado ou utilizado.

Logs: [build](validation-ux/build.log), [segurança](validation-ux/seguranca.log), [financeiro](validation-ux/financeiro.log), [estoque](validation-ux/estoque.log), [geral](validation-ux/geral.log), [UX](validation-ux/ux.log), [JavaScript](validation-ux/javascript-tests.log), [sintaxe](validation-ux/javascript-syntax.log).

## Responsividade e padronização

- CSS compartilhado `ui.css`, conservando a paleta escura, verde e tipografia do CaixaLume. Controles têm foco de teclado visível, áreas mínimas de ação, textos de botão com quebra e modais com limite da área útil/rolagem.
- Layout compacto para telas baixas, menos espaço entre cabeçalhos e rodapé de cobrança, sem esconder a ação principal. No layout estreito, catálogo/carrinho continuam empilhados e o rodapé do carrinho fica acessível.
- Barras de ferramentas e ações de vendas, estoque, despesas, relatórios e caixa WinForms passaram a usar linhas automáticas/quebra onde havia risco de esconder botões no final da linha. O diálogo nativo de despesas ganhou altura para comportar observação e ações.
- Pagamento WinForms passou a declarar corretamente as três linhas do rodapé e a limitar a janela à área de trabalho. Os testes verificam que seu botão principal permanece dentro da área útil nos cinco tamanhos de referência.
- Alertas nativos longos têm corpo com rolagem; confirmação destrutiva deixa o botão seguro como padrão de Enter.
- Confirmações Web de produtos, gestão, NF-e e abandono da venda usam o componente compartilhado, com foco inicial em Voltar, Escape para cancelar e restauração de foco. Mensagens ficam como texto, sem interpolar HTML.
- Toasts têm papel acessível, erros permanecem mais tempo e há no máximo quatro simultâneos. Textos longos das células de gestão podem ser consultados ao passar o mouse, sem ampliar toda a tabela.

## Valores numéricos e monetários

- Parser de apresentação compartilhado `ui-input.js`: aceita `14,27`, `14.27` e agrupamento brasileiro explícito `1.234,56`. Rejeita entradas como `1.234` em campo monetário de duas casas, `12.34,56`, expoentes, negativos e valores acima do limite, em vez de convertê-los silenciosamente.
- Produtos Web, pagamento e formulários de gestão usam essa interpretação. Campos de gestão usam entrada textual com teclado decimal para não depender do tratamento regional inconsistente de `input type=number`.
- Quantidades informam precisão distinta; UN exige inteiro e KG permite três casas. A entrada de NF-e mantém seis casas para custo, sem reduzir a precisão do contrato existente.
- Pagamento, despesas e caixa WinForms usam `EntradaNumerica` para validar inclusive texto colado, sem modificar `PagamentoService`. O pagamento não aceita texto inválido nem mesmo quando o total é zero.
- A cobrança Web informa formato inválido/saldo insuficiente e só habilita a confirmação quando os campos exigidos estão preenchidos. Não houve seleção automática de pagamento nem confirmação automática de venda.

## Operação por busca, leitor e teclado

- Busca por nome ignora acentos e caixa; busca exata por código continua prioritária. A lógica não escolhe arbitrariamente um dos produtos de uma busca ambígua.
- A digitação na grade é agrupada por frame, enquanto Enter do leitor é resolvido imediatamente. Eventos de composição e repetição de tecla não adicionam itens acidentalmente.
- A cobrança procura concluir a edição da quantidade do carrinho antes de abrir o pagamento.
- Produto desconhecido oferece cadastro para administradores e consulta para operadores. A rota rápida aceita códigos internos alfanuméricos curtos, além de EAN; antes a navegação só aceitava 8 a 14 dígitos, embora o cadastro permitisse códigos internos. A consulta externa continua exigindo o formato apropriado; não foi ampliada uma integração externa para códigos arbitrários.
- O rascunho, o retorno ao PDV e as verificações de permissão no backend foram conservados. Acesso de operador não vira permissão de cadastro.

Atalhos:

| Contexto | Tecla | Ação |
|---|---|---|
| PDV | F2 | Focar/selecionar busca |
| PDV | F4 | Abrir cobrança |
| Pagamento | F6 | Selecionar dinheiro |
| Pagamento | F7 | Selecionar Pix |
| Pagamento | F8 | Selecionar débito |
| Pagamento | F9 | Selecionar crédito |
| Gestão com busca | F3 | Focar pesquisa |
| Páginas Web com pesquisa, fora de campos | / | Focar pesquisa |
| Modais | Escape | Voltar, quando não há gravação em andamento |

F6–F9 só selecionam o meio; a confirmação continua explícita. Os atalhos de navegação não retiram o foco de um modal aberto. Os identificadores de operação, tratamento de resultado desconhecido e prevenção de reenvio duplicado não foram simplificados ou removidos.

## GetPdvData, imagens e atualizações

Antes, cada `GetPdvData` percorria o catálogo e tentava abrir/decodificar todas as fotos, incorporando as miniaturas Base64 na resposta. O cache antigo podia crescer até 500 entradas e era esvaziado por inteiro ao atingir o limite.

Agora:

1. A consulta `ListarParaPdv` seleciona apenas os campos usados no checkout e na grade. O catálogo não inclui Base64 nem exige decodificação de fotos.
2. `imageData` permanece no contrato como nulo; `hasImage` e `imageVersion` orientam o carregamento posterior. O cliente distribuído foi atualizado em conjunto.
3. `pdv.getImages` é uma leitura autenticada, aceita no máximo 32 IDs e consulta apenas produtos ativos não excluídos. Não recebe caminhos de arquivo do cliente.
4. O frontend usa `IntersectionObserver`, solicita no máximo 16 miniaturas por lote e mantém um lote em andamento. Fotos fora da área de visualização não são solicitadas antecipadamente.
5. Cache do servidor: no máximo 128 miniaturas e 8 MiB de conteúdo textual calculado em UTF-16; expulsa entradas antigas individualmente, sem limpar tudo de uma vez. A chave inclui caminho, tamanho e data do arquivo. Cache da página: no máximo 128 entradas, identificadas por produto/versão.
6. Imagem ausente, ilegível ou corrompida resulta em marcador visual, sem interromper a venda. Bitmaps e streams são descartados.
7. Requisições de dados iguais, enquanto estão em andamento, compartilham uma leitura. Não existe cache persistente de saldos no helper. Checkout e outras gravações não são agrupados por esse mecanismo; há teste explícito dessa separação.

Os limites de cache e a ausência de fotos no catálogo foram verificados por testes. Não foi feito benchmark com o catálogo de produção nem medição comparativa de RSS/CPU; não há promessa de percentual de aceleração.

## Histórico de caixa, relatórios, estoque e despesas

- O histórico de caixa Web ganhou filtros de período encaminhados ao serviço de consulta existente. O limite já existente de 200 registros é informado na tela, com orientação para refinar as datas.
- A paginação de vendas da etapa 3 foi preservada.
- Cabeçalhos, ações e cartões de gestão podem quebrar linha; tabelas permanecem roláveis horizontalmente e cabeçalhos fixos. Filtros de estoque/validade e paginação incremental de relatórios não foram removidos.
- O formulário de despesas não realiza a gravação novamente enquanto a anterior está em andamento. Escape/fechar não descarta o formulário durante a gravação. Erros de campos numéricos focam o campo e explicam o formato esperado.

## Limites da inspeção visual e validação manual

Foi iniciado um `UiFixture` com banco `.ui-test.db` isolado. A janela de teste apareceu na lista do Windows, mas a captura/inspeção pela ferramenta de controle expirou com `Computer Use app approval timed out`. Assim, não houve inspeção visual válida da janela Windows e nenhuma captura foi considerada aprovada.

Os testes automatizados constroem telas e exercitam componentes em 1024×768, 1280×720, 1366×768, 1440×900 e 1920×1080. Isso não comprova ausência de sobreposição em todos os layouts HTML nem substitui interação visual. Ainda é necessário validar manualmente:

- layout real de todas as telas nas cinco resoluções, incluindo 125%/150% de escala e zoom do WebView2;
- posição/rolagem dos botões de caixa, estoque, relatório e despesa e texto longo em alertas;
- leitores físicos com sufixo Enter/Tab, velocidade de leitura e foco após peso/pagamento/modal;
- percurso completo teclado/mouse: produto desconhecido → cadastro autorizado → retorno ao carrinho → cobrança → nova venda;
- colagem de números nos `NumericUpDown` nativos de produto/mercadoria, além dos campos textuais já testados;
- modais padronizados, Escape, restauração de foco e impedir saída durante resposta pendente;
- carregamento de muitas fotos, rolagem rápida e atualização de foto enquanto a página está aberta;
- impressão/preview de relatórios e DPI da impressora, não testados nesta etapa.

Os testes JavaScript da ponte tinham uma fixture anterior à navegação em iframe e esperavam o código antigo de timeout. A fixture foi atualizada para fornecer `window.parent`, eventos, relógio e IDs distintos; a expectativa de checkout é `UNKNOWN_OUTCOME`, que já era o comportamento de produção. Nenhuma proteção de timeout foi relaxada.

## Arquivos desta etapa

- `Services/PdvWebBridge.cs`, `Services/ManagementWebBridge.cs`, `Data/ProdutoRepository.cs` (consultas de apresentação e miniaturas).
- `Forms/EntradaNumerica.cs`, `PagamentoForm.cs`, `UiDialog.cs`, `VendasForm.cs`, `CaixaForm.cs`, `DespesasForm.cs`, `RelatoriosForm.cs`, `EstoqueForm.cs`, `AppWebViewForm.cs`.
- `Web/ui-input.js`, `ui.css`, `ui.js`, `app.js`, `pdv-domain.js` (somente busca), `products.js`, `modules.js`, `bridge.js`, `shell.js`.
- `Web/home.html`, `index.html`, `products.html`, `modules.html`, `reports.html`, `shell.html` (recursos compartilhados e instrução de atalhos).
- `Tests/CaixaLume.IntegrationTests/UxScenarios.cs`, `Program.cs`, `Tests/Web/ui-input.test.js`, `bridge.test.js`, `Tests/ui-stage-baseline.json`.
- Este relatório e logs em `docs/validation-ux/`.

Alterações anteriores e arquivos que já estavam modificados foram preservados.

## Repetir os testes

```powershell
dotnet build Tests/CaixaLume.IntegrationTests/CaixaLume.IntegrationTests.csproj --no-restore
dotnet run --project Tests/CaixaLume.IntegrationTests/CaixaLume.IntegrationTests.csproj --no-build -- --ux
dotnet run --project Tests/CaixaLume.IntegrationTests/CaixaLume.IntegrationTests.csproj --no-build -- --security
dotnet run --project Tests/CaixaLume.IntegrationTests/CaixaLume.IntegrationTests.csproj --no-build -- --financial
dotnet run --project Tests/CaixaLume.IntegrationTests/CaixaLume.IntegrationTests.csproj --no-build -- --stock
# Fora da restrição de renomeação do sandbox:
dotnet run --project Tests/CaixaLume.IntegrationTests/CaixaLume.IntegrationTests.csproj --no-build
# Com o Node.js disponível:
node --test Tests/Web/*.test.js
```
