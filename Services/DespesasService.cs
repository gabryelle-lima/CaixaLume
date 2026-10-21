using System.Globalization;
using MercadinhoPdv.Data;

namespace MercadinhoPdv.Services;

public sealed class DespesasService
{
    public List<DespesaLinha> Listar(DateTime inicio, DateTime fim, string? busca = null)
    {
        Autorizacao.ExigirAdministrador();
        using var db=new DatabaseConnection(DatabaseInitializer.DatabasePath);
        return db.Query("SELECT d.Id,d.DataDespesa AS Data,d.Descricao,d.Categoria,d.Valor,d.FormaPagamento,d.Status,d.Observacao FROM Despesas d WHERE d.DataDespesa>=@Inicio AND d.DataDespesa<@Fim AND (@Busca='' OR d.Descricao LIKE @Termo OR COALESCE(d.Categoria,'') LIKE @Termo) ORDER BY d.Id DESC;",new Dictionary<string,object?>{["@Inicio"]=inicio.Date.ToString("yyyy-MM-dd"),["@Fim"]=fim.Date.AddDays(1).ToString("yyyy-MM-dd"),["@Busca"]=busca?.Trim()??"",["@Termo"]=$"%{busca?.Trim()}%"})
        .Select(r=>new DespesaLinha{Id=Convert.ToInt64(r["Id"]),Data=DateTime.Parse(Convert.ToString(r["Data"])!,CultureInfo.InvariantCulture),Descricao=Convert.ToString(r["Descricao"])??"",Categoria=Convert.ToString(r["Categoria"])??"",Valor=Convert.ToDecimal(r["Valor"],CultureInfo.InvariantCulture),FormaPagamento=Convert.ToString(r["FormaPagamento"])??"",Status=Convert.ToString(r["Status"])??"",Observacao=Convert.ToString(r["Observacao"])??""}).ToList();
    }

