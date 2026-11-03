# Gestão central — base local implementada em 02/11/2026

**Atualização de acesso:** o painel agora usa conta, senha, autenticador e sessão. As instruções antigas de inserir chave a cada consulta abaixo são históricas. Consulte `ACESSO_PAINEL_AURORA.md` para instalação e uso atuais. A chave é usada somente no cadastro inicial.

## Escopo real
O PDV mantém seu SQLite e sua operação local. O serviço ASP.NET Core 10 separado registra estabelecimentos e instalações em outro SQLite; não consulta o banco das lojas. Não há migração no banco comercial. Nome da loja já era compartilhado entre usuários e permanece local. Identificadores centrais são recebidos somente após vínculo autorizado.

Entregue: painel local de cadastro/consulta, convites de uso único com validade de 30 minutos, vínculo por estabelecimento, autenticação individual, revogação, último contato, versão do aplicativo e versão do schema. O cliente comunica a cada cinco minutos sem bloquear login/vendas, com timeout de oito segundos, sem redirecionamentos. Não há fila de payloads nem tentativas rápidas ilimitadas. Consulta manual em Configurações.

Não entregue nesta base: hospedagem pública, login administrativo com MFA, publicação de versões/atualização automática, backup remoto/monitoramento de backup, coleta de erros e acesso remoto. Estes recursos precisam de etapas próprias; o painel não informa estado de backup que não conhece. Revogar uma instalação apenas interrompe a comunicação: não bloqueia vendas/licença. Nunca excluir dados comerciais para desvincular.

## Revisar o painel
Com SDK .NET 10, executar no PowerShell:

```powershell
& .\scripts\Iniciar-Gestao-Local.ps1
```

Abra http://127.0.0.1:5187 e informe a chave exibida nesse terminal. Cadastrar mercado, consultar, gerar código e revogar são ações reais. O banco central fica em `%LOCALAPPDATA%/CaixaLumeGestao/management.db`, fora das pastas públicas. Ctrl+C encerra o servidor; os cadastros persistem e a chave administrativa é renovada na próxima execução. Nenhum processo foi instalado como serviço automaticamente.

## Vínculo no PDV
Configurações → Conectar gestão central. Só administrador ativo pode autorizar; o diálogo explica exatamente os metadados enviados. É obrigatório um endereço HTTPS de origem, sem caminho/parâmetros/credenciais, e código temporário. O modo HTTP local do painel não serve para vincular o PDV: preparar certificado HTTPS confiável ou ambiente de homologação. O cliente não ignora validação TLS.

A credencial fica ao lado do banco, no arquivo `.gestao.protected`, protegida por DPAPI do usuário Windows. Não integra o banco/backup comercial e não chega ao JavaScript. Usar outro usuário Windows ou mover a instalação requer novo vínculo. Ao clonar/restaurar máquinas, não copiar esse arquivo. O status expõe IDs, servidor e resultado, não tokens. Nome central é cadastrado pelo suporte e não sobrescreve o nome local. Desconectar remove a credencial local; revogar também no painel quando retirar equipamento.

Se a conexão cair depois de o servidor consumir um convite e antes de salvar a resposta, não reutilizar o código: conferir a instalação criada no painel, revogar e emitir outro. Não há recuperação oculta de credencial. Alterar o arquivo protegido não permite alterar permissões locais.

## Servidor e segurança
Projeto `Server/CaixaLume.Management.csproj`, sem novos pacotes NuGet. Usa a ponte SQLite existente, sem alterar seu código. Windows usa winsqlite3; Linux requer libsqlite3.so.0 (suporte Linux ainda não executado). SQLite central é uma base inicial para instância única com volume persistente; não escalar em réplicas compartilhando arquivo.

Variáveis obrigatórias: `CAIXALUME_ADMIN_TOKEN` (mínimo 64 caracteres, gerar aleatoriamente) e `CAIXALUME_MANAGEMENT_DB` (caminho absoluto fora de wwwroot). Produção recusa HTTP; exceção somente Development com conexão loopback. Administração exige chave e conexão/Host locais. Não expor `/admin` por proxy, não usar chave administrativa em uma instalação de cliente. O painel guarda a chave apenas no campo em memória, nunca em localStorage/cookie/URL. Botão limpar remove chave e resultados. Rotacionar chave por ambiente seguro e reiniciar serviço.

