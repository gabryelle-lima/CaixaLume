# Instalação e operação — Mercado PDV

## Executar uma publicação Windows x64

1. Extraia ou copie a pasta publicada inteira para um local gravável do usuário, mantendo a subpasta Web.
2. Instale o .NET Desktop Runtime 10 para Windows x64 e o Microsoft Edge WebView2 Evergreen Runtime. A publicação atual depende desses componentes; não é um instalador.
3. Abra MercadinhoPdv.exe. No primeiro uso, configure o administrador com senha de pelo menos 10 caracteres.
4. Abra o caixa, cadastre produtos e confira os equipamentos antes de operar.

Se o WebView2 não iniciar ou seu processo falhar, o aplicativo mostra o erro e encerra a sessão visual. Ele não abre outro PDV automaticamente. O log fica em %LOCALAPPDATA%\MercadinhoPdv\logs. Feche o aplicativo, confira o WebView2 Runtime e abra novamente; um carrinho em andamento pode não ser recuperado após falha do renderer.

## Banco e atualizações

O banco padrão fica em %LOCALAPPDATA%\MercadinhoPdv\dados\mercadinho.db, fora da pasta de instalação. Não copie o banco para a pasta do executável. MERCADINHOPDV_DATABASE substitui o caminho apenas quando definido explicitamente; não a configure em uma instalação comum.

A inicialização aplica migrações incrementais. A v3 adiciona subtotal, desconto, acréscimo e identificador da operação. A v4 registra exclusões lógicas de produtos e a conta autorizada. Vendas antigas mantêm o total e passam a ter desconto/acréscimo zero. Uma cópia .pre-migration-*.bak é criada antes de migrar um banco existente.

Antes de atualizar, faça backup pela tela Configurações e valide a restauração em uma cópia de teste. Feche o aplicativo, publique em uma pasta nova e preserve a instalação anterior. Não apague o diretório de dados. Um aplicativo antigo não deve abrir uma base de versão superior à que suporta.

Depois de restaurar um backup, o aplicativo encerra a sessão e pede novo login. Preserve uma cópia externa e defina com a loja a frequência e retenção dos backups.

## Regras operacionais

- Descontos e acréscimos começam desabilitados. Um administrador define os limites e quem pode concedê-los em Configurações → Regras de desconto.
- Produtos vencidos são destacados; o bloqueio da venda ainda não é automático, pois a regra comercial não foi definida.
- Pix, débito e crédito são registros locais. Confira o recebimento no banco ou na maquininha antes de confirmar. Não há TEF, adquirente ou emissão fiscal integrada.
- Impressão depende do driver da impressora. O PDF atual usa Microsoft Print to PDF, quando instalado.

## Publicar a partir dos fontes

Execute scripts/Publicar.ps1 com PowerShell na pasta do projeto. O script exige SDK .NET 10, restaura as dependências, gera Windows x64 em uma pasta nova de artifacts/publicacao, cria um ZIP versionado com SHA-256 e confere os arquivos Web. Não inicia o aplicativo, não abre bancos e não substitui instalação existente. O fluxo do GitHub prepara versões privadas em rascunho; consulte `docs/ATUALIZACOES.md`.

Para regressão, execute scripts/Validar.ps1. Os cenários C# usam bancos temporários; os testes Web exigem Node.js. O Node não é necessário para usar a aplicação publicada.

## Ferramentas de desenvolvimento

A limpeza de caixas não está disponível em Release. Em Debug, exige administrador ativo, MERCADINHOPDV_AMBIENTE=DESENVOLVIMENTO, MERCADINHOPDV_DATABASE apontando para outra base e um arquivo marcador com o mesmo caminho da base seguido de .descartavel. Não crie esse marcador em dados reais.
