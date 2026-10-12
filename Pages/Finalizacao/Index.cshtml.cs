// ============================================================================
// TELA: FINALIZAÇÃO — lista de ciclos concluídos (lógica)  ·  endereço /Finalizacao  ·  permissão "finalizacao"
// Regra 05: o coordenador aprova ou retorna cada conclusão. Visitante e Auditor só veem as aprovadas.
// ONDE MEXER:
//   - Filtros e ordem da lista ............ Filtrar / OnGetAsync
//   - Botão Aprovado ...................... OnPostAprovarAsync
//   - Botão Retornar ...................... OnPostRetornarAsync (a OS volta para a data do ciclo)
//   - Planilha do botão Extração .......... OnGetExtracaoAsync (abas Finalização, Checklist e Imagens)
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Finalizacao;

public class IndexModel(AppDb db, Regras regras) : MsPage, IFiltroArea
{
    protected override string Modulo => "finalizacao";
    const int PorPagina = 50;

    [BindProperty(SupportsGet = true)] public int? Ano { get; set; }
    [BindProperty(SupportsGet = true)] public int? Mes { get; set; }
    [BindProperty(SupportsGet = true)] public StatusRevisao? Revisao { get; set; }
    [BindProperty(SupportsGet = true)] public string? Local { get; set; }
    [BindProperty(SupportsGet = true)] public int? CelulaId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;

    public List<string> Localizacoes { get; set; } = new();
    public List<Celula> Celulas { get; set; } = new();
    public List<int> Anos { get; set; } = new();
    public List<Modelos.Finalizacao> Lista { get; set; } = new();
    public int Total { get; set; }
    public int Paginas => Math.Max(1, (int)Math.Ceiling(Total / (double)PorPagina));

