// ============================================================================
// TELA: EQUIPAMENTOS — lista (lógica)  ·  endereço /Equipamentos  ·  visual em Index.cshtml  ·  permissão "equipamentos"
// ONDE MEXER:
//   - Filtros e ordem da lista ............ Filtrar
//   - Itens por página (50) ............... PorPagina (páginas como a lista de OS; Extração e QR code pegam todas)
//   - Planilha do botão Extração .......... OnGetExtracaoAsync
//   - Planilha de QR codes ................ OnGetQrCodeAsync (endereço do QR: "EnderecoSistema" no appsettings.json;
//                                           desenho das etiquetas: Servicos/Excel.cs → QrCodes)
//   - Planilha do Baixar modelo ........... OnGetModelo
//   - Importar Excel ...................... OnPostImportarAsync (máquina que já existe é atualizada, não duplica;
//                                           como a linha sem código acha a máquina: AcharSemCodigo)
//   - Remover selecionados ................ OnPostRemoverAsync (quem tem plano ou OS não é removido)
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Equipamentos;

public class IndexModel(AppDb db, Auditoria auditoria, Regras regras, ConfigEtiquetas etiquetas) : MsPage, IFiltroArea
{
    protected override string Modulo => "equipamentos";
    const int PorPagina = 50;

    [BindProperty(SupportsGet = true)] public string? Predio { get; set; }
    [BindProperty(SupportsGet = true)] public string? Local { get; set; }
    [BindProperty(SupportsGet = true)] public int? CelulaId { get; set; }
    [BindProperty(SupportsGet = true)] public StatusEquipamento? Status { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;

    public List<string> Predios { get; set; } = new();
    public List<string> Localizacoes { get; set; } = new();
    public List<Celula> Celulas { get; set; } = new();
    public List<Equipamento> Lista { get; set; } = new();
    public int Total { get; set; }
    public int Paginas => Math.Max(1, (int)Math.Ceiling(Total / (double)PorPagina));

    IQueryable<Equipamento> Filtrar()
    {
        var q = db.Equipamentos.Include(e => e.Celula).AsQueryable();
        if (!string.IsNullOrEmpty(Predio)) q = q.Where(e => e.Predio == Predio);
        if (!string.IsNullOrEmpty(Local)) q = q.Where(e => e.Localizacao == Local);
        if (CelulaId != null) q = q.Where(e => e.CelulaId == CelulaId);
        if (Status != null) q = q.Where(e => e.Status == Status);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var t = Q.Trim().ToUpperInvariant();   // texto gravado em maiúscula
            q = q.Where(e => e.Codigo.Contains(t) || (e.Irf != null && e.Irf.Contains(t)) || e.Nome.Contains(t) || (e.Modelo != null && e.Modelo.Contains(t))
                || (e.NumeroSerie != null && e.NumeroSerie.Contains(t)));
        }
        return q.OrderBy(e => e.Codigo);
    }

    public async Task OnGetAsync()
    {
        // Filtros Prédio e Localização: só os das máquinas da área escolhida.
        var daArea = db.Equipamentos.Where(e => CelulaId == null || e.CelulaId == CelulaId);
        Predios = await daArea.Where(e => e.Predio != "").Select(e => e.Predio).Distinct().OrderBy(x => x).ToListAsync();
        Localizacoes = await daArea.Select(e => e.Localizacao).Distinct().OrderBy(x => x).ToListAsync();
        Celulas = await db.Celulas.OrderBy(c => c.Nome).ToListAsync();
        var q = Filtrar();
        Total = await q.CountAsync();
        Pagina = Math.Clamp(Pagina, 1, Paginas);
        Lista = await q.Skip((Pagina - 1) * PorPagina).Take(PorPagina).ToListAsync();
    }

