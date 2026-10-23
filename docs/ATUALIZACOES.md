# Atualizações privadas do CaixaLume

## Como está distribuído

O código fica no repositório privado `gabryelle-lima/CaixaLume`. A versão [v0.1.0](https://github.com/gabryelle-lima/CaixaLume/releases/tag/v0.1.0) é um pacote portátil anterior. A partir da 0.1.1, o ZIP gerado pelo projeto inclui `Instalar-CaixaLume.cmd`, `Iniciar-Piloto.cmd`, `VERSAO.txt`, um manifesto de arquivos com SHA-256, `COMO_EXECUTAR.md`, `ROTEIRO_PILOTO.md` e `VALIDACAO_MANUAL.md`. O fluxo `Preparar versão privada` cria um **rascunho** de lançamento no GitHub; ele não instala nada no computador da loja.

O download exige uma conta com acesso ao repositório privado. Nenhuma chave do GitHub é embutida no programa ou no ZIP. Uma tag `vX.Y.Z` prepara automaticamente um rascunho privado; o fluxo também aceita disparo manual. A atualização no computador da loja continua manual: baixar, extrair e executar o instalador com o aplicativo fechado. A operação do caixa foi projetada para ser local; seu funcionamento sem internet deve ser confirmado no piloto.

## Preparar e aplicar uma atualização

1. Incrementar a versão do projeto; não reutilizar um número já publicado.
2. Gerar o ZIP com `scripts/Publicar.ps1` e conferir o checksum `.sha256` e os arquivos principais do pacote.
3. Criar o lançamento pelo fluxo privado do GitHub e revisar o rascunho antes de publicá-lo.
4. No computador da loja, fazer backup pela tela Configurações e guardar uma cópia fora desse computador. Copiar também `%LOCALAPPDATA%\MercadinhoPdv\imagens`, se houver imagens dos produtos.
5. Fechar o CaixaLume e o antigo MercadinhoPdv. Baixar o ZIP privado, extrair tudo e abrir `Instalar-CaixaLume.cmd`.
6. O instalador valida o manifesto, preserva a pasta anterior, copia o banco real para `%LOCALAPPDATA%\CaixaLume\backups-antes-atualizacao` e muda o atalho **CaixaLume** para a pasta da versão nova.
7. Abrir a versão instalada. Para um teste com dados descartáveis, fechar a sessão normal e executar `Iniciar-Piloto.cmd` na pasta da versão instalada. Esse launcher usa `%LOCALAPPDATA%\CaixaLume\piloto\mercado-teste.db` em um processo separado.

As versões ficam em `%LOCALAPPDATA%\CaixaLume\versoes`. O banco real permanece em `%LOCALAPPDATA%\MercadinhoPdv\dados\mercadinho.db`. Atalhos antigos de `MercadinhoPdv.exe` devem ser removidos manualmente para não abrir código obsoleto.

## Recuperação

O instalador aceita `-Reverter`, que volta o atalho para uma versão anterior instalada por ele. Uma instalação portátil antiga não é registrada automaticamente. O procedimento está em `INSTALACAO_E_OPERACAO.md`. Ele **não reverte o banco**: se a versão nova já migrou o esquema, restaurar manualmente o backup correspondente antes de abrir a versão antiga. A restauração descarta as transações feitas depois da cópia. Por isso, primeiro preserve uma cópia do banco no estado atual.

## Limites desta etapa

- O ZIP oferece instalação local e retorno de versão; ainda não há atualização automática nem MSI assinado.
- O .NET Desktop Runtime 10 e o WebView2 Evergreen precisam estar instalados.
- O banco e as imagens importadas no piloto ficam separados dos dados reais. Os logs ainda usam uma pasta compartilhada do aplicativo.
- O registro de tempos de navegação, banco e interface fica em `%LOCALAPPDATA%\MercadinhoPdv\logs\application.log` para investigar lentidão.
