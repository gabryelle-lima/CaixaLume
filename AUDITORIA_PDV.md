# Auditoria técnica e funcional — MercadinhoPDV

**Data:** 17/10/2026  
**Atualização de entrega em 18/10/2026:** esta auditoria permanece como fotografia anterior às correções. Consulte RELATORIO_RODADA_CORRECOES.md para o estado F-01 a F-12 após a implementação, 73 testes C# e 6 JavaScript aprovados, publicação x64 e prontidão aproximada atualizada para 80%.
**Escopo:** leitura do código, documentação e estrutura do projeto; compilação; execução da suíte de integração em banco temporário; verificação sintática dos scripts JavaScript.  
**Restrição respeitada:** nenhum código-fonte ou banco de produção foi alterado. Este documento é o único artefato novo da auditoria.

## A. Diagnóstico geral

O projeto já tem um núcleo operacional consistente para um PDV local de uma loja: cadastro de produtos, movimentação de estoque, caixa, vendas, despesas, histórico, relatórios, usuários, auditoria e backup/restauração. A venda é a parte mais bem protegida: o repositório grava venda, itens, baixa de estoque, movimentação financeira e auditoria dentro de uma transação; as verificações de integração cobrem aprovação, falta de estoque, pagamento insuficiente, troco, rollback e as formas de pagamento.

A interface está em transição. O dashboard e o PDV são páginas locais em WebView2, enquanto os demais módulos continuam em Windows Forms. Isso é funcional como etapa intermediária, mas ainda não equivale a uma experiência visual única nem teve validação interativa nesta auditoria. A documentação de migração diz que há fallback do WebView2 para o dashboard WinForms; o fluxo de inicialização atual, porém, mostra erro e encerra o aplicativo.

**Conclusão:** há uma base boa para um piloto local controlado, mas o estado atual não é suficiente para declarar o produto pronto para uso comercial. Os riscos prioritários são a ferramenta de limpeza de histórico acessível na configuração normal, o fallback de inicialização que não acontece, a falta de validação visual de ponta a ponta e os requisitos de distribuição/operação ainda não fechados. Também faltam integrações fiscais e com adquirente/TEF; os meios Pix e cartão representam o registro da forma de pagamento, não uma cobrança integrada.

### Validação executada

- Compilação do projeto de integração, que também compila o aplicativo referenciado: êxito, 0 avisos e 0 erros, com UseAppHost=false.
- Primeira execução da suíte usando o diretório temporário padrão foi bloqueada por uma restrição de movimentação de arquivos do ambiente durante o cenário de backup. A suíte foi repetida com diretório temporário dentro da área do projeto: **33 de 33 cenários passaram**.
- Os cenários usam banco temporário isolado, incluindo migração, usuários, produtos UN/KG, duplicidade de código de barras, estoque, venda, pagamento/troco, caixa, despesas, relatório, cancelamento e backup/restauração. O banco real não foi usado.
- node --check passou para Web/app.js, Web/bridge.js, Web/home.js e Web/ui.js. Isso confirma sintaxe, não comportamento de interface.
- Não foi possível validar nesta auditoria a abertura e operação da interface em uma sessão visual real, o scanner físico, a consulta online ao Open Food Facts ou uma impressora. A documentação do projeto também registra que uma tentativa anterior de renderização WebView2 encerrou os processos de navegador/renderização antes de gerar uma captura confiável.

## B. Funcionalidades existentes

