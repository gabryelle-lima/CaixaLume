# Piloto local — entrada de mercadorias por XML da NF-e

Esta funcionalidade importa **NF-e de compra recebida** (modelo 55). Ela não emite nota fiscal. O arquivo XML deve ser fornecido pelo fornecedor; DANFE em PDF ou foto não contém os dados estruturados necessários.

## Abrir sem instalar ou publicar

Com o CaixaLume fechado, execute `Abrir-Piloto-Local.cmd` na raiz do projeto. O comando prepara uma versão local e a inicia com o banco separado do piloto em `%LOCALAPPDATA%\CaixaLume\piloto\mercado-teste.db`. Ele precisa do .NET SDK no computador e não cria uma versão no GitHub. A primeira compilação pode demorar; a navegação do aplicativo é uma etapa separada.

## Conferir a entrada

1. Em **Configurações**, cadastre o CNPJ numérico da loja. Cadastre os produtos que serão recebidos, com a unidade usada para vendê-los (`UN` ou `KG`).
2. Em **Notas fiscais**, escolha **Importar XML** e selecione o arquivo `nfeProc` da compra.
3. Confira fornecedor, destinatário, número, chave, itens e mercadoria recebida. O aplicativo compara o destinatário com o CNPJ salvo da loja.
4. Associe cada linha da NF-e a um produto cadastrado. Quando a nota usa caixa ou pacote, informe a quantidade convertida para `UN` ou `KG` e o custo por unidade interna. Confira os valores antes de confirmar.
5. Confirme a entrada apenas depois de conferir fisicamente os produtos. Consulte as abas **Notas importadas** e **Entradas por item** para verificar o histórico e o relatório por período. Use **Exportar Excel** para salvar o período escolhido.

Todos os itens da nota são lançados juntos. A chave da NF-e impede que uma segunda importação credite o mesmo documento novamente. O preço de venda não é alterado pela importação.

O XML local contém dados e protocolo, mas **não comprova a assinatura digital nem a situação fiscal atual**: a NF-e pode ter sido alterada ou cancelada após o arquivo ter sido gerado. Esta versão não consulta a SEFAZ nem registra manifestação do destinatário. A busca automática pelo CNPJ exigirá certificado digital, internet e uma etapa posterior de integração com a Distribuição DF-e. Neste piloto, o importador aceita apenas CNPJ e chave de acesso numéricos; documentos com o novo CNPJ alfanumérico exigem adaptação da validação da chave.
