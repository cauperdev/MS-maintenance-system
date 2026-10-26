// ============================================================================
// TELA: DASHBOARD DE MANUTENÇÃO (lógica)  ·  endereço /  ·  visual em Pages/Index.cshtml  ·  permissão "dashboard"
// Calcula os números dos quadros e dos gráficos. Clicar numa fatia filtra os OUTROS gráficos.
// Barras: "OS por localização" filtra pela localização E mostra a lista das OS; aprovadas/atrasadas só mostram a lista (ListaOs).
// ONDE MEXER:
//   - Números dos 4 quadros do topo ...... OnGetAsync (Equipamentos, Abertas, PlanosAtrasados, Concluidas)
//   - Filtros de célula e localização .... CelulaId / Local (valem para quadros, gráficos, listas e Extração)
//   - Cores das fatias ................... PorStatusEquip / PorSituacao / PorRevisao (variáveis da paleta, estilos.css seção 1)
//   - Filtros por clique ................. funções Sit / Tip / EqSt / Rev dentro de OnGetAsync
//   - Aprovadas / atrasadas por local .... AprovadasPorLocal / AtrasadasPorLocal (planilha da Extração)
//   - Gráficos por localização na tela ... OsPorCelula / AprovadasPorCelula / AtrasadasPorCelula (um gráfico por célula;
//                                           a célula com mais localizações em cima — ordem em celulasLocais)
//   - Lista de OS ao clicar na barra ..... ListaOs (parâmetros Lista = "os", "aprovadas" ou "atrasadas" e ListaLocal)
//   - Planilha do botão Extração ......... OnGetExtracaoAsync
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages;

public class IndexModel(AppDb db, Fotos fotos) : MsPage, IFiltroArea
{
    protected override string Modulo => "dashboard";

    [BindProperty(SupportsGet = true)] public int? CelulaId { get; set; }   // célula: só as máquinas (e localizações) dela
    [BindProperty(SupportsGet = true)] public string? Local { get; set; }
    [BindProperty(SupportsGet = true)] public string? Situacao { get; set; }
    [BindProperty(SupportsGet = true)] public Periodicidade? Tipo { get; set; }
    [BindProperty(SupportsGet = true)] public StatusEquipamento? StatusEquip { get; set; }
    [BindProperty(SupportsGet = true)] public StatusRevisao? Revisao { get; set; }
    [BindProperty(SupportsGet = true)] public string? Lista { get; set; }      // "os", "aprovadas" ou "atrasadas": gráfico da barra clicada
    [BindProperty(SupportsGet = true)] public string? ListaLocal { get; set; } // localização da barra clicada

    public List<Celula> Celulas { get; set; } = new();
    public List<string> Localizacoes { get; set; } = new();
    public int Equipamentos, Abertas, PlanosAtrasados, Concluidas;
    public List<(string Rotulo, int Qtd, string Cor, string? Valor)> PorStatusEquip = new(), PorSituacao = new(), PorLocal = new(), PorRevisao = new();
    public List<(string Local, int Qtd)> AprovadasPorLocal = new(), AtrasadasPorLocal = new();
    // Os 3 gráficos por localização separados por célula: um gráfico por célula, só com as localizações dela.
    public List<(string Celula, List<(string Local, int Qtd)> Itens)> OsPorCelula = new(), AprovadasPorCelula = new(), AtrasadasPorCelula = new();

    // Uma linha da lista que abre ao clicar na barra (Id = OS ou finalização, conforme a lista).
    public record LinhaOs(int Id, string Numero, string? Equipamento, DateTime Data, string? Tecnico, string? Coordenador, bool Atrasada = false);
    public List<LinhaOs> ListaOs = new();

    public override void OnPageHandlerExecuting(PageHandlerExecutingContext ctx)
    {
        // Quem não vê o dashboard cai direto na sua tela inicial (o logo da área abre para quem está logado: tela Áreas).
        if (ctx.HandlerMethod?.Name == "Logo") return;
        if (!Acesso.PodeVer(Grupo, Modulo)) ctx.Result = RedirectToPage(Acesso.PaginaInicial(Grupo));
        AreaEscolhida.Aplicar(this, Request, Response); // abre filtrado pelo cliente escolhido depois do login
    }