    // Extração — mesmas colunas do modelo, então pode ser editada e importada de volta.
    public async Task<IActionResult> OnGetExtracaoAsync()
    {
        var lista = await Filtrar().ToListAsync();
        // Mesmas colunas e ordem da tela (a importação acha as colunas pelo nome, então a planilha volta sem ajuste).
        // A coluna Etiqueta eletrônica não aparece na tela: está aqui para preencher no Excel e importar de volta.
        var bytes = Excel.Planilha(("Equipamentos", ["Código", "IRF", "Etiqueta eletrônica", "Nome", "Modelo", "Número de série", "Prédio", "Localização", "Área", "Status"],
            lista.Select(e => new object?[] { e.Codigo, e.Irf, e.Etiqueta, e.Nome, e.Modelo, e.NumeroSerie, e.Predio, e.Localizacao, e.Celula?.Nome, e.Status.Nome() })));
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

    // Planilha das máquinas que têm etiqueta eletrônica, no formato do cadastro de produto da nuvem (Servicos/Etiquetas.cs):
    // sobe no Import da tela Product e as etiquetas já passam a mostrar a manutenção certa, sem depender da API.
    public async Task<IActionResult> OnGetProdutosEtiquetaAsync()
    {
        var lista = await Etiquetas.MontarAsync(db, etiquetas, DateTime.Now);
        if (lista.Count == 0)
        {
            Erro("Nenhuma máquina tem etiqueta eletrônica cadastrada. Preencha o campo \"Etiqueta eletrônica\" no cadastro da máquina ou pelo Importar Excel.");
            return RedirectToPage(new { Predio, Local, CelulaId, Status, Q });
        }
        return File(Etiquetas.Planilha(lista, etiquetas), Excel.Mime, $"etiquetas-eletronicas-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    // Prévia da etiqueta eletrônica: /Equipamentos?handler=Etiqueta&id=123 devolve a imagem exatamente como ela vai
    // aparecer na tela e-paper (Servicos/Etiquetas.cs). Só desenha — não manda nada para a nuvem do fornecedor.
    public async Task<IActionResult> OnGetEtiquetaAsync(int id)
    {
        var conteudo = await Etiquetas.UmaAsync(db, etiquetas, id, DateTime.Now);
        if (conteudo == null) return NotFound();
        return File(Etiquetas.Desenhar(conteudo), "image/png");
    }

    // Linha sem código (a planilha do Baixar modelo não tem a coluna): acha a máquina já cadastrada que é a mesma, para
    // atualizar em vez de criar outra (pedido do usuário em 13/10/2026: importar de novo não pode duplicar).
    // Cada máquina serve a uma linha só, e cada regra passa pela planilha inteira antes da seguinte (da mais certa para a menos):
    //   1. mesmo IRF
    //   2. tudo igual: nome, modelo, número de série, prédio, localização e área
    //   3. mesmo número de série e nome, na mesma área (mudou o lugar ou o modelo)
    //   4. mesmo número de série, no mesmo prédio, localização e área (mudou o nome ou o modelo)
    //   5. mesmo nome, prédio, localização e área e mesmo modelo, sem número de série diferente
    //   6. igual à 5, sem modelo diferente          (5 e 6: em branco de um lado = ainda não preenchido)
    // Linhas iguais (ex.: 9 MESA ELEVADORA) pegam as máquinas iguais pela ordem do código; a linha que sobrar é máquina nova.
    // Nas regras 3 e 4 o número de série precisa ter algum número ("N/A", "S/N" não identificam uma máquina).
    static Dictionary<Excel.LinhaPlanilha, Equipamento> AcharSemCodigo(List<Excel.LinhaPlanilha> linhas, IEnumerable<Equipamento> cadastrados,
        List<Celula> celulas, int? areaPadrao)
    {
        string Area(int? id) => celulas.FirstOrDefault(c => c.Id == id)?.Nome ?? "";
        static bool Igual(string planilha, string? cadastro) => planilha.Equals(cadastro ?? "", StringComparison.OrdinalIgnoreCase);
        static bool Combina(string planilha, string? cadastro) => planilha == "" || string.IsNullOrEmpty(cadastro) || Igual(planilha, cadastro);

        // As máquinas que a planilha chama pelo código já têm linha; as outras ficam livres para as linhas sem código.
        var comCodigo = linhas.Select(l => l["Código"].ToUpper()).ToHashSet();
        var livres = cadastrados.Where(e => !comCodigo.Contains(e.Codigo)).OrderBy(e => e.Codigo, StringComparer.Ordinal)
            .Select(e => (Equip: e, Area: Area(e.CelulaId))).ToList();
        var semCodigo = linhas.Where(l => l["Código"] == "").Select(l =>
        {
            var area = l.Tem("Área") ? l["Área"] : l["Célula"];   // em branco = a área escolhida nos cartões
            return (Linha: l, Irf: l["IRF"], Nome: l["Nome"], Modelo: l["Modelo"], Serie: l["Número de série"],
                SerieComNumero: l["Número de série"].Any(char.IsDigit), Predio: l["Prédio"], Local: l["Localização"],
                Area: area == "" ? Area(areaPadrao) : area);
        }).ToList();

        var achadas = new Dictionary<Excel.LinhaPlanilha, Equipamento>();
        for (var regra = 1; regra <= 6; regra++)
            foreach (var l in semCodigo.Where(l => !achadas.ContainsKey(l.Linha)))
            {
                var i = livres.FindIndex(m =>
                {
                    var e = m.Equip;
                    var area = Igual(l.Area, m.Area);
                    var lugar = area && Igual(l.Predio, e.Predio) && Igual(l.Local, e.Localizacao);
                    var serie = l.SerieComNumero && Igual(l.Serie, e.NumeroSerie);
                    return regra switch
                    {
                        1 => l.Irf != "" && Igual(l.Irf, e.Irf),
                        2 => lugar && Igual(l.Nome, e.Nome) && Igual(l.Modelo, e.Modelo) && Igual(l.Serie, e.NumeroSerie),
                        3 => serie && area && Igual(l.Nome, e.Nome),
                        4 => serie && lugar,
                        5 => lugar && Igual(l.Nome, e.Nome) && Igual(l.Modelo, e.Modelo) && Combina(l.Serie, e.NumeroSerie),
                        _ => lugar && Igual(l.Nome, e.Nome) && Combina(l.Modelo, e.Modelo) && Combina(l.Serie, e.NumeroSerie),
                    };
                });
                if (i >= 0) { achadas[l.Linha] = livres[i].Equip; livres.RemoveAt(i); }
            }
        return achadas;
    }

    // Importar Excel — a máquina que já existe é atualizada, nunca criada de novo: pelo código ou, na linha sem código,
    // pelos próprios dados (AcharSemCodigo). Só a que não existe é criada, com o código gerado pelo sistema (mesma regra
    // do cadastro manual) ou o código novo da planilha. Área nova é cadastrada.
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
        var areaPadrao = celulas.FirstOrDefault(c => c.Id == AreaId)?.Id;
        var achadas = AcharSemCodigo(linhas, equips.Values, celulas, areaPadrao);
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
            var comCodigo = codigo != "";
            // Sem código: a máquina já cadastrada que é a mesma da linha; se não houver, é máquina nova e o sistema gera o código.
            if (!comCodigo) codigo = achadas.TryGetValue(l, out var achada) ? achada.Codigo
                : regras.ProximoCodigoEquipamento(l["Nome"], l["Prédio"], equips.Values.Select(x => (x.Codigo, x.Nome, x.Predio)));
            // IRF (código da Samsung) é opcional e não se repete entre máquinas.
            var irf = l["IRF"] == "" ? null : l["IRF"].ToUpper();
            if (irf != null && equips.Values.FirstOrDefault(x => x.Irf == irf && x.Codigo != codigo) is { } dono)
            { erros.Add($"Linha {l.Numero}: o IRF {irf} já está na máquina {dono.Descricao}"); continue; }
            // Etiqueta eletrônica (código de barras da etiqueta) também é opcional e uma por máquina. Aceita a coluna
            // "Etiqueta eletrônica" (Extração e modelo) ou só "Etiqueta".
            var colEtq = l.Tem("Etiqueta eletrônica") ? "Etiqueta eletrônica" : "Etiqueta";
            var etiqueta = l[colEtq] == "" ? null : l[colEtq].ToUpper();
            if (etiqueta != null && (equips.Values.FirstOrDefault(x => x.Etiqueta == etiqueta && x.Codigo != codigo) is { } donoEtq))
            { erros.Add($"Linha {l.Numero}: a etiqueta {etiqueta} já está na máquina {donoEtq.Descricao}"); continue; }

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
                    antes[e] = new Equipamento { Codigo = e.Codigo, Irf = e.Irf, Etiqueta = e.Etiqueta, Nome = e.Nome, Modelo = e.Modelo, NumeroSerie = e.NumeroSerie, Predio = e.Predio, Localizacao = e.Localizacao, Status = e.Status, CelulaId = e.CelulaId };
                atualizados++;
            }
            e!.Nome = l["Nome"].ToUpper();
            // Campo opcional (modelo, IRF, etiqueta, número de série, área): planilha antiga (sem a coluna) não apaga o que já
            // está cadastrado; na linha sem código, o campo em branco também não apaga (a máquina foi achada pelos dados).
            bool Grava(string coluna) => l.Tem(coluna) && (comCodigo || l[coluna] != "");
            if (Grava("Modelo")) e.Modelo = l["Modelo"] == "" ? null : l["Modelo"].ToUpper();
            if (Grava("IRF")) e.Irf = irf;
            // Etiqueta em branco nunca apaga a que já está ligada: dá para importar aos poucos, conforme as etiquetas são
            // coladas (pedido do usuário em 14/10/2026). Para tirar a etiqueta de uma máquina, apagar no cadastro.
            if (etiqueta != null) e.Etiqueta = etiqueta;
            if (Grava("Número de série")) e.NumeroSerie = l["Número de série"] == "" ? null : l["Número de série"].ToUpper();
            e.Predio = l["Prédio"].ToUpper();
            e.Localizacao = l["Localização"].ToUpper();
            e.Status = status.Value;
            if (celula != null) e.Celula = celula;
            // Área em branco: a máquina nova entra na área escolhida nos cartões (senão não apareceria em nenhuma).
            else if (novo) e.CelulaId = areaPadrao;
            else if (comCodigo) { e.Celula = null; e.CelulaId = null; }
        }
        await db.SaveChangesAsync();

        // Histórico: criados pela planilha e, nos que já existiam, só o que mudou (campo: antes → depois).
        var nomesCelula = celulas.ToDictionary(c => c.Id, c => c.Nome);
        foreach (var e in novos)
            auditoria.Registrar("Equipamento", e.Id, "Criado pelo Excel", $"{e.Descricao}{(e.Irf != null ? $" · IRF {e.Irf}" : "")}{(e.Etiqueta != null ? $" · etiqueta {e.Etiqueta}" : "")}{(e.Modelo != null ? $" · modelo {e.Modelo}" : "")}{(e.NumeroSerie != null ? $" · número de série {e.NumeroSerie}" : "")} · {e.Predio} · {e.Localizacao} · {e.Status.Nome()}");
        foreach (var (e, a) in antes)
            if (EditarModel.MudancasEquipamento(a, e, nomesCelula) is { Length: > 0 } mudou)
                auditoria.Registrar("Equipamento", e.Id, "Alterado pelo Excel", mudou);
        await db.SaveChangesAsync();

        Resumo($"{criados} equipamento(s) criado(s) e {atualizados} atualizado(s) pelo Excel.", erros);
        return RedirectToPage();
    }

