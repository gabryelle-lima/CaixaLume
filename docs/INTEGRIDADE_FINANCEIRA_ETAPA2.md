# Etapa 2 — integridade financeira do CaixaLume PDV

Validação em 25/10/2026, .NET SDK 10.0.400 / Windows / SQLite nativo.

## Resultado final

- Compilação: sucesso, zero erros e zero avisos.
- Segurança: 13/13 cenários aprovados, novamente após as alterações financeiras.
- Financeiro: 20/20 cenários aprovados.
- Integração geral: 73/73 cenários aprovados, incluindo backup/restauração e construção dos formulários WinForms.
- Na execução final não houve interrupções nem testes ignorados. Execuções anteriores interrompidas não foram contabilizadas como aprovadas.
- Os testes usam bancos temporários e cópias de fixtures; nenhum banco de operação do usuário foi recriado ou migrado durante a validação.

Logs completos: [build](validation-financeira/build.log), [segurança](validation-financeira/seguranca.log), [financeiro](validation-financeira/financeiro.log), [geral](validation-financeira/geral.log).

## Preservação da etapa 1

`UsuarioService.cs` e `Autorizacao.cs` mantêm exatamente os bytes da entrada desta etapa, verificados por SHA-256. A captura está em `Tests/security-stage1-baseline.json`; ela identifica o estado anterior, não os hashes esperados da versão final.

O bloco da migração v6 foi preservado. A migração incremental v7 acrescenta `PagamentosVenda` e mantém os gatilhos v6, usuários, hashes e configurações de permissões. Faz snapshot pré-migração pelo mecanismo existente. Pagamentos antigos reconhecidos são classificados como registros manuais, sem inventar uma confirmação bancária.

Os 13 cenários e suas asserções de segurança foram mantidos. A fixture que reexecuta a v6 passou a remover os marcadores `Version>=6`, pois deixar o marcador v7 impediria essa reexecução. Nenhuma proteção foi relaxada.

Duas fixtures antigas da suíte geral também precisavam de atualização: o teste de sessão desativada agora cria um segundo administrador, em vez de violar a proteção do último; a fixture v2 fornece as tabelas/colunas mínimas necessárias às migrações posteriores. Os testes de produto ausente/inativo enviam ID de operação e total válidos para atingir a validação do produto.

## Diagnóstico exato do backup

A falha foi reproduzida em `File.Move(origem, destino, true)` com um arquivo de texto recém-criado, fechado, em uma pasta temporária exclusiva, sem SQLite envolvido e sem destino existente. O resultado dentro do sandbox foi `UnauthorizedAccessException`, HResult `0x80070005` (acesso negado / Win32 5).

O mesmo teste, no mesmo computador e diretório temporário equivalente, executado fora do sandbox, retornou `MOVE_OUTSIDE_SANDBOX_OK`. A suíte geral fora do sandbox aprovou a criação do backup e a restauração com preservação da cópia anterior (cenário 33).

Conclusão: restrição do ambiente de execução sobre a renomeação; a interrupção observada não foi causada por uma conexão SQLite aberta nem por defeito no algoritmo de backup. `BackupService.cs` não precisou ser alterado. Foi preservada a criação em arquivo temporário, validação e publicação por renomeação, sem adotar uma cópia parcial sobre um backup existente como contorno.

## O que já estava adequado

- `BEGIN IMMEDIATE`, COMMIT e rollback já agrupavam venda, itens, baixa de estoque, movimentos financeiros e auditoria.
- O índice único de `OperacaoId`, a assinatura da solicitação e a consulta da operação por usuário já existiam.
- O backend já validava unidade inteira, até três casas por KG, estoque e política de ajustes.
- Cancelamento já preservava histórico e bloqueava alterações em caixa fechado.
- O fluxo Web já conservava o identificador no rascunho para consultar um resultado desconhecido.

## Correções implementadas

