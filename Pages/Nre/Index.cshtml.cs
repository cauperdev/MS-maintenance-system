// ============================================================================
// TELA: NRE — lista (lógica)  ·  endereço /Nre  ·  visual em Index.cshtml  ·  permissão "nre" (aprovar/remover: "nre_aprovacao")
// NRE = gasto não recorrente de uma máquina (peça, serviço, ferramental...) cobrado do cliente (a célula da máquina).
// Fluxo: Aguardando aprovação → Aprovada → Aguardando chegada → Cobrada do cliente → Concluída (ou Recusada).
// A NRE em si: Editar.cshtml. A lista não tem mais a aba "Resumo do cliente" (pedido do usuário, 2026-10-07):
// o resumo no formato do cliente (aba Summary) continua na planilha da Extração.
// ONDE MEXER:
//   - Filtros e ordem da lista ............ Filtrar (aguardando aprovação primeiro, depois as mais novas)
//   - Colunas da lista .................... Index.cshtml (Modelo · NRE · Título · NF · Invoice · Valor · DI; NF, invoice e
//                                           DI vêm do Detalhado; Valor = Final Amount)
//   - Planilha do botão Extração .......... OnGetExtracaoAsync (abas Summary, Detalhado e NRE — Servicos/ResumoNre.cs)
//   - Remover selecionados ................ OnPostRemoverAsync (só quem aprova NRE)
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Nre;

public class IndexModel(AppDb db, Auditoria auditoria, Anexos anexos) : MsPage, IFiltroArea
{
    protected override string Modulo => "nre";
    const int PorPagina = 50;

    [BindProperty(SupportsGet = true)] public StatusNre? Status { get; set; }
    [BindProperty(SupportsGet = true)] public TipoNre? Tipo { get; set; }
    [BindProperty(SupportsGet = true)] public int? CelulaId { get; set; }
    [BindProperty(SupportsGet = true)] public int? Ano { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;

    public List<Celula> Celulas { get; set; } = new();
    public List<int> Anos { get; set; } = new();
    public List<Modelos.Nre> Lista { get; set; } = new();
    public int Total { get; set; }
    public int Paginas => Math.Max(1, (int)Math.Ceiling(Total / (double)PorPagina));
    public bool Aprova => Pode("nre_aprovacao");

    IQueryable<Modelos.Nre> Filtrar()
    {
        var q = db.Nres.Include(n => n.Equipamento).Include(n => n.Celula)
            .Include(n => n.OrdemServico).Include(n => n.Solicitante).Include(n => n.AnalisadaPor).Include(n => n.Itens)
            .Include(n => n.Remessas).ThenInclude(r => r.Itens).Include(n => n.Anexos).AsSplitQuery().AsQueryable();
        if (Status != null) q = q.Where(n => n.Status == Status);
        if (Tipo != null) q = q.Where(n => n.Tipo == Tipo);
        if (CelulaId != null) q = q.Where(n => n.CelulaId == CelulaId);
        if (Ano != null) q = q.Where(n => n.CriadaEm.Year == Ano);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var t = Q.Trim().ToUpper();
            q = q.Where(n => n.Numero.Contains(t) || (n.Maquina != null && n.Maquina.Contains(t))
                || (n.Equipamento != null && n.Equipamento.Codigo.Contains(t))
                || n.Descricao.Contains(Q) || (n.OrdemServico != null && n.OrdemServico.Numero.Contains(t))
                || (n.Titulo != null && n.Titulo.ToUpper().Contains(t)) || (n.Modelo != null && n.Modelo.Contains(t))
                || (n.CodigoNre != null && n.CodigoNre.Contains(t)) || (n.PedidoCompra != null && n.PedidoCompra.Contains(t))
                || n.Remessas.Any(r => (r.NotaFiscal != null && r.NotaFiscal.Contains(t)) || (r.Invoice != null && r.Invoice.Contains(t))
                    || (r.Di != null && r.Di.Contains(t))));
        }
        return q.OrderBy(n => n.Status == StatusNre.Aberta ? 0 : 1).ThenByDescending(n => n.CriadaEm);
    }

    public async Task OnGetAsync()
    {
        Celulas = await db.Celulas.OrderBy(c => c.Nome).ToListAsync();
        Anos = await db.Nres.Select(n => n.CriadaEm.Year).Distinct().OrderByDescending(a => a).ToListAsync();
        var todas = await Filtrar().ToListAsync();
        Total = todas.Count;
        Pagina = Math.Clamp(Pagina, 1, Paginas);
        Lista = todas.Skip((Pagina - 1) * PorPagina).Take(PorPagina).ToList();
    }

    // Extração — Excel com as NRE filtradas: Summary (formato do cliente, com total), Detalhado (itens por DI/NF) e NRE.
    public async Task<IActionResult> OnGetExtracaoAsync()
    {
        var lista = await Filtrar().ToListAsync();
        return File(ResumoNre.Planilha(lista), Excel.Mime, $"nre-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    // Remover selecionados — só quem aprova NRE (padrão: Coordenador e Administrador).
    public async Task<IActionResult> OnPostRemoverAsync(int[] ids)
    {
        if (!Aprova) return RedirectToPage("/Conta/Negado");
        var lista = await db.Nres.Include(n => n.Itens).Include(n => n.Remessas).ThenInclude(r => r.Itens).Include(n => n.Anexos)
            .Where(n => ids.Contains(n.Id)).ToListAsync();
        foreach (var n in lista)
        {
            auditoria.Registrar("Nre", n.Id, "Removida", $"{n.Numero} · {n.Total().Reais()}");
            foreach (var a in n.Anexos) anexos.Apagar(a.Arquivo);   // os arquivos da NRE saem junto
        }
        db.Nres.RemoveRange(lista);
        await db.SaveChangesAsync();
        Ok($"{lista.Count} NRE removida(s).");
        return RedirectToPage(new { Status, Tipo, CelulaId, Ano, Q });
    }
}
