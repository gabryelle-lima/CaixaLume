# Identidade e compatibilidade

Projetos e namespaces C# usam CaixaLume. O diretório que contém o clone pode ter qualquer nome; nenhum caminho particular de desenvolvimento é necessário para executar o projeto.

## Dados existentes

Instalações novas utilizam `%LOCALAPPDATA%/CaixaLume/dados/caixalume.db`. Se existir um banco anterior em `%LOCALAPPDATA%/MercadinhoPdv/dados/mercadinho.db`, ele continua selecionado no local original. A atualização de identidade não recria nem transfere automaticamente bancos e imagens.

As variáveis `CAIXALUME_DATABASE` e `CAIXALUME_AMBIENTE` têm prioridade. Os nomes anteriores continuam aceitos como alternativa por compatibilidade. Verificações de processos de versões antigas permanecem no instalador para impedir atualizações com o aplicativo aberto.

## Organização

Relatórios anteriores estão em `docs/auditorias`. Os dois comandos CMD em `scripts` fazem parte do empacotamento do instalador. Procedimentos específicos de máquinas particulares e instruções de acesso não integram a documentação pública.

Os baselines das etapas anteriores são registros históricos; seus hashes não representam uma validação automática da implementação atual. Consulte [testes](TESTES.md) para os comandos e resultados registrados.
