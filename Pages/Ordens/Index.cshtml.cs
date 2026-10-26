// ============================================================================
// TELA: ORDENS DE SERVIÇO — lista (lógica)  ·  endereço /Ordens  ·  visual em Index.cshtml  ·  permissão "ordens"
// As OS nascem no Cronograma (Regra 01). O QR code das máquinas abre esta tela já pesquisando o código (/Ordens?Q=...).
// ONDE MEXER:
//   - Filtros, pesquisa e ordem ........... Filtrar
//   - Itens por página (50) ............... PorPagina
//   - Planilha do botão Extração .......... OnGetExtracaoAsync (permissão "ordens_gestao")
//   - Remover selecionados ................ OnPostRemoverAsync
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Ordens;

public class IndexModel(AppDb db, Regras regras) : MsPage, IFiltroArea
{
    protected override string Modulo => "ordens";
    const int PorPagina = 50;

    [BindProperty(SupportsGet = true)] public int? Ano { get; set; }
    [BindProperty(SupportsGet = true)] public int? Mes { get; set; }
    [BindProperty(SupportsGet = true)] public string? Situacao { get; set; }
    [BindProperty(SupportsGet = true)] public string? Local { get; set; }
    [BindProperty(SupportsGet = true)] public int? CelulaId { get; set; }
    [BindProperty(SupportsGet = true)] public Periodicidade? Periodicidade { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public bool Lembretes { get; set; } // "Ver todos" do sino

    public List<string> Localizacoes { get; set; } = new();
    public List<Celula> Celulas { get; set; } = new();
    public List<int> Anos { get; set; } = new();
    public List<OrdemServico> Lista { get; set; } = new();
    public int Total { get; set; }
    public int Paginas => Math.Max(1, (int)Math.Ceiling(Total / (double)PorPagina));
    public DateTime Agora { get; } = DateTime.Now;

    IQueryable<OrdemServico> Filtrar()
    {
        var q = db.Ordens.Include(o => o.Equipamento).Include(o => o.Tecnico).Include(o => o.Coordenador).AsQueryable();
        if (Ano != null) q = q.Where(o => o.DataAbertura.Year == Ano);
        if (Mes != null) q = q.Where(o => o.DataAbertura.Month == Mes);
        if (!string.IsNullOrEmpty(Local)) q = q.Where(o => o.Equipamento!.Localizacao == Local);
        if (CelulaId != null) q = q.Where(o => o.Equipamento!.CelulaId == CelulaId);
        if (Periodicidade != null) q = q.Where(o => o.Tipo == Periodicidade);
        q = Situacao switch
        {
            // Mesma regra de OrdemServico.Atrasada: atrasada só depois do dia da manutenção.
            "aberta" => q.Where(o => o.DataConclusao >= Agora.Date),
            "atrasada" => q.Where(o => o.DataConclusao < Agora.Date),
            _ => q,
        };
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var t = Q.Trim().ToUpperInvariant();   // texto gravado em maiúscula
            q = q.Where(o => o.Numero.Contains(t) || o.Equipamento!.Codigo.Contains(t) || o.Equipamento.Nome.Contains(t));
        }
        if (Lembretes)
        {
            var ids = Servicos.Lembretes.Pendentes(db, Grupo, UsuarioId, CelulaId).Select(o => o.Id);
            q = q.Where(o => ids.Contains(o.Id));
        }
        // Mesma ordem do Cronograma: por equipamento e, dentro dele, pela periodicidade (Semanal, Mensal, ...).
        return q.OrderBy(o => o.Equipamento!.Codigo).ThenBy(o => o.Tipo).ThenBy(o => o.DataConclusao);
    }

    public async Task OnGetAsync()
    {
        Localizacoes = await db.Equipamentos.Where(e => CelulaId == null || e.CelulaId == CelulaId)   // só as da área escolhida
            .Select(e => e.Localizacao).Distinct().OrderBy(x => x).ToListAsync();
        Celulas = await db.Celulas.OrderBy(c => c.Nome).ToListAsync();
        Anos = await db.Ordens.Select(o => o.DataAbertura.Year).Distinct().OrderByDescending(a => a).ToListAsync();
        var q = Filtrar();
        Total = await q.CountAsync();
        Pagina = Math.Clamp(Pagina, 1, Paginas);
        Lista = await q.Skip((Pagina - 1) * PorPagina).Take(PorPagina).ToListAsync();
    }

    // Extração — baixa em Excel as OS filtradas (as OS vêm do Cronograma; aqui é só consulta).
    public async Task<IActionResult> OnGetExtracaoAsync()
    {
        if (!Ve("ordens_gestao")) return RedirectToPage("/Conta/Negado");
        var lista = await Filtrar().ToListAsync();
        var bytes = Excel.Planilha(("Ordens de serviço",
            // Mesmas colunas e ordem da tela (Status = Aberta ou Atrasada, como na lista); a descrição vem no fim.
            ["Ordem de serviço", "Prédio", "Equipamento", "Localização", "Periodicidade", "Status",
             "Técnico responsável", "Coordenador responsável", "Data da manutenção", "Próxima manutenção", "Descrição"],
            lista.Select(o => new object?[]
            {
                o.Numero, o.Equipamento?.Predio, o.Equipamento?.Descricao, o.Equipamento?.Localizacao, o.Tipo.Nome(),
                o.Atrasada(Agora) ? "Atrasada" : o.Status.Nome(), o.Tecnico?.NomeCompleto, o.Coordenador?.NomeCompleto,
                o.DataConclusao.Date, Regras.ProximaManutencao(o).Date, o.Descricao,
            })));
        return File(bytes, Excel.Mime, $"ordens-de-servico-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    // Remover selecionados — apaga as OS marcadas; com "marcar todas" (todos), todas as OS da lista filtrada.
    public async Task<IActionResult> OnPostRemoverAsync(int[] ids, bool todos)
    {
        if (!Pode("ordens_gestao")) return RedirectToPage("/Conta/Negado");
        if (todos) ids = await Filtrar().Select(o => o.Id).ToArrayAsync();
        var removidas = await regras.RemoverOrdensAsync(db.Ordens.Where(o => ids.Contains(o.Id)));
        await db.SaveChangesAsync();
        Ok($"{removidas} ordem(ns) de serviço removida(s).");
        return RedirectToPage(new { Ano, Mes, Situacao, Local, CelulaId, Periodicidade, Q });
    }
}
