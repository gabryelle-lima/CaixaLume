# Auditoria de arquitetura — Mercadinho PDV

Data da avaliação: 2026-10-08.

Atualização de implementação em 18/10/2026: a rodada de estabilização manteve a aplicação híbrida existente (dashboard/PDV WebView2 e módulos WinForms), acrescentou fallback funcional, autorização por usuário ativo no serviço, foreign_keys centralizado, cálculo financeiro compartilhado e migração v3. Não houve troca de framework ou separação em novos projetos. A análise abaixo permanece como registro da decisão anterior; o estado atual e as pendências estão em RELATORIO_RODADA_CORRECOES.md.

## Estrutura atual

- Aplicativo desktop único em .NET 10 e Windows Forms; não há solução `.sln` nem projetos separados para domínio, aplicação, dados e interface.
- `Forms/` implementa telas e navegação. Formulários criam serviços diretamente, portanto a composição e o ciclo de vida das dependências ficam distribuídos pela UI.
- `Services/` concentra regras e casos de uso, mas algumas classes também abrem o banco diretamente; outras delegam a repositórios em `Data/`.
- `Data/` contém repositórios SQLite, inicialização do banco e acesso nativo por `winsqlite3.dll`. O caminho do banco pode ser substituído por `MERCADINHOPDV_DATABASE`, útil para testes isolados.
- `Models/` contém modelos usados tanto pela interface quanto pelos serviços. `SessaoUsuario` é estado estático global.
- `Tests/MercadinhoPdv.IntegrationTests/` é um executável de integração com banco temporário, não uma biblioteca de testes convencional.

Fluxo representativo: `FormPDV → VendaService → VendaRepository → DatabaseConnection`; outros formulários seguem `Form → Service → DatabaseConnection` diretamente. A validação de regras existe nos serviços e no caminho transacional da venda, mas a fronteira não é uniforme.

## O que já oferece boa base

- Venda, baixa de estoque, movimento financeiro e auditoria são gravados na mesma transação SQLite.
- Senhas são derivadas por PBKDF2-SHA256 com salt aleatório; o acesso administrativo tem verificações nos serviços relevantes.
- Integrações externas e imagens são opcionais; o PDV continua local e não depende de rede para vender.
- O banco pode ser isolado em testes por variável de ambiente.
- O acesso ao SQLite usa a biblioteca nativa do Windows e evita dependência NuGet em execução.

## Riscos e custo de manutenção

- A interface instancia serviços diretamente; não há composição central, interfaces para infraestrutura ou substituição simples de dependências.
- Regras SQL, autorização e cálculos ficam espalhados entre serviços e repositórios. `SessaoUsuario` dificulta testes paralelos e torna a autorização dependente de estado global.
- O schema era atualizado por `EnsureColumn` durante a inicialização sem histórico explícito de versões. O código agora registra versões 1 e 2 em `SchemaMigrations`, aplica cada alteração dentro de transação e cria snapshot antes de migrar um banco preexistente.
- Inicialização inclui normalização e consolidação de cadastros, misturando migração estrutural com reparo de dados. Essas rotinas precisam ganhar migrações nomeadas e relatório de alterações conforme o schema crescer.
- A implementação SQLite é uma ponte P/Invoke feita no projeto; é pequena, mas exige atenção a códigos de retorno, compatibilidade com `winsqlite3.dll`, fechamento de handles e transações.
- Os testes cobrem fluxos integrados mas ainda não substituem testes unitários de regra nem um fluxo visual automatizado.

## Opções de interface

| Opção | Vantagens | Custo e risco neste produto |
|---|---|---|
| WinForms | Já implementado, inicialização simples, impressão/diálogos Windows e sem runtime web adicional | Designer e controles podem dificultar modernização visual; requer consolidar padrões e desacoplar os formulários |
| WebView2 | Interface web flexível e melhor reutilização de componentes visuais | Adiciona runtime WebView2, ponte JavaScript/C#, empacotamento e novos modos de falha; requer preservar acesso local/offline e fluxos de impressão |
| WPF | Data binding, templates e estilos nativos mais expressivos | Reescrita da camada de UI, migração de telas e testes; ganho limitado enquanto serviços e dependências seguem acoplados |

## Decisão recomendada

Manter WinForms nesta rodada. O PDV já é um aplicativo Windows local, usa recursos do sistema e não precisa de rede para o fluxo principal. Uma troca de UI antes de separar casos de uso e dados amplia o risco e não corrige o acoplamento atual. Consolidar uma composição central, reduzir dependência de estado global, definir fronteiras de serviço e estabilizar contratos de banco primeiro; reavaliar WebView2 somente se existir requisito concreto de UI web ou suporte multiplataforma.

## Mudanças estruturais aplicadas nesta rodada

- Backup/restauração migrados de cópia simples de arquivo para a API de backup do SQLite, com validação de `integrity_check` e `foreign_key_check`, substituição por arquivo temporário e cópia de segurança antes de restaurar.
- `SchemaMigrations` agora registra versões e executa alterações em transação, criando backup antes de migrar base existente.
- Migração v2 adiciona `ItensVenda.PrecoCustoUnitario`; vendas novas registram o custo praticado no instante da venda e relatórios calculam margem com o snapshot. Em linhas antigas, o custo disponível no cadastro atual é usado como aproximação, pois o histórico original não existia.

## Próximas etapas arquiteturais

1. Introduzir uma composição central de dependências sem reescrever as telas.
2. Extrair abstrações de sessão/identidade e tempo para remover estado global de fluxos críticos.
3. Agrupar SQL por repositório e tirar acesso direto ao banco dos serviços que ainda o abrem.
4. Transformar correções de dados em migrações explícitas com contagem/log das linhas alteradas.
5. Adicionar testes de unidade e automação visual em ambiente Windows com dados descartáveis.