    public void Salvar(DespesaLinha item)
    {
        Autorizacao.ExigirAdministrador();
        item.Descricao=item.Descricao.Trim().ToUpperInvariant(); item.Categoria=item.Categoria.Trim().ToUpperInvariant(); item.Observacao=item.Observacao.Trim().ToUpperInvariant(); item.FormaPagamento=item.FormaPagamento.Trim().ToUpperInvariant();
        if(item.Descricao.Length==0||item.Valor<=0||decimal.Round(item.Valor,2)!=item.Valor)throw new InvalidOperationException("Informe descrição e valor maior que zero, com até duas casas decimais.");
        if(item.FormaPagamento is not ("DINHEIRO" or "PIX" or "DÉBITO" or "CRÉDITO"))throw new InvalidOperationException("Forma de pagamento inválida.");
        using var db=new DatabaseConnection(DatabaseInitializer.DatabasePath);db.Execute("BEGIN IMMEDIATE;");
        try
        {
            var existing = item.Id == 0 ? null : db.Query("SELECT d.CaixaId,d.Status,d.FormaPagamento,d.Valor,c.Status AS CaixaStatus FROM Despesas d LEFT JOIN Caixa c ON c.Id=d.CaixaId WHERE d.Id=@Id;",new Dictionary<string,object?> { ["@Id"]=item.Id }).SingleOrDefault() ?? throw new InvalidOperationException("Despesa não encontrada.");
            if (existing is not null && Convert.ToString(existing["Status"]) == "Cancelada") throw new InvalidOperationException("Esta despesa foi cancelada.");
            if (existing?["CaixaId"] is not null && Convert.ToString(existing["CaixaStatus"]) != "Aberto") throw new InvalidOperationException("Não é possível editar despesa vinculada a um caixa fechado.");
            long? cashId = existing?["CaixaId"] is { } linked ? Convert.ToInt64(linked) : null;
            if (cashId is null)
            {
                var cash = db.Query("SELECT Id FROM Caixa WHERE Status='Aberto' ORDER BY Id DESC LIMIT 1;").FirstOrDefault();
                if (cash is not null) cashId = Convert.ToInt64(cash["Id"]);
            }
            if(item.FormaPagamento=="DINHEIRO"&&cashId is null)throw new InvalidOperationException("Abra o caixa para lançar uma despesa paga em dinheiro.");
            if (item.FormaPagamento == "DINHEIRO" && cashId.HasValue)
            {
                decimal available = CaixaService.CriarResumo(db,cashId.Value).ValorEsperadoDinheiro;
                if (existing is not null && Convert.ToString(existing["FormaPagamento"]) == "DINHEIRO" && existing["CaixaId"] is not null)
                    available += Convert.ToDecimal(existing["Valor"],CultureInfo.InvariantCulture);
                if (item.Valor > available) throw new InvalidOperationException("O dinheiro disponível no caixa é insuficiente para essa despesa.");
            }
            var parameters=Params(item,cashId);
            if(item.Id==0){parameters.Remove("@Id");db.Execute("INSERT INTO Despesas (Descricao,Categoria,Valor,DataDespesa,FormaPagamento,CaixaId,UsuarioId,Status,Observacao) VALUES (@Desc,@Categoria,@Valor,@Data,@Forma,@Caixa,@Usuario,'Paga',@Obs);",parameters);}
            else
            {
                parameters.Remove("@Usuario");
                db.Execute("UPDATE Despesas SET Descricao=@Desc,Categoria=@Categoria,Valor=@Valor,DataDespesa=@Data,FormaPagamento=@Forma,CaixaId=@Caixa,Observacao=@Obs WHERE Id=@Id AND Status<>'Cancelada';",parameters);
            }
            if (db.Changes != 1) throw new InvalidOperationException("A despesa não pôde ser salva.");
            db.Execute("INSERT INTO Auditoria (UsuarioId,DataHora,Acao,Entidade,Descricao) VALUES (@Usuario,@Data,@Acao,'DESPESA',@Descricao);",new Dictionary<string,object?>{["@Usuario"]=Models.SessaoUsuario.UsuarioId,["@Data"]=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),["@Acao"]=item.Id==0?"CRIACAO":"EDICAO",["@Descricao"]=$"Despesa {item.Descricao} · {item.Valor:C2}"});db.Execute("COMMIT;");
        }catch{db.Execute("ROLLBACK;");throw;}
    }
    public void Cancelar(long id,string motivo)
    {
        Autorizacao.ExigirAdministrador();
        if(string.IsNullOrWhiteSpace(motivo))throw new InvalidOperationException("Informe o motivo.");using var db=new DatabaseConnection(DatabaseInitializer.DatabasePath);
        db.Execute("BEGIN IMMEDIATE;");
        try
        {
            var row=db.Query("SELECT d.CaixaId,c.Status AS CaixaStatus,d.Status FROM Despesas d LEFT JOIN Caixa c ON c.Id=d.CaixaId WHERE d.Id=@Id;",new Dictionary<string,object?>{["@Id"]=id}).FirstOrDefault()??throw new InvalidOperationException("Despesa não encontrada.");
            if(row["CaixaId"] is not null&&Convert.ToString(row["CaixaStatus"])!="Aberto")throw new InvalidOperationException("Não é possível cancelar uma despesa vinculada a um caixa já fechado.");
            if(Convert.ToString(row["Status"])=="Cancelada")throw new InvalidOperationException("Esta despesa já foi cancelada.");
            db.Execute("UPDATE Despesas SET Status='Cancelada',Observacao=COALESCE(Observacao,'') || ' · CANCELADA: ' || @Motivo WHERE Id=@Id AND Status<>'Cancelada';",new Dictionary<string,object?>{["@Motivo"]=motivo.Trim().ToUpperInvariant(),["@Id"]=id});
            db.Execute("INSERT INTO Auditoria (UsuarioId,DataHora,Acao,Entidade,Descricao) VALUES (@Usuario,@Data,'CANCELAMENTO','DESPESA',@Descricao);",new Dictionary<string,object?>{["@Usuario"]=Models.SessaoUsuario.UsuarioId,["@Data"]=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),["@Descricao"]=$"Despesa #{id}. Motivo: {motivo.Trim().ToUpperInvariant()}"});db.Execute("COMMIT;");
        }catch{db.Execute("ROLLBACK;");throw;}
    }
    private static Dictionary<string,object?> Params(DespesaLinha x,long? cash)=>new(){["@Desc"]=x.Descricao,["@Categoria"]=x.Categoria,["@Valor"]=x.Valor,["@Data"]=x.Data.ToString("yyyy-MM-dd HH:mm:ss"),["@Forma"]=x.FormaPagamento,["@Caixa"]=cash,["@Usuario"]=Models.SessaoUsuario.UsuarioId,["@Obs"]=x.Observacao,["@Id"]=x.Id};
}
public sealed class DespesaLinha{public long Id{get;set;}public DateTime Data{get;set;}=DateTime.Now;public string Descricao{get;set;}="";public string Categoria{get;set;}="";public decimal Valor{get;set;}public string FormaPagamento{get;set;}="DINHEIRO";public string Status{get;set;}="Paga";public string Observacao{get;set;}="";}
