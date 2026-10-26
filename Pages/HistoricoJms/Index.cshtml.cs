// ============================================================================
// TELA: HISTÓRICO JMS — Work Orders do JMS antigo (lógica)  ·  endereço /HistoricoJms  ·  visual em Index.cshtml  ·  permissão "jms"
// Só consulta: as Work Orders finalizadas no sistema anterior entram pelo Importar Excel — a aba JMS da planilha
// MANUTENÇÃO PREVENTIVA (preenchida pelo ExtrairWorkOrdersFinalizadas.py) ou a planilha da Extração desta tela.
// ONDE MEXER:
//   - Filtros, pesquisa e ordem ........... Filtrados / Filtrar
//   - Itens por página (50) ............... PorPagina
//   - Colunas lidas na importação ......... OnPostImportarAsync (nome do JMS em inglês ou o da tela, em português)
//   - Tradução de periodicidade e tipo .... Periodicidades / Tipos (Weekly → Semanal, Preventive → Preventiva)
//   - Datas do JMS (mês/dia/ano) .......... LerData
//   - Planilha do Baixar modelo ........... OnGetModelo
//   - Planilha do botão Extração .......... OnGetExtracaoAsync (mesmas colunas da tela: dá para importar de volta)
//   - Remover selecionados ................ OnPostRemoverAsync
// ============================================================================
using System.Globalization;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.HistoricoJms;

public class IndexModel(AppDb db) : MsPage
{
    protected override string Modulo => "jms";
    const int PorPagina = 50;

    // Colunas da tela, da Extração e do Baixar modelo (em português); a importação aceita também os nomes do JMS.
    static readonly string[] Colunas = ["Work Order", "Código", "Equipamento", "Periodicidade", "Tipo", "Localização", "Área",
        "Técnico responsável", "Coordenador responsável", "Criada em", "Data da manutenção", "Tempo sugerido (h)"];

    [BindProperty(SupportsGet = true)] public int? Ano { get; set; }
    [BindProperty(SupportsGet = true)] public int? Mes { get; set; }
    [BindProperty(SupportsGet = true)] public string? Local { get; set; }
    [BindProperty(SupportsGet = true)] public string? Celula { get; set; }
    [BindProperty(SupportsGet = true)] public string? Periodicidade { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;

    public List<int> Anos { get; set; } = new();
    public List<string> Localizacoes { get; set; } = new();
    public List<string> Celulas { get; set; } = new();
    public List<string> Periodicidades { get; set; } = new();
    public List<WorkOrderJms> Lista { get; set; } = new();
    public int Total { get; set; }
    public int Paginas => Math.Max(1, (int)Math.Ceiling(Total / (double)PorPagina));

    IQueryable<WorkOrderJms> Filtrados()
    {
        var q = db.WorkOrdersJms.AsQueryable();
        if (Ano != null) q = q.Where(w => w.DataManutencao != null && w.DataManutencao.Value.Year == Ano);
        if (Mes != null) q = q.Where(w => w.DataManutencao != null && w.DataManutencao.Value.Month == Mes);
        if (!string.IsNullOrEmpty(Local)) q = q.Where(w => w.Localizacao == Local);
        if (!string.IsNullOrEmpty(Celula)) q = q.Where(w => w.Celula == Celula);
        if (!string.IsNullOrEmpty(Periodicidade)) q = q.Where(w => w.Periodicidade == Periodicidade);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var t = Q.Trim().ToUpper();
            q = q.Where(w => w.WorkOrder.Contains(t) || w.Codigo.Contains(t)
                || (w.Equipamento != null && w.Equipamento.Contains(t)) || (w.Tecnico != null && w.Tecnico.Contains(t)));
        }
        return q;
    }

    // Mais recente primeiro (pela data da manutenção).
    IQueryable<WorkOrderJms> Filtrar() =>
        Filtrados().OrderByDescending(w => w.DataManutencao).ThenByDescending(w => w.CriadaEm).ThenByDescending(w => w.Id);

