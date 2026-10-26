// ============================================================================
// TELA: CALENDÁRIO (lógica)  ·  endereço /Calendario  ·  visual em Index.cshtml  ·  permissão "calendario"
// Cada OS aparece no dia da Data da manutenção. Clicar num dia lista as OS pendentes dele embaixo.
// ONDE MEXER:
//   - Mês mostrado, filtros ................. propriedades Mes / Local / CelulaId
//   - OS de cada dia e lista do dia clicado . OnGetAsync
//   - Cor da etiqueta (aberta / atrasada) ... Cor
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Calendario;

public class IndexModel(AppDb db) : MsPage, IFiltroArea
{
    protected override string Modulo => "calendario";

    [BindProperty(SupportsGet = true)] public string? Mes { get; set; }   // yyyy-MM
    [BindProperty(SupportsGet = true)] public string? Local { get; set; }
    [BindProperty(SupportsGet = true)] public int? CelulaId { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? Dia { get; set; }
    [BindProperty(SupportsGet = true)] public int? Os { get; set; }        // OS clicada: fica destacada na lista

    public DateTime Inicio { get; set; }
    public DateTime InicioGrade { get; set; }
    public DateTime Agora { get; } = DateTime.Now;
    public List<string> Localizacoes { get; set; } = new();
    public List<Celula> Celulas { get; set; } = new();
    public Dictionary<DateTime, List<OrdemServico>> PorDia { get; set; } = new();
    public int TotalMes { get; set; }
    public List<OrdemServico> DoDia { get; set; } = new();

    // Cor da etiqueta: azul aberta, vermelha atrasada.
    public string Cor(OrdemServico o) => o.Atrasada(Agora) ? "atrasada" : "aberta";

    public async Task OnGetAsync()
    {
        Inicio = DateTime.TryParseExact(Mes, "yyyy-MM", null, System.Globalization.DateTimeStyles.None, out var m)
            ? m : new DateTime(Agora.Year, Agora.Month, 1);
        if (Dia != null && Mes == null) Inicio = new DateTime(Dia.Value.Year, Dia.Value.Month, 1);
        InicioGrade = Inicio.AddDays(-(int)Inicio.DayOfWeek);
        var fimGrade = InicioGrade.AddDays(42);

        Localizacoes = await db.Equipamentos.Where(e => CelulaId == null || e.CelulaId == CelulaId)   // só as da área escolhida
            .Select(e => e.Localizacao).Distinct().OrderBy(x => x).ToListAsync();
        Celulas = await db.Celulas.OrderBy(c => c.Nome).ToListAsync();
        var q = db.Ordens.Include(o => o.Equipamento).Where(o => o.DataConclusao >= InicioGrade && o.DataConclusao < fimGrade);
        if (!string.IsNullOrEmpty(Local)) q = q.Where(o => o.Equipamento!.Localizacao == Local);
        if (CelulaId != null) q = q.Where(o => o.Equipamento!.CelulaId == CelulaId);
        var ordens = await q.OrderBy(o => o.DataConclusao).ToListAsync();

        // Cada OS aparece no dia da Data de conclusão (prazo do ciclo).
        PorDia = ordens.GroupBy(o => o.DataConclusao.Date).ToDictionary(g => g.Key, g => g.ToList());
        TotalMes = ordens.Count(o => o.DataConclusao.Year == Inicio.Year && o.DataConclusao.Month == Inicio.Month);

        if (Dia != null)
        {
            // OS pendentes do dia (abertas ou atrasadas), listadas abaixo do calendário.
            var dq = db.Ordens.Include(o => o.Equipamento).Include(o => o.Tecnico)
                .Where(o => o.Status != StatusOS.Concluida && o.DataConclusao >= Dia.Value.Date && o.DataConclusao < Dia.Value.Date.AddDays(1));
            if (!string.IsNullOrEmpty(Local)) dq = dq.Where(o => o.Equipamento!.Localizacao == Local);
            if (CelulaId != null) dq = dq.Where(o => o.Equipamento!.CelulaId == CelulaId);
            DoDia = await dq.OrderBy(o => o.Equipamento!.Codigo).ToListAsync();
        }
    }
}
