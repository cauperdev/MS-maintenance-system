// ============================================================================
// ÁREA ESCOLHIDA — a área do card clicado na tela "Escolha a área" (Pages/EscolherArea), que abre depois do login.
//   - Fica guardada num cookie deste navegador (1 ano); "Todas as áreas" apaga o cookie.
//   - As telas com ": IFiltroArea" ficam sempre filtradas por ela (MsPage → OnPageHandlerExecutionAsync); elas não
//     têm mais o campo "Área" — trocar de área é só pelos cards. Sem área escolhida, mostram todas.
//   - Para trocar de área: item "Escolher área" do menu lateral (Pages/Shared/_Layout.cshtml), que volta para os cards.
// ONDE MEXER: tempo que fica guardada → Gravar · tela nova que deve seguir a área → acrescente ", IFiltroArea" na
// classe da tela (precisa ter [BindProperty(SupportsGet = true)] public int? CelulaId).
// ============================================================================
using MS.BancoDeDados;

namespace MS.Servicos;

// Tela cujo filtro "Área" segue a área escolhida nos cards.
public interface IFiltroArea
{
    int? CelulaId { get; set; }
}

public static class AreaEscolhida
{
    const string Cookie = "ms-area";

    // Área guardada, só se ela ainda existe (pode ter sido removida na tela Áreas).
    public static int? Ler(HttpContext ctx) => Buscar(ctx)?.Id;

    static Modelos.Celula? Buscar(HttpContext ctx)
    {
        if (!int.TryParse(ctx.Request.Cookies[Cookie], out var id)) return null;
        return ctx.RequestServices.GetRequiredService<AppDb>().Celulas.FirstOrDefault(c => c.Id == id);
    }

    public static void Gravar(HttpContext ctx, int? celulaId)
    {
        if (celulaId == null) { ctx.Response.Cookies.Delete(Cookie); return; }
        ctx.Response.Cookies.Append(Cookie, celulaId.Value.ToString(), new CookieOptions
        {
            Expires = DateTimeOffset.Now.AddYears(1), HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = ctx.Request.IsHttps, IsEssential = true,
        });
    }
}
