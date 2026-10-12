// ============================================================================
// TELA: USUÁRIOS — lista (lógica)  ·  endereço /Usuarios  ·  visual em Index.cshtml  ·  permissão "usuarios"
// ONDE MEXER:
//   - Filtros (grupo, célula, pesquisa) ... OnGetAsync
//   - Quem pode ser removido .............. PodeRemover (ninguém remove a si mesmo; só administrador remove administrador)
//   - Remover selecionados ................ OnPostRemoverAsync
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Usuarios;

public class IndexModel(AppDb db, Auditoria auditoria) : MsPage, IFiltroArea
{
    protected override string Modulo => "usuarios";

    [BindProperty(SupportsGet = true)] public Grupo? GrupoFiltro { get; set; }
    [BindProperty(SupportsGet = true)] public int? CelulaId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public List<Celula> Celulas { get; set; } = new();
    public List<Usuario> Lista { get; set; } = new();

    public async Task OnGetAsync()
    {
        Celulas = await db.Celulas.OrderBy(c => c.Nome).ToListAsync();
        var q = db.Usuarios.Include(u => u.Celula).AsQueryable();
        if (GrupoFiltro != null) q = q.Where(u => u.Grupo == GrupoFiltro);
        if (CelulaId != null) q = q.Where(u => u.CelulaId == CelulaId);
        if (!string.IsNullOrWhiteSpace(Q))
            q = q.Where(u => u.Login.Contains(Q) || u.PrimeiroNome.Contains(Q) || u.UltimoNome.Contains(Q) || u.Email.Contains(Q));
        Lista = await q.OrderBy(u => u.Login).ToListAsync();
    }

    // Ninguém remove o próprio usuário, e só administrador remove administrador.
    public bool PodeRemover(Usuario u) => u.Id != UsuarioId && (u.Grupo != Grupo.Administrador || Grupo == Grupo.Administrador);

    // Remove os usuários marcados. OS e finalizações deles continuam, só ficam sem técnico/coordenador.
    public async Task<IActionResult> OnPostRemoverAsync(int[] ids)
    {
        var lista = (await db.Usuarios.Where(u => ids.Contains(u.Id)).ToListAsync()).Where(PodeRemover).ToList();
        if (lista.Count > 0)
        {
            foreach (var u in lista) auditoria.Registrar("Usuario", u.Id, "Removido", u.Login);
            db.Usuarios.RemoveRange(lista);
            await db.SaveChangesAsync();
            Ok(lista.Count == 1 ? $"Usuário {lista[0].Login} removido." : $"{lista.Count} usuário(s) removido(s).");
        }
        return RedirectToPage(new { GrupoFiltro, CelulaId, Q });
    }
}
