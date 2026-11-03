using System.Globalization;
using CaixaLume.Models;

namespace CaixaLume.Services;

public static class PagamentoService
{
    public static decimal CalcularTroco(decimal total, decimal recebido)
    {
        ValoresMonetarios.Centavos(total);
        ValoresMonetarios.Centavos(recebido);
        if (total < 0) throw new ArgumentOutOfRangeException(nameof(total), "O total não pode ser negativo.");
        if (recebido < total) throw new InvalidOperationException("O valor recebido precisa cobrir o total da venda.");
        return recebido - total;
    }

    internal static List<PagamentoRegistrado> Distribuir(decimal total, string forma, decimal? recebido, IReadOnlyList<PagamentoVenda>? pagamentos)
    {
        long totalCentavos = ValoresMonetarios.Centavos(total);
        var entradas = pagamentos?.ToList() ?? [new PagamentoVenda(forma, forma == "DINHEIRO" ? recebido ?? total : total)];
        if (entradas.Count is < 1 or > 4) throw new InvalidOperationException("Informe de um a quatro pagamentos.");
        var normalizados = entradas.Select(p => new PagamentoVenda(p.Forma.Trim().ToUpperInvariant(), p.Valor)).ToList();
        if (normalizados.Any(p => p.Forma is not ("DINHEIRO" or "PIX" or "DÉBITO" or "CRÉDITO")) || normalizados.Select(p => p.Forma).Distinct().Count() != normalizados.Count)
            throw new InvalidOperationException("Formas de pagamento inválidas ou repetidas.");
        if ((forma == "MISTO") != (normalizados.Count > 1) || (forma != "MISTO" && normalizados[0].Forma != forma))
            throw new InvalidOperationException("A composição não corresponde à forma de pagamento.");
        var valores = normalizados.Select(p => ValoresMonetarios.Centavos(p.Valor)).ToArray();
        if (totalCentavos > 0 && valores.Any(v => v == 0)) throw new InvalidOperationException("Pagamentos devem ser maiores que zero.");
        long soma = valores.Sum(), troco = soma - totalCentavos;
        int dinheiro = normalizados.FindIndex(p => p.Forma == "DINHEIRO");
        if (troco < 0 || (troco > 0 && (dinheiro < 0 || troco > valores[dinheiro])))
            throw new InvalidOperationException("Pagamentos não cobrem o total ou geram troco sobre pagamento sem dinheiro.");
        return normalizados.Select((p, i) => new PagamentoRegistrado(p.Forma, valores[i] - (i == dinheiro ? troco : 0), valores[i])).ToList();
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