| Módulo | Funcionalidade | Situação | Observações |
|---|---|---|---|
| Inicialização | Login, primeiro administrador e abertura do shell | Parcial | Inicialização do administrador e autenticação são exercitadas nos testes. Se o WebView2 falhar, o fluxo de “fallback WinForms” não inicia o formulário antigo; ver F-02. |
| Dashboard | Faturamento do dia, vendas, ticket médio, caixa e estoque baixo | Funcional | Consulta implementada em DashboardService.ObterResumo e cobertura de integração. Sem validação visual nesta rodada. |
| Dashboard | Alertas de validade | Parcial | Mostra itens que vencem de hoje até 30 dias. A consulta exclui produtos com validade anterior a hoje; itens já vencidos não aparecem como alerta. |
| PDV | Pesquisa por nome/código e carrinho | Parcial | A interface filtra por código de barras, mas ao pressionar Enter tenta adicionar apenas por nome ou ID; leitura de scanner não inclui o código automaticamente (F-12). |
| PDV | Venda em dinheiro, Pix, débito e crédito; troco | Funcional no serviço | Os quatro tipos são validados e registrados; dinheiro exige valor recebido suficiente. Os testes cobrem troco e movimentos. Não há integração com adquirente/TEF/Pix. |
| PDV | Persistência atômica e baixa de estoque | Funcional no serviço | Venda, itens, estoque, caixa e auditoria ficam na mesma transação; falha intermediária e falta de estoque são testadas com rollback. |
| PDV | Venda de produto por KG | Parcial | Tanto a tela Web quanto PdvForm incrementam em blocos fixos de 250 g. O serviço aceita até 1 g, mas a tela não permite informar livremente a pesagem que não caia nesses passos. |
| PDV | Descontos e acréscimos | Não implementado | O resumo do carrinho mostra uma linha “Desconto” estática; o total da venda é apenas a soma dos subtotais. Não há valor de desconto/acréscimo no contrato da ponte nem na confirmação. |
| PDV | Máscara de valor recebido e uso por teclado | Parcial | O JavaScript formata a entrada por centavos e há atalhos F2/F4. A sintaxe foi verificada; a interação real de digitação/seleção/foco e escalas do Windows não foi exercitada visualmente. |
| Produtos | Inclusão, edição, desativação, UN/KG, preço, custo e validade | Funcional no serviço | Cadastro, edição, unidade, normalização de texto e validação de duplicidade têm cobertura de integração. Formulário visual não verificado nesta rodada. |
| Produtos | Consulta de código de barras no Open Food Facts | Não verificado externamente | O serviço chama a API v3, trata respostas de sucesso em formatos numérico e textual e devolve dados para revisão. Nenhuma chamada de rede foi executada nesta auditoria; a queixa anterior de que a busca não funciona permanece sem reprodução conclusiva. |
| Estoque | Entrada, saída, ajuste, entrada de mercadoria e histórico | Parcial | Fluxos de serviço e histórico são testados. Ajuste de saldo registra movimentação, mas não grava evento na auditoria geral (F-07). |
| Estoque | Estoque baixo e produtos vencidos | Parcial | Estoque baixo aparece no dashboard e em relatórios. Não há alerta próprio de produtos vencidos no dashboard; apenas itens dentro do período filtrado podem aparecer no relatório. |
| Caixa | Abertura, suprimento, sangria, despesa, fechamento e histórico | Funcional no serviço | Cenários de abertura, venda vinculada, dinheiro esperado, sangria, despesas e fechamento passam na integração. Não foi feita operação visual em caixa real. |
| Caixa | Limpeza de histórico para testes | Com risco de uso | Botão está em Configurações e requer administrador/confirmação, mas aparece no aplicativo normal. Remove todos os caixas e movimentações e desvincula vendas/despesas dos caixas. Ver F-01. |
| Vendas | Histórico, detalhes e cancelamento com estorno de estoque | Funcional no serviço | Cobertura confirma cancelamento sem apagar a venda e restauração de estoque. O cancelamento é restringido quando o caixa vinculado já está fechado. Interface não verificada visualmente. |
| Despesas | Pesquisa, edição, cancelamento e registro de auditoria | Funcional no serviço | Cadastro e cancelamento são transacionais e cobertos pela integração. A tela continua em Windows Forms. |
| Relatórios | Período, pagamentos, produtos, custo/margem estimados, estoque e caixas | Parcial | Consultas e relatório de período têm cobertura. Não foram encontrados comandos de exportação/impressão no formulário de relatórios; esses recursos não aparecem como entregues. |
| Categorias/fornecedores | Cadastro, edição e desativação | Funcional no serviço | Serviços e formulários existem; aparência e fluxo interativo não foram testados nesta rodada. |
| Usuários/permissões | Administrador, operador, senha derivada e restrições de tela | Parcial | PBKDF2-SHA256 com salt aleatório e verificação em tempo constante. Política de senha inicial diverge da política para usuários novos (F-08); validação de permissões ocorre principalmente na navegação da interface. |
| Configurações | Nome da loja, auditoria e backup/restauração | Funcional no serviço | Backup/restauração com validação e proteção do banco anterior são exercitados na integração. O nome inicial é “Mercado” e pode ser alterado. |
| Distribuição | Instalador e rotina de atualização | Não implementado/não localizado | Há projeto .NET e cópia dos arquivos Web para saída/publicação, mas não foi encontrado projeto ou manifesto de instalador/atualização. A publicação final e os pré-requisitos do computador de destino não foram validados. |