    // Logo do cliente da área (cartões do alto do Dashboard e tela Áreas). Trocar o logo muda o endereço (&v=arquivo).
    public async Task<IActionResult> OnGetLogoAsync(int id)
    {
        var arquivo = await db.Celulas.Where(c => c.Id == id).Select(c => c.Logo).FirstOrDefaultAsync();
        if (arquivo == null || !System.IO.File.Exists(fotos.Caminho(arquivo))) return NotFound();
        Response.Headers.CacheControl = "private, max-age=86400";
        return PhysicalFile(fotos.Caminho(arquivo), "image/png");
    }

    public static string SituacaoDe(OrdemServico o, DateTime agora) =>
        o.Atrasada(agora) ? "atrasada" : "aberta";

    public bool TemFiltroClique => Situacao != null || Tipo != null || StatusEquip != null || Revisao != null;

    // Cada gráfico é filtrado pelos cliques nos OUTROS gráficos (o próprio filtro só destaca a fatia).
    public async Task OnGetAsync()
    {
        var agora = DateTime.Now;
        Celulas = await db.Celulas.OrderBy(c => c.Nome).ToListAsync();
        // Com célula escolhida, a lista (e os gráficos por localização) só tem as localizações das máquinas dela.
        Localizacoes = await db.Equipamentos.Where(e => CelulaId == null || e.CelulaId == CelulaId)
            .Select(e => e.Localizacao).Distinct().OrderBy(x => x).ToListAsync();
        if (!string.IsNullOrEmpty(Local) && !Localizacoes.Contains(Local)) Local = null; // trocou para uma célula sem essa localização

        var equipQ = db.Equipamentos.AsQueryable();
        var osQ = db.Ordens.Include(o => o.Equipamento).AsQueryable();
        var finQ = db.Finalizacoes.Include(f => f.Equipamento).AsQueryable();
        if (CelulaId != null)
        {
            equipQ = equipQ.Where(e => e.CelulaId == CelulaId);
            osQ = osQ.Where(o => o.Equipamento!.CelulaId == CelulaId);
            finQ = finQ.Where(f => f.Equipamento!.CelulaId == CelulaId);
        }
        if (!string.IsNullOrEmpty(Local))
        {
            equipQ = equipQ.Where(e => e.Localizacao == Local);
            osQ = osQ.Where(o => o.Equipamento!.Localizacao == Local);
            finQ = finQ.Where(f => f.Equipamento!.Localizacao == Local);
        }
        var equips = await equipQ.ToListAsync();
        var todas = await osQ.ToListAsync();
        var fins = await finQ.ToListAsync();
        var osPorId = todas.ToDictionary(o => o.Id);

        bool Sit(OrdemServico o) => Situacao == null || SituacaoDe(o, agora) == Situacao;
        bool Tip(OrdemServico o) => Tipo == null || o.Tipo == Tipo;
        bool EqSt(Equipamento? e) => StatusEquip == null || e?.Status == StatusEquip;
        var osComRevisao = Revisao == null ? null : fins.Where(f => f.StatusRevisao == Revisao).Select(f => f.OrdemServicoId).ToHashSet();
        bool Rev(OrdemServico o) => osComRevisao == null || osComRevisao.Contains(o.Id);

        var ordens = todas.Where(o => Sit(o) && Tip(o) && EqSt(o.Equipamento) && Rev(o)).ToList();

        // Equipamentos: vale o filtro de status do equipamento e ter OS que passe nos outros filtros.
        bool EquipPassaOs(Equipamento e) =>
            (Situacao == null && Tipo == null && Revisao == null) ||
            todas.Any(o => o.EquipamentoId == e.Id && Sit(o) && Tip(o) && Rev(o));
        var equipsBase = equips.Where(EquipPassaOs).ToList();

        Equipamentos = equipsBase.Count(e => EqSt(e));
        Abertas = ordens.Count(o => o.Status != StatusOS.Concluida);
        PlanosAtrasados = ordens.Count(o => o.PlanoId != null && o.Atrasada(agora));

        PorStatusEquip = Textos.StatusEquipamentoOpcoes
            .Select(s => (s.Nome(), equipsBase.Count(e => e.Status == s), s switch
            {
                StatusEquipamento.Operando => "var(--ok)", StatusEquipamento.Parado => "var(--atrasada)",
                StatusEquipamento.EmManutencao => "var(--amarelo)", _ => "var(--cinza)"
            }, (string?)s.ToString())).ToList();

        // Concluídas = ciclos que viraram finalização (Regra 04).
        Concluidas = fins.Count(f => EqSt(f.Equipamento) && (Revisao == null || f.StatusRevisao == Revisao) && (Tipo == null || f.Tipo == Tipo));

        var porSitBase = todas.Where(o => Tip(o) && EqSt(o.Equipamento) && Rev(o)).ToList();
        PorSituacao = new List<(string, int, string, string?)>
        {
            ("Aberta", porSitBase.Count(o => SituacaoDe(o, agora) == "aberta"), "var(--aberta)", "aberta"),
            ("Atrasada", porSitBase.Count(o => SituacaoDe(o, agora) == "atrasada"), "var(--atrasada)", "atrasada"),
        };

        PorLocal = ordens.GroupBy(o => o.Equipamento!.Localizacao).OrderByDescending(g => g.Count())
            .Select(g => (g.Key, g.Count(), "var(--lateral)", (string?)null)).ToList();

        // Finalizações: filtradas pelo status do equipamento e pela situação/tipo da OS de origem.
        var finsBase = fins.Where(f => EqSt(f.Equipamento) &&
            (Situacao == null && Tipo == null || osPorId.TryGetValue(f.OrdemServicoId, out var o) && Sit(o) && Tip(o))).ToList();
        PorRevisao = Enum.GetValues<StatusRevisao>().Where(s => s != StatusRevisao.Retornado) // retornada sai da Finalização
            .Select(s => (s.Nome(), finsBase.Count(f => f.StatusRevisao == s), s switch
            {
                StatusRevisao.Aguardando => "var(--amarelo)", StatusRevisao.Aprovado => "var(--ok)", _ => "var(--atrasada)"
            }, (string?)s.ToString())).ToList();

        // Aprovadas e atrasadas por localização: todas as localizações aparecem (inclusive com 0) para comparar.
        var aprovadas = finsBase.Where(f => f.StatusRevisao == StatusRevisao.Aprovado).ToList();
        var atrasadas = todas.Where(o => o.Atrasada(agora) && Tip(o) && EqSt(o.Equipamento) && Rev(o)).ToList();
        var locais = string.IsNullOrEmpty(Local) ? Localizacoes : [Local];
        AprovadasPorLocal = locais.Select(l => (l, aprovadas.Count(f => f.Equipamento?.Localizacao == l))).ToList();
        AtrasadasPorLocal = locais.Select(l => (l, atrasadas.Count(o => o.Equipamento?.Localizacao == l))).ToList();

        // Mesmos gráficos separados por célula (menos barras em cada um). A localização vai para a célula das máquinas dela;
        // máquina sem célula entra em "Sem célula".
        var nomes = Celulas.ToDictionary(c => c.Id, c => c.Nome);
        string NomeCelula(int? id) => id is int i && nomes.TryGetValue(i, out var n) ? n : "Sem área";
        var celulasLocais = (await db.Equipamentos.Where(e => CelulaId == null || e.CelulaId == CelulaId)
                .Select(e => new { e.CelulaId, e.Localizacao }).Distinct().ToListAsync())
            .Where(x => string.IsNullOrEmpty(Local) || x.Localizacao == Local)
            .GroupBy(x => NomeCelula(x.CelulaId))
            // Ordem dos cartões: a célula com mais localizações primeiro (empate: nome); "Sem célula" sempre no fim.
            .OrderBy(g => g.Key == "Sem área").ThenByDescending(g => g.Select(x => x.Localizacao).Distinct().Count()).ThenBy(g => g.Key)
            .Select(g => (Celula: g.Key, Locais: g.Select(x => x.Localizacao).Distinct().OrderBy(l => l).ToList()))
            .ToList();
        OsPorCelula = celulasLocais.Select(c => (c.Celula, ordens.Where(o => NomeCelula(o.Equipamento?.CelulaId) == c.Celula)
            .GroupBy(o => o.Equipamento!.Localizacao).Select(g => (g.Key, g.Count())).ToList())).ToList();
        AprovadasPorCelula = celulasLocais.Select(c => (c.Celula, c.Locais.Select(l => (l, aprovadas.Count(f =>
            NomeCelula(f.Equipamento?.CelulaId) == c.Celula && f.Equipamento?.Localizacao == l))).ToList())).ToList();
        AtrasadasPorCelula = celulasLocais.Select(c => (c.Celula, c.Locais.Select(l => (l, atrasadas.Count(o =>
            NomeCelula(o.Equipamento?.CelulaId) == c.Celula && o.Equipamento?.Localizacao == l))).ToList())).ToList();

        // Lista da barra clicada (com técnico e coordenador, que os gráficos não precisam carregar).
        if (Lista == "aprovadas")
        {
            var ids = aprovadas.Where(f => f.Equipamento?.Localizacao == ListaLocal).Select(f => f.Id).ToList();
            ListaOs = await db.Finalizacoes.Where(f => ids.Contains(f.Id))
                .OrderByDescending(f => f.ConcluidaEm)
                .Select(f => new LinhaOs(f.Id, f.NumeroOS, f.Equipamento!.Descricao, f.ConcluidaEm, f.Tecnico!.NomeCompleto, f.Coordenador!.NomeCompleto))
                .ToListAsync();
        }
        else if (Lista is "os" or "atrasadas")
        {
            var ids = (Lista == "os" ? ordens : atrasadas).Where(o => o.Equipamento?.Localizacao == ListaLocal).Select(o => o.Id).ToList();
            ListaOs = await db.Ordens.Where(o => ids.Contains(o.Id))
                .OrderBy(o => o.DataConclusao)
                .Select(o => new LinhaOs(o.Id, o.Numero, o.Equipamento!.Descricao, o.DataConclusao, o.Tecnico!.NomeCompleto, o.Coordenador!.NomeCompleto,
                    o.Status != StatusOS.Concluida && o.DataConclusao < agora.Date)) // atrasada só depois do dia da manutenção
                .ToListAsync();
        }
    }

