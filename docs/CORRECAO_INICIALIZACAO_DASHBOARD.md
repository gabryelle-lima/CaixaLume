# Visão geral — Cannot access 'data' before initialization

29/10/2026.

A mensagem da captura foi reproduzida ao executar `Web/home.js` inteiro em um teste de inicialização, com DOM e ponte controlados. O carregamento falhava antes mesmo de enviar `dashboard.getData`.

Causa: na correção anterior do menu, `updateInvoiceNavigation(data.userRole)` foi acrescentado antes de `const data = await request('dashboard.getData')`. A variável `const` ainda estava na zona de inicialização, gerando ReferenceError. A chamada foi movida para depois da resposta. O erro podia ocorrer em qualquer login, sem depender de senha incorreta.

O rótulo Operador da imagem é o placeholder inicial de `home.html`. Como o JavaScript falhava antes de receber os dados, nome e perfil nunca eram atualizados. Isso não comprova alteração da permissão real do usuário.

Revisão do fluxo existente: uma autenticação inválida não chama `SessaoUsuario.Iniciar`; logout encerra a sessão e fecha o formulário WebView2; o próximo login abre outro `AppWebViewForm`, recriando o shell e seus iframes. O ambiente WebView2 é reaproveitado, mas o mapa de telas JavaScript pertence ao novo documento. Não foi necessário desativar o carregamento/reuso das telas nem modificar autenticação, banco, senha, permissões ou regras financeiras para corrigir o erro mostrado.

Testes:

- Quatro testes novos reproduziram a falha antes da correção; nenhum foi considerado aprovado nessa execução.
- Depois da correção, os 20 testes JavaScript passaram, incluindo carregamento inicial completo, falha de comunicação e Tentar novamente, novos documentos de administrador/operador/administrador e atualização de uma tela já carregada.
- Esses testes executam o JavaScript real da tela com DOM/ponte simulados; não simulam digitação em uma janela de login Windows real.
- Os 13 cenários C# de segurança passaram.
- Compilação sem erros nem avisos; recursos Web corrigidos copiados para a pasta de saída.
- Demais baterias C# não foram repetidas nesta correção pontual.

Logs em `docs/validation-dashboard/`, incluindo a reprodução falha anterior. Arquivos alterados: `Web/home.js` e `Tests/Web/home.test.js`.

Feche e reabra `bin/Debug/net10.0-windows/CaixaLume.exe` para carregar os recursos atualizados. A instalação antiga não foi atualizada automaticamente. A alternância de contas pela interface Windows real ainda requer validação manual; o teste automatizado não é apresentado como inspeção visual.