## C. Problemas identificados

### F-01 — Limpeza destrutiva de caixas de teste disponível no aplicativo normal

- **Gravidade:** Alta — risco de perda de histórico operacional.
- **Local:** Forms/ConfiguracoesForm.cs, construtor e ClearCashHistory; Services/CaixaService.cs, LimparHistoricoParaTestes.
- **Causa:** a ação foi criada para facilitar testes e está exposta na tela normal de configurações. A confirmação reduz erro acidental, mas não limita a operação a um banco de teste.
- **Efeito:** apaga todos os registros de caixa e movimentos de caixa; vendas e despesas permanecem, mas perdem o vínculo com o caixa. Uma execução em dados reais compromete reconciliação e histórico.
- **Recomendação:** condicionar a opção a uma configuração de desenvolvimento/teste que não exista na publicação comercial, ou removê-la do produto publicado. Se mantida em ambiente de teste, exigir backup validado e deixar explícita a identificação do banco de testes.

### F-02 — Fallback do WebView2 fecha o aplicativo em vez de abrir o dashboard antigo

- **Gravidade:** Alta — pode impedir a inicialização do sistema.
- **Local:** Forms/AppWebViewForm.cs, Fallback; Program.cs, fluxo após AppWebViewForm.ShowDialog.
- **Causa:** Fallback fecha a janela com DialogResult.Retry. Program.cs recebe Retry, mostra mensagem de erro e sai do laço; não instancia MainForm. A documentação UI_MIGRATION_STATUS.md:11 promete que o dashboard WinForms será usado como fallback.
- **Efeito:** se WebView2, os arquivos locais da interface ou o renderer falharem, o usuário não chega ao dashboard alternativo descrito na documentação.
- **Recomendação:** implementar de fato a abertura do MainForm no caminho de erro e validar em uma máquina sem o runtime WebView2/sem os assets locais. Se não houver fallback pretendido, corrigir a documentação e mostrar uma instrução de recuperação clara.

### F-03 — Integridade de chaves estrangeiras depende de cada serviço lembrar de ativá-la

- **Gravidade:** Média.
- **Local:** Data/DatabaseConnection.cs:18-21; Data/DatabaseInitializer.cs:15; Services/CaixaService.cs; Services/DespesasService.cs; Serviços que abrem DatabaseConnection.
- **Causa:** PRAGMA foreign_keys é ligado durante a inicialização e em alguns repositórios/serviços, mas não no construtor de DatabaseConnection. No SQLite, a configuração vale para a conexão atual, não para todas as futuras. A busca encontrou ativações explícitas em venda, produto, estoque e inicialização, e ausência delas nos fluxos de caixa e despesa.
- **Efeito:** constraints declaradas no esquema não oferecem proteção uniforme quando uma conexão desses fluxos grava referência inválida ou um novo caminho de exclusão é adicionado. Não foi reproduzida inconsistência nas operações testadas.
- **Recomendação:** ativar foreign_keys em toda conexão recém-aberta e incluir uma verificação de integridade referencial na suíte e no diagnóstico/backup.

### F-04 — Interface do PDV limita peso a incrementos de 250 g

