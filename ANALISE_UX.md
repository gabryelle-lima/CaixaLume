# Mercado PDV — análise de UX

Data: 19/10/2026. Escopo: experiência do operador e do administrador na versão atual do projeto.

## Avaliação geral

A identidade escura com verde esmeralda oferece uma boa base visual. A organização do PDV em catálogo, carrinho e pagamento também faz sentido. A principal fragilidade está na previsibilidade: alguns campos mudam de significado, a navegação pode descartar trabalho e certas mensagens não ajudam o operador a saber o que aconteceu ou como continuar.

Minha recomendação é tratar primeiro os riscos de registrar produto, peso, valor ou forma de pagamento incorretos. Depois, consolidar a apresentação dos módulos e a legibilidade. O operador precisa conseguir trabalhar com atenção voltada ao cliente e à mercadoria, sem ter de interpretar o comportamento de cada tela.

Esta avaliação identifica 16 achados principais: 5 de prioridade alta (P1) e 11 de prioridade média (P2). A prioridade representa impacto potencial e recorrência provável; não é uma medição de incidentes em produção. Não atribuí nota ou percentual de UX porque não houve pesquisa com operadores nem medição de desempenho.

## Método e limites

- **Inspeção visual atual:** visão geral nativa, lista de produtos e formulário de novo produto. Executei o ambiente de demonstração existente com uma base nova, isolada, e janela solicitada de 1280 × 720. Não medi a escala efetiva do Windows nem validei uma matriz de resoluções.
- **Leitura do código:** PDV Web e nativo, pagamento, caixa, estoque, produtos, despesas, navegação, configurações, usuários e componentes compartilhados. Login e relatórios receberam uma revisão complementar de estrutura.
- **Referências enviadas anteriormente:** usadas como contexto de intenção visual, sem considerar os defeitos antigos automaticamente presentes na versão atual.
- **Limite da interface Web:** nesta sessão, o aplicativo abriu a interface nativa alternativa. A abertura direta dos arquivos no navegador foi bloqueada pela política de URLs locais da ferramenta. Os achados Web abaixo são de código, sem alegação de reprodução visual nesta rodada.
- **Contraste:** calculado a partir das cores definidas no código, sem amostragem de pixels de screenshot.
- **Produção:** não utilizei o banco real nem executei transações reais. A sessão de demonstração foi encerrada. Não alterei o código da aplicação nem rodei novamente a suíte funcional; a entrega desta rodada é a análise.

Evidência **visual** significa observação da interface em execução; **código** significa comportamento ou estrutura verificados no fonte. Os impactos descritos são a avaliação de UX desses comportamentos.

## Achados de prioridade alta

### UX-01 — A navegação pode descartar a venda e a referência de uma operação pendente

**Evidência: código.** O carrinho e o identificador da operação existem apenas em memória. A navegação para outra página e o logout não verificam se existe venda em andamento. O botão de limpar também atua imediatamente.

**Cenário:** o operador adiciona itens, vai à visão geral e retorna. O contexto anterior se perde. Se houve demora na resposta do pagamento, sair também elimina a referência usada para repetir a consulta da mesma operação. Isso pode levar o operador a refazer uma venda cujo resultado ainda desconhece; não foi observado um caso real de duplicação nesta análise.

**Recomendação:** preservar o rascunho e a referência da operação; avisar ao abandonar uma venda; oferecer desfazer para a limpeza. Em resultado desconhecido, exibir uma ação explícita de consulta antes de permitir outra cobrança.

**Aceite proposto:** sair e voltar preserva o carrinho; reabrir após resultado incerto recupera a mesma operação; descartar exige decisão explícita e não perde uma cobrança pendente.

Fonte: [estado do PDV](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Web/app.js:4), [limpeza](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Web/app.js:209), [navegação e logout](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Web/app.js:257).

### UX-02 — A mesma digitação de dinheiro produz valores diferentes

