# Acesso ao painel de gestão

## Primeiro cadastro

A chave `CAIXALUME_ADMIN_TOKEN` autoriza a configuração inicial. Informe-a apenas no ambiente que você administra, escolha login e senha de pelo menos 14 caracteres, adicione a chave TOTP ao autenticador e confirme o código. Não há senha padrão. O cadastro só é concluído após a confirmação do segundo fator.

Depois da configuração, a chave inicial não autoriza APIs administrativas nem recria a conta. A configuração inicial pendente expira em dez minutos e admite até cinco erros de confirmação.

## Sessões e recuperação

Acesso diário usa senha e autenticador. A sessão padrão dura 12 horas; a opção explícita de manter conectado permite até sete dias. Em computador compartilhado, use Sair e não mantenha a sessão.

Dez códigos de recuperação são apresentados uma única vez. Guarde-os em local privado, separado do servidor. Cada código pode substituir o autenticador uma vez, mas ainda exige a senha. A troca de senha ou autenticador revoga sessões anteriores; trocar o autenticador também substitui os códigos de recuperação.

Sem senha e sem recursos de recuperação, é necessário um procedimento assistido pelo responsável pelo servidor. Não existe conta oculta ou senha universal de suporte.

## Administração por SSH

O painel permanece restrito a loopback. Configure uma conexão SSH ao servidor sob sua responsabilidade e confira sua chave de host por um canal confiável. Exemplo com domínio e usuário fictícios:

```powershell
ssh -o StrictHostKeyChecking=yes -o ExitOnForwardFailure=yes -N -L 127.0.0.1:5188:127.0.0.1:5187 operador@servidor.example
```

Substitua o usuário e o domínio pelos valores da sua implantação. A chave de host deve ter sido verificada e registrada previamente; não desative essa verificação. Mantenha a conexão aberta e acesse `http://127.0.0.1:5188` no navegador.

A senha SSH e a senha do painel pertencem a serviços diferentes. Fechar o túnel encerra o acesso por aquela conexão, sem desligar o servidor. Reabrir o navegador restaura somente uma sessão ainda válida.

## Informações privadas

Não publique senhas, chaves privadas, tokens, códigos de recuperação nem dados operacionais particulares em issues, commits ou capturas de tela. Use um gerenciador de senhas para segredos e documentação privada para endereços e rotinas específicas da implantação.