- **Gravidade:** Média — pode impedir a venda pelo peso indicado na balança.
- **Local:** Web/app.js, addProduct e changeQuantity; Forms/PdvForm.cs, inclusão e alteração de quantidade.
- **Causa:** ambas as interfaces usam incremento fixo de 0,250 kg; não existe campo de quantidade/peso livre no carrinho. O serviço de venda valida precisão de até três casas, mas isso não chega ao operador pela tela.
- **Efeito:** uma pesagem como 0,315 kg não pode ser lançada exatamente pelos controles disponíveis.
- **Recomendação:** permitir informar o peso real até 0,001 kg ou integrar uma balança compatível, mantendo validação de estoque e preço na confirmação.

### F-05 — Produtos vencidos ficam fora do alerta operacional do dashboard

- **Gravidade:** Média.
- **Local:** Services/DashboardService.cs, ObterResumo.
- **Causa:** a consulta de validade filtra DataValidade a partir da data de hoje e até 30 dias no futuro; registros anteriores a hoje são excluídos.
- **Efeito:** o operador não recebe no dashboard um sinal imediato de produto já vencido, embora o requisito de estoque peça alerta de produtos vencidos.
- **Recomendação:** separar itens vencidos de itens próximos do vencimento e destacá-los com status e quantidade; validar como o produto deve ser bloqueado na venda.

### F-06 — Falha do Open Food Facts não foi reproduzida nem validada ao vivo

- **Gravidade:** Média como pendência de validação; não é defeito confirmado nesta auditoria.
- **Local:** Services/OpenFoodFactsService.cs, BuscarPorCodigoAsync; Forms/ProdutoForm.cs, busca de catálogo.
- **Causa/limite da auditoria:** o código remove caracteres não numéricos, aceita códigos com 8 a 14 dígitos, consulta API v3 com timeout de 20 segundos e procura nome/marca/categoria/imagem. Não foi feita chamada de rede, portanto não há confirmação do resultado para o código 3017620425035 nem da disponibilidade atual do serviço.
- **Efeito:** a consulta pode depender de rede, de produto existente no catálogo público ou de resposta da API; a falha reportada anteriormente continua em aberto.
- **Recomendação:** verificar com rede disponível, testar código encontrado, código ausente, timeout e resposta de erro, e validar que o usuário ainda consegue preencher o produto manualmente quando não houver resultado.

### F-07 — Ajuste de saldo não entra na auditoria geral

- **Gravidade:** Média — lacuna de rastreabilidade.
- **Local:** Services/EstoqueService.cs, AjustarSaldo, especialmente linhas 58-62.
- **Causa:** AjustarSaldo grava a movimentação de estoque e confirma a transação, mas não insere linha em Auditoria. RegistrarMovimentacao e RegistrarEntradaMercadoria registram a ação.
- **Efeito:** a tela de auditoria não explica quem ajustou o saldo ou por qual motivo; existe uma movimentação no histórico de estoque, mas falta a trilha uniforme de operações sensíveis.
- **Recomendação:** registrar no mesmo transaction o usuário, produto, saldo anterior, saldo novo, diferença e motivo.

### F-08 — Regras de senha diferentes para o primeiro administrador e usuários seguintes

- **Gravidade:** Média.
- **Local:** Services/UsuarioService.cs, CriarAdministradorInicial e Salvar.
- **Causa:** o primeiro administrador aceita senha de 6 caracteres; Salvar exige 10 caracteres para novo usuário e troca de senha. O hash usa PBKDF2-SHA256 e 210.000 iterações.
- **Efeito:** a credencial mais privilegiada pode ter política inferior à dos demais usuários e a regra não é previsível para quem configura a aplicação.
- **Recomendação:** adotar a mesma política mínima e validar também casos de espaços e senhas longas nos testes de autenticação.

### F-09 — Cobertura automatizada não valida as telas e o hardware

- **Gravidade:** Média — risco de defeitos de fluxo visual permanecerem sem detecção.
- **Local:** Tests/MercadinhoPdv.IntegrationTests/Program.cs; Web/app.js; Forms/PdvForm.cs e formulários Windows Forms.
- **Causa:** a suíte é um executável de cenários de integração de serviço/banco. Os scripts tiveram somente checagem sintática nesta auditoria; não há automação da interface.
- **Efeito:** os 33 cenários passam, mas isso não comprova posicionamento dos controles, funcionamento de foco/atalhos, máscara em digitação real, WebView2, leitor físico, impressora ou consulta externa.
- **Recomendação:** antes de liberar, executar roteiro manual documentado e automatizar pelo menos os fluxos de venda, caixa, salvamento de produto e visualização em escalas/resoluções representativas.

