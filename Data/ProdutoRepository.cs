using System.Globalization;
using CaixaLume.Models;

namespace CaixaLume.Data;

internal sealed class ProdutoRepository
{
    public List<Produto> Listar(string? pesquisa = null)
    {
        const string sql = """
            SELECT p.Id, p.Versao, p.CodigoBarras, p.Nome, p.Marca, p.ImagemUrl, p.LocalImagePath, p.CategoriaId, c.Nome AS CategoriaNome,
                   p.FornecedorId, f.Nome AS FornecedorNome, p.PrecoCusto, p.PrecoVenda, p.UnidadeMedida, p.EstoqueAtual,
                   p.EstoqueMinimo, p.DataValidade, p.Ativo
            FROM Produtos p
            LEFT JOIN Categorias c ON c.Id = p.CategoriaId
            LEFT JOIN Fornecedores f ON f.Id = p.FornecedorId
            WHERE p.ExcluidoEm IS NULL AND (@Pesquisa = '' OR p.Nome LIKE @Termo OR p.CodigoBarras LIKE @Termo)
            ORDER BY p.Ativo DESC, p.Nome;
            """;
        var parameters = new Dictionary<string, object?>
        {
            ["@Pesquisa"] = pesquisa?.Trim() ?? string.Empty,
            ["@Termo"] = $"%{pesquisa?.Trim()}%"
        };
        using var database = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        return database.Query(sql, parameters).Select(ToProduto).ToList();
    }

