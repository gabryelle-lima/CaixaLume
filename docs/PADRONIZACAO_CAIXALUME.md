# Padronização CaixaLume

Projetos e namespaces C# usam CaixaLume. Os relatórios históricos da raiz estão em `docs/auditorias`; scripts permanecem em `scripts`, com atalhos de entrada na raiz.

Instalações novas usam `%LOCALAPPDATA%/CaixaLume/dados/caixalume.db`. Havendo banco anterior em `%LOCALAPPDATA%/MercadinhoPdv/dados/mercadinho.db`, ele continua selecionado no próprio local. Isso preserva vendas, usuários, permissões, backups e a vinculação protegida ao servidor. Não houve recriação nem migração destrutiva. Imagens antigas permanecem acessíveis. Logs e cache do navegador novos usam CaixaLume.

As variáveis CAIXALUME_DATABASE e CAIXALUME_AMBIENTE são prioritárias; os nomes antigos são aceitos como alternativa. Verificações de processos antigos permanecem no instalador para impedir atualização enquanto uma versão anterior está aberta. Essas referências são compatibilidade, não identidade do produto.

Os arquivos de baseline das etapas anteriores são registros históricos: os hashes não foram atualizados para simular ausência de alterações. A validação atual deve ser feita executando novamente as suítes.

A pasta de trabalho será denominada `CaixaLume`, com atualização dos atalhos locais. O projeto não depende do nome da pasta externa.

Validação em 02/11/2026: compilação do aplicativo e servidor sem avisos; 76 testes gerais (73 anteriores + 3 de caminhos), 13 de segurança, 20 financeiros, 15 de estoque, 8 de UX, 12 centrais, 54 do servidor, 6 vetores TOTP e 20 de JavaScript: 224 aprovados. Nenhuma suíte foi interrompida.

A tentativa de renomear a pasta externa em 02/11/2026 foi recusada pelo Windows por uso em outro processo. A pasta original e seus atalhos foram preservados. Foi preparado o comando `Renomear pasta para CaixaLume.cmd` na Área de Trabalho: ele deve ser executado após fechar o projeto e o aplicativo; valida os caminhos, não substitui destino existente e atualiza os atalhos. Os nomes dos projetos e testes já estão atualizados independentemente dessa etapa.
