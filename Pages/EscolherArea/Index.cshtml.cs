// ============================================================================
// TELA: ESCOLHA A ÁREA (lógica)  ·  endereço /EscolherArea  ·  visual em Index.cshtml  ·  abre logo depois do login
// Cards grandes, um por área, só com a logo e o nome da área. Clicar num card guarda a área
// (Servicos/AreaEscolhida.cs) e abre a tela inicial do grupo: os filtros "Área" das telas já vêm com ela.
// Qualquer pessoa logada usa a tela (não depende de permissão). Esta tela não mexe no cadastro de Áreas (criar,
// renomear, remover e enviar a logo é só na tela Áreas). O × só esconde o card da tela da pessoa
// (Usuario.CardsEscondidos).
// ONDE MEXER:
//   - Cards da tela .................... OnGetAsync
//   - Logo de cada card ................ OnGetLogo (arquivos em dados/fotos/logos-areas — Servicos/LogosArea.cs; enviar na tela Áreas)
//   - Para onde o card leva ........... OnGetEntrarAsync (tela inicial do grupo — Servicos/Acesso.cs → PaginaInicial)
//   - Esconder card / mostrar todos .... OnPostRemoverAsync / OnPostTodasAsync
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.EscolherArea;

public class IndexModel(AppDb db, LogosArea logos) : MsPage
{
    protected override string Modulo => "celulas"; // não é usado: a tela é de todos (OnPageHandlerExecuting abaixo)

    public record Card(int Id, string Nome, long? Logo);   // Logo = versão da imagem (null = área sem logo)
    public List<Card> Cards { get; set; } = new();
    public int Escondidas { get; set; }                      // áreas que a pessoa tirou da tela com o ×
    public int? Atual { get; set; }                          // área guardada neste navegador

    public override void OnPageHandlerExecuting(PageHandlerExecutingContext ctx) { } // todos os logados usam esta tela

    public async Task OnGetAsync()
    {
        var u = await db.Usuarios.FindAsync(UsuarioId);
        Atual = AreaEscolhida.Ler(HttpContext);

        var areas = await db.Celulas.OrderBy(c => c.Nome).ToListAsync();
        var escondidos = Escondidos(u);
        Cards = areas.Where(c => !escondidos.Contains(c.Id)).Select(c => new Card(c.Id, c.Nome, logos.Versao(c.Id))).ToList();
        Escondidas = areas.Count - Cards.Count;
    }

    // Logo da área. O endereço leva a versão, então pode ficar guardada no navegador.
    public IActionResult OnGetLogo(int id)
    {
        if (logos.Caminho(id) is not string c) return NotFound();
        Response.Headers.CacheControl = "private, max-age=31536000";
        return PhysicalFile(c, "image/png");
    }

    // Card clicado: guarda a área e abre a tela inicial do grupo.
    public async Task<IActionResult> OnGetEntrarAsync(int? id)
    {
        if (id != null && !await db.Celulas.AnyAsync(c => c.Id == id)) id = null;
        AreaEscolhida.Gravar(HttpContext, id);
        return RedirectToPage(Acesso.PaginaInicial(Grupo));
    }

    // Esconde o card só da tela desta pessoa; a área continua cadastrada (remover a área de vez é na tela Áreas).
    public async Task<IActionResult> OnPostRemoverAsync(int id)
    {
        var u = await db.Usuarios.FindAsync(UsuarioId);
        if (u == null || !await db.Celulas.AnyAsync(c => c.Id == id)) return RedirectToPage();
        var ids = Escondidos(u);
        ids.Add(id);
        u.CardsEscondidos = string.Join(",", ids);
        await db.SaveChangesAsync();
        return RedirectToPage();
    }

    // Volta ao padrão: todas as áreas como card.
    public async Task<IActionResult> OnPostTodasAsync()
    {
        if (await db.Usuarios.FindAsync(UsuarioId) is Usuario u) { u.CardsEscondidos = null; await db.SaveChangesAsync(); }
        return RedirectToPage();
    }

    // Ids das áreas que a pessoa escondeu com o ×.
    static HashSet<int> Escondidos(Usuario? u) =>
        (u?.CardsEscondidos ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => int.TryParse(x, out var n) ? n : 0).Where(n => n > 0).ToHashSet();
}