API: POST /admin/stores {name}; GET /admin/stores; POST /admin/stores/{id}/invites; POST /admin/installations/{id}/revoke. Bearer administrativo. POST /api/enroll {code} retorna establishmentId/installationId/token uma única vez. POST /api/heartbeat {version,schemaVersion} usa Bearer individual. A identidade do mercado é obtida da credencial, nunca de um ID enviado pelo dispositivo. GET /health não mostra dados internos.

Segredos e convites armazenados por hash SHA-256 de valores aleatórios de 256 bits (não senhas humanas). Consumo do convite e criação da instalação são transacionais. Consultas parametrizadas; limite global de 120 chamadas/minuto/instância, sem fila; corpo até 4 KiB. Respostas no-store e painel usa textContent. Não há CORS liberado. O limite global é intencional para o piloto, precisa de dimensionamento/limitação por origem no ambiente público.

Antes de produção pública: escolher hospedagem/domínio, TLS, serviço supervisionado, backup consistente do banco central, monitoração, autenticação administrativa com MFA e auditoria, isolamento de rede, política de privacidade/retenção, recuperação e teste de carga. Não confiar automaticamente em cabeçalhos de proxy. Esta implementação não constitui liberação de produção.

## Testes
`dotnet build Server/CaixaLume.Management.csproj`
`dotnet build Tests/CaixaLume.IntegrationTests/CaixaLume.IntegrationTests.csproj`
`python Tests/management_server_test.py`
`dotnet run --project Tests/CaixaLume.IntegrationTests --no-build -- --central`

Servidor: HTTP real/Kestrel/SQLite descartável, autenticação, origem administrativa, convites, concorrência, isolamento, persistência, revogação, limite de corpo/rate e recusa HTTP em produção. Cliente: DPAPI real Windows, permissões reais, transporte simulado para vínculo, payload, indisponibilidade e revogação; não equivale a teste HTTPS ponta a ponta. Aparência do painel/diálogos e vínculo HTTPS confiável ainda exigem validação manual.

Fontes técnicas: https://learn.microsoft.com/aspnet/core/performance/rate-limit?view=aspnetcore-10.0 e https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0.

## Resultado da execução
Compilação aplicativo/testes e servidor: zero erros e avisos. Suítes concluídas: geral 73, segurança 13, financeiro 20, estoque 15, UX 8, cliente central 11, servidor central 20 e JavaScript 20. Total 180 cenários/testes. Logs em docs/validation-central (ignorados pelo Git por extensão .log).

Durante desenvolvimento, restrições do ambiente impediram sockets locais e File.Move; repetição autorizada fora dessas restrições concluiu as suítes. Um bloqueio do banco descartável foi identificado no próprio teste Python: context manager de sqlite3 fazia commit mas não fechava a conexão; corrigido com contextlib.closing e commit explícito. Não houve alteração de permissões ou teste destrutivo no banco do mercado.

## Instalação assistida no Aurora (Ubuntu x86_64)
Preparação em Windows: `scripts/Preparar-Gestao-Linux.ps1`. Publica Release sem apphost Windows, dependente do runtime ASP.NET Core 10 do Ubuntu e empacota em `artifacts/caixalume-gestao-linux.tar.gz`. Lista SHA-256 confere cada arquivo antes de instalar. O pacote não contém banco nem credenciais.

`Instalar-Gestao-no-Aurora.cmd` copia por SCP e executa a instalação por SSH como gabryelle. A senha é digitada no terminal do usuário, não lida/salva pelo script. O destino inicial é 192.168.0.7; valida estritamente a chave do Aurora já registrada em 192.168.0.6. Se a chave não existir ou divergir, interrompe sem aceitar outra automaticamente. Endereço mudou por DHCP: reservar o IP no roteador posteriormente.

O instalador usa sudo para instalar aspnetcore-runtime-10.0/libsqlite3-0/sqlite3/openssl/curl dos repositórios Ubuntu, criar usuário de serviço sem login, diretórios e unidade systemd. Não altera firewall/roteador, SSH ou banco do PDV. Programa em `/opt/caixalume-gestao/releases`; banco em `/var/lib/caixalume-gestao/management.db`; chave persistente root-only em `/etc/caixalume-gestao/server.env`. Arquivo env preexistente nunca é substituído. O banco existente recebe cópia consistente antes da troca de versão; atualizações não restauram banco silenciosamente.