### F-10 — Distribuição pronta para usuário final não foi comprovada

- **Gravidade:** Média para implantação comercial.
- **Local:** MercadinhoPdv.csproj e arquivos da raiz do projeto.
- **Causa:** o projeto compila o aplicativo Windows e copia os assets Web, mas não foi encontrado instalador, atualizador ou pacote de publicação validado. O projeto também depende do ambiente Windows e do runtime WebView2 para a interface atual.
- **Efeito:** não há evidência de uma instalação repetível, com pré-requisitos, atalho, localização do banco, atualização e recuperação suportados em computador limpo.
- **Recomendação:** definir publicação Windows x64, validar os runtimes .NET/WebView2 no destino, criar instalador e documentar backup, atualização e restauração.

### F-11 — A linha “Desconto” do carrinho é apenas visual

- **Gravidade:** Baixa a média, conforme o processo de venda planejado.
- **Local:** Web/index.html, resumo do carrinho; Web/app.js e Services/PdvWebBridge.cs, fluxo de checkout.
- **Causa:** existe um rótulo com R$ 0,00, mas o total é calculado como soma dos itens. Não há campo, regra, validação ou dado persistido de desconto/acréscimo.
- **Efeito:** o operador pode esperar um recurso que não altera a venda; o requisito de aplicar descontos/acréscimos não está atendido.
- **Recomendação:** ou implementar a política de desconto/acréscimo com limites, autorização, persistência e auditoria, ou remover o rótulo até a funcionalidade existir.

### F-12 — Enter no campo de busca do PDV não adiciona pelo código de barras

- **Gravidade:** Média — o uso do leitor como entrada de teclado não conclui o fluxo esperado de leitura rápida.
- **Local:** Web/app.js, manipulador keydown do campo product-search (linha 175).
- **Causa:** a filtragem da grade considera product.barcode, mas a lógica acionada com Enter procura correspondência exata de nome/ID e, depois, nomes parciais. Ela não compara o texto digitado com o código de barras.
- **Efeito:** com um leitor que digita o EAN e envia Enter, o produto pode aparecer filtrado, porém não é incluído automaticamente no carrinho. Não foi possível exercitar um leitor físico nesta auditoria; a inconsistência entre filtro e ação está presente no código.
- **Recomendação:** tratar código exato como correspondência prioritária no Enter, incluir o produto se houver um único resultado e cobrir com teste de interface usando código conhecido, código ausente e código duplicado.

## D. Funcionalidades pendentes

### Requisitos descritos no pedido ou na documentação do projeto

1. **Proteção do caixa de teste:** impedir que o botão de limpeza seja usado em dados de produção.
2. **Fallback confiável do WebView2:** abrir um caminho utilizável quando a interface nova não iniciar.
3. **Leitura de código no PDV:** incluir automaticamente o produto ao pressionar Enter após leitura do código de barras.
4. **Operação por peso real:** permitir quantidade exata até 0,001 kg ou integrar balança, não apenas passos de 250 g.
5. **Descontos/acréscimos:** completar o rótulo que já aparece no PDV, com política, limite, autorização e trilha.
6. **Alerta de vencidos:** mostrar separadamente produtos vencidos e próximos do vencimento.
7. **Open Food Facts:** validar a busca online para o código reportado e manter cadastro manual como alternativa.
8. **Permissões e senhas:** padronizar política de credenciais e assegurar que operações administrativas tenham verificação consistente.
9. **Rastreabilidade de ajuste de estoque:** criar registro na auditoria geral.
10. **Verificação de interface:** validar venda, cadastro e histórico em execução real, incluindo resolução, escala, foco, teclado e erros.
11. **Distribuição:** preparar e validar publicação/instalação em uma máquina Windows limpa.
12. **Relatórios:** manter filtros por período; exportação e impressão foram citadas como condicionais no pedido, mas não aparecem como capacidade entregue.
13. **Fiscal e pagamentos:** documentação declara inexistência de NFC-e/NF-e, integração fiscal, adquirente/TEF ou conciliação. O app registra o meio de pagamento, mas não processa a cobrança.

