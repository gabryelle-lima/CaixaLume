using System.Globalization;
using System.Text.RegularExpressions;

namespace CaixaLume.Forms;

// Interpretação da digitação; as regras e os cálculos financeiros permanecem nos serviços.
internal static class EntradaNumerica
{
    internal static bool TryParseDinheiro(string? input,out decimal value)
    {
        value=0;
        string text=(input??"").Trim();
        if(text.StartsWith("R$",StringComparison.OrdinalIgnoreCase))text=text[2..].TrimStart();
        if(text.Contains(',')&&text.Contains('.'))
        {
            if(!Regex.IsMatch(text,@"^\d{1,3}(?:\.\d{3})+,\d{1,2}$",RegexOptions.CultureInvariant))return false;
            text=text.Replace(".","");
        }
        return Regex.IsMatch(text,@"^\d+(?:[.,]\d{1,2})?$",RegexOptions.CultureInvariant)
            && decimal.TryParse(text.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out value)
            && value<=1_000_000_000m;
    }
}
