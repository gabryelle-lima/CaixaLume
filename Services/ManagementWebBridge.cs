using System.Globalization;
using System.Text.Json;
using MercadinhoPdv.Data;
using MercadinhoPdv.Models;

namespace MercadinhoPdv.Services;

/// <summary>Operações dos módulos de gestão dentro do shell local.</summary>
internal sealed class ManagementWebBridge
{
    private readonly ProdutoService _products = new();
    private readonly EstoqueService _stock = new();
    private readonly CaixaService _cash = new();
    private readonly VendaService _sales = new();
    private readonly DespesasService _expenses = new();
    private readonly FornecedorService _suppliers = new();
    private readonly CategoriaService _categories = new();
    private readonly UsuarioService _users = new();
    private readonly ConfiguracaoService _settings = new();
    private readonly AjustesVendaService _policy = new();

    internal object GetData(JsonElement payload)
    {
        Autorizacao.ExigirUsuario();
        string module = Text(payload, "module").ToLowerInvariant();
        DateTime start = Date(payload, "start", DateTime.Today.AddDays(-29));
        DateTime end = Date(payload, "end", DateTime.Today);
        if (end < start) throw new InvalidOperationException("A data final deve ser igual ou posterior à inicial.");
        object data = module switch
        {
            "stock" => new { products = _stock.Listar().Where(p => p.Ativo).ToList(), categories = _products.ListarCategorias(), suppliers = _products.ListarFornecedores(), movements = Text(payload, "tab") == "movements" ? _stock.ListarMovimentacoes(inicio: start, fim: end, pesquisaProduto: Text(payload, "search")) : new List<MovimentoEstoqueLinha>() },
            "cash" => CashData(Text(payload, "tab")),
            "sales" => new { sales = _sales.ListarHistorico(start, end, Text(payload, "status") switch { "Concluida" => "Concluida", "Cancelada" => "Cancelada", _ => null }) },
            "expenses" => new { expenses = _expenses.Listar(start, end, Text(payload, "search")) },
            "suppliers" => AdministratorData(() => new { suppliers = _suppliers.Listar(null, true), categories = _categories.Listar(null, true) }),
            "settings" => AdministratorData(() => new { storeName = _settings.ObterNomeLoja(), policy = _policy.ObterPolitica(), audit = AuditRows(), testTools = AmbienteAplicacao.FerramentasTesteDisponiveis }),
            "users" => AdministratorData(() => new { users = _users.Listar(), currentUserId = SessaoUsuario.UsuarioId }),
            _ => throw new InvalidOperationException("Módulo inválido.")
        };
        return new { module, storeName = _settings.ObterNomeLoja(), userName = SessaoUsuario.Nome, userRole = SessaoUsuario.Perfil, data };
    }

    internal object GetDetails(JsonElement payload)
    {
        Autorizacao.ExigirUsuario();
        string module = Text(payload, "module");
        long id = Long(payload, "id");
        if (id <= 0) throw new InvalidOperationException("Selecione um registro.");
        return module switch
        {
            "sales" => new { rows = _sales.Detalhes(id), totals = _sales.ObterTotais(id) },
            "cash" => new { rows = _cash.ListarMovimentacoes(id) },
            _ => throw new InvalidOperationException("Detalhes indisponíveis para este módulo.")
        };
    }