**Evidência: código.** No pagamento Web, os dígitos são tratados como centavos. No pagamento nativo, o valor é interpretado como número decimal literal. Ao substituir o conteúdo por `1427`, a Web interpreta R$ 14,27 e a interface nativa interpreta R$ 1.427,00. Os campos de desconto Web usam ainda uma entrada decimal literal.

**Impacto:** trocar de tela ou cair na interface alternativa exige reaprender a digitação. No pagamento, isso pode resultar em um troco muito diferente do esperado.

**Recomendação:** definir uma única convenção monetária para pagamento, abertura de caixa, despesas e descontos em reais. Mostrar a formatação enquanto se digita, com rótulos e exemplos claros. Campos percentuais devem ter identificação própria.

**Aceite proposto:** `1427`, `14,27`, colagem de `R$ 14,27`, apagar e corrigir seguem a mesma regra em todos os campos monetários correspondentes.

Fonte: [entrada monetária Web](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Web/app.js:12), [pagamento nativo](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/PagamentoForm.cs:88), [interpretação decimal](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Services/PagamentoService.cs:14).

### UX-03 — Produtos por peso entram com 250 g presumidos

**Evidência: código.** Adicionar um produto KG inclui automaticamente 0,250 kg, ou o saldo restante se menor. Os botões de aumentar e diminuir também usam passos de 250 g. Não existe etapa obrigatória de informar a pesagem. No carrinho Web, a unidade está na descrição de acessibilidade do campo, mas não aparece junto do número visível.

**Impacto:** a inclusão parece completa e pode seguir para pagamento com um peso que não foi informado pelo operador.

**Recomendação:** solicitar o peso ao incluir o produto, ou manter a linha claramente como “Peso pendente”. Mostrar `Peso (kg)`, `R$/kg` e subtotal. Um incremento fixo pode existir como opção explícita, quando fizer sentido para a operação.

**Aceite proposto:** um produto KG não é cobrado sem peso confirmado; 0,315 kg a R$ 19,90/kg exibe R$ 6,27, com unidade e quantidade reconhecíveis.

Fonte: [inclusão e quantidade Web](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Web/app.js:66), [linha do carrinho](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Web/app.js:95), [inclusão nativa](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/PdvForm.cs:272).

### UX-04 — Código desconhecido pode deixar outro produto selecionado na entrada de mercadoria

**Evidência: código.** Quando o código não corresponde a um produto, o formulário mantém a seleção anterior. Salvar usa essa seleção sem verificar a correspondência com o código. Além disso, o código inicial é preenchido antes de conectar o evento de seleção, e Enter está associado a “Registrar entrada”.

**Impacto:** o usuário pode acreditar que identificou uma mercadoria e lançar a entrada em outro produto. Leitores que enviam Enter aumentam o risco de uma confirmação precoce.

**Recomendação:** separar a identificação do produto da confirmação da entrada. Código desconhecido deve limpar a seleção ou sinalizar a divergência, impedindo o lançamento. Mostrar nome e unidade reais em destaque. Enter no código deve identificar o produto e avançar para quantidade.

**Aceite proposto:** código inexistente não permite registrar a mercadoria anterior; o código recebido de outra tela seleciona corretamente o produto; a leitura não salva automaticamente a entrada.

Fonte: [inicialização e eventos](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/EntradaMercadoriaForm.cs:29), [correspondência e gravação](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/EntradaMercadoriaForm.cs:39).

### UX-05 — A forma de pagamento anterior permanece selecionada na próxima venda Web

**Evidência: código.** A nova venda limpa carrinho e ajustes, mas não reinicia a seleção dos meios de pagamento. A interface nativa, por sua vez, começa em dinheiro.

**Cenário:** após receber uma venda em Pix, o operador inicia outra e confirma por hábito, sem perceber que Pix continua selecionado.