1. A ponte exige GUID não vazio e total esperado. Vendas novas recebem identificador mesmo quando um chamador legado não o informa; para repetir a mesma operação o chamador deve guardar e reutilizar o GUID. A assinatura inclui a composição dos pagamentos mistos.
2. O reconhecimento de reenvio ocorre dentro da transação, antes de reler catálogo, preço, estoque ou caixa. Uma venda já registrada continua reconhecida se o produto foi desativado, o preço mudou ou o caixa fechou. Outro usuário e dados divergentes são rejeitados.
3. Catálogo e custos são lidos dentro da mesma transação da gravação. A resposta da ponte usa os valores persistidos. O WinForms conserva o identificador em falhas de resultado desconhecido e não afirma que uma venda certamente não ocorreu.
4. `ValoresMonetarios` centraliza arredondamento comercial (`AwayFromZero`), subtotal e conversão validada para centavos. Cálculos usam `decimal`; composição de pagamento usa `long` em centavos. Valores informados aceitam até duas casas e até R$ 1 bilhão. Quantidades repetidas são consolidadas antes de arredondar e de verificar estoque. Baixas/estornos normalizam o saldo a três casas.
5. `PagamentosVenda` grava uma parcela por meio, dentro da transação da venda. Pagamento misto exige cobertura exata ou troco coberto pela parcela em dinheiro. Excesso eletrônico, valores negativos e formas repetidas são rejeitados. Só o dinheiro líquido entra no saldo físico.
6. Cancelamento revalida administrador dentro da transação, confere a composição financeira, exige dinheiro disponível para a devolução e estorna cada movimento original. Falhas revertem status, estoque, estornos e auditoria. Operações canceladas não podem ser reenviadas como novas vendas com o mesmo ID.
7. Relatórios discriminam as parcelas mistas por meio e conciliam o total líquido; o histórico de caixa continua calculando apenas dinheiro físico. Recibos, movimentos e resposta de checkout identificam o caráter manual do pagamento.
8. `PRAGMA synchronous=FULL` explicita a durabilidade das confirmações. O teste de falha real inicia um processo filho, executa a venda pelo repositório real, pausa após inserir `ItensVenda` e antes do COMMIT por uma extensão SQLite existente somente no projeto de testes, encerra o processo à força e verifica rollback, integridade e reenvio. Não há gancho de interrupção no aplicativo de produção.

## Pagamento misto pela ponte

O contrato existente continua válido para pagamentos simples. Para mistura, o payload de `pdv.checkout` aceita:

```json
{
  "items": [{ "productId": 1, "quantity": 1 }],
  "paymentMethod": "MISTO",
  "amountReceived": 0,
  "operationId": "7bc2bd81-5cf0-4765-97de-1afaa8e2f360",
  "expectedTotal": 10,
  "payments": [
    { "forma": "DINHEIRO", "valor": 20 },
    { "forma": "PIX", "valor": 4 }
  ]
}
```

Nesse exemplo o total é R$ 10, o troco é R$ 14, o movimento em dinheiro é R$ 6 e o Pix manual é R$ 4. `amountReceived` da resposta vem da composição persistida (R$ 24); o valor global do pedido não substitui as parcelas. A interface atual de seleção de pagamento simples foi preservada; o suporte misto foi acrescentado no serviço e na ponte, sem uma nova tela.

## Limites da validação

- `MANUAL` / `REGISTRADO_MANUALMENTE` significa informação do operador. Não há confirmação de Pix, adquirente ou banco. A ponte rejeita uma declaração de confirmação externa; os estornos eletrônicos registrados também não executam uma devolução bancária.
- O teste de término forçado valida recuperação de processo e rollback SQLite; não substitui ensaio físico de queda de energia, defeito de disco/controladora ou corrupção de armazenamento.
- O rascunho Web preserva operação entre recargas. O formulário nativo conserva o ID durante falhas na sessão, mas não ganhou recuperação persistente de carrinho após reiniciar o aplicativo; nessa situação o operador deve conferir o histórico antes de repetir a venda.
- As tabelas legadas `NUMERIC` foram preservadas para compatibilidade. A composição nova é inteira em centavos; não houve reescrita destrutiva de todo o histórico monetário.

## Arquivos desta etapa

- `Data/DatabaseConnection.cs`, `Data/DatabaseInitializer.cs`, `Data/VendaRepository.cs`.
- `Services/VendaService.cs`, `Services/PdvWebBridge.cs`, `Services/PagamentoService.cs`, `Services/ValoresMonetarios.cs`, `Services/AjustesVendaService.cs`, `Services/CaixaService.cs`, `Services/RelatoriosService.cs`, `Services/ComprovanteService.cs`.
- `Models/ItemCarrinho.cs`, `Models/PagamentoVenda.cs`, `Forms/PdvForm.cs`.
- `Tests/CaixaLume.IntegrationTests/FinancialScenarios.cs`, `Program.cs`, `PhaseOneScenarios.cs`, `PhaseTwoScenarios.cs`, `SecurityScenarios.cs` (somente fixture de migração), `Tests/security-stage1-baseline.json`.
- Este relatório e os quatro logs em `docs/validation-financeira/`.

Demais alterações já presentes no workspace foram preservadas.

## Repetição

```powershell
dotnet build Tests/CaixaLume.IntegrationTests/CaixaLume.IntegrationTests.csproj --no-restore
dotnet run --project Tests/CaixaLume.IntegrationTests/CaixaLume.IntegrationTests.csproj --no-build -- --security
dotnet run --project Tests/CaixaLume.IntegrationTests/CaixaLume.IntegrationTests.csproj --no-build -- --financial
# Em execução Windows normal, fora da restrição de rename do sandbox:
dotnet run --project Tests/CaixaLume.IntegrationTests/CaixaLume.IntegrationTests.csproj --no-build
```