    internal List<Produto> ListarParaPdv()
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        return db.Query("SELECT p.Id,p.Nome,p.CodigoBarras,p.PrecoVenda,p.UnidadeMedida,p.EstoqueAtual,p.EstoqueMinimo,p.LocalImagePath,p.Versao,c.Nome AS CategoriaNome FROM Produtos p LEFT JOIN Categorias c ON c.Id=p.CategoriaId WHERE p.Ativo=1 AND p.ExcluidoEm IS NULL ORDER BY p.Nome;")
            .Select(r => new Produto { Id=ToInt(r["Id"]),Nome=ToText(r["Nome"]),CodigoBarras=ToNullableText(r["CodigoBarras"]),PrecoVenda=ToDecimal(r["PrecoVenda"]),UnidadeMedida=ToText(r["UnidadeMedida"]),EstoqueAtual=ToDecimal(r["EstoqueAtual"]),EstoqueMinimo=ToDecimal(r["EstoqueMinimo"]),LocalImagePath=ToNullableText(r["LocalImagePath"]),Versao=Convert.ToInt64(r["Versao"]),CategoriaNome=ToNullableText(r["CategoriaNome"]) }).ToList();
    }

    public Dictionary<int, Produto> BuscarParaVenda(IReadOnlyCollection<int> productIds)
    {
        int[] ids = productIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<int, Produto>();
        if (ids.Length > 250) throw new InvalidOperationException("A venda excede o limite de 250 produtos.");

        var parameters = new Dictionary<string, object?>(ids.Length);
        var placeholders = new string[ids.Length];
        for (int index = 0; index < ids.Length; index++)
        {
            string name = $"@Id{index}";
            placeholders[index] = name;
            parameters[name] = ids[index];
        }

        using var database = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        string sql = $"SELECT Id, Nome, CodigoBarras, PrecoVenda, UnidadeMedida, EstoqueAtual, Ativo FROM Produtos WHERE ExcluidoEm IS NULL AND Id IN ({string.Join(",", placeholders)});";
        return database.Query(sql, parameters).Select(row => new Produto
        {
            Id = ToInt(row["Id"]),
            Nome = ToText(row["Nome"]),
            CodigoBarras = ToNullableText(row["CodigoBarras"]),
            PrecoVenda = ToDecimal(row["PrecoVenda"]),
            UnidadeMedida = ToNullableText(row["UnidadeMedida"]) ?? "UN",
            EstoqueAtual = ToDecimal(row["EstoqueAtual"]),
            Ativo = ToInt(row["Ativo"]) == 1
        }).ToDictionary(product => product.Id);
    }

    public Produto? BuscarPorId(int id)
    {
        const string sql = "SELECT Id, Versao, CodigoBarras, Nome, Marca, ImagemUrl, LocalImagePath, CategoriaId, FornecedorId, PrecoCusto, PrecoVenda, UnidadeMedida, EstoqueAtual, EstoqueMinimo, DataValidade, Ativo FROM Produtos WHERE Id = @Id AND ExcluidoEm IS NULL;";
        using var database = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        return database.Query(sql, new Dictionary<string, object?> { ["@Id"] = id }).Select(ToProduto).FirstOrDefault();
    }

    public Produto? BuscarPorCodigoBarras(string codigo)
    {
        using var database = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        var matches = database.Query("SELECT Id, Versao, CodigoBarras, Nome, Marca, ImagemUrl, LocalImagePath, PrecoCusto, PrecoVenda, UnidadeMedida, EstoqueAtual, EstoqueMinimo, DataValidade, CategoriaId, FornecedorId, Ativo FROM Produtos WHERE TRIM(CodigoBarras)=@Codigo AND Ativo=1 AND ExcluidoEm IS NULL;",
            new Dictionary<string,object?> { ["@Codigo"]=codigo.Trim() }).Select(ToProduto).ToList();
        if (matches.Count>1) throw new InvalidOperationException("Código de barras ambíguo no cadastro legado. Revise os produtos duplicados.");
        return matches.SingleOrDefault();
    }

    public bool CodigoBarrasJaExiste(string codigoBarras, int produtoId)
    {
        const string sql = "SELECT Id FROM Produtos WHERE TRIM(CodigoBarras) = TRIM(@CodigoBarras) AND Id <> @Id LIMIT 1;";
        using var database = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        return database.Query(sql, new Dictionary<string, object?> { ["@CodigoBarras"] = codigoBarras, ["@Id"] = produtoId }).Count > 0;
    }

    public void Inserir(Produto produto) => ExecuteSave("""
        INSERT INTO Produtos (Nome, CodigoBarras, Marca, ImagemUrl, LocalImagePath, CategoriaId, FornecedorId, PrecoCusto, PrecoVenda, UnidadeMedida, EstoqueAtual, EstoqueMinimo, DataValidade, Ativo)
        VALUES (@Nome, @CodigoBarras, @Marca, @ImagemUrl, @LocalImagePath, @CategoriaId, @FornecedorId, @PrecoCusto, @PrecoVenda, @Unidade, @EstoqueAtual, @EstoqueMinimo, @DataValidade, @Ativo);
        """, produto);

    public void Atualizar(Produto produto) => ExecuteSave("""
        UPDATE Produtos SET Nome = @Nome, CodigoBarras = @CodigoBarras, Marca = @Marca, ImagemUrl = @ImagemUrl, LocalImagePath = @LocalImagePath, CategoriaId = @CategoriaId, FornecedorId = @FornecedorId,
        PrecoCusto = @PrecoCusto, PrecoVenda = @PrecoVenda, UnidadeMedida = @Unidade, EstoqueAtual = @EstoqueAtual, EstoqueMinimo = @EstoqueMinimo, DataValidade = @DataValidade, Ativo = @Ativo
        WHERE Id = @Id;
        """, produto);

    public void Desativar(int id)
        => DefinirAtivo(id, false);

    public void Reativar(int id)
        => DefinirAtivo(id, true);

    public void Excluir(int id)
    {
        using var database = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        database.Execute("BEGIN IMMEDIATE;");
        try
        {
            Services.Autorizacao.ExigirExclusaoProduto(database);
            var product = database.Query("SELECT Nome,CodigoBarras FROM Produtos WHERE Id=@Id AND ExcluidoEm IS NULL;", new Dictionary<string, object?> { ["@Id"] = id }).FirstOrDefault()
                ?? throw new InvalidOperationException("Produto não encontrado ou já excluído.");
            database.Execute("UPDATE Produtos SET Ativo=0,CodigoBarras=NULL,ExcluidoEm=@Data WHERE Id=@Id;", new Dictionary<string, object?> { ["@Id"] = id, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
            database.Execute("INSERT INTO Auditoria (UsuarioId,DataHora,Acao,Entidade,Descricao) VALUES (@Usuario,@Data,'EXCLUSAO','PRODUTO',@Descricao);",
                new Dictionary<string, object?> { ["@Usuario"] = SessaoUsuario.UsuarioId, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Descricao"] = $"Produto #{id} {Convert.ToString(product["Nome"])} excluído do catálogo; código anterior {Convert.ToString(product["CodigoBarras"]) ?? "sem código"}; histórico preservado." });
            database.Execute("COMMIT;");
        }
        catch { database.Execute("ROLLBACK;"); throw; }
    }

    private static void DefinirAtivo(int id, bool ativo)
    {
        using var database = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        database.Execute("BEGIN IMMEDIATE;");
        try
        {
            Services.Autorizacao.ExigirUsuario(database, administrador: true);
            var product=database.Query("SELECT Nome, Ativo FROM Produtos WHERE Id=@Id AND ExcluidoEm IS NULL;",new Dictionary<string,object?>{["@Id"]=id}).FirstOrDefault()??throw new InvalidOperationException("Produto não encontrado.");
            if (Convert.ToInt32(product["Ativo"]) == (ativo ? 1 : 0)) throw new InvalidOperationException(ativo ? "O produto já está ativo." : "O produto já está inativo.");
            database.Execute("UPDATE Produtos SET Ativo = @Ativo WHERE Id = @Id;", new Dictionary<string, object?> { ["@Id"] = id, ["@Ativo"] = ativo ? 1 : 0 });
            database.Execute("INSERT INTO Auditoria (UsuarioId,DataHora,Acao,Entidade,Descricao) VALUES (@Usuario,@Data,@Acao,'PRODUTO',@Descricao);",new Dictionary<string,object?>{["@Usuario"]=SessaoUsuario.UsuarioId,["@Data"]=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),["@Acao"]=ativo?"REATIVACAO":"DESATIVACAO",["@Descricao"]=$"Produto {Convert.ToString(product["Nome"])} {(ativo ? "reativado" : "desativado")}"});
            database.Execute("COMMIT;");
        }catch{database.Execute("ROLLBACK;");throw;}
    }

    public List<OpcaoCadastro> ListarCategorias(int? selectedId = null) => ListarOpcoes("SELECT Id, Nome FROM Categorias WHERE Ativa = 1 OR Id = @Selecionado ORDER BY Nome;", selectedId);
    public List<OpcaoCadastro> ListarFornecedores(int? selectedId = null) => ListarOpcoes("SELECT Id, Nome FROM Fornecedores WHERE Ativo = 1 OR Id = @Selecionado ORDER BY Nome;", selectedId);

    private static List<OpcaoCadastro> ListarOpcoes(string sql, int? selectedId)
    {
        using var database = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        return database.Query(sql, new Dictionary<string, object?> { ["@Selecionado"] = selectedId }).Select(row => new OpcaoCadastro { Id = ToInt(row["Id"]), Nome = ToText(row["Nome"]) }).ToList();
    }

    private static void ExecuteSave(string sql, Produto produto)
    {
        using var database = new DatabaseConnection(DatabaseInitializer.DatabasePath);

        database.Execute("BEGIN IMMEDIATE;");
        try
        {
            Services.Autorizacao.ExigirUsuario(database, administrador: true);
            bool creating = produto.Id == 0;
            decimal estoqueAnterior = 0;
            if (produto.Id != 0)
            {
                var current = database.Query("SELECT EstoqueAtual, UnidadeMedida, Versao FROM Produtos WHERE Id = @Id AND ExcluidoEm IS NULL;", new Dictionary<string, object?> { ["@Id"] = produto.Id }).FirstOrDefault();
                if (current is null) throw new InvalidOperationException("O produto foi removido ou não existe mais.");
                estoqueAnterior = ToDecimal(current["EstoqueAtual"]);
                if (produto.Versao is null || produto.Versao != Convert.ToInt64(current["Versao"]))
                    throw new InvalidOperationException("O produto ou o estoque mudou desde a abertura do cadastro. Recarregue antes de salvar.");
                if (Convert.ToString(current["UnidadeMedida"]) != produto.UnidadeMedida &&
                    (database.Query("SELECT 1 FROM MovimentacoesEstoque WHERE ProdutoId=@Id LIMIT 1;", new Dictionary<string,object?> { ["@Id"]=produto.Id }).Any()
                    || database.Query("SELECT 1 FROM ItensVenda WHERE ProdutoId=@Id LIMIT 1;", new Dictionary<string,object?> { ["@Id"]=produto.Id }).Any()))
                    throw new InvalidOperationException("Produtos com histórico não podem trocar de unidade. Cadastre outro produto para preservar vendas e estornos.");
                if (Convert.ToString(current["UnidadeMedida"]) != produto.UnidadeMedida && estoqueAnterior != 0)
                    throw new InvalidOperationException("A unidade não pode ser alterada enquanto houver estoque. Zere o saldo por uma movimentação de estoque e tente novamente.");
            }
            if (produto.CodigoBarras is not null && database.Query("SELECT 1 FROM Produtos WHERE TRIM(CodigoBarras)=@Codigo AND Id<>@Id LIMIT 1;", new Dictionary<string,object?> { ["@Codigo"]=produto.CodigoBarras,["@Id"]=produto.Id }).Any())
                throw new Services.ProdutoValidationException("Já existe um produto com este código de barras.");
            if (produto.CategoriaId.HasValue && !database.Query("SELECT 1 FROM Categorias WHERE Id=@Id;", new Dictionary<string,object?> { ["@Id"]=produto.CategoriaId }).Any())
                throw new InvalidOperationException("Categoria não encontrada.");
            if (produto.FornecedorId.HasValue && !database.Query("SELECT 1 FROM Fornecedores WHERE Id=@Id;", new Dictionary<string,object?> { ["@Id"]=produto.FornecedorId }).Any())
                throw new InvalidOperationException("Fornecedor não encontrado.");
            var parameters = new Dictionary<string, object?>
            {
                ["@Nome"] = produto.Nome, ["@CodigoBarras"] = produto.CodigoBarras,
                ["@Marca"] = produto.Marca, ["@ImagemUrl"] = produto.ImagemUrl, ["@LocalImagePath"] = produto.LocalImagePath,
                ["@CategoriaId"] = produto.CategoriaId, ["@FornecedorId"] = produto.FornecedorId,
                ["@PrecoCusto"] = produto.PrecoCusto, ["@PrecoVenda"] = produto.PrecoVenda, ["@Unidade"] = produto.UnidadeMedida,
                ["@EstoqueAtual"] = produto.EstoqueAtual, ["@EstoqueMinimo"] = produto.EstoqueMinimo,
                ["@DataValidade"] = produto.DataValidade, ["@Ativo"] = produto.Ativo
            };
            if (!creating) parameters["@Id"] = produto.Id;
            database.Execute(sql, parameters);
            long savedId = creating ? database.LastInsertRowId : produto.Id;
            decimal diferenca = produto.EstoqueAtual - estoqueAnterior;
            if (diferenca != 0)
            {
                long productId = savedId;
                database.Execute("INSERT INTO MovimentacoesEstoque (ProdutoId, Tipo, Quantidade, UnidadeMedida, DataMovimentacao, Motivo, UsuarioId,SaldoAnterior,SaldoPosterior) VALUES (@Produto, @Tipo, @Quantidade, @Unidade, @Data, @Motivo, @Usuario,@Anterior,@Posterior);",
                    new Dictionary<string, object?> { ["@Produto"] = productId, ["@Tipo"] = diferenca > 0 ? "Entrada" : "Saida", ["@Quantidade"] = Math.Abs(diferenca), ["@Unidade"] = produto.UnidadeMedida, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Motivo"] = creating ? "Estoque inicial" : "Ajuste explícito pelo cadastro", ["@Anterior"]=estoqueAnterior,["@Posterior"]=produto.EstoqueAtual,["@Usuario"] = SessaoUsuario.UsuarioId });
            }
            database.Execute("INSERT INTO Auditoria (UsuarioId,DataHora,Acao,Entidade,Descricao) VALUES (@Usuario,@Data,@Acao,'PRODUTO',@Descricao);",new Dictionary<string,object?>{["@Usuario"]=SessaoUsuario.UsuarioId,["@Data"]=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),["@Acao"]=creating?"CRIACAO":"EDICAO",["@Descricao"]=$"Produto #{savedId} {produto.Nome}; custo {produto.PrecoCusto:C2}; venda {produto.PrecoVenda:C2}"});
            long savedVersion = Convert.ToInt64(database.Query("SELECT Versao FROM Produtos WHERE Id=@Id;", new Dictionary<string,object?> { ["@Id"]=savedId }).Single()["Versao"]);
            database.Execute("COMMIT;");
            if (creating) produto.Id = checked((int)savedId);
            produto.Versao = savedVersion;
        }
        catch { database.Execute("ROLLBACK;"); throw; }
    }

    private static Produto ToProduto(Dictionary<string, object?> row) => new()
    {
        Id = ToInt(row["Id"]), Versao = row.GetValueOrDefault("Versao") is { } version ? Convert.ToInt64(version) : null, Nome = ToText(row["Nome"]), CodigoBarras = ToNullableText(row["CodigoBarras"]), Marca = ToNullableText(row.GetValueOrDefault("Marca")), ImagemUrl = ToNullableText(row.GetValueOrDefault("ImagemUrl")), LocalImagePath = ToNullableText(row.GetValueOrDefault("LocalImagePath")),
        CategoriaId = ToNullableInt(row["CategoriaId"]), CategoriaNome = ToNullableText(row.GetValueOrDefault("CategoriaNome")),
        FornecedorId = ToNullableInt(row["FornecedorId"]), FornecedorNome = ToNullableText(row.GetValueOrDefault("FornecedorNome")),
        PrecoCusto = ToDecimal(row.GetValueOrDefault("PrecoCusto")), PrecoVenda = ToDecimal(row["PrecoVenda"]), UnidadeMedida = ToNullableText(row.GetValueOrDefault("UnidadeMedida")) ?? "UN", EstoqueAtual = ToDecimal(row["EstoqueAtual"]), EstoqueMinimo = ToDecimal(row["EstoqueMinimo"]),
        DataValidade = DateTime.TryParse(ToNullableText(row["DataValidade"]), out DateTime date) ? date : null,
        Ativo = ToInt(row["Ativo"]) == 1
    };

    private static int ToInt(object? value) => Convert.ToInt32(value, CultureInfo.InvariantCulture);
    private static int? ToNullableInt(object? value) => value is null ? null : ToInt(value);
    private static decimal ToDecimal(object? value) => Convert.ToDecimal(value, CultureInfo.InvariantCulture);
    private static string ToText(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    private static string? ToNullableText(object? value) => value is null ? null : ToText(value);
}
