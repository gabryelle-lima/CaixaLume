# Aceitação manual do CaixaLume

Este roteiro deve ser executado na máquina do piloto com o banco isolado criado por `Iniciar-Piloto.cmd`. A revisão está pendente; a existência do roteiro não equivale a aprovação. Preserve o banco real e nunca ensaie restauração nele.

## Abertura e navegação

1. Instalar a versão privada com o programa fechado, abrir o lançador do piloto e conferir a indicação **AMBIENTE DE TESTE**.
2. Criar administrador, sair e entrar. Abrir todos os módulos e voltar ao PDV. Conferir que não aparece uma segunda versão do programa e que a troca de páginas responde sem espera longa.
3. Fechar e abrir o programa. Conferir dados e sessão conforme a política do aplicativo. Desligar a internet e confirmar que cadastro, venda, caixa e relatórios continuam acessíveis.

## Venda e caixa

4. Cadastrar produtos UN e KG com preço, custo e estoque. Conferir busca por nome e código de barras, edição, imagem e inativação. Testar um produto sem estoque.
5. Abrir o caixa com saldo inicial, tentar abrir novamente e confirmar que o caixa existente é reconhecido sem bloquear a tela.
6. Adicionar e remover itens, alterar quantidade/peso e conferir subtotal, desconto, acréscimo e total. Fechar o modal sem confirmar e conferir que nada foi gravado.
7. Concluir uma venda em dinheiro, uma com troco e uma em cada forma Pix, débito e crédito. No dinheiro, rejeitar valor insuficiente. Conferir número da venda, baixa de estoque, movimento do caixa, histórico e relatório.
8. Caso uma confirmação demore ou a resposta se perca, consultar vendas e caixa antes de repetir a operação. Nunca presumir que uma mensagem de espera significa que nada foi gravado.
9. Fazer suprimento, sangria e despesa, conferir saldo esperado e fechar caixa. Conferir histórico após reiniciar o aplicativo.

## Gestão, recuperação e apresentação

10. Como operador, tentar ações restritas a administrador. Como administrador, exportar relatório em Excel e PDF e imprimir em dispositivo configurado.
11. Fazer backup; restaurar **somente em outro banco de teste**; conferir integridade, vendas e estoque após reiniciar. Copiar a pasta de imagens correspondente e conferir a prévia dos produtos. Simular atualização preservando a versão anterior e o backup correspondente.
12. Conferir telas em 1024×768, 1366×768 e 1920×1080, com escalas de 100%, 125% e 150% usadas na loja: textos, tabelas, modais, rolagem e botões não devem ficar cortados.
13. Se houver leitor de código, balança ou impressora no local, verificar cada equipamento. Consultar Open Food Facts com `3017620425035`, com código inexistente e offline; os dados digitados devem permanecer, com cadastro manual disponível.

Registre qualquer falha com horário, versão, tela, passos e resultado esperado. O log de diagnóstico fica em `%LOCALAPPDATA%\MercadinhoPdv\logs\application.log`. Evite compartilhar senhas ou dados de clientes. Os critérios de liberação constam em [PILOTO_0.1.3.md](PILOTO_0.1.3.md).
