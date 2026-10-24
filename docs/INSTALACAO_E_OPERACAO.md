# Instalação e operação — CaixaLume

## Instalar a versão 0.1.3 no computador de teste

1. Instale o **.NET Desktop Runtime 10 para Windows x64** e o **Microsoft Edge WebView2 Evergreen Runtime**. O pacote é dependente desses componentes.
2. Baixe o ZIP da versão privada com uma conta autorizada no repositório `gabryelle-lima/CaixaLume`. Extraia o ZIP inteiro para uma pasta temporária; mantenha a subpasta `Web` e todos os arquivos juntos.
3. Feche qualquer janela do CaixaLume ou do antigo MercadinhoPdv. Faça também um backup pela tela **Configurações** antes de atualizar uma instalação que já contém vendas.
4. Na pasta extraída, abra `Instalar-CaixaLume.cmd`. Ele confere os arquivos do pacote, copia a versão para `%LOCALAPPDATA%\CaixaLume\versoes\0.1.3`, faz uma cópia adicional do banco existente e cria o atalho **CaixaLume** para o usuário atual.
5. Abra o aplicativo pelo atalho **CaixaLume** no menu Iniciar ou na área de trabalho. Na primeira utilização, cadastre a conta administradora com senha de pelo menos 10 caracteres. Abra o caixa, cadastre produtos e confira os equipamentos antes de operar.

O instalador não precisa de acesso ao GitHub: o download do ZIP privado é feito separadamente. A operação local sem internet deve ser conferida na aceitação do piloto. O pacote não contém o banco de dados da loja. `ROTEIRO_PILOTO.md` e `VALIDACAO_MANUAL.md` acompanham o ZIP para orientar essa conferência.

Atalhos antigos que apontam para `MercadinhoPdv.exe` não são atualizados pelo instalador. Remova esses atalhos manualmente para evitar abrir a versão antiga. Não apague o banco em `%LOCALAPPDATA%\MercadinhoPdv\dados`.

### Se a versão 0.1.1 não iniciou

A v0.1.1 podia exibir **"another row available"** ao abrir. Essa falha ocorria antes das migrações e antes de o aplicativo alterar o banco. O instalador da 0.1.1 pode ter feito uma cópia de segurança e apontado o atalho para a versão, mas **não é necessário apagar nem restaurar o banco por causa desse erro**. Feche o CaixaLume, descarte **somente a pasta onde o ZIP 0.1.1 foi extraído**, baixe e extraia o ZIP 0.1.3 em uma pasta nova e execute o instalador dessa pasta. Se a 0.1.1 tiver sido instalada, sua pasta continuará preservada como versão anterior; a base fica fora das pastas do programa. Evite voltar à 0.1.1 com esse defeito.

## Testar sem tocar nos dados reais

Abra `Iniciar-Piloto.cmd` dentro da pasta da versão instalada em `%LOCALAPPDATA%\CaixaLume\versoes\0.1.3`. Ele inicia uma sessão separada que usa `%LOCALAPPDATA%\CaixaLume\piloto\mercado-teste.db`. Cadastros, caixas, vendas e imagens importadas nessa sessão ficam separados dos dados reais. Feche a sessão piloto antes de abrir o atalho normal. O atalho **CaixaLume** usa o banco normal, desde que `MERCADINHOPDV_DATABASE` não tenha sido configurada no Windows. Os logs continuam em uma pasta compartilhada.

## Dados, backup e retorno à versão anterior

O banco padrão fica em `%LOCALAPPDATA%\MercadinhoPdv\dados\mercadinho.db`, fora das pastas de versão. A variável `MERCADINHOPDV_DATABASE` substitui o caminho apenas quando definida explicitamente. Não a configure em uma instalação comum.

Antes de ativar uma versão nova, o instalador copia o banco e, se existirem, os arquivos SQLite `-wal` e `-shm` para uma pasta única em `%LOCALAPPDATA%\CaixaLume\backups-antes-atualizacao`, conferindo o SHA-256 de cada cópia. Isso verifica a cópia dos arquivos, mas não substitui a verificação de integridade e a restauração de teste do backup feito pelo aplicativo. Guarde também um backup fora do computador.

