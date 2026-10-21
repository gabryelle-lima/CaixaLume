# Relatório de entrega — rodada extensa de correções

Execução: 17 e 18/10/2026. Implementação e validação automatizada concluídas; validações externas e visuais pendentes estão identificadas abaixo.

## Estado preservado e referência inicial

- A pasta não é um repositório Git; não há estado de branches ou alterações rastreadas disponível.
- Fontes anteriores preservados em artifacts/round-baseline/sources-before.zip, com caminhos relativos e manifesto source-sha256.csv.
- Auditoria e documentos de arquitetura lidos antes das mudanças.
- Referência inicial: compilação sem avisos/erros e 33/33 cenários aprovados, exclusivamente em banco temporário.

## Etapa 1 — dados, permissões e inicialização

- Limpeza bloqueada no serviço e escondida no uso normal. Disponibilidade apenas em Debug, ambiente explicitamente DESENVOLVIMENTO, banco sobrescrito fora do caminho padrão e marcador .descartavel.
- SQLite ativa e verifica foreign_keys em todas as conexões; ativações espalhadas removidas.
- Ajuste de saldo grava auditoria na mesma transação, com usuário, produto, saldo anterior/posterior, diferença e motivo.
- Senhas novas exigem 10 caracteres; hashes existentes continuam compatíveis.
- Cadastros, estoque, configurações, usuários, despesas administrativas, cancelamento e backup verificam o usuário ativo e a permissão no banco.
- Falha WebView2 abre a alternativa WinForms uma vez, preservando sessão; inclui ausência de assets, timeout e falha de processo.
- Corrigido caminho de navegação que mantinha um módulo pendente mesmo após falha de autorização.
- Validação: compilação sem avisos/erros; 44/44 cenários aprovados. Testes do fallback verificam o fluxo de seleção da interface, sem comprovar renderização visual.

## Etapas 2 e 3 — operação do PDV e interface

- Leitura por código exato tem prioridade no PDV Web. Enter inclui o produto, limpa o campo e preserva foco; eventos repetidos de tecla são ignorados. Código ausente oferece consulta/cadastro em uma janela de módulo, mantendo o carrinho.
- Carrinho Web e WinForms aceitam peso editável com vírgula ou ponto, até 0,001 kg. Quantidades fracionadas de UN, peso zero, precisão excessiva e estoque insuficiente são rejeitados.
- Cálculo financeiro C# usa decimal e arredondamento por item. A interface Web usa centavos e inteiros de precisão arbitrária nos produtos de preço/peso; o servidor confere o total exibido antes de persistir.
- Desconto em reais ou percentual e acréscimo em reais são suportados. A política começa desabilitada; o administrador deve definir os limites e se operadores podem aplicá-los. A autorização é validada novamente na transação da venda.
- Total bruto, desconto, acréscimo, total líquido e valor original do desconto são preservados na venda. Histórico, comprovante e relatórios incluem os valores; totais por produto são identificados como brutos.
- Identificador de operação e assinatura impedem duplicação do checkout. Repetir a mesma operação retorna a venda existente, inclusive após mudança no cadastro; reutilizá-la com outros dados ou após cancelamento é bloqueado.
- O PDV mantém a operação pendente quando não recebe resposta, evita editar o carrinho durante essa situação e orienta a reconfirmar antes de iniciar outra venda.
- Dashboard distingue vencidos de próximos do vencimento. A venda de vencidos não foi bloqueada automaticamente, conforme a instrução de aguardar a regra de negócio.
- Open Food Facts ganhou cliente HTTP substituível para testes, campos localizados na consulta, distinção entre ausência e erro, timeout e cancelamento. Campos já digitados no cadastro têm prioridade sobre dados externos.
- Botões X dos modais Web deixaram de submeter os formulários de venda/abertura de caixa. Atalhos passaram a desconsiderar modificadores; no nativo, Alt+F4 não deve disparar o atalho de cobrança.
- Formulários são ajustados à área de trabalho disponível. Houve ajustes nos tamanhos mínimos, rolagem, cabeçalhos, resumos de relatórios, edição de peso e contador de itens. O caixa deixou de repetir rótulos dentro dos valores de data/saldo.

## Etapa 4 — consistência entre módulos

Os cenários de regressão verificam venda → estoque → caixa → histórico → relatório, fechamento e cancelamento. Além dos pontos da auditoria, a revisão encontrou e corrigiu falha de edição de despesas causada por parâmetros extras no comando SQL.