    Task<List<string>> Distintos(Expression<Func<WorkOrderJms, string?>> campo) =>
        db.WorkOrdersJms.Select(campo).Where(x => x != null && x != "").Distinct().OrderBy(x => x).Select(x => x!).ToListAsync();

    public async Task OnGetAsync()
    {
        Anos = await db.WorkOrdersJms.Where(w => w.DataManutencao != null).Select(w => w.DataManutencao!.Value.Year)
            .Distinct().OrderByDescending(a => a).ToListAsync();
        Localizacoes = await Distintos(w => w.Localizacao);
        Celulas = await Distintos(w => w.Celula);
        Periodicidades = await Distintos(w => w.Periodicidade);
        var q = Filtrar();
        Total = await q.CountAsync();
        Pagina = Math.Clamp(Pagina, 1, Paginas);
        Lista = await q.Skip((Pagina - 1) * PorPagina).Take(PorPagina).ToListAsync();
    }

    public IActionResult OnGetModelo() => File(Excel.Planilha(("JMS", Colunas,
        [["WO-0001", "PRDNZS00120", "NOZZLE STATION NA74A", "Semanal", "Preventiva", "1 - LINHA 04", "SAMSUNG",
          "TÉCNICO", "COORDENADOR", new DateTime(2026, 2, 20, 6, 0, 0), new DateTime(2026, 2, 23), 1.5m]])), Excel.Mime, "modelo-historico-jms.xlsx");

    // Extração — as Work Orders filtradas, com as colunas da tela (dá para editar e importar de volta).
    public async Task<IActionResult> OnGetExtracaoAsync()
    {
        var lista = await Filtrar().ToListAsync();
        var bytes = Excel.Planilha(("JMS", Colunas, lista.Select(w => new object?[]
        {
            w.WorkOrder, w.Codigo, w.Equipamento, w.Periodicidade, w.Tipo, w.Localizacao, w.Celula,
            w.Tecnico, w.Coordenador, w.CriadaEm, w.DataManutencao, w.TempoSugerido,
        })));
        return File(bytes, Excel.Mime, $"historico-jms-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    // Maintenance e Type do JMS em português (o que não está aqui fica como veio; "None" fica em branco).
    static readonly Dictionary<string, string> PeriodicidadesJms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Daily"] = "Diária", ["Weekly"] = "Semanal", ["Biweekly"] = "Quinzenal", ["Monthly"] = "Mensal",
        ["Bimonthly"] = "Bimestral", ["Trimonthly"] = "Trimestral", ["Four-month"] = "Quadrimestral",
        ["Half-yearly"] = "Semestral", ["Yearly"] = "Anual",
    };
    static readonly Dictionary<string, string> TiposJms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Preventive"] = "Preventiva", ["Corrective"] = "Corretiva", ["Inspection"] = "Inspeção",
    };

    static string? Traduzir(string s, Dictionary<string, string> mapa) =>
        s == "" || s.Equals("None", StringComparison.OrdinalIgnoreCase) ? null : mapa.GetValueOrDefault(s, s);

    static string? Texto(string s) => s == "" ? null : s.ToUpper();

    // Data do JMS em texto americano (mês/dia/ano, com ou sem hora: "2/20/2026 6:00:01 AM", "02/23/2026") ou célula de
    // data do Excel, que a leitura entrega como "dd/MM/yyyy HH:mm[:ss]" (ex.: planilha da Extração desta tela).
    static DateTime? LerData(string s)
    {
        if (s == "") return null;
        if (DateTime.TryParseExact(s, ["dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm"], CultureInfo.GetCultureInfo("pt-BR"), DateTimeStyles.None, out var d)) return d;
        return DateTime.TryParse(s, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out d) ? d : null;
    }

    static decimal? LerHoras(string s) =>
        decimal.TryParse(s.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var h) ? h : null;

