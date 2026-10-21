namespace MercadinhoPdv.Data;

public static class DatabaseInitializer
{
    private const int CurrentSchemaVersion = 4;
    public static string DatabasePath { get; } = Environment.GetEnvironmentVariable("MERCADINHOPDV_DATABASE") is { Length: > 0 } overridePath
        ? Path.GetFullPath(overridePath)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MercadinhoPdv", "dados", "mercadinho.db");

    public static void Initialize() => Initialize(DatabasePath);

    internal static void Initialize(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        bool hadDatabase = File.Exists(databasePath) && new FileInfo(databasePath).Length > 0;
        using var database = new DatabaseConnection(databasePath);
        
        bool hasMigrationTable = database.Query("SELECT 1 FROM sqlite_master WHERE type='table' AND name='SchemaMigrations' LIMIT 1;").Count > 0;
        int currentVersion = hasMigrationTable ? Convert.ToInt32(database.Query("SELECT COALESCE(MAX(Version),0) AS Version FROM SchemaMigrations;").Single()["Version"]) : 0;
        if (currentVersion > CurrentSchemaVersion) throw new InvalidOperationException($"O banco está na versão {currentVersion}, superior à versão suportada {CurrentSchemaVersion} por este aplicativo.");
        string? safetyCopy = currentVersion < CurrentSchemaVersion && hadDatabase ? CreatePreMigrationBackup(database, currentVersion, databasePath) : null;
        if (currentVersion < 1)
        {
            try
            {
                database.Execute("BEGIN IMMEDIATE;");
                database.ExecuteScript("CREATE TABLE IF NOT EXISTS SchemaMigrations (Version INTEGER PRIMARY KEY, AppliedAt TEXT NOT NULL);");
                ApplyLegacySchemaMigration(database);
                database.Execute("INSERT INTO SchemaMigrations (Version, AppliedAt) VALUES (1, @Data);", new Dictionary<string, object?> { ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
                database.Execute("COMMIT;");
                currentVersion = 1;
            }
            catch
            {
                try { database.Execute("ROLLBACK;"); } catch { }
                throw new InvalidOperationException(safetyCopy is null
                    ? "A migração inicial do banco falhou; nenhuma alteração foi confirmada."
                    : $"A migração inicial do banco falhou e foi revertida. Cópia de segurança: {safetyCopy}");
            }
        }
        if (currentVersion < 2)
        {
            try
            {
                database.Execute("BEGIN IMMEDIATE;");
                EnsureColumn(database, "ItensVenda", "PrecoCustoUnitario", "NUMERIC NOT NULL DEFAULT 0");
                database.ExecuteScript("UPDATE ItensVenda SET PrecoCustoUnitario = COALESCE((SELECT PrecoCusto FROM Produtos WHERE Produtos.Id = ItensVenda.ProdutoId), 0) WHERE PrecoCustoUnitario = 0;");
                database.Execute("INSERT INTO SchemaMigrations (Version, AppliedAt) VALUES (2, @Data);", new Dictionary<string, object?> { ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
                database.Execute("COMMIT;");
                currentVersion = 2;
            }
            catch
            {
                try { database.Execute("ROLLBACK;"); } catch { }
                throw new InvalidOperationException(safetyCopy is null
                    ? "A migração v2 do banco falhou; nenhuma alteração foi confirmada."
                    : $"A migração v2 do banco falhou e foi revertida. Cópia de segurança: {safetyCopy}");
            }
        }
        if (currentVersion < 3)
        {
            try
            {
                database.Execute("BEGIN IMMEDIATE;");
                EnsureColumn(database, "Vendas", "Subtotal", "NUMERIC NOT NULL DEFAULT 0");
                EnsureColumn(database, "Vendas", "ValorDesconto", "NUMERIC NOT NULL DEFAULT 0");
                EnsureColumn(database, "Vendas", "ValorAcrescimo", "NUMERIC NOT NULL DEFAULT 0");
                EnsureColumn(database, "Vendas", "TipoDesconto", "TEXT NOT NULL DEFAULT 'VALOR'");
                EnsureColumn(database, "Vendas", "DescontoInformado", "NUMERIC NOT NULL DEFAULT 0");
                EnsureColumn(database, "Vendas", "OperacaoId", "TEXT");
                EnsureColumn(database, "Vendas", "AssinaturaOperacao", "TEXT");
                database.ExecuteScript("UPDATE Vendas SET Subtotal=ValorTotal WHERE Subtotal=0 AND ValorDesconto=0 AND ValorAcrescimo=0; CREATE UNIQUE INDEX IF NOT EXISTS IX_Vendas_OperacaoId ON Vendas(OperacaoId) WHERE OperacaoId IS NOT NULL;");
                database.Execute("INSERT INTO SchemaMigrations (Version,AppliedAt) VALUES (3,@Data);", new Dictionary<string, object?> { ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
                database.Execute("COMMIT;");
            }
            catch (Exception exception)
            {
                try { database.Execute("ROLLBACK;"); }
                catch (Exception rollback) { exception.Data["MigrationRollback"] = rollback.ToString(); }
                throw new InvalidOperationException($"A migração v3 foi revertida. Cópia de segurança: {safetyCopy ?? "banco novo"}", exception);
            }
        }
        if (currentVersion < 4)
        {
            try
            {
                database.Execute("BEGIN IMMEDIATE;");
                EnsureColumn(database, "Produtos", "ExcluidoEm", "TEXT");
                var owners = database.Query("SELECT Id FROM Usuarios WHERE UPPER(TRIM(Nome))='GABRYELLE' AND UPPER(Perfil)='ADMINISTRADOR' AND Ativo=1;");
                if (owners.Count == 1)
                    database.Execute("INSERT OR IGNORE INTO Configuracoes (Chave,Valor) VALUES ('ProdutoExcluirUsuarioId',@Id);", new Dictionary<string, object?> { ["@Id"] = owners[0]["Id"] });
                database.Execute("INSERT INTO SchemaMigrations (Version,AppliedAt) VALUES (4,@Data);", new Dictionary<string, object?> { ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
                database.Execute("COMMIT;");
            }
            catch (Exception exception)
            {
                try { database.Execute("ROLLBACK;"); }
                catch (Exception rollback) { exception.Data["MigrationRollback"] = rollback.ToString(); }
                throw new InvalidOperationException($"A migração v4 foi revertida. Cópia de segurança: {safetyCopy ?? "banco novo"}", exception);
            }
        }
    }

    private static string CreatePreMigrationBackup(DatabaseConnection database, int currentVersion, string databasePath)
    {
        string backupPath = $"{databasePath}.pre-migration-v{Math.Max(1, currentVersion)}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.bak";
        database.BackupTo(backupPath);
        return backupPath;
    }

    private static void ApplyLegacySchemaMigration(DatabaseConnection database)
    {
        database.ExecuteScript(CreateTablesScript);
        EnsureColumn(database, "Produtos", "PrecoCusto", "NUMERIC NOT NULL DEFAULT 0");
        EnsureColumn(database, "Produtos", "UnidadeMedida", "TEXT NOT NULL DEFAULT 'UN'");
        EnsureColumn(database, "Produtos", "Marca", "TEXT");
        EnsureColumn(database, "Produtos", "ImagemUrl", "TEXT");
        EnsureColumn(database, "Produtos", "LocalImagePath", "TEXT");
        EnsureColumn(database, "ItensVenda", "UnidadeMedida", "TEXT NOT NULL DEFAULT 'UN'");
        EnsureColumn(database, "MovimentacoesEstoque", "UnidadeMedida", "TEXT NOT NULL DEFAULT 'UN'");
        EnsureColumn(database, "MovimentacoesEstoque", "Motivo", "TEXT");
        EnsureColumn(database, "MovimentacoesEstoque", "VendaId", "INTEGER");
        EnsureColumn(database, "MovimentacoesEstoque", "UsuarioId", "INTEGER");
        EnsureColumn(database, "MovimentacoesEstoque", "FornecedorId", "INTEGER");
        EnsureColumn(database, "MovimentacoesEstoque", "Documento", "TEXT");
        EnsureColumn(database, "Fornecedores", "Endereco", "TEXT");
        EnsureColumn(database, "Fornecedores", "Observacao", "TEXT");
        EnsureColumn(database, "Despesas", "Status", "TEXT NOT NULL DEFAULT 'Paga'");
        EnsureColumn(database, "Despesas", "Observacao", "TEXT");
        EnsureColumn(database, "Usuarios", "Perfil", "TEXT NOT NULL DEFAULT 'OPERADOR'");
        EnsureColumn(database, "Vendas", "UsuarioId", "INTEGER");
        EnsureColumn(database, "Vendas", "ValorRecebido", "NUMERIC NOT NULL DEFAULT 0");
        EnsureColumn(database, "Vendas", "Troco", "NUMERIC NOT NULL DEFAULT 0");
        EnsureColumn(database, "Vendas", "CanceladaEm", "TEXT");
        EnsureColumn(database, "Vendas", "CanceladaPorId", "INTEGER");
        EnsureColumn(database, "Vendas", "MotivoCancelamento", "TEXT");
        database.ExecuteScript("CREATE TABLE IF NOT EXISTS MovimentacoesCaixa (Id INTEGER PRIMARY KEY AUTOINCREMENT, CaixaId INTEGER NOT NULL, Tipo TEXT NOT NULL, Valor NUMERIC NOT NULL, DataMovimentacao TEXT NOT NULL, Observacao TEXT, UsuarioId INTEGER, FOREIGN KEY (CaixaId) REFERENCES Caixa(Id), FOREIGN KEY (UsuarioId) REFERENCES Usuarios(Id));");
        EnsureColumn(database, "MovimentacoesCaixa", "UsuarioId", "INTEGER");
        EnsureColumn(database, "MovimentacoesCaixa", "VendaId", "INTEGER");
        database.ExecuteScript("CREATE TABLE IF NOT EXISTS Auditoria (Id INTEGER PRIMARY KEY AUTOINCREMENT, UsuarioId INTEGER, DataHora TEXT NOT NULL, Acao TEXT NOT NULL, Entidade TEXT, Descricao TEXT, FOREIGN KEY (UsuarioId) REFERENCES Usuarios(Id)); CREATE INDEX IF NOT EXISTS IX_Auditoria_DataHora ON Auditoria(DataHora);");
        database.ExecuteScript("CREATE INDEX IF NOT EXISTS IX_Vendas_CaixaId ON Vendas(CaixaId); CREATE INDEX IF NOT EXISTS IX_MovimentacoesCaixa_CaixaId ON MovimentacoesCaixa(CaixaId);");
        PadronizarCadastros(database);
        CriarCategoriasIniciais(database);
    }

    private static void EnsureColumn(DatabaseConnection database, string table, string column, string definition)
    {
        bool exists = database.Query($"PRAGMA table_info({table});").Any(row => string.Equals(Convert.ToString(row["name"]), column, StringComparison.OrdinalIgnoreCase));
        if (!exists) database.ExecuteScript($"ALTER TABLE {table} ADD COLUMN {column} {definition};");
    }

    private static void PadronizarCadastros(DatabaseConnection database)
    {
        foreach (var row in database.Query("SELECT Id, Nome, CodigoBarras FROM Produtos;"))
        {
            string currentName = Convert.ToString(row["Nome"]) ?? string.Empty;
            string? currentBarcode = Convert.ToString(row["CodigoBarras"]);
            string name = currentName.Trim().ToUpperInvariant();
            string? barcode = string.IsNullOrWhiteSpace(currentBarcode) ? null : currentBarcode.Trim();
            if (currentName != name || currentBarcode != barcode)
                database.Execute("UPDATE Produtos SET Nome = @Nome, CodigoBarras = @Codigo WHERE Id = @Id;", new Dictionary<string, object?> { ["@Nome"] = name, ["@Codigo"] = barcode, ["@Id"] = row["Id"] });
        }
        foreach (var row in database.Query("SELECT Id, Nome FROM Fornecedores;"))
        {
            string current = Convert.ToString(row["Nome"]) ?? string.Empty;
            if (current != current.Trim().ToUpperInvariant()) database.Execute("UPDATE Fornecedores SET Nome = @Nome WHERE Id = @Id;", new Dictionary<string, object?> { ["@Nome"] = current.Trim().ToUpperInvariant(), ["@Id"] = row["Id"] });
        }
        foreach (var row in database.Query("SELECT Id, Descricao FROM Despesas;"))
        {
            string current = Convert.ToString(row["Descricao"]) ?? string.Empty;
            if (current != current.Trim().ToUpperInvariant()) database.Execute("UPDATE Despesas SET Descricao = @Descricao WHERE Id = @Id;", new Dictionary<string, object?> { ["@Descricao"] = current.Trim().ToUpperInvariant(), ["@Id"] = row["Id"] });
        }
        foreach (var row in database.Query("SELECT Chave, Valor FROM Configuracoes WHERE Chave = 'NomeLoja';"))
        {
            string current = Convert.ToString(row["Valor"]) ?? string.Empty;
            if (current != current.Trim()) database.Execute("UPDATE Configuracoes SET Valor = @Valor WHERE Chave = @Chave;", new Dictionary<string, object?> { ["@Valor"] = current.Trim(), ["@Chave"] = row["Chave"] });
        }

        var rows = database.Query("SELECT Id, Nome FROM Categorias ORDER BY Id;");
        var groups = rows.GroupBy(row => (Convert.ToString(row["Nome"]) ?? string.Empty).Trim().ToUpperInvariant());
        foreach (var group in groups)
        {
            if (string.IsNullOrWhiteSpace(group.Key)) continue;
            var entries = group.ToList();
            long primaryId = Convert.ToInt64(entries[0]["Id"]);
            foreach (var duplicate in entries.Skip(1))
            {
                long duplicateId = Convert.ToInt64(duplicate["Id"]);
                database.Execute("UPDATE Categorias SET Nome = @Nome WHERE Id = @Id;", new Dictionary<string, object?> { ["@Nome"] = $"__CATEGORIA_DUPLICADA_{duplicateId}", ["@Id"] = duplicateId });
            }
            if (Convert.ToString(entries[0]["Nome"]) != group.Key)
                database.Execute("UPDATE Categorias SET Nome = @Nome WHERE Id = @Id;", new Dictionary<string, object?> { ["@Nome"] = group.Key, ["@Id"] = primaryId });
            foreach (var duplicate in entries.Skip(1))
            {
                long duplicateId = Convert.ToInt64(duplicate["Id"]);
                database.Execute("UPDATE Produtos SET CategoriaId = @Primary WHERE CategoriaId = @Duplicate;", new Dictionary<string, object?> { ["@Primary"] = primaryId, ["@Duplicate"] = duplicateId });
                database.Execute("UPDATE Categorias SET Ativa = 0 WHERE Id = @Id;", new Dictionary<string, object?> { ["@Id"] = duplicateId });
            }
        }
    }

    private static void CriarCategoriasIniciais(DatabaseConnection database)
    {
        string[] categories = ["MERCEARIA", "BEBIDAS", "FRIOS", "LATICÍNIOS", "CARNES", "HIGIENE", "LIMPEZA", "PADARIA", "HORTIFRUTI", "DOCES", "CONGELADOS", "OUTROS"];
        foreach (string category in categories)
            database.Execute("INSERT INTO Categorias (Nome) SELECT @Nome WHERE NOT EXISTS (SELECT 1 FROM Categorias WHERE UPPER(TRIM(Nome)) = @Nome);", new Dictionary<string, object?> { ["@Nome"] = category });
    }

    // Datas usam ISO-8601, legível e ordenável pelo SQLite.
    private const string CreateTablesScript = """
        CREATE TABLE IF NOT EXISTS Categorias (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nome TEXT NOT NULL UNIQUE, Ativa INTEGER NOT NULL DEFAULT 1, CriadoEm TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
        CREATE TABLE IF NOT EXISTS Fornecedores (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nome TEXT NOT NULL, Documento TEXT, Telefone TEXT, Email TEXT, Endereco TEXT, Observacao TEXT, Ativo INTEGER NOT NULL DEFAULT 1, CriadoEm TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
        CREATE TABLE IF NOT EXISTS Usuarios (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nome TEXT NOT NULL, Login TEXT NOT NULL UNIQUE, SenhaHash TEXT, Perfil TEXT NOT NULL DEFAULT 'OPERADOR', Ativo INTEGER NOT NULL DEFAULT 1, CriadoEm TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
        CREATE TABLE IF NOT EXISTS Produtos (Id INTEGER PRIMARY KEY AUTOINCREMENT, CodigoBarras TEXT UNIQUE, Nome TEXT NOT NULL, CategoriaId INTEGER, FornecedorId INTEGER, PrecoCusto NUMERIC NOT NULL DEFAULT 0, PrecoVenda NUMERIC NOT NULL DEFAULT 0, UnidadeMedida TEXT NOT NULL DEFAULT 'UN', EstoqueAtual NUMERIC NOT NULL DEFAULT 0, EstoqueMinimo NUMERIC NOT NULL DEFAULT 0, DataValidade TEXT, Ativo INTEGER NOT NULL DEFAULT 1, CriadoEm TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, FOREIGN KEY (CategoriaId) REFERENCES Categorias(Id), FOREIGN KEY (FornecedorId) REFERENCES Fornecedores(Id));
        CREATE TABLE IF NOT EXISTS Caixa (Id INTEGER PRIMARY KEY AUTOINCREMENT, DataAbertura TEXT NOT NULL, DataFechamento TEXT, SaldoInicial NUMERIC NOT NULL DEFAULT 0, SaldoFinal NUMERIC, Status TEXT NOT NULL, UsuarioAberturaId INTEGER, UsuarioFechamentoId INTEGER, Observacao TEXT, FOREIGN KEY (UsuarioAberturaId) REFERENCES Usuarios(Id), FOREIGN KEY (UsuarioFechamentoId) REFERENCES Usuarios(Id));
        CREATE TABLE IF NOT EXISTS Vendas (Id INTEGER PRIMARY KEY AUTOINCREMENT, DataVenda TEXT NOT NULL, ValorTotal NUMERIC NOT NULL DEFAULT 0, FormaPagamento TEXT, Status TEXT NOT NULL DEFAULT 'Concluida', CaixaId INTEGER, UsuarioId INTEGER, ValorRecebido NUMERIC NOT NULL DEFAULT 0, Troco NUMERIC NOT NULL DEFAULT 0, CanceladaEm TEXT, CanceladaPorId INTEGER, MotivoCancelamento TEXT, FOREIGN KEY (CaixaId) REFERENCES Caixa(Id), FOREIGN KEY (UsuarioId) REFERENCES Usuarios(Id));
        CREATE TABLE IF NOT EXISTS ItensVenda (Id INTEGER PRIMARY KEY AUTOINCREMENT, VendaId INTEGER NOT NULL, ProdutoId INTEGER NOT NULL, Quantidade NUMERIC NOT NULL, UnidadeMedida TEXT NOT NULL DEFAULT 'UN', ValorUnitario NUMERIC NOT NULL, PrecoCustoUnitario NUMERIC NOT NULL DEFAULT 0, Subtotal NUMERIC NOT NULL, FOREIGN KEY (VendaId) REFERENCES Vendas(Id), FOREIGN KEY (ProdutoId) REFERENCES Produtos(Id));
        CREATE TABLE IF NOT EXISTS MovimentacoesEstoque (Id INTEGER PRIMARY KEY AUTOINCREMENT, ProdutoId INTEGER NOT NULL, Tipo TEXT NOT NULL, Quantidade NUMERIC NOT NULL, UnidadeMedida TEXT NOT NULL DEFAULT 'UN', DataMovimentacao TEXT NOT NULL, Motivo TEXT, VendaId INTEGER, UsuarioId INTEGER, FornecedorId INTEGER, Documento TEXT, FOREIGN KEY (ProdutoId) REFERENCES Produtos(Id), FOREIGN KEY (VendaId) REFERENCES Vendas(Id), FOREIGN KEY (UsuarioId) REFERENCES Usuarios(Id), FOREIGN KEY (FornecedorId) REFERENCES Fornecedores(Id));
        CREATE TABLE IF NOT EXISTS Despesas (Id INTEGER PRIMARY KEY AUTOINCREMENT, Descricao TEXT NOT NULL, Categoria TEXT, Valor NUMERIC NOT NULL, DataDespesa TEXT NOT NULL, FormaPagamento TEXT, CaixaId INTEGER, UsuarioId INTEGER, Status TEXT NOT NULL DEFAULT 'Paga', Observacao TEXT, FOREIGN KEY (CaixaId) REFERENCES Caixa(Id), FOREIGN KEY (UsuarioId) REFERENCES Usuarios(Id));
        CREATE TABLE IF NOT EXISTS Configuracoes (Chave TEXT PRIMARY KEY, Valor TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS IX_Produtos_CodigoBarras ON Produtos(CodigoBarras);
        CREATE INDEX IF NOT EXISTS IX_Vendas_DataVenda ON Vendas(DataVenda);
        CREATE INDEX IF NOT EXISTS IX_MovimentacoesEstoque_ProdutoId ON MovimentacoesEstoque(ProdutoId);
        """;
}