### Melhorias sugeridas, além do escopo já registrado

- Tela rápida para cadastrar um produto ausente sem abandonar o fluxo do PDV.
- Cadastro de clientes, histórico por cliente, fidelidade e limites de venda, caso o negócio necessite.
- Exportação CSV/PDF e impressão de relatórios, com totais conciliáveis com vendas, despesas e caixa.
- Bloqueio/alerta de venda de produto vencido e conferência periódica de validade.
- Política operacional de backup automático, retenção, teste periódico de restauração e suporte a atualização.
- Autorização no serviço para operações administrativas, além da restrição visual no menu.
- Estratégia de banco para várias estações. A configuração atual aponta para SQLite local no perfil Windows; o uso simultâneo em vários terminais não foi demonstrado.
- Métricas/logs para falhas de WebView2, consulta de catálogo, restauração e erros de venda, com procedimentos simples de diagnóstico.

## E. Plano de desenvolvimento

| Fase/atividade | Prioridade | Dificuldade | Dependências |
|---|---|---:|---|
| **Fase 1 — Correções críticas** ||||
| Remover/condicionar a limpeza de caixa à base de testes e validar backup antes da operação | P0 | Baixa | Definir como a aplicação identifica ambiente de testes |
| Corrigir a recuperação de inicialização WebView2 e testar renderer/runtime ausente | P0 | Média | Windows de teste e decisão do fluxo de fallback |
| Habilitar chaves estrangeiras por conexão e verificar integridade em todos os fluxos transacionais | P1 | Baixa | Suíte em banco isolado |
| Registrar ajuste de saldo na auditoria e unificar política de senha | P1 | Baixa | Testes de regressão de estoque/usuários |
| Fazer validação manual de venda, salvamento de produto, caixa/histórico e máscara monetária | P0 | Média | Conta e banco de teste; sessão Windows interativa |
| **Fase 2 — Funcionalidades essenciais** ||||
| Permitir quantidade real de KG até três casas decimais ou integrar balança | P1 | Média | Escolha de hardware e regra de arredondamento |
| Corrigir Enter para adicionar produto encontrado por código de barras | P1 | Baixa | Teste de interface ou leitor em modo teclado |
| Mostrar produtos vencidos e definir bloqueio/alerta de venda | P1 | Média | Regras operacionais do estabelecimento |
| Validar Open Food Facts com rede, código conhecido, ausente, timeout e erro | P1 | Baixa | Internet no ambiente de teste |
| Concluir reconciliação de valores entre venda, caixa, pagamento, despesa e fechamento | P1 | Média | Roteiro operacional e casos com dinheiro/PIX/cartão |
| **Fase 3 — Interface e usabilidade** ||||
| Validar WebView2 e formulários em 1024×768, 1280×720, 1366×768, 1440×900, 1920×1080 e escalas 125%/150% | P1 | Média | Máquina Windows interativa e roteiro de QA |
| Padronizar foco, atalhos, teclado, mensagens e navegação nos formulários restantes | P2 | Média | Inventário de telas e feedback de operador |
| Implementar desconto/acréscimo ou remover a apresentação estática do carrinho | P1 | Média | Regras de autorização e limites comerciais |
| Exportar/imprimir relatórios se fizer parte do processo do mercado | P2 | Média | Definição dos modelos de relatório |
| **Fase 4 — Recursos avançados** ||||
| Integração fiscal NFC-e/NF-e aplicável ao estabelecimento | P1 antes de comercializar nesse cenário | Alta | Contabilidade, certificado, credenciamento e provedor compatível |
| Integração TEF/adquirente/Pix e conciliação | P2 | Alta | Fornecedor, homologação e hardware |
| Instalador, atualização, recuperação e suporte para computador limpo | P1 | Média | Alvo Windows, runtime .NET/WebView2 e política de dados |
| Clientes, fidelidade, múltiplas lojas/terminais e automações | P3 | Alta | Requisitos do negócio e decisão de arquitetura |

## F. Avaliação de prontidão