**Recomendação:** exigir escolha por venda ou definir uma preferência explícita, consistente nas duas interfaces. No momento de confirmar, reforçar a ação: “Registrar R$ 14,27 em dinheiro”, por exemplo. A redação deve deixar claro quando o aplicativo apenas registra um pagamento recebido externamente.

**Aceite proposto:** duas vendas consecutivas, com meios diferentes, não dependem de o operador lembrar a seleção anterior; forma e total ficam claros antes da confirmação.

Fonte: [abertura do pagamento](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Web/app.js:141), [sucesso e limpeza](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Web/app.js:165), [padrão nativo](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/PagamentoForm.cs:108).

## Achados de prioridade média

### UX-06 — Espera e falha podem parecer travamento

**Evidência: código.** Uma falha no carregamento inicial vira apenas uma notificação que desaparece após 3,6 segundos. O status pode continuar em “Verificando caixa...”, sem ação para repetir. Durante o pagamento, o X continua fechando o modal; após resposta incerta, a explicação está dentro dele e é apagada ao reabrir.

**Recomendação:** estados persistentes e distintos: “Carregando”, “Não foi possível carregar”, “Processando venda” e “Resultado pendente”. Exibir “Tentar novamente” ou “Consultar resultado” conforme o caso. Fechar a janela de progresso não deve esconder a situação da venda.

**Aceite:** uma falha deixa uma mensagem e uma ação visíveis até ser resolvida; o operador consegue explicar se a venda foi concluída, recusada ou permanece pendente.

Fonte: [carregamento](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Web/app.js:130), [resultado pendente](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Web/app.js:170), [fechar modal](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Web/app.js:219), [notificações](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Web/ui.js:10).

### UX-07 — O CSS pode manter conteúdos que o código tenta ocultar

