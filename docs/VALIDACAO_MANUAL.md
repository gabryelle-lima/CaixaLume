# Validação manual antes do piloto

Os testes automatizados aprovados não substituem este roteiro. Use uma base nova de teste, fora do caminho de produção, e preserve uma cópia antes de ensaiar restauração.

## Situação observada nesta rodada

- O WebView2 não completou a inicialização no ambiente de inspeção; a alternativa WinForms foi efetivamente aberta com o usuário de teste autenticado.
- Foi possível adicionar um produto no catálogo nativo, editar o peso para 0,315 kg e observar subtotal de R$ 6,27 para R$ 19,90/kg.
- Capturas de janelas ocasionalmente retornaram conteúdo da janela que estava na frente, e ações posteriores encontraram estado de elemento indisponível ou geometria indisponível. A inspeção foi encerrada; isso não é aprovação visual dos demais módulos.
- Os formulários de dashboard, PDV, produtos, estoque, caixa, vendas, despesas, relatórios, configurações, fornecedores, usuários e entrada de mercadoria foram construídos e receberam layout nos cinco tamanhos em um teste de componentes. Esse teste não avalia cortes, sobreposições ou aparência final.
- A consulta online ao catálogo falhou por resolução de endereço. Os testes HTTP simulados cobrem sucesso, ausência, erro HTTP, JSON inválido, timeout e cancelamento.

## Matriz visual pendente

Verifique 1024×768, 1280×720, 1366×768, 1440×900 e 1920×1080, em escalas de 100%, 125% e 150%. Não altere a escala durante uma venda. Em cada combinação, conferir:

| Tela | O que conferir |
|---|---|
| Dashboard | Indicadores, nome da loja, menus, vencidos em destaque e próximos do vencimento |
| PDV Web e WinForms | Busca, grade, carrinho, campo de peso, contador, total, modais e rodapé acessíveis |
| Produtos/cadastro | Rolagem dos campos, imagem, categoria, fornecedor, validade, salvar e cancelar |
| Estoque/entrada | Filtros e ações acessíveis, quantidade UN/KG, motivo, histórico e documento |
| Caixa | Data de abertura completa, valores, ações, conferência e histórico |
| Vendas/despesas | Datas, valores monetários, detalhes, cancelamento e preservação do histórico |
| Relatórios | Cabeçalhos, totais bruto/líquido, descontos/acréscimos e leitura com rolagem |
| Configurações/usuários | Nome, backup, restauração e regras de ajuste; nenhuma limpeza disponível em Release |

## Fluxos de aceitação

1. Criar administrador com senha curta deve falhar; senha com 10 ou mais caracteres deve funcionar. Credenciais existentes devem continuar autenticando.
2. Como operador, tentar alterar cadastros, políticas, usuários, relatórios gerenciais e cancelar vendas deve ser negado pelo serviço.
3. Abrir caixa, tentar abrir novamente e conferir que a tela usa o caixa já aberto.
4. Ler um EAN conhecido e pressionar Enter: incluir uma vez, limpar a busca e manter foco. Repetir Enter sem código não deve incluir novamente.
5. Ler código ausente: consultar/cadastrar sem perder os itens da venda. Produto inativo ou sem estoque não pode ser incluído na confirmação.
6. Informar 0,125; 0.315; 0,750; 1.235 e 2,500 kg. Conferir subtotal e impedir fração em produto UN, zero, peso com quatro casas e quantidade acima do estoque.
7. Conferir que fechar o modal pelo X ou cancelar não confirma pagamento nem abre caixa. Alt+F4 deve fechar a janela; F4 sem modificador deve iniciar a cobrança.
8. Confirmar uma venda de cada forma, dinheiro exato, dinheiro com troco e pagamento insuficiente. Conferir baixa, caixa, auditoria e histórico.
9. Repetir uma requisição de checkout com o mesmo identificador deve retornar a mesma venda. Após timeout, reconfirmar a operação pendente antes de iniciar outra venda.
10. Ajustes devem começar desabilitados. Definir a política como administrador, conferir limites, aplicar valor/percentual e acréscimo; conciliar total, troco, caixa e relatório. Restaurar a política definida para o piloto.
11. Consultar Open Food Facts com código 3017620425035, um código ausente e internet desligada. Confirmar que dados digitados permanecem e o cadastro manual continua disponível.
12. Lançar despesa Pix sem caixa, abrir caixa e alterar para dinheiro; conferir vínculo e saldo. Fechar caixa e confirmar que editar/cancelar essa despesa é bloqueado.
13. Cancelar venda com caixa aberto: estoque e movimento financeiro devem ser estornados uma vez, mantendo a venda cancelada no histórico e fora do faturamento.
14. Ajustar estoque com motivo: conferir usuário, antes/depois e diferença na auditoria.
15. Fazer backup, restaurar apenas em cópia de teste, fechar/reabrir e conferir dados. Ensaiar nova publicação em computador limpo e reinicialização do Windows.

Leitor físico, balança, impressora, computador limpo e funcionamento após reinicialização real continuam pendentes de equipamento/ambiente.
