# Testes automatizados

## Dependências

Windows, SDK .NET 10, Node.js com suporte ao test runner e Python 3 com o pacote `cryptography` para os testes HTTPS do servidor. Os cenários usam bancos e arquivos temporários. O teste de recuperação precisa de permissão para mover arquivos nesse diretório; uma execução interrompida não conta como aprovada.

## Execução completa

```powershell
python -m pip install cryptography
./scripts/Validar.ps1
```

O script encerra com erro se uma compilação ou suíte falhar. Há também verificações de sintaxe dos scripts JavaScript. As contagens abaixo se referem a cenários e testes, não ao número de asserções.

## Execução por suíte

```powershell
dotnet build Tests/CaixaLume.IntegrationTests/CaixaLume.IntegrationTests.csproj
dotnet run --project Tests/CaixaLume.IntegrationTests/CaixaLume.IntegrationTests.csproj --no-build
```

Acrescente `-- --security`, `-- --financial`, `-- --stock`, `-- --ux` ou `-- --central` ao último comando para executar a suíte correspondente.

```powershell
dotnet run --project Tests/CaixaLume.Management.UnitTests/CaixaLume.Management.UnitTests.csproj
node --test Tests/Web/*.test.js
dotnet build Server/CaixaLume.Management.csproj
python Tests/management_server_test.py
```

## Registro de referência — 02/11/2026

| Suíte | Verificações concluídas |
| --- | ---: |
| Integração geral, incluindo compatibilidade de caminhos | 76 |
| Segurança e usuários | 13 |
| Integridade financeira | 20 |
| Estoque e consultas | 15 |
| Interface e desempenho | 8 |
| Cliente de gestão | 12 |
| Servidor e autenticação HTTP/HTTPS | 54 |
| Vetores oficiais TOTP | 6 |
| JavaScript | 20 |
| **Total** | **224** |

As suítes estavam aprovadas na execução local registrada na base `c31a7114d5dbe45311d93cbed5d19cf4be19b57d`. Esse número não substitui uma nova execução após alterações de comportamento. Os arquivos `*-baseline.json` são registros históricos, não certificados de que o código atual não mudou.

## Limites

Os testes de construção das telas não substituem inspeção visual e uso com leitor, impressora e periféricos reais. A suíte HTTPS usa certificados temporários; não comprova a configuração de TLS de uma instalação externa. A interrupção forçada verifica recuperação de journal, não todos os tipos de falha física de disco ou alimentação.

## Revisão da apresentação pública — 02/11/2026

Após a revisão dos guias e a generalização de três mensagens que citavam um ambiente particular, foram executados novamente 12 cenários do cliente de gestão, 13 de segurança e 54 do servidor: **79 aprovados**. Aplicativo e servidor compilaram sem avisos e erros; a sintaxe do JavaScript do painel foi validada. Links principais e exclusões de arquivos particulares também foram conferidos.

As demais suítes não foram repetidas nesta revisão de documentação. O resultado completo de 224 permanece identificado como execução de referência, não como nova execução desta alteração. O fluxo de publicação e abertura da demonstração requer validação manual em Windows.
