// ============================================================================
// TELA: EQUIPAMENTOS — lista (lógica)  ·  endereço /Equipamentos  ·  visual em Index.cshtml  ·  permissão "equipamentos"
// ONDE MEXER:
//   - Filtros e ordem da lista ............ Filtrar
//   - Coluna Preditiva .................... OnGetAsync (Preditiva: a pior situação das peças de cada máquina — aba
//                                           Manutenção preditiva do cadastro, Modelos/Entidades.cs → PecaPreditiva)
//   - Planilha do botão Extração .......... OnGetExtracaoAsync
//   - Planilha de QR codes ................ OnGetQrCodeAsync (endereço do QR: "EnderecoSistema" no appsettings.json;
//                                           desenho das etiquetas: Servicos/Excel.cs → QrCodes)
//   - Planilha do Baixar modelo ........... OnGetModelo
//   - Importar Excel ...................... OnPostImportarAsync
//   - Remover selecionados ................ OnPostRemoverAsync (quem tem plano, OS ou NRE não é removido)
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Equipamentos;

public class IndexModel(AppDb db, Auditoria auditoria, Regras regras) : MsPage, IFiltroArea
{
    protected override string Modulo => "equipamentos";

    [BindProperty(SupportsGet = true)] public string? Predio { get; set; }
    [BindProperty(SupportsGet = true)] public string? Local { get; set; }
    [BindProperty(SupportsGet = true)] public int? CelulaId { get; set; }
    [BindProperty(SupportsGet = true)] public StatusEquipamento? Status { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public List<string> Predios { get; set; } = new();
    public List<string> Localizacoes { get; set; } = new();
    public List<Celula> Celulas { get; set; } = new();
    public List<Equipamento> Lista { get; set; } = new();
    // Coluna Preditiva: por máquina, a pior situação das peças e quais peças estão nela (ex.: Trocar · BELT).
    public Dictionary<int, (SituacaoPrazo Situacao, List<string> Pecas)> Preditiva { get; set; } = new();

    IQueryable<Equipamento> Filtrar()
    {
        var q = db.Equipamentos.Include(e => e.Celula).AsQueryable();
        if (!string.IsNullOrEmpty(Predio)) q = q.Where(e => e.Predio == Predio);
        if (!string.IsNullOrEmpty(Local)) q = q.Where(e => e.Localizacao == Local);
        if (CelulaId != null) q = q.Where(e => e.CelulaId == CelulaId);
        if (Status != null) q = q.Where(e => e.Status == Status);
        if (!string.IsNullOrWhiteSpace(Q)) q = q.Where(e => e.Codigo.Contains(Q) || e.Nome.Contains(Q) || (e.Modelo != null && e.Modelo.Contains(Q))
            || (e.Irf != null && e.Irf.Contains(Q.Trim().ToUpper())));
        return q.OrderBy(e => e.Codigo);
    }

    public async Task OnGetAsync()
    {
        Predios = await db.Equipamentos.Where(e => e.Predio != "" && (CelulaId == null || e.CelulaId == CelulaId)).Select(e => e.Predio).Distinct().OrderBy(x => x).ToListAsync();
        Localizacoes = await db.Equipamentos.Where(e => CelulaId == null || e.CelulaId == CelulaId).Select(e => e.Localizacao).Distinct().OrderBy(x => x).ToListAsync();
        Celulas = await db.Celulas.OrderBy(c => c.Nome).ToListAsync();
        Lista = await Filtrar().ToListAsync();
        var hoje = DateTime.Today;
        Preditiva = (await db.PecasPreditivas.ToListAsync()).GroupBy(p => p.EquipamentoId).ToDictionary(g => g.Key, g =>
        {
            var pior = g.Max(p => p.Situacao(hoje));
            return (pior, g.Where(p => p.Situacao(hoje) == pior).Select(p => p.Nome).OrderBy(n => n).ToList());
        });
    }

    // Extração — mesmas colunas do modelo, então pode ser editada e importada de volta.
    public async Task<IActionResult> OnGetExtracaoAsync()
    {
        var lista = await Filtrar().ToListAsync();
        // Mesmas colunas e ordem da tela (a importação acha as colunas pelo nome, então a planilha volta sem ajuste).
        var bytes = Excel.Planilha(("Equipamentos", ["Código", "Nome", "Modelo", "IRF", "Prédio", "Localização", "Área", "Status"],
            lista.Select(e => new object?[] { e.Codigo, e.Nome, e.Modelo, e.Irf, e.Predio, e.Localizacao, e.Celula?.Nome, e.Status.Nome() })));
        return File(bytes, Excel.Mime, $"equipamentos-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    // QR code — planilha com nome, código e QR de cada máquina da lista que tem OS (gerada na hora: máquina nova entra,
    // máquina sem OS fica de fora). Ler o QR abre Ordens de serviço já pesquisando o código da máquina.
    public async Task<IActionResult> OnGetQrCodeAsync([FromServices] IConfiguration config)
    {
        var lista = await Filtrar().Where(e => db.Ordens.Any(o => o.EquipamentoId == e.Id)).ToListAsync();
        if (lista.Count == 0)
        {
            Erro("Nenhuma máquina com OS para gerar QR code.");
            return RedirectToPage(new { Predio, Local, CelulaId, Status, Q });
        }
        // Endereço que o celular vai abrir: o de "EnderecoSistema" (appsettings.json) ou, se vazio, o que está sendo usado agora.
        var endereco = config["EnderecoSistema"];
        if (string.IsNullOrWhiteSpace(endereco)) endereco = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
        endereco = endereco.Trim().TrimEnd('/');
        var bytes = Excel.QrCodes(lista.Select(e => (e.Nome, e.Codigo, $"{endereco}/Ordens?Q={Uri.EscapeDataString(e.Codigo)}")));
        return File(bytes, Excel.Mime, $"qrcodes-equipamentos-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    public IActionResult OnGetModelo() => File(Excel.ModeloEquipamentos(), Excel.Mime, "modelo-equipamentos.xlsx");

    // Importar Excel — código em branco: o sistema gera (mesma regra do cadastro manual); código existente é
    // atualizado; código novo é criado. Célula nova é cadastrada.
    public async Task<IActionResult> OnPostImportarAsync(IFormFile? arquivo)
    {
        var linhas = LerPlanilha(arquivo);
        if (linhas == null) return RedirectToPage();
        if (linhas.Count == 0) { Erro("A planilha está vazia: preencha as linhas abaixo do cabeçalho."); return RedirectToPage(); }
        if (!linhas[0].Tem("Nome") || !linhas[0].Tem("Prédio") || !linhas[0].Tem("Localização"))
        {
            Erro("A planilha não tem as colunas Nome, Prédio e Localização. Use a planilha do botão \"Baixar modelo\" (ou a da Extração).");
            return RedirectToPage();
        }

        var equips = await db.Equipamentos.ToDictionaryAsync(e => e.Codigo);
        var celulas = await db.Celulas.ToListAsync();
        var erros = new List<string>();
        int criados = 0, atualizados = 0;
        // Para o Histórico: como cada equipamento estava antes da planilha, e os que a planilha criou.
        var antes = new Dictionary<Equipamento, Equipamento>();
        var novos = new List<Equipamento>();
        foreach (var l in linhas)
        {
            if (l["Nome"] == "" || l["Prédio"] == "" || l["Localização"] == "") { erros.Add($"Linha {l.Numero}: preencha nome, prédio e localização"); continue; }
            var status = Excel.LerStatusEquipamento(l["Status"]);
            if (status == null) { erros.Add($"Linha {l.Numero}: status inválido (use Operando, Em manutenção ou Inativo)"); continue; }
            var codigo = l["Código"].ToUpper();
            if (codigo == "") codigo = regras.ProximoCodigoEquipamento(l["Nome"], l["Prédio"], equips.Values.Select(x => (x.Codigo, x.Nome, x.Predio)));

            Celula? celula = null;
            // Coluna "Área" (planilhas antigas: "Célula").
            var nomeArea = l.Tem("Área") ? l["Área"] : l["Célula"];
            if (nomeArea != "")
            {
                celula = celulas.FirstOrDefault(c => c.Nome.Equals(nomeArea, StringComparison.OrdinalIgnoreCase));
                if (celula == null) { celula = new Celula { Nome = nomeArea.ToUpper() }; db.Celulas.Add(celula); celulas.Add(celula); }
            }

            var novo = !equips.TryGetValue(codigo, out var e);
            if (novo) { e = new Equipamento { Codigo = codigo }; db.Equipamentos.Add(e); equips[codigo] = e; novos.Add(e); criados++; }
            else
            {
                if (e!.Id != 0 && !antes.ContainsKey(e))
                    antes[e] = new Equipamento { Codigo = e.Codigo, Nome = e.Nome, Modelo = e.Modelo, Predio = e.Predio, Localizacao = e.Localizacao, Irf = e.Irf, Status = e.Status, CelulaId = e.CelulaId };
                atualizados++;
            }
            e!.Nome = l["Nome"].ToUpper();
            // Modelo é opcional; planilha antiga (sem a coluna) não apaga o modelo já cadastrado.
            if (l.Tem("Modelo")) e.Modelo = l["Modelo"] == "" ? null : l["Modelo"].ToUpper();
            // IRF (máquinas da Samsung) também é opcional; planilha sem a coluna não apaga o IRF já cadastrado.
            if (l.Tem("IRF")) e.Irf = l["IRF"] == "" ? null : l["IRF"].ToUpper();
            e.Predio = l["Prédio"].ToUpper();
            e.Localizacao = l["Localização"].ToUpper();
            e.Status = status.Value;
            e.Celula = celula;
            if (celula == null) e.CelulaId = null;
        }
        await db.SaveChangesAsync();

        // Histórico: criados pela planilha e, nos que já existiam, só o que mudou (campo: antes → depois).
        var nomesCelula = celulas.ToDictionary(c => c.Id, c => c.Nome);
        foreach (var e in novos)
            auditoria.Registrar("Equipamento", e.Id, "Criado pelo Excel", $"{e.Descricao}{(e.Modelo != null ? $" · modelo {e.Modelo}" : "")}{(e.Irf != null ? $" · IRF {e.Irf}" : "")} · {e.Predio} · {e.Localizacao} · {e.Status.Nome()}");
        foreach (var (e, a) in antes)
            if (EditarModel.MudancasEquipamento(a, e, nomesCelula) is { Length: > 0 } mudou)
                auditoria.Registrar("Equipamento", e.Id, "Alterado pelo Excel", mudou);
        await db.SaveChangesAsync();

        Resumo($"{criados} equipamento(s) criado(s) e {atualizados} atualizado(s) pelo Excel.", erros);
        return RedirectToPage();
    }

    // Remover selecionados — equipamentos com planos ou OS ficam (marque como Inativo).
    public async Task<IActionResult> OnPostRemoverAsync(int[] ids)
    {
        var lista = await db.Equipamentos.Where(e => ids.Contains(e.Id)).ToListAsync();
        var emUso = await db.Planos.Where(p => ids.Contains(p.EquipamentoId)).Select(p => p.EquipamentoId)
            .Union(db.Ordens.Where(o => ids.Contains(o.EquipamentoId)).Select(o => o.EquipamentoId))
            .Union(db.Nres.Where(n => n.EquipamentoId != null && ids.Contains(n.EquipamentoId.Value)).Select(n => n.EquipamentoId!.Value)).ToListAsync();
        var remover = lista.Where(e => !emUso.Contains(e.Id)).ToList();
        foreach (var e in remover) auditoria.Registrar("Equipamento", e.Id, "Removido", e.Descricao);
        db.Equipamentos.RemoveRange(remover);
        await db.SaveChangesAsync();

        var mantidos = lista.Where(e => emUso.Contains(e.Id)).Select(e => e.Codigo).ToList();
        if (mantidos.Count == 0) Ok($"{remover.Count} equipamento(s) removido(s).");
        else Erro($"{remover.Count} equipamento(s) removido(s). Não removidos por terem planos, OS ou NRE (marque como Inativo): {string.Join(", ", mantidos)}.");
        return RedirectToPage(new { Predio, Local, CelulaId, Status, Q });
    }
}
