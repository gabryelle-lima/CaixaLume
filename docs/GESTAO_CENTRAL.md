# Gestão central de instalações

## Responsabilidades

O serviço em `Server/CaixaLume.Management.csproj` registra estabelecimentos, dispositivos vinculados e metadados de versão, schema e último contato. Seu SQLite é independente do banco financeiro do PDV. O cliente é opcional: indisponibilidade do servidor não impede vendas locais.

O serviço não recebe vendas, não sincroniza estoques entre computadores e não oferece controle remoto do sistema operacional.

## Executar em desenvolvimento

```powershell
./scripts/Iniciar-Gestao-Local.ps1
```

O script prepara o serviço em modo de desenvolvimento, com escuta em loopback. Use o endereço local informado no terminal. Não publique o HTTP de desenvolvimento por proxy ou diretamente na internet. O vínculo do PDV exige HTTPS com certificado confiável.

## Autenticação e vínculo

O painel tem uma conta própria, distinta dos usuários do PDV e do sistema operacional. A chave inicial autoriza apenas o primeiro cadastro, confirmado com senha e autenticador TOTP; depois não serve como acesso administrativo. As APIs administrativas exigem sessão válida e proteção contra requisições de outra origem.

O administrador cadastra o estabelecimento e gera um convite temporário, válido por 30 minutos. O PDV consome o convite uma única vez e recebe uma credencial de dispositivo. O cliente protege a credencial com os recursos do Windows; o servidor armazena seu hash. Revogar o dispositivo impede novos contatos, sem remover o histórico financeiro local.

Veja [acesso ao painel](ACESSO_PAINEL.md) para sessões, códigos de recuperação e encerramento de acesso.

## Implantação Linux

O empacotamento está em `scripts/Preparar-Gestao-Linux.ps1`; o instalador e a unidade systemd estão em `scripts/gestao-linux`. A rotina é direcionada a Ubuntu x86_64. Revise o script e as dependências antes de executá-lo em outra distribuição.

O instalador utiliza usuário de serviço dedicado, releases em `/opt/caixalume-gestao`, banco em `/var/lib/caixalume-gestao` e ambiente em `/etc/caixalume-gestao/server.env`. Ele preserva configurações existentes e cria backup antes de atualizar. A instalação básica não configura automaticamente domínio, certificado público ou firewall.

Para um servidor sob sua responsabilidade:

1. Prepare o pacote e transfira-o por um canal autenticado.
2. Verifique o manifesto SHA-256 e examine o instalador antes de executá-lo.
3. Configure HTTPS e a confiança no certificado, preservando o acesso administrativo local.
4. Confira `/health`, os registros do serviço e o vínculo de um dispositivo de teste.
5. Teste reinício, backup, recuperação e revogação antes de utilizar dados reais.

O painel administrativo é restrito a acesso local. Para administração remota, use um túnel SSH autenticado conforme o [guia do painel](ACESSO_PAINEL.md). A instalação mantém verificação de certificado; não é necessário desativar TLS para vincular o PDV.

## Validação

A suíte `Tests/management_server_test.py` exercita Kestrel e SQLite reais em ambiente temporário, com 54 cenários. O cliente tem 12 cenários em `CentralScenarios.cs`, e o TOTP possui seis vetores oficiais. Veja [testes](TESTES.md) para executar e interpretar os resultados.

Testes locais não comprovam a disponibilidade ou a configuração de uma instalação externa. Endereços, usuários SSH, certificados e procedimentos particulares de operação devem ser mantidos fora do repositório público.
