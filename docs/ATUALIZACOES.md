# Atualizações do CaixaLume

## Estado atual

O código e os pacotes serão guardados em um repositório GitHub privado. A publicação local cria uma pasta executável, um ZIP versionado e seu SHA-256. O fluxo do GitHub cria uma versão **rascunho**, que precisa ser revisada e publicada pelo responsável. Nenhum pacote é instalado automaticamente neste estágio.

O instalador privado exige autenticação para baixar uma versão do GitHub. O aplicativo não deve conter uma chave de acesso embutida. A integração de atualização deverá usar login do proprietário e guardar a credencial protegida no Windows. Esse passo depende do repositório e da identidade GitHub que serão usados.

## Processo de manutenção

1. Corrigir o código e incrementar a versão sem reutilizar um número anterior.
2. Gerar o pacote e conferir o arquivo `manifesto-sha256.csv` e o checksum do ZIP.
3. Criar a versão privada como rascunho pelo fluxo `Preparar versão privada`.
4. Revisar a versão, publicar e instalar com o aplicativo fechado.
5. Fazer uma cópia do banco antes da atualização. Os dados ficam fora da pasta do programa, em `%LOCALAPPDATA%\MercadinhoPdv\dados`, e nunca entram no ZIP.
6. Caso a versão nova falhe, voltar à pasta executável anterior. Para alterações de esquema do banco, restaurar o backup correspondente antes de reabrir a versão antiga.

## Diagnóstico da lentidão

O aplicativo registra no `application.log` os tempos de navegação, resposta de dados e conclusão da tela. Isso permite separar custo do WebView2, banco e desenho da interface. O log fica em `%LOCALAPPDATA%\MercadinhoPdv\logs`.