O backup `.db` não contém as imagens dos produtos. Para recuperação completa em outro computador, copie também `%LOCALAPPDATA%\MercadinhoPdv\imagens`; para dados do piloto, copie `%LOCALAPPDATA%\CaixaLume\piloto\imagens`. Depois da restauração, coloque as imagens nesses caminhos no novo perfil e confira a prévia dos produtos. O aplicativo reconhece imagens recuperadas pelo nome do arquivo, mesmo quando o caminho absoluto antigo não existe mais.

O instalador preserva a pasta de cada versão instalada por ele. O retorno automático só fica disponível depois de instalar pelo menos duas dessas versões. Uma instalação portátil antiga, fora dessa estrutura, continua no lugar, mas não é registrada como versão anterior. Para voltar ao executável anterior registrado, feche o aplicativo e execute estas duas linhas no PowerShell do mesmo usuário:

```powershell
$atual = (Get-Content "$env:LOCALAPPDATA\CaixaLume\versao-atual.txt" -Raw).Trim()
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\CaixaLume\versoes\$atual\Instalar-CaixaLume.ps1" -Reverter
```

Essa ação troca os atalhos e mantém os dados como estão. **Ela não restaura o banco automaticamente.** Se a versão nova alterou o esquema do banco, o executável anterior pode não conseguir abri-lo. Nesse caso, mantenha o aplicativo fechado, restaure a cópia correspondente do banco e dos arquivos `-wal`/`-shm` antes de abrir a versão antiga. Vendas registradas depois da cópia serão perdidas ao restaurá-la; preserve uma cópia atual antes de fazer isso.

A inicialização aplica migrações incrementais. A v3 adiciona subtotal, desconto, acréscimo e identificador da operação. A v4 registra exclusões lógicas de produtos e a conta autorizada. Vendas antigas mantêm o total e passam a ter desconto/acréscimo zero. Uma cópia `.pre-migration-*.bak` é criada antes de migrar um banco existente.

Depois de restaurar um backup pela aplicação, ela encerra a sessão e pede novo login.

## Se o aplicativo não abrir

O log fica em `%LOCALAPPDATA%\MercadinhoPdv\logs`. Se o WebView2 falhar, confira o runtime, feche o aplicativo e abra pelo atalho CaixaLume. A aplicação não inicia outro PDV automaticamente. Um carrinho em andamento pode não ser recuperado após a falha da interface.

## Regras operacionais do piloto

- Descontos e acréscimos começam desabilitados. Um administrador define limites e permissões em **Configurações → Regras de desconto**.
- Produtos vencidos são destacados; o bloqueio da venda não é automático.
- Pix, débito e crédito são registros locais. Confira o recebimento no banco ou na maquininha antes de confirmar. Não há TEF, adquirente ou emissão fiscal integrada.
- Impressão física depende do driver da impressora. A exportação de relatório em PDF usa o WebView2.

## Publicar a partir dos fontes

Execute `scripts/Publicar.ps1` na pasta do projeto com o SDK .NET 10. Ele gera uma pasta Windows x64 nova, inclui o instalador local, confere os arquivos da interface, gera o manifesto SHA-256 e cria um ZIP versionado com seu checksum. Ele não abre o aplicativo ou o banco, nem substitui uma instalação existente. O fluxo do GitHub prepara versões privadas em rascunho; veja `docs/ATUALIZACOES.md`.

## Ferramentas de desenvolvimento

A limpeza de caixas não está disponível em Release. Em Debug, exige administrador ativo, `MERCADINHOPDV_AMBIENTE=DESENVOLVIMENTO`, `MERCADINHOPDV_DATABASE` apontando para outra base e um arquivo marcador com o mesmo caminho da base seguido de `.descartavel`. Não crie esse marcador em dados reais.
