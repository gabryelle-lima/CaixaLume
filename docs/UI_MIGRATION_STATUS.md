# Migração da interface — MercadinhoPDV

Atualizado em 2026-10-20.

## Estado atual

O padrão visual é o shell WebView2. Visão geral, PDV, Produtos e Relatórios abrem como páginas integradas. Produtos oferece cadastro, imagem com prévia e remoção. Relatórios oferece impressão, PDF e Excel. Estoque, Caixa, Vendas, Despesas, Fornecedores, Configurações e Usuários ainda usam formulários WinForms durante a migração. O início do programa não abre o dashboard/PDV WinForms automaticamente quando WebView2 falha; mostra uma mensagem e encerra a interface. Os registros a seguir descrevem a rodada histórica de 18/10/2026 e não representam o estado atual.

## Atualização da estabilização

Dashboard/PDV Web e os demais módulos WinForms foram preservados. O fallback agora abre efetivamente a alternativa WinForms, uma vez, com a sessão existente. Falha real de inicialização do WebView2 foi observada na base de inspeção, seguida da abertura nativa.

Carrinho ganhou peso editável, cálculo consistente em centavos, ajustes configuráveis e proteção de operação pendente. Enter prioriza código exato; modais de caixa/pagamento têm fechamento independente do envio. As telas nativas receberam rolagem, limites pela área de trabalho e revisão de resumos/cabeçalhos.

O cenário nativo de 0,315 kg por R$ 19,90/kg apresentou R$ 6,27. Isso não aprova toda a interface: capturas/elementos ficaram instáveis, e a renderização WebView2 completa não foi homologada. O teste de construção de 13 formulários em cinco tamanhos também não é aprovação visual. A matriz 125%/150% e os demais fluxos estão em docs/VALIDACAO_MANUAL.md.

A lógica Web possui 6 testes funcionais de domínio/ponte, além da checagem de sintaxe. Os registros abaixo descrevem a migração anterior de 08/10/2026.

## Entregue nesta etapa

- Infraestrutura local WebView2 com HTML, CSS e JavaScript copiados para a saída/publicação; nenhuma fonte, biblioteca ou servidor remoto é necessário em execução.
- Ponte C# versionada (`version`, `id`, `action`, `payload`) com allowlist de ações, limite de mensagem, DTOs explícitos, validação de origem virtual local e resposta de erro.
- PDV web usa catálogo, categorias, dados de sessão e status do caixa reais; carrinho, busca por nome/código, quantidade, pagamento em dinheiro/PIX/débito/crédito, troco, abertura do caixa e conclusão passam pelos serviços existentes.
- A tela usa imagens locais em data URI com extensões permitidas e limite de 2 MB; sem requisição de rede para recursos visuais.
- Dashboard e PDV abrem pelo shell WebView2 após o login. Se a inicialização WebView2 falhar, o dashboard WinForms antigo é usado como fallback.
- CSS responsivo com catálogo/carrinho, rolagem independente, rodapé fixo do carrinho e modais próprios.

## Não migrado

- Login, produtos, estoque, caixa completo, histórico/cancelamento de vendas, despesas, relatórios, fornecedores, configurações e usuários ainda são telas WinForms.
- A navegação do shell abre os formulários WinForms desses módulos até que cada tela web seja migrada.
- React + TypeScript + Vite não foi usado: Node está disponível, mas npm/Vite não está instalado. A implementação usa JavaScript modular sem etapa de build.

## Validação

- `dotnet build Tests/MercadinhoPdv.IntegrationTests/MercadinhoPdv.IntegrationTests.csproj --no-restore` — êxito, 0 avisos e 0 erros.
- `dotnet run --project Tests/MercadinhoPdv.IntegrationTests/MercadinhoPdv.IntegrationTests.csproj --no-build --no-restore` — 33 cenários de integração passaram, com banco temporário.
- `node --check` passou para `Web/app.js`, `Web/bridge.js` e `Web/ui.js`.
- Teste de renderização WebView2 foi tentado com catálogo simulado, mas os processos GPU/renderer/browser encerraram antes de completar a navegação. Nenhuma screenshot confiável foi gerada; nenhuma resolução ou escala foi declarada visualmente aprovada.

## Próximo passo

Reproduzir a abertura do WebView2 em uma sessão Windows interativa compatível e capturar/verificar 1366×768, 1440×900, 1920×1080, 1280×720, 1024×768 e escalas de 125%/150%. Depois migrar produtos/estoque, caixa, vendas e demais telas, mantendo os formulários antigos até validar cada fluxo.
