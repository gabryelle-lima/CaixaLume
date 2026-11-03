using System.Globalization;
using CaixaLume.Data;
using CaixaLume.Models;

namespace CaixaLume.Services;

public sealed class CaixaService
{
    /// <summary>Remove os caixas para reiniciar cenários de teste, preservando vendas e despesas.</summary>
    public int LimparHistoricoParaTestes()
    {
        AmbienteAplicacao.ExigirBancoDescartavel();
        new BackupService().Criar(DatabaseInitializer.DatabasePath + ".antes-limpeza-teste-" + Guid.NewGuid().ToString("N") + ".bak");
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        db.Execute("BEGIN IMMEDIATE;");
        try
        {
            int count = checked((int)Convert.ToInt64(db.Query("SELECT COUNT(*) AS Total FROM Caixa;").Single()["Total"], CultureInfo.InvariantCulture));
            db.Execute("UPDATE Vendas SET CaixaId = NULL WHERE CaixaId IS NOT NULL;");
            db.Execute("UPDATE Despesas SET CaixaId = NULL WHERE CaixaId IS NOT NULL;");
            db.Execute("DELETE FROM MovimentacoesCaixa;");
            db.Execute("DELETE FROM Caixa;");
            db.Execute("INSERT INTO Auditoria (UsuarioId, DataHora, Acao, Entidade, Descricao) VALUES (@Usuario, @Data, 'LIMPEZA_TESTE', 'CAIXA', @Descricao);", new Dictionary<string, object?>
            {
                ["@Usuario"] = SessaoUsuario.UsuarioId,
                ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ["@Descricao"] = $"Histórico de caixa removido para testes ({count} registro(s)); vendas e despesas preservadas sem vínculo com caixa."
            });
            db.Execute("COMMIT;");
            return count;
        }
        catch { db.Execute("ROLLBACK;"); throw; }
    }