**Evidência: código e regra de CSS; não reproduzido visualmente nesta sessão.** Os campos de dinheiro/troco têm `display:flex`, e os painéis de “nenhum resultado” têm `display:grid`. O JavaScript alterna apenas `hidden`, sem uma regra compartilhada para garantir a ocultação. Uma declaração de `display` pode sobrepor o estado oculto, conforme a [documentação do atributo hidden](https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Global_attributes/hidden#the_hidden_state).

**Consequência prevista:** campos de dinheiro/troco continuam presentes em Pix/cartão; mensagens como “Nenhuma venda” ou “Nenhum produto vencido” podem aparecer junto dos registros existentes. Isso enfraquece a confiança nos dados apresentados.

**Recomendação e aceite:** garantir a ocultação no estilo compartilhado e verificar as transições dinheiro→Pix→cartão e vazio→dados. Nenhuma tela deve apresentar simultaneamente dados e uma mensagem que os negue.

Fonte: [estilos do pagamento](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Web/styles.css:5), [estilos do painel](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Web/home.css:1), [alternância do pagamento](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Web/app.js:153), [alternância dos estados vazios](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Web/home.js:20).

### UX-08 — Validade vencida usa verde e falta de data parece situação verificada

**Evidência: visual e código.** “Vencido” e “Validade próxima” apareceram em verde escuro, pouco legível. Um produto sem data de validade apareceu como “Dentro da validade”. A coluna única de status também faz alertas de estoque e validade competirem pelo mesmo espaço.

**Recomendação:** usar “Vencido”, “Vence em X dias” e “Validade não informada”, com texto e cor correspondentes. Permitir que estoque baixo e vencimento sejam apresentados juntos. A decisão de bloquear a venda de vencidos continua dependendo da política do negócio.

**Aceite:** ausência de data não comunica validade conferida; todos os alertas aplicáveis podem ser identificados sem depender apenas de cores.

Fonte: [cores dos status](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/ProdutosForm.cs:72), [definição dos status](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/ProdutosForm.cs:93).

### UX-09 — O resumo de caixa chama despesas totais de “despesas em dinheiro”

**Evidência: código.** O cartão “DESPESAS EM DINHEIRO” recebe o total de despesas, inclusive outras formas de pagamento. O cálculo de dinheiro esperado distingue essas categorias corretamente.

**Impacto:** uma despesa Pix pode aparecer nesse cartão sem reduzir o dinheiro esperado, dando a impressão de números incompatíveis. Este achado é de apresentação; não demonstra erro no cálculo do saldo.

**Recomendação e aceite:** exibir somente despesas em dinheiro no cartão correspondente e separar as demais. Conferir um cenário com uma despesa em dinheiro e outra em Pix: cada uma deve aparecer na categoria correta.

Fonte: [rótulo](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/CaixaForm.cs:61), [valor apresentado](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/CaixaForm.cs:99).

### UX-10 — O cadastro prioriza imagem antes de informações necessárias para vender

**Evidência: visual e código.** Na abertura de “Novo produto”, a área visível contém nome, código, marca, categoria, fornecedor e imagem. Preço, unidade e estoque ficam depois da rolagem. “Buscar catálogo” está junto das ações de imagem, embora também preencha nome e marca.

**Recomendação:** primeiro mostrar código + “Consultar produto”, nome, preço de venda, unidade e estoque inicial. Reunir marca, fornecedor e imagem em detalhes complementares. Manter o rodapé fixo de salvar/cancelar já existente. Indicar que a consulta pública exige internet e que o preenchimento manual pode continuar.

**Aceite:** o cadastro básico pode ser compreendido na primeira área visível; a consulta por código está junto do campo que a inicia; campos obrigatórios e erros são evidentes.

Fonte: [ordem dos grupos](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/ProdutoForm.cs:55), [consulta pública](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/ProdutoForm.cs:189).

### UX-11 — Alguns erros fecham o formulário antes de permitir a correção

**Evidência: código.** Os diálogos de movimentação e fechamento de caixa são encerrados antes de validar o valor ou executar a operação. O auxiliar de abertura pode retornar sem mensagem quando a entrada monetária é inválida.

**Impacto:** um erro obriga a reabrir e redigitar; uma recusa pode parecer falta de resposta do botão.

**Recomendação e aceite:** validar antes de fechar; manter valor e observação; mostrar erro junto do campo e levar o foco a ele. Uma sangria recusada deve permitir corrigir o valor no mesmo formulário.

Fonte: [movimentação de caixa](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/CaixaForm.cs:179), [fechamento](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/CaixaForm.cs:197), [abertura](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/CaixaForm.cs:215).

### UX-12 — “Ajustar saldo” muda o significado do campo de quantidade

**Evidência: código.** Entrada, saída e ajuste usam o mesmo campo. No ajuste, o número significa o novo saldo inteiro; nas demais operações, representa a quantidade movimentada. A interface não apresenta claramente essa mudança nem uma prévia do resultado.

**Recomendação:** mostrar “Saldo atual: 20 UN → Novo saldo: 5 UN; diferença: −15 UN”. Distinguir “Quantidade a adicionar/retirar” de “Novo saldo contado” e pedir um motivo específico.

**Aceite:** antes de confirmar, o usuário identifica o saldo resultante e a diferença sem fazer cálculo mental.

Fonte: [ações de estoque](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/EstoqueForm.cs:65), [mudança e aplicação da operação](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/EstoqueForm.cs:132).

### UX-13 — A navegação não mantém um contexto único entre módulos

**Evidência: visual e código.** Abrir Produtos e depois Novo produto cria janelas empilhadas. A interface Web também abre os módulos de gestão em diálogos nativos. O fallback altera a apresentação sem uma explicação ao operador. Os menus ainda oferecem áreas administrativas a operadores que só recebem a restrição depois do clique.

**Recomendação:** estabelecer um padrão de navegação, cabeçalho, ações e formulários para todo o produto. Manter módulos de trabalho no contexto principal e reservar modais para decisões curtas. Indicar permissões antes do clique. Ao entrar na interface alternativa, explicar de forma breve qual modo está ativo.

**Aceite:** o operador identifica onde está, como voltar e quais ações pode executar; o comportamento é consistente independentemente da tecnologia usada na tela.

Fonte: [abertura de módulos](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/AppWebViewForm.cs:170), [fallback](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/AppWebViewForm.cs:185), [restrição após clique](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/MainForm.cs:241).

### UX-14 — Cortes e baixo contraste prejudicam a leitura rápida

**Evidência: visual e código.** Na visão geral nativa, o subtítulo não ficou legível e o status do caixa apareceu parcialmente cortado; nomes e datas da tabela de validade estavam truncados. Em Produtos, o status também sofreu truncamento. O modal de produto desconhecido no estoque tem três botões cujas larguras somam 430 px, mais margens, para cerca de 404 px úteis; esse último caso foi identificado apenas no layout do código.

Os botões nativos usam branco sobre `#10B981`, com contraste calculado de **2,54:1**. O verde escuro dos status sobre `#161B22` fica em **2,33:1**. Como referência de legibilidade, a WCAG estabelece 4,5:1 para texto comum e 3:1 para texto grande; isso não constitui uma auditoria completa de conformidade do aplicativo. [W3C — contraste mínimo](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html)

**Recomendação:** corrigir alturas fixas de cabeçalhos; dar largura mínima a datas/valores; permitir quebra de ações; oferecer detalhe completo para nomes truncados; usar texto escuro nos botões esmeralda ou outra combinação que atinja contraste adequado. A combinação Web `#04251B` sobre `#10B981`, já usada, resulta em aproximadamente 6,44:1.

**Aceite:** ações principais, datas e valores completos acessíveis nas resoluções suportadas e escalas 100%, 125% e 150%; textos ativos com contraste apropriado. Essa matriz ainda precisa ser executada.

Fonte: [cabeçalho nativo](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/MainForm.cs:45), [cores dos botões](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/UiTheme.cs:57), [modal de estoque](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/EstoqueForm.cs:187).

### UX-15 — Foco e atalhos não seguem sempre as mesmas regras dos botões

**Evidência: código.** Editar quantidade reconstrói os controles do carrinho, o que pode interromper a sequência de foco. F4 na Web permite abrir pagamento sobre os diálogos de ajuste ou produto desconhecido. No PDV nativo, o atalho chama a finalização sem a mesma verificação inicial de caixa aberto que desabilita o botão.

**Recomendação:** compartilhar as regras de disponibilidade entre clique e teclado; preservar o foco ao editar uma linha; não empilhar decisões modais. Depois de confirmar peso ou quantidade, definir um destino previsível para o foco, normalmente a busca.

**Aceite:** completar uma venda por teclado sem saltos inesperados; F4 respeita caixa fechado e qualquer modal aberto; editar quantidade não perde a próxima ação.

Fonte: [reconstrução do carrinho](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Web/app.js:88), [atalhos Web](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Web/app.js:266), [atalhos nativos](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/PdvForm.cs:110), [finalização nativa](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/PdvForm.cs:418).

### UX-16 — Ações de edição e recuperação não acompanham o estado do registro

**Evidência: código.** Produtos e usuários oferecem desativação, mas não uma ação de reativação no formulário. Em Despesas, “Editar” pode abrir “Nova despesa” quando não há seleção. Alguns comandos inválidos retornam sem feedback.

**Recomendação:** oferecer “Reativar” a administradores nos registros inativos, preservando histórico e auditoria. Habilitar edição/cancelamento conforme a seleção e o estado. Exibir uma mensagem de lista vazia com uma ação adequada.

**Aceite:** editar nunca inicia criação silenciosamente; um administrador consegue recuperar uma desativação indevida pela interface; comandos indisponíveis são reconhecíveis antes do clique.

Fonte: [ações de produtos](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/ProdutosForm.cs:115), [ações de usuários](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/UsuariosForm.cs:10), [edição de despesas](C:/Users/macha/Documents/Codex/2026-08-24/sis/MercadinhoPdv/Forms/DespesasForm.cs:13).

## Elementos que vale preservar

- Estrutura catálogo → carrinho → pagamento, com total destacado.
- Busca por código exato e atalhos visíveis na interface Web.
- Total, valor recebido e troco apresentados no pagamento.
- Confirmação de venda concluída com número da venda.
- Proteção de repetição da mesma operação no serviço, que precisa ganhar um fluxo de recuperação visível.
- Rodapé fixo de salvar/cancelar no cadastro de produtos.
- Revisão dos dados externos antes de preencher o cadastro, com alternativa manual.
- Conferência do fechamento com esperado, contado e diferença.

## Sequência recomendada de melhoria

| Ordem | Entrega | Achados principais | Resultado esperado |
|---|---|---|---|
| 1 | Prevenção de erros de operação | UX-01 a UX-05 | Nenhum produto, peso, valor ou meio de pagamento é assumido de maneira ambígua |
| 2 | Recuperação e confiança nos estados | UX-06 a UX-09, UX-11 | O usuário entende o resultado e consegue corrigir sem redigitar ou repetir cobrança |
| 3 | Cadastro e estoque claros | UX-10, UX-12, UX-16 | Menos rolagem e correções, com significado explícito das ações |
| 4 | Consistência visual e teclado | UX-13 a UX-15 | Mesma linguagem entre módulos, leitura legível e operação contínua por teclado |

As correções de contraste, rótulos e ocultação de conteúdo podem acompanhar a primeira entrega porque são pequenas e atingem várias telas. A mudança de estrutura dos módulos deve preservar os fluxos já estabilizados.

## Roteiro proposto de validação com operadores

Recomendo observar pelo menos dois operadores habituados a caixa e uma pessoa nova no sistema. Isso serve como rodada inicial qualitativa, sem pretensão de representar estatisticamente todos os usuários. As tarefas abaixo ainda não foram executadas com participantes.

| Tarefa | O que observar | Critério de aceite proposto |
|---|---|---|
| Abrir turno, vender um item UN e outro KG, receber dinheiro | Digitação, peso, foco, troco | Concluir sem ajuda; nenhuma quantidade ou quantia incorreta |
| Fazer duas vendas, uma Pix e outra em dinheiro | Seleção do meio de pagamento | Registrar corretamente sem herdar uma escolha despercebida |
| Simular demora na resposta e fechar/reabrir a tela | Compreensão do estado e recuperação | Recuperar a mesma operação; nenhuma repetição de cobrança |
| Ler código desconhecido na entrada de mercadoria | Identificação e bloqueio do lançamento | Nenhuma movimentação em outro produto |
| Cadastrar produto sem imagem e sem consulta pública | Descoberta dos campos essenciais | Concluir manualmente sem depender de internet ou orientação |
| Corrigir estoque de 20 para 5 unidades | Entendimento de saldo e diferença | Antecipar saldo final 5 e movimentação −15 antes de confirmar |
| Corrigir valor inválido numa sangria | Recuperação de erro | Permanecer no formulário com dados preservados |
| Conferir caixa com despesas em dinheiro e Pix | Leitura do resumo | Explicar corretamente por que só dinheiro altera o numerário |
| Localizar vencidos e produtos sem data | Reconhecimento dos alertas | Diferenciar vencimento confirmado de ausência de informação |

Registrar tempo por tarefa, pedidos de ajuda, retornos de tela, erros de valor/quantidade e tentativas repetidas. Medir uma linha de base antes das mudanças e repetir as mesmas tarefas depois. Aprovar visualmente também as resoluções e escalas realmente usadas na loja, incluindo as telas menores já previstas na documentação do projeto.

O resultado desta análise é uma lista de melhorias verificáveis. A prontidão de UX para uso diário depende de resolver os riscos prioritários e observar os fluxos com operadores e equipamentos reais.
