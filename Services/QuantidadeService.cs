using System.Globalization;
using System.Text.RegularExpressions;

namespace MercadinhoPdv.Services;

public static class QuantidadeService
{
    public static decimal Interpretar(string texto, string unidade, decimal estoque)
    {
        string normalized = (texto ?? "").Trim().Replace(',', '.');
        if (!Regex.IsMatch(normalized, @"^\d+(?:\.\d{1,3})?$")
            || !decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal quantidade)
            || quantidade <= 0 || quantidade > 100000)
            throw new InvalidOperationException("Informe quantidade positiva com até três casas decimais.");
        if (unidade == "UN" && quantidade % 1 != 0) throw new InvalidOperationException("Produtos por unidade exigem quantidade inteira.");
        if (quantidade > estoque) throw new InvalidOperationException($"Disponível: {estoque:0.###} {unidade}.");
        return quantidade;
    }
}
