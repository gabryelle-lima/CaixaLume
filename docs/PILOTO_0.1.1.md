# CaixaLume 0.1.1 — primeiro piloto controlado

> **Versão substituída:** a 0.1.1 pode falhar na inicialização com "another row available". Use o pacote 0.1.2 e o [roteiro atualizado](PILOTO_0.1.2.md). Essa falha ocorre antes das migrações e não altera o banco pelo aplicativo; preserve os dados existentes.

## Escopo

Esta versão permite avaliar em **um computador** o fluxo local e offline: cadastro de produtos, estoque, abertura de caixa, venda, movimentações, fechamento, histórico, relatórios e backup. O nome inicial da loja é **Mercado** e pode ser editado pelo administrador. A interface usa um único padrão de navegação; o programa não abre automaticamente outro PDV no meio do trabalho.

O pacote está no repositório privado. O ZIP inclui `CaixaLume.exe`, os arquivos `Web`, o instalador por usuário e um lançador de piloto. A atualização é manual. O banco padrão permanece em `%LOCALAPPDATA%\MercadinhoPdv\dados\mercadinho.db`, separado do programa. Use `Iniciar-Piloto.cmd` para trabalhar em `%LOCALAPPDATA%\CaixaLume\piloto\mercado-teste.db`; as imagens novas e o perfil da interface também ficam separados dos dados normais.

## Antes de começar

1. Instale o .NET Desktop Runtime 10 e o Microsoft Edge WebView2 Runtime no Windows x64.
2. Baixe o ZIP privado, extraia tudo e execute `Instalar-CaixaLume.cmd` com o programa fechado. Confira que a cópia de segurança da base existente foi criada antes da troca da versão.
3. Abra `Iniciar-Piloto.cmd` e confira **AMBIENTE DE TESTE** na janela. Crie uma conta administrativa e dados descartáveis; não copie o banco real para o piloto.
4. Preserve a versão anterior e um backup correspondente. Copie também a pasta `%LOCALAPPDATA%\MercadinhoPdv\imagens` se houver imagens locais; o arquivo `.db` sozinho guarda apenas os caminhos. Um banco migrado pode não abrir em um executável antigo sem restauração do backup anterior.

## Critérios para aceitar a versão

- Abrir, fechar e reiniciar o programa sem travar; navegar por todos os módulos sem abrir janelas antigas.
- Vender com e sem internet; cada confirmação deve aparecer uma única vez em vendas, estoque e caixa. Em resposta incerta, consultar o histórico antes de tentar novamente.
- Operar caixa, despesas e estoque e recuperar os dados após reinicialização.
- Gerar relatórios legíveis e exportar Excel/PDF; conferir a impressora e os periféricos que a loja usará.
- Fazer backup e restaurar **uma cópia de teste** com sucesso. Conferir separadamente a cópia e a abertura das imagens locais.
- Confirmar que o instalador preserva a base fora da pasta do programa e que o piloto não altera a base padrão.

O passo a passo está em [VALIDACAO_MANUAL.md](VALIDACAO_MANUAL.md). Esses critérios exigem conferência prática na máquina de uso antes de declarar a versão pronta para operação.

## Limites

Pix e cartões são lançamentos locais: confirme o recebimento no banco ou na maquininha antes de registrar a venda. Não há emissão fiscal NFC-e/NF-e, TEF ou conciliação automática. A consulta Open Food Facts é opcional e exige internet. A revisão fiscal, os equipamentos reais, a política de backups e a aceitação na loja são condições separadas para uso comercial.