    // Mantém os filtros depois de aprovar/retornar.
    public Dictionary<string, string> Rota() => new Dictionary<string, string?>
    {
        ["Ano"] = Ano?.ToString(), ["Mes"] = Mes?.ToString(),
        ["Revisao"] = Revisao?.ToString(), ["Local"] = Local, ["CelulaId"] = CelulaId?.ToString(), ["Q"] = Q,
    }.Where(kv => !string.IsNullOrEmpty(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value!);

    IQueryable<Modelos.Finalizacao> Filtrar()
    {
        var q = db.Finalizacoes.Include(f => f.Equipamento).Include(f => f.Tecnico).Include(f => f.Coordenador).Include(f => f.AprovadaPor).AsQueryable();
        // Visitante e Auditor só veem as aprovadas.
        if (Acesso.SoAprovadas(Grupo)) q = q.Where(f => f.StatusRevisao == StatusRevisao.Aprovado);
        if (Ano != null) q = q.Where(f => f.ConcluidaEm.Year == Ano);
        if (Mes != null) q = q.Where(f => f.ConcluidaEm.Month == Mes);
        if (Revisao != null) q = q.Where(f => f.StatusRevisao == Revisao);
        if (!string.IsNullOrEmpty(Local)) q = q.Where(f => f.Equipamento!.Localizacao == Local);
        if (CelulaId != null) q = q.Where(f => f.Equipamento!.CelulaId == CelulaId);
        if (!string.IsNullOrWhiteSpace(Q))
            q = q.Where(f => f.NumeroOS.Contains(Q) || f.Equipamento!.Codigo.Contains(Q) || f.Equipamento.Nome.Contains(Q));
        return q.OrderBy(f => f.StatusRevisao).ThenByDescending(f => f.ConcluidaEm);
    }

    public async Task OnGetAsync()
    {
        Localizacoes = await db.Equipamentos.Where(e => CelulaId == null || e.CelulaId == CelulaId).Select(e => e.Localizacao).Distinct().OrderBy(x => x).ToListAsync();
        Celulas = await db.Celulas.OrderBy(c => c.Nome).ToListAsync();
        Anos = await db.Finalizacoes.Select(f => f.ConcluidaEm.Year).Distinct().OrderByDescending(a => a).ToListAsync();
        var q = Filtrar();
        Total = await q.CountAsync();
        Pagina = Math.Clamp(Pagina, 1, Paginas);
        Lista = await q.Skip((Pagina - 1) * PorPagina).Take(PorPagina).ToListAsync();
    }

    // Aprovado — aprova as marcadas.
    public async Task<IActionResult> OnPostAprovarAsync(int[] ids)
    {
        var lista = await db.Finalizacoes.Where(f => ids.Contains(f.Id) && f.StatusRevisao != StatusRevisao.Aprovado).ToListAsync();
        foreach (var f in lista) regras.Aprovar(f, UsuarioId);
        await db.SaveChangesAsync();
        Ok($"{lista.Count} conclusão(ões) aprovada(s).");
        return RedirectToPage(new { Ano, Mes, Revisao, Local, CelulaId, Q });
    }

    // Retornar — a conclusão sai da Finalização e a OS volta para Ordens de serviço com a data daquele ciclo.
    // O motivo é obrigatório (campo da janela de confirmação) e aparece na lista de Ordens de serviço, coluna Retorno.
    public async Task<IActionResult> OnPostRetornarAsync(int[] ids, string? motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo))
        {
            Erro("Para retornar, escreva o motivo do retorno.");
            return RedirectToPage(new { Ano, Mes, Revisao, Local, CelulaId, Q });
        }
        // Qualquer conclusão pode voltar, não só a mais recente (Regras.RetornarAsync).
        var lista = await db.Finalizacoes.Where(f => ids.Contains(f.Id)).OrderBy(f => f.ConcluidaEm).ToListAsync();
        foreach (var f in lista) await regras.RetornarAsync(f, motivo.Trim(), User.Identity?.Name ?? "");
        await db.SaveChangesAsync();
        Ok(lista.Count == 0 ? "Nenhuma conclusão foi retornada."
            : $"{lista.Count} conclusão(ões) retornada(s): as OS voltaram para Ordens de serviço com a data daquele ciclo.");
        return RedirectToPage(new { Ano, Mes, Revisao, Local, CelulaId, Q });
    }

    // Extração — Excel com abas Finalização, Checklist e Imagens.
    public async Task<IActionResult> OnGetExtracaoAsync()
    {
        var lista = await Filtrar().Include(f => f.Imagens).ToListAsync();
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var bytes = Excel.Planilha(
            ("Finalização",
                // Mesmas colunas e ordem da tela; depois as extras.
                ["Número da OS", "Prédio", "Código", "Equipamento", "Tipo", "Técnico responsável", "Coordenador responsável", "Concluída em",
                 "Status da revisão", "Aprovada em", "Aprovada por", "Localização", "Data da manutenção", "No prazo", "Peças"],
                lista.Select(f => new object?[]
                {
                    f.NumeroOS, f.Equipamento?.Predio, f.Equipamento?.Codigo, f.Equipamento?.Descricao, f.Tipo.Nome(), f.Tecnico?.NomeCompleto, f.Coordenador?.NomeCompleto,
                    f.ConcluidaEm, f.StatusRevisao.Nome(), f.AprovadaEm, f.AprovadaPor?.NomeCompleto,
                    f.Equipamento?.Localizacao, f.DataConclusao.Date, f.ConcluidaEm <= f.DataConclusao ? "Sim" : "Não", f.Pecas,
                })),
            ("Checklist",
                ["Número da OS", "Concluída em", "Tarefa", "Concluída"],
                lista.SelectMany(f => f.Checklist.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => new object?[]
                {
                    f.NumeroOS, f.ConcluidaEm, l.Length > 4 ? l[4..] : l, l.StartsWith("[x]") ? "Sim" : "Não",
                }))),
            ("Imagens",
                ["Número da OS", "Concluída em", "Tipo", "Enviada em", "Link"],
                lista.SelectMany(f => f.Imagens.Select(i => new object?[]
                {
                    f.NumeroOS, f.ConcluidaEm, i.Tipo.ToString(), i.CriadaEm, $"{baseUrl}/Foto/{i.Id}",
                }))));
        return File(bytes, Excel.Mime, $"finalizacao-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }
}