Estimativas aproximadas, não métricas de código. Consideram existência do fluxo, regra de negócio, persistência, testes automatizados e grau de validação da interface/distribuição.

| Área | Conclusão estimada | Critério principal |
|---|---:|---|
| Dashboard | 75% | Consultas e integração existentes; visual não validado |
| PDV e pagamentos registrados | 70% | Venda transacional testada; peso exato, descontos, UI e hardware pendentes |
| Produtos/cadastro | 75% | CRUD, validação e unidades testados; API externa e tela não verificados ao vivo |
| Estoque | 70% | Movimentações e entrada testadas; alertas de vencidos e auditoria do ajuste pendentes |
| Caixa | 70% | Abertura, movimentações e fechamento testados; ferramenta de limpeza é risco de release |
| Vendas/histórico | 80% | Fluxos centrais e cancelamento cobertos; interface não validada |
| Despesas | 75% | Persistência e cancelamento cobertos; operação visual não validada |
| Relatórios | 65% | Consultas e filtros disponíveis; exportação/impressão e conferência visual pendentes |
| Categorias/fornecedores | 80% | Fluxos presentes e serviços disponíveis; QA visual pendente |
| Segurança/usuários | 65% | Hash e autenticação presentes; regra de senha e autorização em camadas a uniformizar |
| Interface geral | 45% | Dashboard/PDV migrados para WebView2; demais telas WinForms e QA visual pendente |
| Instalação/operação em produção | 25% | Projeto executável existe; pacote, pré-requisitos, atualização e rotina de suporte não comprovados |
| **Projeto geral** | **66%** | Backend operacional relativamente maduro, com validação visual e preparação de release em aberto |

### Respostas de prontidão

- **Já pode ser utilizado em um mercadinho real?** Não recomendo liberar como produto pronto. Os serviços centrais passam nos cenários automatizados, mas não houve confirmação visual ponta a ponta e permanecem riscos no botão de limpeza, na recuperação do WebView2, nos pesos, na validade e na distribuição. Um piloto interno supervisionado só deve ocorrer depois das correções P0 e da validação no banco de teste.
- **Riscos que impedem o uso:** perda semântica do histórico por limpeza, aplicativo sem fallback quando WebView2 falha, ausência de teste visual em execução real, quantidade de KG limitada, alertas de vencimento incompletos, consulta Open Food Facts não verificada, instalação e restauração não ensaiadas em máquina limpa.
- **Menor conjunto para um MVP operacional local:** retirar/guardar a limpeza de testes; corrigir o caminho de inicialização; executar roteiro visual completo em banco de teste; validar scanner e entrada de peso; conferir uma venda de cada meio, estoque e fechamento; habilitar integridade do SQLite em todas as conexões; testar backup/restauração; documentar pré-requisitos e procedimento de recuperação.
- **Antes de disponibilizar comercialmente:** cumprir as fases 1–3, empacotar e testar instalação/atualização/backup em computador limpo, fazer revisão de segurança e permissões, fechar operação de suporte e recuperação, definir os meios de pagamento e validação fiscal necessários para o estabelecimento. A documentação confirma que emissão fiscal e processamento integrado de pagamentos não existem atualmente.

## Referências principais

- Inicialização e interface: Program.cs; Forms/AppWebViewForm.cs; docs/UI_MIGRATION_STATUS.md.
- Caixa e configuração de teste: Services/CaixaService.cs; Forms/ConfiguracoesForm.cs.
- Banco e integridade referencial: Data/DatabaseConnection.cs; Data/DatabaseInitializer.cs; Services/DespesasService.cs.
- Venda e ponte do PDV: Services/VendaService.cs; Data/VendaRepository.cs; Services/PdvWebBridge.cs; Web/app.js; Web/index.html.
- Produtos, catálogo e estoque: Services/OpenFoodFactsService.cs; Forms/ProdutoForm.cs; Services/EstoqueService.cs; Services/DashboardService.cs.
- Cobertura: Tests/MercadinhoPdv.IntegrationTests/Program.cs.
- Limitações declaradas pelo projeto: README.md e docs/IMPLEMENTATION_STATUS.md.