    internal object Execute(JsonElement payload)
    {
        Autorizacao.ExigirUsuario();
        string operation = Text(payload, "operation");
        switch (operation)
        {
            case "stock.move":
                if (Text(payload, "type") == "Ajustar saldo") _stock.AjustarSaldo(Int(payload, "productId"), Decimal(payload, "quantity"), Text(payload, "reason"));
                else _stock.RegistrarMovimentacao(Int(payload, "productId"), Text(payload, "type"), Decimal(payload, "quantity"), Text(payload, "reason"));
                break;
            case "stock.receive":
                _stock.RegistrarEntradaMercadoria(Int(payload, "productId"), Decimal(payload, "quantity"), Decimal(payload, "cost"), NullableDecimal(payload, "price"), NullableDate(payload, "expiry"), NullableInt(payload, "supplierId"), Text(payload, "document"), Text(payload, "note"));
                break;
            case "cash.open": _cash.Abrir(Decimal(payload, "amount")); break;
            case "cash.move": _cash.RegistrarMovimentacao(Text(payload, "type"), Decimal(payload, "amount"), Text(payload, "reason")); break;
            case "cash.expense": _cash.RegistrarDespesa(Text(payload, "description"), Decimal(payload, "amount"), Text(payload, "payment")); break;
            case "cash.close": return new { closed = _cash.Fechar(Decimal(payload, "amount")) };
            case "sales.cancel": _sales.Cancelar(Long(payload, "id"), Text(payload, "reason")); break;
            case "expenses.save":
                _expenses.Salvar(new DespesaLinha { Id = Long(payload, "id"), Descricao = Text(payload, "description"), Categoria = Text(payload, "category"), Valor = Decimal(payload, "amount"), Data = DateTimeValue(payload, "date", DateTime.Now), FormaPagamento = Text(payload, "payment"), Observacao = Text(payload, "note") });
                break;
            case "expenses.cancel": _expenses.Cancelar(Long(payload, "id"), Text(payload, "reason")); break;
            case "suppliers.save":
                _suppliers.Salvar(new Fornecedor { Id = Int(payload, "id"), Nome = Text(payload, "name"), Telefone = Text(payload, "phone"), Email = Text(payload, "email"), Documento = Text(payload, "document"), Endereco = Text(payload, "address"), Observacao = Text(payload, "note") });
                break;
            case "suppliers.deactivate": _suppliers.Desativar(Int(payload, "id")); break;
            case "categories.save": _categories.Salvar(new Categoria { Id = Int(payload, "id"), Nome = Text(payload, "name") }); break;
            case "categories.deactivate": _categories.Desativar(Int(payload, "id")); break;
            case "settings.name": _settings.SalvarNomeLoja(Text(payload, "name")); break;
            case "settings.policy":
                _policy.SalvarPolitica(new PoliticaAjustesVenda { Habilitada = Boolean(payload, "enabled"), PermitirOperador = Boolean(payload, "operators"), MaxDescontoPercentual = Decimal(payload, "discount"), MaxAcrescimoValor = Decimal(payload, "surcharge") });
                break;
            case "settings.clearCash": Autorizacao.ExigirAdministrador(); return new { cleared = _cash.LimparHistoricoParaTestes() };
            case "users.save": _users.Salvar(Int(payload, "id"), Text(payload, "name"), Text(payload, "login"), Text(payload, "role"), Text(payload, "password")); break;
            case "users.deactivate": _users.Desativar(Int(payload, "id")); break;
            case "users.reactivate": _users.Reativar(Int(payload, "id")); break;
            default: throw new InvalidOperationException("Operação inválida.");
        }
        return new { updated = true };
    }

    private object CashData(string tab)
    {
        ResumoCaixa? current = _cash.ObterAberto();
        return new { current, movements = tab == "history" || current is null ? new List<MovimentoCaixaLinha>() : _cash.ListarMovimentacoes(current.Id), history = tab == "history" ? _cash.ListarHistorico(200) : new List<HistoricoCaixaLinha>() };
    }

    private static object AdministratorData(Func<object> factory)
    {
        Autorizacao.ExigirAdministrador();
        return factory();
    }

    private static object AuditRows()
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        return db.Query("SELECT A.DataHora AS Data,COALESCE(U.Nome,'SISTEMA') AS Usuario,A.Acao,A.Entidade,A.Descricao FROM Auditoria A LEFT JOIN Usuarios U ON U.Id=A.UsuarioId ORDER BY A.Id DESC LIMIT 200;");
    }

    private static string Text(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? "" : "";
    private static int Int(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
    private static int? NullableInt(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
    private static long Long(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : 0;
    private static decimal Decimal(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : 0;
    private static decimal? NullableDecimal(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : null;
    private static bool Boolean(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static DateTime Date(JsonElement payload, string name, DateTime fallback)
        => DateTime.TryParseExact(Text(payload, name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date) ? date : fallback;
    private static DateTime? NullableDate(JsonElement payload, string name)
        => DateTime.TryParseExact(Text(payload, name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date) ? date : null;
    private static DateTime DateTimeValue(JsonElement payload, string name, DateTime fallback)
        => DateTime.TryParse(Text(payload, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime date) ? date : fallback;
}
