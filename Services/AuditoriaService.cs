using CaixaLume.Data;
using CaixaLume.Models;

namespace CaixaLume.Services;

public sealed class AuditoriaService
{
    public void Registrar(string acao, string? entidade = null, string? descricao = null)
    {
        using var db = new DatabaseConnection(DatabaseInitializer.DatabasePath);
        db.Execute("INSERT INTO Auditoria (UsuarioId, DataHora, Acao, Entidade, Descricao) VALUES (@Usuario, @Data, @Acao, @Entidade, @Descricao);",
            new Dictionary<string, object?> { ["@Usuario"] = SessaoUsuario.UsuarioId, ["@Data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["@Acao"] = acao, ["@Entidade"] = entidade, ["@Descricao"] = descricao });
    }
}