    // Extração (Editar do Dashboard): os indicadores do filtro atual em Excel, uma aba por gráfico.
    public async Task<IActionResult> OnGetExtracaoAsync()
    {
        if (!PodeEditar) return RedirectToPage("/Conta/Negado");
        await OnGetAsync();
        string[] cab = ["Item", "Quantidade", "%"];
        IEnumerable<object?[]> Linhas(IEnumerable<(string Rotulo, int Qtd)> itens)
        {
            var lista = itens.ToList();
            var total = lista.Sum(i => i.Qtd);
            return lista.OrderByDescending(i => i.Qtd)
                .Select(i => new object?[] { i.Rotulo, i.Qtd, total == 0 ? "0%" : $"{i.Qtd * 100.0 / total:0.#}%" });
        }
        var filtros = string.Join(" · ", new[]
        {
            CelulaId != null ? $"Área: {Celulas.FirstOrDefault(c => c.Id == CelulaId)?.Nome}" : null,
            Local != null ? $"Localização: {Local}" : null, Situacao != null ? $"Situação: {Situacao}" : null,
            Tipo != null ? $"Tipo: {Tipo.Value.Nome()}" : null, StatusEquip != null ? $"Equipamento: {StatusEquip.Value.Nome()}" : null,
            Revisao != null ? $"Revisão: {Revisao.Value.Nome()}" : null,
        }.Where(f => f != null));

        var bytes = Excel.Planilha(
            ("Indicadores", ["Indicador", "Valor"], new[]
            {
                new object?[] { "Equipamentos cadastrados", Equipamentos },
                new object?[] { "Ordens de serviço abertas", Abertas },
                new object?[] { "Planos preventivos atrasados", PlanosAtrasados },
                new object?[] { "Ordens de serviço concluídas", Concluidas },
                new object?[] { "Filtros", filtros == "" ? "Nenhum" : filtros },
                new object?[] { "Gerado em", DateTime.Now },
            }),
            ("Equipamentos por status", cab, Linhas(PorStatusEquip.Select(b => (b.Rotulo, b.Qtd)))),
            ("OS por status", cab, Linhas(PorSituacao.Select(b => (b.Rotulo, b.Qtd)))),
            ("Finalizações por revisão", cab, Linhas(PorRevisao.Select(b => (b.Rotulo, b.Qtd)))),
            ("OS por localização", cab, Linhas(PorLocal.Select(b => (b.Rotulo, b.Qtd)))),
            ("Aprovadas por localização", cab, Linhas(AprovadasPorLocal)),
            ("Atrasadas por localização", cab, Linhas(AtrasadasPorLocal)));
        return File(bytes, Excel.Mime, $"dashboard-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }
}
