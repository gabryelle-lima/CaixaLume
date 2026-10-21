using System.Text.Json;
using MercadinhoPdv.Data;
using MercadinhoPdv.Models;

namespace MercadinhoPdv.Services;

public sealed class AjustesVendaService
{
    public PoliticaAjustesVenda ObterPolitica()
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        return LerPolitica(db);
    }

    internal static PoliticaAjustesVenda LerPolitica(DatabaseConnection db)
    {
        var row = db.Query("SELECT Valor FROM Configuracoes WHERE Chave='PoliticaAjustesVenda';").FirstOrDefault();
        if (row is null) return new();
        try { return JsonSerializer.Deserialize<PoliticaAjustesVenda>(Convert.ToString(row["Valor"])!) ?? new(); }
        catch (JsonException exception) { throw new InvalidOperationException("A política de descontos está inválida. Peça ao administrador para revisá-la.", exception); }
    }

    public void SalvarPolitica(PoliticaAjustesVenda politica)
    {
        int user = Autorizacao.ExigirUsuario(administrador: true);
        if (politica.MaxDescontoPercentual < 0 || politica.MaxDescontoPercentual > 100 || decimal.Round(politica.MaxDescontoPercentual, 2) != politica.MaxDescontoPercentual || politica.MaxAcrescimoValor < 0
            || decimal.Round(politica.MaxAcrescimoValor, 2) != politica.MaxAcrescimoValor)
            throw new InvalidOperationException("Informe desconto máximo entre 0 e 100% e acréscimo máximo em reais, com até duas casas.");
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        db.Execute("BEGIN IMMEDIATE;");
        try
        {
            string json = JsonSerializer.Serialize(politica);
            db.Execute("INSERT INTO Configuracoes (Chave,Valor) VALUES ('PoliticaAjustesVenda',@Valor) ON CONFLICT(Chave) DO UPDATE SET Valor=excluded.Valor;", new Dictionary<string, object?> { ["@Valor"] = json });
            db.Execute("INSERT INTO Auditoria (UsuarioId,DataHora,Acao,Entidade,Descricao) VALUES (@Usuario,@Data,'POLITICA','VENDA',@Descricao);",
                new Dictionary<string, object?> { ["@Usuario"] = user, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Descricao"] = json });
            db.Execute("COMMIT;");
        }
        catch { db.Execute("ROLLBACK;"); throw; }
    }

    public TotaisVenda Calcular(IReadOnlyCollection<ItemCarrinho> itens, AjusteVenda? ajuste = null)
        => Calcular(itens.Sum(item => item.Subtotal), ajuste, ObterPolitica(), SessaoUsuario.Administrador);

    internal static TotaisVenda Calcular(decimal subtotal, AjusteVenda? ajuste, PoliticaAjustesVenda politica, bool administrador)
    {
        ajuste ??= new();
        string tipo = (ajuste.TipoDesconto ?? "").Trim().ToUpperInvariant();
        if (tipo is not ("VALOR" or "PERCENTUAL") || ajuste.Desconto < 0 || ajuste.Acrescimo < 0)
            throw new InvalidOperationException("Desconto ou acréscimo inválido.");
        if ((tipo == "VALOR" && decimal.Round(ajuste.Desconto, 2) != ajuste.Desconto) || decimal.Round(ajuste.Acrescimo, 2) != ajuste.Acrescimo)
            throw new InvalidOperationException("Valores monetários aceitam até duas casas decimais.");
        if (tipo == "PERCENTUAL" && decimal.Round(ajuste.Desconto, 2) != ajuste.Desconto)
            throw new InvalidOperationException("Percentuais aceitam até duas casas decimais.");
        bool alterou = ajuste.Desconto != 0 || ajuste.Acrescimo != 0;
        if (alterou && (!politica.Habilitada || (!administrador && !politica.PermitirOperador)))
            throw new InvalidOperationException("Descontos e acréscimos estão desabilitados para este usuário. O administrador deve definir a política.");
        decimal desconto = tipo == "PERCENTUAL" ? decimal.Round(subtotal * ajuste.Desconto / 100, 2, MidpointRounding.AwayFromZero) : ajuste.Desconto;
        if (desconto > subtotal || (tipo == "PERCENTUAL" && ajuste.Desconto > 100))
            throw new InvalidOperationException("O desconto não pode superar o subtotal da venda.");
        decimal limite = decimal.Round(subtotal * politica.MaxDescontoPercentual / 100, 2, MidpointRounding.AwayFromZero);
        if (desconto > limite || ajuste.Acrescimo > politica.MaxAcrescimoValor)
            throw new InvalidOperationException("O ajuste ultrapassa o limite definido pelo administrador.");
        return new(subtotal, desconto, ajuste.Acrescimo, subtotal - desconto + ajuste.Acrescimo);
    }
}
