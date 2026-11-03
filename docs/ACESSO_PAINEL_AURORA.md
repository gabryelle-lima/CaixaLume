# Acesso ao painel do Aurora — 02/11/2026

## O que mudou
O painel tem uma conta própria, diferente dos usuários do PDV e do usuário gabryelle do Linux. Não precisa consultar uma chave no servidor a cada acesso. A chave CAIXALUME_ADMIN_TOKEN permanece somente para autorizar o primeiro cadastro e nunca autoriza as APIs administrativas. Depois de criada a conta, nem essa chave recria a conta ou contorna os dois fatores. A conta também continua funcionando se o responsável retirar a variável de chave inicial do ambiente; ela só é obrigatória antes do primeiro cadastro.

Primeiro uso após a atualização: abrir o painel pelo túnel, informar a chave inicial atual uma vez, escolher login e senha de pelo menos 14 caracteres, adicionar a chave ao autenticador do celular como TOTP baseado em tempo e confirmar o código. Não foi criada senha padrão. Conta só é criada após confirmar o autenticador. Pendências expiram em 10 minutos e aceitam no máximo cinco erros.

A configuração mostra dez códigos de recuperação uma única vez. Guardar fora do servidor, preferencialmente em um gerenciador de senhas. Cada código substitui o autenticador uma vez, mas ainda exige a senha. Não compartilhar fotos da chave de configuração, dos códigos ou da chave inicial. Após recuperar acesso, trocar o autenticador em Segurança da conta.

## Uso diário
Abrir `Abrir-Painel-Aurora.cmd`. O atalho aproveita o túnel existente ou abre SSH, verifica estritamente a chave conhecida do Aurora e abre o navegador quando o painel responder. O usuário digita a senha SSH quando solicitada, sem que o programa a salve. Manter a janela da conexão aberta. A senha SSH não é a senha da conta do painel.

O painel restaura a sessão válida ao abrir/recarregar. Duração padrão: 12 horas; opção explícita de manter conectado: até sete dias. Os prazos são absolutos, sem renovação silenciosa infinita. A sessão persistente sobrevive a reiniciar o navegador e o servidor, desde que o cookie não seja apagado e continue válida. Usar o mesmo navegador para manter a sessão. Em computador compartilhado, não marcar manter conectado e usar Sair.

Não houve abertura de porta no roteador nem liberação da administração diretamente por IP. O painel continua em http://127.0.0.1:5188 sobre túnel SSH, com verificação de origem local. O HTTPS do PDV e o HTTP local no servidor foram preservados. Não confundir com publicar o painel na internet.

Segurança da conta permite trocar senha com senha atual + segundo fator, trocar autenticador após confirmar senha e sair de todos os dispositivos. Troca de senha ou autenticador revoga sessões anteriores. A troca do autenticador também substitui todos os códigos de recuperação. Se perder a senha e os códigos, recuperação continua assistida pelo responsável pelo servidor: não há redefinição por e-mail/SMS fictícia nem chave mestra escondida. Não apagar tabelas para recuperar acesso sem procedimento assistido e backup.

## Implantar no Aurora
`Atualizar-Painel-Aurora.cmd` usa o instalador assistido com o endereço atual Wi-Fi 192.168.0.8. Digitar senha do Linux no terminal quando pedir. Os arquivos já preparados em artifacts/caixalume-gestao-linux.tar.gz devem ser da compilação final. Não é necessário reinstalar Linux, certificados ou PDV.

O instalador faz backup consistente do banco central antes da troca de executáveis. Também copia o diretório de chaves de proteção se existir. Não substitui /etc/caixalume-gestao/server.env nem o drop-in HTTPS em /etc/systemd/system/caixalume-gestao.service.d. Dados e credenciais dos dispositivos não são modificados. Exige ainda confirmação do resultado real no Linux: `curl --fail http://127.0.0.1:5187/health`, serviço ativo, login e restauração de sessão pelo navegador.

## Armazenamento e controles
Tabelas novas e compatíveis apenas no banco central: PanelAccount, PanelSetup, PanelSessions, PanelRecovery e PanelAudit. Nada foi recriado em Stores, Invites ou Devices; banco comercial do PDV não recebe migração.

Senha: PBKDF2-SHA256, 600.000 iterações, sal aleatório de 16 bytes e derivação de 32 bytes. TOTP RFC 6238 com SHA-1, seis dígitos, intervalos de 30 segundos e tolerância de um intervalo; passos usados não podem ser repetidos. Consumo de fator e criação de sessão são transacionais. Confirmar relógio sincronizado do servidor/celular se os códigos forem recusados.

Segredo TOTP protegido com ASP.NET Core Data Protection. Chaves em `management.db.authkeys`, ao lado do banco central e fora dos executáveis/pasta pública. Linux restringe diretório a 0700 e serviço usa umask 0077; arquivos de chave devem ser preservados junto do backup e protegidos, pois não há criptografia de disco garantida. Windows protege as chaves por DPAPI. Não afirmar que esta proteção substitui criptografia do disco/backup. Não apagar o diretório ao atualizar. Backup só do SQLite não basta para restaurar o autenticador em outra máquina.

Sessões de 256 bits e recuperação de 128 bits aleatórios. Só hashes das credenciais de sessão/recuperação no banco. Cookie HttpOnly, SameSite Strict, Path / e expiração; Secure em HTTPS. Exceção HTTP restrita ao loopback no modo Development, necessário ao túnel SSH local. Cookie tem nome por banco para evitar colisões entre instâncias localhost em portas diferentes. Não são salvos tokens em localStorage/sessionStorage.

Escritas administrativas exigem sessão, token anti-CSRF, cabeçalho explícito e origem compatível. Não há CORS liberado. Rate limit de autenticação + limite global; cinco falhas bloqueiam a conta por cinco minutos, inclusive após reinício. Chave administrativa ou token de instalação não substituem cookie de sessão. Eventos de acesso e segurança registrados sem senhas, códigos ou segredos.

## Testes e limites
Suíte de servidor usa Kestrel real e SQLite descartável, com banco legado simulado. Abrange autenticação, MFA, recuperação, replay concorrente, CSRF, expiração, revogação, persistência, cookie Secure em HTTPS com certificado de teste e contratos dos dispositivos. Vetores oficiais RFC 6238 conferidos separadamente.

UI conferida no navegador com conta e mercado fictícios: configuração inicial, login, cadastro, restauração ao recarregar e logout. Nenhuma conta real criada pelo agente. Primeira configuração real e implantação no Aurora exigem interação do usuário para suas credenciais. Acessibilidade e diferentes tamanhos precisam de validação ampliada. Esta versão tem uma conta proprietária central; equipe com múltiplos administradores não foi implementada.

Fontes: https://www.rfc-editor.org/rfc/rfc6238 e https://learn.microsoft.com/aspnet/core/security/data-protection/configuration/overview e https://learn.microsoft.com/aspnet/core/security/anti-request-forgery.

### Resultado da execução final
Compilação Debug/Release do servidor e compilação isolada do PDV/testes: zero erros/avisos. Concluídos: 54 cenários de servidor/autenticação, 6 vetores RFC, 73 gerais do PDV, 13 segurança, 20 financeiros, 15 estoque, 8 UX, 12 cliente central e 20 JavaScript — 221 no total. Nenhum teste interrompido foi contado. Logs em docs/validation-painel-login. Testes ocorreram em Windows, com servidor Kestrel e HTTPS reais; atualização do serviço Linux e primeiro acesso real no Aurora ainda não executados pelo agente por exigirem a senha SSH do usuário.