    public ResumoCaixa? ObterAberto()
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        var row = db.Query("SELECT Id, DataAbertura, SaldoInicial FROM Caixa WHERE Status = 'Aberto' ORDER BY Id DESC LIMIT 1;").FirstOrDefault();
        return row is null ? null : CriarResumo(db, ToLong(row["Id"]));
    }

    public long Abrir(decimal saldoInicial)
    {
        Autorizacao.ExigirUsuario();
        if (saldoInicial < 0 || decimal.Round(saldoInicial, 2) != saldoInicial) throw new InvalidOperationException("Informe saldo inicial não negativo com até duas casas decimais.");
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        db.Execute("BEGIN IMMEDIATE;");
        try
        {
            Autorizacao.ExigirUsuario(db);
            if (db.Query("SELECT Id FROM Caixa WHERE Status = 'Aberto' LIMIT 1;").Count > 0) throw new InvalidOperationException("Já existe um caixa aberto.");
            db.Execute("INSERT INTO Caixa (DataAbertura, SaldoInicial, Status, UsuarioAberturaId) VALUES (@Data, @Saldo, 'Aberto', @Usuario);", new Dictionary<string, object?> { ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Saldo"] = saldoInicial, ["@Usuario"] = SessaoUsuario.UsuarioId });
            long id = db.LastInsertRowId;
            db.Execute("INSERT INTO Auditoria (UsuarioId,DataHora,Acao,Entidade,Descricao) VALUES (@Usuario,@Data,'ABERTURA','CAIXA',@Descricao);", new Dictionary<string, object?> { ["@Usuario"] = SessaoUsuario.UsuarioId, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Descricao"] = $"Caixa #{id} aberto com saldo inicial {saldoInicial:C2}" });
            db.Execute("COMMIT;");
            return id;
        }
        catch { db.Execute("ROLLBACK;"); throw; }
    }

    public void RegistrarMovimentacao(string tipo, decimal valor, string observacao)
    {
        Autorizacao.ExigirUsuario();
        tipo = tipo.Trim().ToUpperInvariant();
        if (tipo is not ("SUPRIMENTO" or "SANGRIA")) throw new InvalidOperationException("Selecione suprimento ou sangria.");
        if (valor <= 0 || decimal.Round(valor, 2) != valor) throw new InvalidOperationException("Informe um valor maior que zero com até duas casas decimais.");
        if (string.IsNullOrWhiteSpace(observacao)) throw new InvalidOperationException("Informe o motivo da movimentação.");
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        db.Execute("BEGIN IMMEDIATE;");
        try
        {
            Autorizacao.ExigirUsuario(db);
            var row = db.Query("SELECT Id FROM Caixa WHERE Status = 'Aberto' ORDER BY Id DESC LIMIT 1;").FirstOrDefault() ?? throw new InvalidOperationException("Não há caixa aberto.");
            long id = ToLong(row["Id"]);
            if (tipo == "SANGRIA" && CriarResumo(db, id).ValorEsperadoDinheiro < valor) throw new InvalidOperationException("A sangria não pode superar o dinheiro disponível no caixa.");
            db.Execute("INSERT INTO MovimentacoesCaixa (CaixaId, Tipo, Valor, DataMovimentacao, Observacao, UsuarioId) VALUES (@Caixa, @Tipo, @Valor, @Data, @Obs, @Usuario);", new Dictionary<string, object?> { ["@Caixa"] = id, ["@Tipo"] = tipo, ["@Valor"] = valor, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Obs"] = observacao.Trim().ToUpperInvariant(), ["@Usuario"] = SessaoUsuario.UsuarioId });
            db.Execute("INSERT INTO Auditoria (UsuarioId,DataHora,Acao,Entidade,Descricao) VALUES (@Usuario,@Data,@Acao,'CAIXA',@Descricao);", new Dictionary<string, object?> { ["@Usuario"] = SessaoUsuario.UsuarioId, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Acao"] = tipo, ["@Descricao"] = $"Caixa #{id}; {valor:C2}; {observacao.Trim().ToUpperInvariant()}" });
            db.Execute("COMMIT;");
        }
        catch { db.Execute("ROLLBACK;"); throw; }
    }

    public void RegistrarDespesa(string descricao, decimal valor, string formaPagamento)
    {
        Autorizacao.ExigirUsuario();
        descricao = descricao.Trim().ToUpperInvariant();
        formaPagamento = formaPagamento.Trim().ToUpperInvariant();
        if (descricao.Length == 0) throw new InvalidOperationException("Informe a descrição da despesa.");
        if (valor <= 0 || decimal.Round(valor, 2) != valor) throw new InvalidOperationException("Informe um valor maior que zero com até duas casas decimais.");
        if (formaPagamento is not ("DINHEIRO" or "PIX" or "DÉBITO" or "CRÉDITO")) throw new InvalidOperationException("Selecione uma forma de pagamento válida.");
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        db.Execute("BEGIN IMMEDIATE;");
        try
        {
            Autorizacao.ExigirUsuario(db);
            var row = db.Query("SELECT Id FROM Caixa WHERE Status = 'Aberto' ORDER BY Id DESC LIMIT 1;").FirstOrDefault() ?? throw new InvalidOperationException("Não há caixa aberto para registrar a despesa.");
            long id = ToLong(row["Id"]);
            if (formaPagamento == "DINHEIRO" && CriarResumo(db, id).ValorEsperadoDinheiro < valor) throw new InvalidOperationException("O dinheiro disponível no caixa é insuficiente para essa despesa.");
            db.Execute("INSERT INTO Despesas (Descricao, Valor, DataDespesa, FormaPagamento, CaixaId, UsuarioId, Status) VALUES (@Descricao, @Valor, @Data, @Pagamento, @Caixa, @Usuario, 'Paga');", new Dictionary<string, object?> { ["@Descricao"] = descricao, ["@Valor"] = valor, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Pagamento"] = formaPagamento, ["@Caixa"] = id, ["@Usuario"] = SessaoUsuario.UsuarioId });
            db.Execute("INSERT INTO Auditoria (UsuarioId,DataHora,Acao,Entidade,Descricao) VALUES (@Usuario,@Data,'CRIACAO','DESPESA',@Descricao);", new Dictionary<string, object?> { ["@Usuario"] = SessaoUsuario.UsuarioId, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Descricao"] = $"{descricao}; {valor:C2}; {formaPagamento}" });
            db.Execute("COMMIT;");
        }
        catch { db.Execute("ROLLBACK;"); throw; }
    }

    public ResumoFechamento Fechar(decimal valorContado)
    {
        Autorizacao.ExigirUsuario();
        if (valorContado < 0 || decimal.Round(valorContado, 2) != valorContado) throw new InvalidOperationException("Informe valor contado não negativo com até duas casas decimais.");
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        db.Execute("BEGIN IMMEDIATE;");
        try
        {
            Autorizacao.ExigirUsuario(db);
            var row = db.Query("SELECT Id FROM Caixa WHERE Status = 'Aberto' ORDER BY Id DESC LIMIT 1;").FirstOrDefault() ?? throw new InvalidOperationException("Não há caixa aberto para fechar.");
            long id = ToLong(row["Id"]);
            ResumoCaixa summary = CriarResumo(db, id);
            db.Execute("UPDATE Caixa SET DataFechamento = @Data, SaldoFinal = @Contado, Status = 'Fechado', UsuarioFechamentoId=@Usuario, Observacao = @Obs WHERE Id = @Id AND Status = 'Aberto';", new Dictionary<string, object?> { ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Contado"] = valorContado, ["@Obs"] = $"ESPERADO {summary.ValorEsperadoDinheiro.ToString(CultureInfo.InvariantCulture)}; CONTADO {valorContado.ToString(CultureInfo.InvariantCulture)}", ["@Id"] = id, ["@Usuario"] = SessaoUsuario.UsuarioId });
            if (db.Changes != 1) throw new InvalidOperationException("O caixa foi fechado em outra sessão.");
            db.Execute("INSERT INTO Auditoria (UsuarioId,DataHora,Acao,Entidade,Descricao) VALUES (@Usuario,@Data,'FECHAMENTO','CAIXA',@Descricao);", new Dictionary<string, object?> { ["@Usuario"] = SessaoUsuario.UsuarioId, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Descricao"] = $"Caixa #{id}; esperado {summary.ValorEsperadoDinheiro:C2}; contado {valorContado:C2}" });
            db.Execute("COMMIT;");
            return new ResumoFechamento { ValorEsperado = summary.ValorEsperadoDinheiro, ValorContado = valorContado };
        }
        catch { db.Execute("ROLLBACK;"); throw; }
    }

    public List<MovimentoCaixaLinha> ListarMovimentacoes(long caixaId)
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        return db.Query("SELECT m.DataMovimentacao AS Data, m.Tipo, m.Valor, m.Observacao, COALESCE(u.Nome,'—') AS Usuario FROM MovimentacoesCaixa m LEFT JOIN Usuarios u ON u.Id=m.UsuarioId WHERE m.CaixaId = @Id UNION ALL SELECT d.DataDespesa AS Data, 'DESPESA' AS Tipo, d.Valor, d.Descricao AS Observacao, COALESCE(u.Nome,'—') AS Usuario FROM Despesas d LEFT JOIN Usuarios u ON u.Id=d.UsuarioId WHERE d.CaixaId = @Id AND d.Status <> 'Cancelada' UNION ALL SELECT v.DataVenda AS Data, CASE WHEN v.Status='Cancelada' THEN 'VENDA CANCELADA ' || v.FormaPagamento ELSE 'VENDA ' || v.FormaPagamento END AS Tipo, v.ValorTotal AS Valor, 'VENDA #' || v.Id || CASE WHEN v.Status='Cancelada' THEN ' · ' || COALESCE(v.MotivoCancelamento,'') ELSE '' END AS Observacao, COALESCE(u.Nome,'—') AS Usuario FROM Vendas v LEFT JOIN Usuarios u ON u.Id=v.UsuarioId WHERE v.CaixaId = @Id AND NOT EXISTS (SELECT 1 FROM MovimentacoesCaixa mc WHERE mc.VendaId=v.Id) ORDER BY Data DESC;", new Dictionary<string, object?> { ["@Id"] = caixaId })
            .Select(row => new MovimentoCaixaLinha { Data = DateTime.Parse(Convert.ToString(row["Data"]) ?? "", CultureInfo.InvariantCulture), Tipo = Convert.ToString(row["Tipo"]) ?? "", Valor = ToDecimal(row["Valor"]), Observacao = Convert.ToString(row["Observacao"]), Usuario = Convert.ToString(row["Usuario"]) ?? "—" }).ToList();
    }

    public List<HistoricoCaixaLinha> ListarHistorico(int limite = 500) => ListarHistorico(null, null, limite);

    public List<HistoricoCaixaLinha> ListarHistorico(DateTime? inicio, DateTime? fim, int limite = 500)
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        return db.Query("""
            SELECT c.Id, c.DataAbertura, c.DataFechamento, c.SaldoInicial, c.SaldoFinal, c.Status, COALESCE(ua.Nome,'—') AS UsuarioAbertura, COALESCE(uf.Nome,'—') AS UsuarioFechamento,
                COALESCE((SELECT SUM(v.ValorTotal) FROM Vendas v WHERE v.CaixaId = c.Id AND v.Status = 'Concluida'), 0) AS Vendas,
                COALESCE((SELECT SUM(m.Valor) FROM MovimentacoesCaixa m WHERE m.CaixaId=c.Id AND m.Tipo='VENDA_DINHEIRO'),0) + COALESCE((SELECT SUM(v.ValorTotal) FROM Vendas v WHERE v.CaixaId=c.Id AND v.Status='Concluida' AND v.FormaPagamento='DINHEIRO' AND NOT EXISTS(SELECT 1 FROM MovimentacoesCaixa m WHERE m.VendaId=v.Id)),0) - COALESCE((SELECT SUM(m.Valor) FROM MovimentacoesCaixa m WHERE m.CaixaId=c.Id AND m.Tipo='ESTORNO_VENDA_DINHEIRO'),0) AS VendasDinheiro,
                COALESCE((SELECT SUM(m.Valor) FROM MovimentacoesCaixa m WHERE m.CaixaId = c.Id AND m.Tipo = 'SUPRIMENTO'), 0) AS Suprimentos,
                COALESCE((SELECT SUM(m.Valor) FROM MovimentacoesCaixa m WHERE m.CaixaId = c.Id AND m.Tipo = 'SANGRIA'), 0) AS Sangrias,
                COALESCE((SELECT SUM(d.Valor) FROM Despesas d WHERE d.CaixaId = c.Id AND d.Status <> 'Cancelada'), 0) AS Despesas,
                COALESCE((SELECT SUM(d.Valor) FROM Despesas d WHERE d.CaixaId = c.Id AND d.FormaPagamento = 'DINHEIRO' AND d.Status <> 'Cancelada'), 0) AS DespesasDinheiro
            FROM Caixa c LEFT JOIN Usuarios ua ON ua.Id=c.UsuarioAberturaId LEFT JOIN Usuarios uf ON uf.Id=c.UsuarioFechamentoId
            WHERE (@Inicio='' OR c.DataAbertura>=@Inicio) AND (@Fim='' OR c.DataAbertura<@Fim)
            ORDER BY c.Id DESC LIMIT @Limite;
            """, new Dictionary<string, object?> { ["@Inicio"] = inicio?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", ["@Fim"] = fim?.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", ["@Limite"] = Math.Clamp(limite, 1, 2000) })
            .Select(row =>
            {
                decimal opening = ToDecimal(row["SaldoInicial"]);
                decimal moneySales = ToDecimal(row["VendasDinheiro"]);
                decimal supply = ToDecimal(row["Suprimentos"]);
                decimal withdrawal = ToDecimal(row["Sangrias"]);
                decimal moneyExpenses = ToDecimal(row["DespesasDinheiro"]);
                return new HistoricoCaixaLinha
                {
                    Id = ToLong(row["Id"]),
                    Abertura = DateTime.Parse(Convert.ToString(row["DataAbertura"]) ?? "", CultureInfo.InvariantCulture),
                    Fechamento = DateTime.TryParse(Convert.ToString(row["DataFechamento"]), CultureInfo.InvariantCulture, out var closed) ? closed : null,
                    SaldoInicial = opening, Vendas = ToDecimal(row["Vendas"]), Suprimentos = supply,
                    Sangrias = withdrawal, Despesas = ToDecimal(row["Despesas"]),
                    ValorEsperado = opening + moneySales + supply - withdrawal - moneyExpenses,
                    ValorContado = row["SaldoFinal"] is null ? null : ToDecimal(row["SaldoFinal"]),
                    Status = Convert.ToString(row["Status"]) ?? "",
                    UsuarioAbertura = Convert.ToString(row["UsuarioAbertura"]) ?? "—",
                    UsuarioFechamento = Convert.ToString(row["UsuarioFechamento"]) ?? "—"
                };
            }).ToList();
    }

    internal static ResumoCaixa CriarResumo(DatabaseConnection db, long id)
    {
        var row = db.Query("""
            SELECT c.Id, c.DataAbertura, c.SaldoInicial,
                (SELECT COUNT(*) FROM Vendas v WHERE v.CaixaId = c.Id AND v.Status = 'Concluida') AS QuantidadeVendas,
                COALESCE((SELECT SUM(ValorTotal) FROM Vendas v WHERE v.CaixaId = c.Id AND v.Status = 'Concluida'), 0) AS TotalVendas,
                COALESCE((SELECT SUM(m.Valor) FROM MovimentacoesCaixa m WHERE m.CaixaId=c.Id AND m.Tipo='VENDA_DINHEIRO'),0) + COALESCE((SELECT SUM(v.ValorTotal) FROM Vendas v WHERE v.CaixaId=c.Id AND v.Status='Concluida' AND v.FormaPagamento='DINHEIRO' AND NOT EXISTS(SELECT 1 FROM MovimentacoesCaixa m WHERE m.VendaId=v.Id)),0) - COALESCE((SELECT SUM(m.Valor) FROM MovimentacoesCaixa m WHERE m.CaixaId=c.Id AND m.Tipo='ESTORNO_VENDA_DINHEIRO'),0) AS VendasDinheiro,
                COALESCE((SELECT SUM(Valor) FROM MovimentacoesCaixa m WHERE m.CaixaId = c.Id AND m.Tipo = 'SUPRIMENTO'), 0) AS Suprimentos,
                COALESCE((SELECT SUM(Valor) FROM MovimentacoesCaixa m WHERE m.CaixaId = c.Id AND m.Tipo = 'SANGRIA'), 0) AS Sangrias,
                COALESCE((SELECT SUM(Valor) FROM Despesas d WHERE d.CaixaId = c.Id AND d.FormaPagamento = 'DINHEIRO' AND d.Status <> 'Cancelada'), 0) AS DespesasDinheiro
                ,COALESCE((SELECT SUM(Valor) FROM Despesas d WHERE d.CaixaId = c.Id AND d.Status <> 'Cancelada'), 0) AS DespesasTotais
            FROM Caixa c WHERE c.Id = @Id;
            """, new Dictionary<string, object?> { ["@Id"] = id }).Single();
        return new ResumoCaixa
        {
            Id = ToLong(row["Id"]), DataAbertura = DateTime.Parse(Convert.ToString(row["DataAbertura"]) ?? "", CultureInfo.InvariantCulture),
            SaldoInicial = ToDecimal(row["SaldoInicial"]), QuantidadeVendas = checked((int)ToLong(row["QuantidadeVendas"])),
            TotalVendas = ToDecimal(row["TotalVendas"]), VendasDinheiro = ToDecimal(row["VendasDinheiro"]),
            Suprimentos = ToDecimal(row["Suprimentos"]), Sangrias = ToDecimal(row["Sangrias"]), DespesasDinheiro = ToDecimal(row["DespesasDinheiro"]), DespesasTotais = ToDecimal(row["DespesasTotais"])
        };
    }

    private static decimal ToDecimal(object? value) => ValoresMonetarios.Arredondar(Convert.ToDecimal(value, CultureInfo.InvariantCulture));
    private static long ToLong(object? value) => Convert.ToInt64(value, CultureInfo.InvariantCulture);
}
