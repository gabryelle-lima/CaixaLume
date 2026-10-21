# Atualizações do CaixaLume

## Estado atual

O código está no repositório privado `gabryelle-lima/CaixaLume`. A versão inicial [v0.1.0](https://github.com/gabryelle-lima/CaixaLume/releases/tag/v0.1.0) foi publicada como pré-lançamento privado. A publicação local cria uma pasta executável, um ZIP versionado e seu SHA-256. O fluxo do GitHub cria as versões seguintes como **rascunho**, para revisão antes da publicação. Nenhum pacote é instalado automaticamente neste estágio.

O pacote atual é um ZIP executável, não um instalador. Para baixar um lançamento privado do GitHub é necessário autenticar a conta com acesso ao repositório. O aplicativo não deve conter uma chave de acesso embutida. Uma futura atualização automática precisará de login do proprietário e armazenamento protegido da credencial no Windows.

## Processo de manutenção

1. Corrigir o código e incrementar a versão sem reutilizar um número anterior.
2. Gerar o pacote e conferir o arquivo `manifesto-sha256.csv` e o checksum do ZIP.
3. Criar a versão privada como rascunho pelo fluxo `Preparar versão privada`.
4. Revisar a versão, publicar e instalar com o aplicativo fechado.
5. Fazer uma cópia do banco antes da atualização. Os dados ficam fora da pasta do programa, em `%LOCALAPPDATA%\MercadinhoPdv\dados`, e nunca entram no ZIP.
6. Caso a versão nova falhe, voltar à pasta executável anterior. Para alterações de esquema do banco, restaurar o backup correspondente antes de reabrir a versão antiga.

## Diagnóstico da lentidão

O aplicativo registra no `application.log` os tempos de navegação, resposta de dados e conclusão da tela. Isso permite separar custo do WebView2, banco e desenho da interface. O log fica em `%LOCALAPPDATA%\MercadinhoPdv\logs`.

