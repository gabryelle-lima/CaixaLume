# Demonstração local

## Preparação

Use Windows com SDK .NET 10 e WebView2. Para demonstrar sem utilizar uma base de operação existente:

```powershell
./scripts/Abrir-Piloto-Local.ps1
```

O script compila o aplicativo e utiliza uma base de piloto separada. Examine o caminho informado antes de cadastrar dados. O primeiro uso exige criar um administrador; não existe uma conta universal de demonstração.

## Roteiro sugerido

1. Cadastre um produto UN e outro KG, com estoque inicial e preços de teste.
2. Abra o caixa com um valor de demonstração.
3. Faça uma venda com produto por unidade e quantidade fracionária por peso.
4. Registre um pagamento misto e confira o total e o troco.
5. Consulte a venda no histórico e o movimento de estoque.
6. Com o caixa ainda aberto e permissão administrativa, cancele a venda e confira os estornos.
7. Registre uma despesa de teste e faça o fechamento.

Use somente dados fictícios. Pagamentos são registros manuais; não movimentam contas bancárias. A demonstração não emite documentos fiscais.

O servidor de gestão é opcional e não é necessário para esse roteiro. Para executá-lo, consulte [gestão central](GESTAO_CENTRAL.md).
