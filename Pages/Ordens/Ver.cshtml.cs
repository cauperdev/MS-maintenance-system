// ============================================================================
// TELAS DE UMA OS (lógica) — duas telas usam este arquivo:
//   - VerModel ....... Visualização (só leitura) ... /Ordens/Ver/{id}      · visual em Ver.cshtml
//   - ImprimirModel .. Folha para imprimir ......... /Ordens/Imprimir/{id} · visual em Imprimir.cshtml
// O conteúdo da ficha está em _OsDetalhe.cshtml.
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Ordens;

// Visualização (só para ler) e Imprimir usam o mesmo carregamento.
public class VerModel(AppDb db) : MsPage
{
    protected override string Modulo => "ordens";
    public OrdemServico Os { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var os = await db.Ordens
            .Include(o => o.Equipamento).Include(o => o.Plano).ThenInclude(p => p!.Tarefas).Include(o => o.Tecnico).Include(o => o.Coordenador)
            .Include(o => o.Tarefas).Include(o => o.Pecas).Include(o => o.Imagens)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (os == null) return NotFound();
        Os = os;
        return Page();
    }
}

public class ImprimirModel(AppDb db) : VerModel(db);
