// ============================================================================
// SINO DE LEMBRETES (topo da tela) — quais OS aparecem no sino.
// Uma OS entra no sino a partir de "data da manutenção − dias de lembrete" (campo do plano no Cronograma).
// ONDE MEXER:
//   - Quem vê o quê (técnico só as dele) ..... Pendentes
//   - Quantas aparecem na lista do sino (8) .. ListarAsync
// Desenho do sino: Pages/Shared/_Lembretes.cshtml.
// ============================================================================
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;

namespace MS.Servicos;

// Sino de lembretes: OS cujo "Lembrar a partir de" já chegou.
// O técnico vê as OS dele e as sem técnico; os outros grupos veem todas. Sempre só as da área escolhida nos cartões.
public static class Lembretes
{
    public static IQueryable<OrdemServico> Pendentes(AppDb db, Grupo grupo, int usuarioId, int? areaId)
    {
        var hoje = DateTime.Today.AddDays(1);
        var q = db.Ordens.Where(o => o.DataLembrete != null && o.DataLembrete < hoje);
        if (areaId != null) q = q.Where(o => o.Equipamento!.CelulaId == areaId);   // só as máquinas da área escolhida nos cartões
        if (grupo == Grupo.Tecnico) q = q.Where(o => o.TecnicoId == null || o.TecnicoId == usuarioId);
        return q;
    }

    public static async Task<(int Total, List<OrdemServico> Primeiros)> ListarAsync(AppDb db, Grupo grupo, int usuarioId, int? areaId)
    {
        var q = Pendentes(db, grupo, usuarioId, areaId);
        var total = await q.CountAsync();
        var primeiros = await q.Include(o => o.Equipamento).OrderBy(o => o.DataConclusao).Take(8).ToListAsync();
        return (total, primeiros);
    }
}