**Esta etapa instala um piloto local supervisionado**, sem publicar por rede: Development restrito a `127.0.0.1:5187`. Isso mantém o controle de HTTP existente, não afrouxa exigência HTTPS no PDV. Acesso ao painel do Windows por túnel: `ssh -o HostKeyAlias=192.168.0.6 -o StrictHostKeyChecking=yes -N -L 127.0.0.1:5188:127.0.0.1:5187 gabryelle@192.168.0.7`, e navegador em `http://127.0.0.1:5188`. Porta diferente evita conflito com painel antigo Windows. Não expor esse modo pela internet nem por proxy. Vincular PDV permanece pendente de HTTPS confiável.

Verificação no Aurora: `systemctl status caixalume-gestao --no-pager`, `curl --fail http://127.0.0.1:5187/health`, `sudo journalctl -u caixalume-gestao -n 40 --no-pager`. A chave administrativa pode ser lida somente pelo responsável em seu terminal com `sudo cat /etc/caixalume-gestao/server.env`; não enviar foto dessa chave ao suporte/chat. Dados do painel Windows não são migrados automaticamente: se existirem cadastros reais, planejar exportação/backup antes de substituir a utilização por este novo painel.

O serviço inicia com o Linux e reinicia em caso de falha; suspensão do notebook ainda suspende o serviço. Precisamos validar comportamento ao fechar tampa, alimentação, backup externo e estabilidade do disco. Scripts compilados/empacotados no Windows; instalação e testes Linux só serão considerados concluídos após observar os resultados no Aurora.

### Correção de permissão do diretório principal
O primeiro teste no Aurora retornou systemd 200/CHDIR (Permission denied), antes de executar .NET. O instalador definia modo de releases, mas deixava o diretório pai implícito sob umask 027. Agora /opt/caixalume-gestao e releases são criados explicitamente root:root 0755. Não afrouxar /etc/caixalume-gestao ou o banco; não usar chmod recursivo 777. Para a instalação anterior, conferir `namei -l /opt/caixalume-gestao/current` e corrigir somente o diretório de programa; reiniciar e verificar /health. Não considerar instalação Linux aprovada antes desse resultado.

### Diagnóstico de recusa no vínculo
Cliente agora distingue respostas HTTP 400/401/404/429/5xx sem mostrar tokens nem corpo de resposta. Testes locais: 12 cenários centrais e 13 de segurança concluídos. Compilação isolada em artifacts/diagnostico-vinculo sem erros/avisos. Atualização da pasta Debug bloqueada pelo executável em uso; não confundir com versão instalada. Causa da recusa no Aurora ainda depende do status de /api/enroll nos logs, não comprovada como código expirado.

### Automação do HTTPS para Wi-Fi
`Configurar-Wifi-Aurora.cmd` executa `scripts/Configurar-Wifi-Aurora.ps1`: SCP autenticado e sudo via SSH interativo, sem registrar senha. Destinos atuais: cabo 192.168.0.7, Wi-Fi 192.168.0.8; chave SSH conferida pelo alias conhecido 192.168.0.6. Não aceita chaves desconhecidas automaticamente.

`configurar-wifi-https.sh` confirma o IP na wlp2s0, reutiliza a chave privada do servidor, preserva a CA e assina novo certificado com SAN para ambos os IPs/localhost/aurora. Verifica a correspondência das chaves e o certificado antes de alterar o serviço. Copia de segurança do certificado e drop-in em /var/backups/caixalume-gestao; restauração desses dois arquivos em caso de erro após a troca. HTTPS escuta IPv4 em 0.0.0.0:5443 para sobreviver à ausência do cabo; HTTP permanece somente loopback. Nenhuma mudança no banco, firewall, roteador, confiança Windows ou credenciais. Processo protegido por flock. Teste TLS com CA no Aurora e validação normal do Windows, sem ignorar certificados.

Preparação local: sintaxe PowerShell validada. Shell Linux e execução remota ainda precisam ser confirmados no Aurora; acesso automatizado sem senha não disponível. Não considerar Wi-Fi migrado até sucesso no teste Windows e ajuste posterior da URL do cliente e do túnel. Endereços continuam por DHCP e podem mudar. A confiança local tem escopo de usuário Windows; executar o atalho com o mesmo usuário que importou a CA.
