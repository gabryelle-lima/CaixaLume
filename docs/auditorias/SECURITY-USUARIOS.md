# Segurança e permissões de usuários

A migração v6 é incremental, faz backup antes de migrar e preserva usuários e hashes. Não recria o banco.

A exclusão de produtos exige administrador ativo e a permissão `PRODUTO_EXCLUIR`. A exceção individual prevalece sobre o perfil, inclusive para negar. Operadores não recebem poderes administrativos por uma concessão individual.

- `Autorizacao.ConfigurarPermissaoPerfil("ADMINISTRADOR", true/false)` altera a concessão do perfil.
- `Autorizacao.ConfigurarPermissaoUsuario(id, true/false/null)` concede, nega ou restaura a herança.
- A ponte de gestão expõe `users.permission.profile` com `role` e `allow` e `users.permission.user` com `id` e `allow` (booleano ou null para herdar).
- Todas essas alterações exigem administrador ativo no backend e geram auditoria na mesma transação. Não foi acrescentada uma tela de permissões.

Quando existe `ProdutoExcluirUsuarioId`, a migração preserva a concessão explícita pelo ID, com o perfil negado por padrão. Sem essa configuração legada, o perfil administrador recebe a concessão. Nenhum nome de pessoa participa da autorização. Alterar nomes não altera permissões.

Gatilhos SQLite impedem desativação, exclusão, rebaixamento e remoção da senha do último administrador ativo. Administradores inativos ou sem senha não contam como substitutos. O cadastro inicial só fica disponível quando ainda não existe nenhuma credencial; a falta de administrador em uma base já configurada não abre um novo cadastro administrativo público.

Senhas continuam com PBKDF2-SHA256, salt aleatório, 210.000 iterações e comparação em tempo constante. Hashes existentes no formato suportado continuam válidos. Entradas malformadas e parâmetros abusivos são rejeitados. A ponte preserva espaços nas senhas ao salvar.

Execute a bateria isolada (banco temporário):

```powershell
dotnet build Tests/CaixaLume.IntegrationTests/CaixaLume.IntegrationTests.csproj
dotnet run --project Tests/CaixaLume.IntegrationTests/CaixaLume.IntegrationTests.csproj --no-build -- --security
```

Os gatilhos protegem operações SQL normais; quem possui acesso de escrita ao arquivo SQLite e ao esquema continua podendo adulterar o banco. Bases já sem administrador utilizável precisam de recuperação explícita por um responsável; não há credencial de emergência ou acesso oculto.