    // Importar Excel — lê a aba "JMS" (ou a primeira). Work Order que já existe é atualizada; nova é criada (não duplica).
    public async Task<IActionResult> OnPostImportarAsync(IFormFile? arquivo)
    {
        var linhas = LerArquivo(arquivo, s => Excel.LerTabela(s, "JMS"));
        if (linhas == null) return RedirectToPage();
        if (linhas.Count == 0) { Erro("A planilha está vazia: preencha as linhas abaixo do cabeçalho."); return RedirectToPage(); }
        if (!linhas[0].Tem("Work Order"))
        {
            Erro("A planilha não tem a coluna Work Order. Use a aba JMS da planilha MANUTENÇÃO PREVENTIVA (ou a planilha da Extração desta tela).");
            return RedirectToPage();
        }

        // Cada campo: nome da coluna no JMS (inglês) ou na tela (português).
        static string Valor(Excel.LinhaPlanilha l, string jms, string tela) => l.Tem(jms) ? l[jms] : l[tela];

        var existentes = await db.WorkOrdersJms.ToDictionaryAsync(w => w.WorkOrder);
        var erros = new List<string>();
        int criadas = 0, atualizadas = 0;
        foreach (var l in linhas)
        {
            var wo = l["Work Order"].ToUpper();
            if (wo == "") { erros.Add($"Linha {l.Numero}: sem Work Order"); continue; }
            if (!existentes.TryGetValue(wo, out var w))
            {
                w = new WorkOrderJms { WorkOrder = wo };
                db.WorkOrdersJms.Add(w);
                existentes[wo] = w;
                criadas++;
            }
            else atualizadas++;
            w.Codigo = Valor(l, "Asset", "Código").ToUpper();
            w.Equipamento = Texto(Valor(l, "NickName", "Equipamento"));
            w.Periodicidade = Traduzir(Valor(l, "Maintenance", "Periodicidade"), PeriodicidadesJms);
            w.Tipo = Traduzir(Valor(l, "Type", "Tipo"), TiposJms);
            w.Localizacao = Texto(Valor(l, "Location", "Localização"));
            w.Celula = Texto(Valor(l, "Customer", l.Tem("Área") ? "Área" : "Célula")); // planilhas antigas: "Célula"
            w.Tecnico = Texto(Valor(l, "Technician", "Técnico responsável"));
            w.Coordenador = Texto(Valor(l, "Coordinator", "Coordenador responsável"));
            w.TempoSugerido = LerHoras(Valor(l, "Suggested Time", "Tempo sugerido"));
            var criada = Valor(l, "Created Date", "Criada em");
            var data = Valor(l, "Required Date", "Data da manutenção");
            w.CriadaEm = LerData(criada);
            w.DataManutencao = LerData(data);
            if (criada != "" && w.CriadaEm == null) erros.Add($"Linha {l.Numero} ({wo}): data de criação \"{criada}\" não reconhecida");
            if (data != "" && w.DataManutencao == null) erros.Add($"Linha {l.Numero} ({wo}): data da manutenção \"{data}\" não reconhecida");
        }
        await db.SaveChangesAsync();
        Resumo($"{criadas} work order(s) do JMS criada(s) e {atualizadas} atualizada(s) pelo Excel.", erros);
        return RedirectToPage();
    }

    // Remover selecionados — as marcadas; com "marcar todas" (todos), todas as da lista filtrada (todas as páginas).
    public async Task<IActionResult> OnPostRemoverAsync(int[] ids, bool todos)
    {
        var removidas = await (todos ? Filtrados() : db.WorkOrdersJms.Where(w => ids.Contains(w.Id))).ExecuteDeleteAsync();
        Ok($"{removidas} work order(s) do JMS removida(s).");
        return RedirectToPage(new { Ano, Mes, Local, Celula, Periodicidade, Q });
    }
}
