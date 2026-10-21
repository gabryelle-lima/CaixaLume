# CaixaLume

CaixaLume é o nome do produto; **Mercado** continua sendo o nome inicial da loja e pode ser editado pelo administrador. O aplicativo usa C#/.NET 10, Windows Forms e WebView2, com banco SQLite no perfil do Windows (`%LOCALAPPDATA%\MercadinhoPdv\dados\mercadinho.db`). A interface é HTML/CSS/JavaScript local e usa os serviços C# pela ponte WebView2. As migrações têm versões registradas em `SchemaMigrations`, rodam em transação e criam snapshot antes de atualizar um banco existente.

## Executar

Baixe a [versão privada mais recente](https://github.com/gabryelle-lima/CaixaLume/releases), entre na sua conta GitHub e extraia o ZIP em uma pasta nova. Abra `MercadinhoPdv.exe` dentro dessa pasta, preservando a subpasta `Web`. Para executar a partir dos fontes, gere uma publicação em `artifacts/publicacao` com `scripts/Publicar.ps1`. Veja `docs/INSTALACAO_E_OPERACAO.md` para pré-requisitos, atualização e backup. O fluxo de versões privadas está descrito em `docs/ATUALIZACOES.md`.

```powershell
dotnet run
```

No primeiro acesso, crie o usuário administrador. Cadastre usuários adicionais em **Configurações → Usuários**. As senhas são derivadas com PBKDF2-SHA256 e salt aleatório. Antes de vender, abra o caixa.

## Funcionalidades

- Dashboard com faturamento, vendas, ticket médio, caixa, estoque baixo e itens próximos da validade.
- Produtos UN/KG, categorias, fornecedores, custo, preço, validade e código de barras; consulta opcional ao Open Food Facts API v3 com revisão antes de preencher os dados e cache local das imagens.
- PDV com busca por nome/código, carrinho, pagamento em dinheiro/PIX/débito/crédito, troco e comprovante para impressão ou PDF quando o Windows disponibilizar a impressora virtual.
- Dashboard, PDV, produtos, estoque, caixa, vendas, despesas, relatórios, fornecedores, configurações e usuários usam a interface integrada WebView2. O programa não alterna automaticamente para o PDV WinForms antigo.
- Vendas transacionais, histórico, detalhes e cancelamento administrativo com devolução de estoque. O cancelamento exige que o caixa vinculado ainda esteja aberto.
- Estoque com entrada, saída, ajuste, entrada de mercadoria com custo/fornecedor/documento/validade e histórico filtrável.
- Caixa com abertura, suprimento, sangria, despesa, conferência, fechamento e histórico persistente.
- Despesas pesquisáveis, editáveis e canceláveis; relatórios por período com custo, lucro bruto e margem estimados, impressão, PDF e Excel com abas por seção.
- Auditoria de ações, gerenciamento de usuários/perfis e backup/restauração do SQLite. O backup usa snapshot consistente do SQLite; a restauração valida integridade e cria uma cópia do banco atual.
- Desconto em reais ou percentual e acréscimo monetário, com política configurável pelo administrador, desabilitada por padrão.
- Edição livre de peso com precisão de 1 g, distinção de produtos vencidos/próximos e proteção contra repetição de uma mesma operação de checkout.
- Interface escura com navegação lateral e formulários com validação. Operadores podem consultar produtos e estoque; alterações administrativas permanecem protegidas.

## Validação

```powershell
dotnet build
dotnet build Tests/MercadinhoPdv.IntegrationTests/MercadinhoPdv.IntegrationTests.csproj --no-restore
dotnet run --project Tests/MercadinhoPdv.IntegrationTests/MercadinhoPdv.IntegrationTests.csproj --no-build --no-restore
```

Os cenários de integração usam um banco temporário, incluindo migração, autenticação, estoque, entrada de mercadoria, vendas, cancelamento, caixa, despesas, relatórios e backup/restauração. Para arquitetura e estado da migração visual, consulte `docs/ARCHITECTURE_AUDIT.md`, `docs/IMPLEMENTATION_STATUS.md` e `docs/UI_MIGRATION_STATUS.md`.

## Limitações para produção comercial/fiscal

O sistema ainda não emite NFC-e/NF-e nem integra com SEFAZ, SAT/MFE, adquirentes, TEF ou conciliação bancária. O PDF depende da impressora virtual do Windows; impressão física depende de uma impressora instalada. A margem usa o custo unitário gravado na venda; para vendas antigas, a migração usa o custo atual como aproximação. Ainda são necessários revisão fiscal/contábil, testes de recuperação em ambiente de implantação, política de retenção de backups e avaliação de segurança operacional antes de uso em produção.