Editar uma despesa de Pix para dinheiro agora vincula a despesa ao caixa aberto e confere disponibilidade de dinheiro. Despesas de caixa fechado não podem ter seus valores alterados ou ser canceladas retroativamente. Movimentos de caixa e valor contado rejeitam frações inferiores a centavos. O último administrador não pode remover o próprio perfil administrativo.

## Situação F-01 a F-12

| Item | Situação | Evidência/limite |
|---|---|---|
| F-01 | Corrigido | Release bloqueia a operação no serviço; configuração normal não contém a opção; teste de bloqueio e presença na árvore de controles |
| F-02 | Corrigido | Fallback WinForms implementado, sem laço e preservando sessão; testes de seleção e abertura real da alternativa em base isolada |
| F-03 | Corrigido | Conexões habilitam/verificam foreign_keys; teste de referência inválida e rollback |
| F-04 | Corrigido | Edição livre Web/nativa; testes de precisão, estoque e valores; observação nativa de 0,315 kg → R$ 6,27 |
| F-05 | Corrigido para alertas | Vencidos separados de próximos; política de bloqueio da venda continua sem definição |
| F-06 | Parcialmente validado | Implementação e respostas simuladas aprovadas; chamada real falhou por DNS do ambiente |
| F-07 | Corrigido | Auditoria do ajuste com usuário/antes/depois/diferença/motivo e teste de rollback |
| F-08 | Corrigido | Política de senha uniforme, compatibilidade com hashes antigos e testes de autorização por chamada direta |
| F-09 | Parcialmente corrigido | 73 cenários C# e 6 JavaScript; inspeção nativa parcial; matriz visual, hardware e escalas ainda pendentes |
| F-10 | Parcialmente corrigido | Publicação Windows x64 reproduzível gerada e verificada; instalador, máquina limpa e atualização/reinício reais ainda pendentes |
| F-11 | Implementado e desabilitado por padrão | Modelo, migração, serviço, persistência e telas; a loja deve definir a política para habilitar |
| F-12 | Corrigido no código e nos testes funcionais | Código exato, ambiguidade/ausência e inativos testados; leitor físico e fluxo completo WebView2 ainda exigem aceitação manual |

## Migração de banco

A migração **v3** adiciona campos em Vendas: Subtotal, ValorDesconto, ValorAcrescimo, TipoDesconto, DescontoInformado, OperacaoId e AssinaturaOperacao. O identificador de operação tem índice único quando informado.

Vendas anteriores mantêm ValorTotal, recebem Subtotal igual ao total anterior e desconto/acréscimo zero. O processo é incremental, transacional e cria snapshot antes de migrar um banco existente. Há teste específico v2 → v3, repetição idempotente da inicialização e preservação de registro antigo.

Nenhuma base de produção foi aberta ou migrada nesta rodada. As migrações foram executadas em bases temporárias. A nova versão aplicará v3 ao ser aberta normalmente pelo usuário; fazer backup antes da atualização.

## Compilação e testes

| Marco | Resultado |
|---|---|
| Referência inicial | 33/33 cenários C#; compilação sem avisos/erros |
| Proteções iniciais | 44/44 cenários C# |
| Financeiro, migração, API simulada e ponte | 64/64 cenários C# |
| Validação final Release | **73/73 cenários C# + 6/6 testes JavaScript** |
| Scripts Web | Checagem de sintaxe aprovada; os testes funcionais são contabilizados separadamente |
| Compilação final | 0 erros de compilação e nenhum aviso C#; 2 ocorrências NU1900 por indisponibilidade da consulta de vulnerabilidades NuGet |
| Publicação Windows x64 | Êxito; assets Web, executável e manifesto presentes; arquitetura PE 0x8664 confirmada; nenhum banco empacotado |

As falhas intermediárias de regressão foram corrigidas antes da aprovação final. A execução da configuração Release está registrada em artifacts/validacao-release.log; a publicação está em artifacts/publicacao-final.log.

Os testes usam bancos isolados. O cenário de construção de formulários nas cinco dimensões apenas verifica construção/layout sem exceção; **não comprova aprovação visual**. A inspeção real do Windows foi limitada por falha WebView2 e capturas/elementos instáveis. O roteiro restante está em docs/VALIDACAO_MANUAL.md.

## Arquivos modificados e adicionados

O inventário completo, comparado com os hashes anteriores à rodada, está em **artifacts/arquivos-alterados.txt**. Principais grupos:

- Infraestrutura: Program.cs, Data/DatabaseConnection.cs, Data/DatabaseInitializer.cs, Data/ProdutoRepository.cs, Data/VendaRepository.cs.
- Serviços novos: Autorizacao, AmbienteAplicacao, InicializacaoInterface, QuantidadeService e AjustesVendaService.
- Serviços alterados: Venda, PdvWebBridge, Caixa, Estoque, Usuario, Produto, Cadastro, Despesas, Dashboard, Relatorios, Comprovante, Backup e OpenFoodFacts.
- Modelos: AjusteVenda/TotaisVenda/PoliticaAjustesVenda, ItemCarrinho, Entities, ResumoDashboard e ResumoRelatorio.
- Telas: AppWebViewForm, MainForm, PdvForm, ProdutoForm, ProdutosForm, CaixaForm, EstoqueForm, VendasForm, RelatoriosForm, ConfiguracoesForm, CadastroDialog e UiTheme; novas AjusteVendaForm e PoliticaAjustesForm.
- Web: app.js, bridge.js, home.js/home.html, index.html, styles.css e novo pdv-domain.js.
- Testes: Program.cs, PhaseOneScenarios, PhaseTwoScenarios, PhaseThreeScenarios, UiFixture e testes Web de domínio/ponte.
- Publicação/documentação: perfil Windows-x64, scripts/Publicar.ps1, scripts/Validar.ps1, README e documentos de instalação, validação e estado.

Nenhuma dependência de aplicação foi adicionada.

## Executar a nova versão

Abra **artifacts/publicacao/Mercado-win-x64-20261018-001148/MercadinhoPdv.exe**, mantendo todos os arquivos da pasta e a subpasta Web. A publicação é dependente do .NET Desktop Runtime 10 x64 e utiliza WebView2 para as telas Web. Leia docs/INSTALACAO_E_OPERACAO.md antes de atualizar dados existentes.

Para repetir a regressão, execute scripts/Validar.ps1. Para gerar uma pasta nova de publicação, execute scripts/Publicar.ps1. O script de publicação não inicia o sistema nem substitui uma instalação.

## Riscos e pendências

- Consulta real Open Food Facts não aprovada: a tentativa retornou “Este host não é conhecido”. Os testes HTTP simulados não provam disponibilidade externa.
- A auditoria de vulnerabilidades dos pacotes ficou indisponível pela falta de acesso ao NuGet; não foi declarada uma verificação de segurança online aprovada.
- A interface WebView2 não foi validada visualmente de ponta a ponta; o fallback nativo foi observado funcionando.
- Ainda faltam aceitação visual de todos os módulos em 1024×768, 1280×720, 1366×768, 1440×900 e 1920×1080, com escalas 125%/150%.
- Leitor físico, balança, impressora, máquina limpa e reinicialização do Windows permanecem sem homologação.
- Política de descontos começa desligada; limites e autorização devem ser definidos pelo administrador. Bloqueio de vencidos permanece como decisão pendente.
- Não há instalador/atualizador, emissão fiscal, TEF/adquirente ou processamento de cobrança Pix. As formas de pagamento são registros locais.
- SQLite local e catálogo grande não foram submetidos a teste de carga ou operação em múltiplos terminais. foreign_keys protege as relações declaradas no esquema; tabelas legadas que não tinham constraints não são reconstruídas nesta migração.

## Prontidão atualizada

Estimativa aproximada baseada em funcionalidades e validação, não em quantidade de linhas:

| Área | Estimativa | Evidência principal |
|---|---:|---|
| PDV/financeiro | 88% | Venda, ajustes, idempotência e arredondamento cobertos; aceitação Web/hardware pendente |
| Produtos/catálogo | 82% | Cadastro, preservação dos campos e HTTP simulado; rede real pendente |
| Estoque | 88% | Quantidade, transação e auditoria testadas; equipamento/fluxo visual pendentes |
| Caixa/vendas | 90% | Abertura, fechamento, movimentos, histórico e estorno cobertos |
| Despesas | 86% | Edição/vínculo/saldo e proteção de histórico cobertos |
| Relatórios | 78% | Totais líquidos conciliados; exportação/impressão de relatórios não implementadas |
| Segurança/permissões | 85% | Autorização real no serviço e credenciais compatíveis; revisão operacional continua necessária |
| Interface geral | 65% | Ajustes em formulários e teste de construção; matriz visual incompleta |
| Distribuição/operação | 60% | Pasta x64 reproduzível; falta instalador e ensaio em máquina limpa |
| **Projeto geral** | **80%** | Avanço de confiabilidade comprovado, com homologação operacional restante |

A versão está mais próxima de um piloto local supervisionado. Não está declarada pronta para comercialização geral. As próximas prioridades são homologar a interface/equipamentos, validar catálogo com rede, testar instalação/recuperação em máquina limpa e definir as integrações e regras comerciais que a loja exige.
