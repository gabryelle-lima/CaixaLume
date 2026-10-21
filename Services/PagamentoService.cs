using System.Globalization;

namespace MercadinhoPdv.Services;

public static class PagamentoService
{
    public static decimal CalcularTroco(decimal total, decimal recebido)
    {
        if (total < 0) throw new ArgumentOutOfRangeException(nameof(total), "O total não pode ser negativo.");
        if (recebido < total) throw new InvalidOperationException("O valor recebido precisa cobrir o total da venda.");
        return recebido - total;
    }

    public static bool TryParseValor(string? input, out decimal valor)
    {
        string value = (input ?? string.Empty).Trim().Replace("R$", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(" ", string.Empty);
        if (value.Length == 0) { valor = 0; return false; }
        int comma = value.LastIndexOf(',');
        int dot = value.LastIndexOf('.');
        char separator = comma > dot ? ',' : '.';
        if (comma >= 0 && dot >= 0) value = value.Replace(separator == ',' ? "." : ",", string.Empty);
        value = value.Replace(separator, '.');
        return decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out valor);
    }
}
