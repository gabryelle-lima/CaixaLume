namespace MercadinhoPdv.Services;

internal static class InicializacaoInterface
{
    internal static DialogResult Executar(Func<DialogResult> abrirWeb, Func<DialogResult> abrirNativa)
    {
        DialogResult result = abrirWeb();
        return result == DialogResult.Retry ? abrirNativa() : result;
    }
}