    // Remover selecionados — equipamentos com planos ou OS ficam (marque como Inativo).
    // Com "marcar todos" (todos), todos os equipamentos da lista filtrada, de todas as páginas.
    public async Task<IActionResult> OnPostRemoverAsync(int[] ids, bool todos)
    {
        if (todos) ids = await Filtrar().Select(e => e.Id).ToArrayAsync();
        var lista = await db.Equipamentos.Where(e => ids.Contains(e.Id)).ToListAsync();
        var emUso = await db.Planos.Where(p => ids.Contains(p.EquipamentoId)).Select(p => p.EquipamentoId)
            .Union(db.Ordens.Where(o => ids.Contains(o.EquipamentoId)).Select(o => o.EquipamentoId)).ToListAsync();
        var remover = lista.Where(e => !emUso.Contains(e.Id)).ToList();
        foreach (var e in remover) auditoria.Registrar("Equipamento", e.Id, "Removido", e.Descricao);
        db.Equipamentos.RemoveRange(remover);
        await db.SaveChangesAsync();

        var mantidos = lista.Where(e => emUso.Contains(e.Id)).Select(e => e.Codigo).ToList();
        if (mantidos.Count == 0) Ok($"{remover.Count} equipamento(s) removido(s).");
        else Erro($"{remover.Count} equipamento(s) removido(s). Não removidos por terem planos ou OS (marque como Inativo): " +
            $"{string.Join(", ", mantidos.Take(30))}{(mantidos.Count > 30 ? $" e mais {mantidos.Count - 30}" : "")}.");
        return RedirectToPage(new { Predio, Local, CelulaId, Status, Q });
    }
}
