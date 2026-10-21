# Estado de implementação — Mercadinho PDV

Atualizado em 2026-10-18.

## Rodada atual — 18/10/2026

- Proteções de limpeza de teste, autorização no serviço, política de senha e auditoria de ajuste de estoque implementadas.
- Recuperação WinForms após falha WebView2 implementada e observada em base isolada.
- Migração v3 adiciona ajustes financeiros e idempotência do checkout, com snapshot e preservação das vendas antigas.
- Peso livre de três casas, desconto/acréscimo configuráveis e alertas de vencidos disponíveis; ajustes começam desabilitados.
- Edição de despesas corrigida; vínculo com caixa e proteção de caixas fechados testados.
- Release: 73 cenários C# e 6 JavaScript aprovados; nenhum erro/aviso C#; consulta de vulnerabilidades NuGet indisponível (NU1900).
- Publicação Windows x64 em artifacts/publicacao/Mercado-win-x64-20261018-001148.
- Validação visual completa, catálogo online, equipamentos, máquina limpa e reinicialização real permanecem pendentes.

Veja RELATORIO_RODADA_CORRECOES.md para a entrega completa e docs/VALIDACAO_MANUAL.md para aceitação. As seções seguintes preservam o registro anterior de 08/10/2026.

## Entregue no código atual

- Fluxos de produtos, estoque, fornecedores, usuários, caixa, vendas, despesas, relatórios, auditoria, configurações e backup/restauração estão presentes na aplicação.
- Venda grava venda, itens, custo unitário histórico, baixa de estoque, movimento de caixa e auditoria dentro de transação.
- Banco recebe versões registradas em `SchemaMigrations`; a versão 2 adiciona o custo histórico de item. Bancos existentes são copiados pelo backup online do SQLite antes de qualquer migração pendente.
- Backup e restauração verificam integridade e referências, criam snapshot do banco atual antes de restaurar e mantêm cópia temporária até validação.
- O nome inicial da loja é “Mercado” e pode ser alterado pelo administrador nas configurações.

## Decisões

- Manter Windows Forms nesta etapa; consultar `ARCHITECTURE_AUDIT.md` para a comparação com WebView2 e WPF.
- Preservar SQLite e sua localização padrão, sem recriar ou substituir a base do usuário.
- Não executar validação usando o banco real: testes devem usar `MERCADINHOPDV_DATABASE` e diretório temporário.

## Limitações conhecidas

- Relatórios anteriores à v2 usam o custo atual disponível no cadastro ao preencher `PrecoCustoUnitario`; não é possível reconstruir custo histórico que nunca foi armazenado.
- Camadas ainda compartilham modelos e vários formulários instanciam serviços diretamente; a extração gradual de dependências e da sessão global continua pendente.
- A suíte atual é de integração em processo, sem teste automatizado de interface.
- Não há emissão NFC-e/NF-e, integração fiscal, TEF ou adquirente.
- Impressão/PDF depende dos recursos e drivers disponíveis no Windows.
- Não foi possível confirmar visualmente os fluxos em uma sessão interativa do aplicativo nesta rodada.

## Verificação desta rodada

Validação executada em banco temporário:

- `dotnet build Tests/MercadinhoPdv.IntegrationTests/MercadinhoPdv.IntegrationTests.csproj --no-restore` — êxito, 0 avisos e 0 erros.
- `dotnet run --project Tests/MercadinhoPdv.IntegrationTests/MercadinhoPdv.IntegrationTests.csproj --no-build --no-restore` — 33 cenários passaram, incluindo migração legada, snapshot pré-migração, custo histórico e restauração.
- `dotnet build --no-restore` — êxito após corrigir a captura do custo por produto no fluxo de venda.

Não foi executada automação visual nesta rodada; a aplicação interativa não estava disponível para inspeção visual confiável.

## Próximas prioridades

1. Executar build sem restauração de pacotes e a suíte de integração.
2. Testar snapshot de migração, atualização v1→v2, custo histórico após edição de custo, e rejeição de arquivo de backup inválido.
3. Automatizar os fluxos visuais em uma conta Windows de teste.
4. Planejar composição central e extração de sessão/identidade em mudanças pequenas.
