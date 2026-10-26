// ============================================================================
// TELA: SPARE PARTS (lógica)  ·  endereço /SpareParts  ·  visual em Index.cshtml  ·  permissão "spareparts"
// Histórico dos códigos das peças — código, descrição e equipamento — tudo numa tela só: o quadro do alto adiciona uma
// peça nova ou altera a peça clicada na lista (?editar=id).
// Equipamento: tipo de máquina (só o nome, ex.: ADESIVADORA; opcional) — a lista são os nomes das máquinas cadastradas,
// sem repetir; dá para digitar outro.
// SEM QUANTIDADE (pedido do usuário em 08/10/2026: "o spare parts só vai servir para histórico de código com descrição e
// equipamento"): a coluna e o campo saíram da tela, da importação e da Extração. O uso nas OS continua registrado
// (Servicos/Estoque.cs). SparePart.Estoque e os ajustes antigos ficam no banco, sem uso.
// CADA ÁREA TEM AS SUAS PEÇAS (pedido do usuário em 13/10/2026: "cada um vai ter seu histórico de maquinário"): a tela
// só mostra, adiciona, altera, importa e remove as peças da área escolhida nos cartões (AreaId). O mesmo código pode
// existir em outra área: é outra peça, e mexer nesta não muda nem apaga a de lá.
// ONDE MEXER:
//   - Lista e pesquisa ...................... Filtrar (só peças ativas da área; pesquisa por código, descrição ou equipamento)
//   - Itens por página (50) ................. PorPagina (a página aberta continua ao alterar uma peça; Extração = todas)
//   - Adicionar / alterar (quadro do alto) .. OnPostSalvarAsync (o código liga a peça às OS: trocar o código troca
//                                             também nas "Peças utilizadas" das OS que usavam o código antigo)
//   - Remover selecionados .................. OnPostRemoverAsync (peça já usada em OS fica inativa, não é apagada)
//   - Planilha do botão Extração ............ OnGetExtracaoAsync (abas Peças e Uso em OS)
//   - Planilha do Baixar modelo ............. OnGetModelo (colunas em Servicos/Excel.cs → ColunasSpareParts)
//   - Importar Excel ........................ OnPostImportarAsync (código novo = peça nova; código que existe na área é atualizado)
//   - Coluna "Saldo SAP" .................... OnGetSaldoSapAsync (pedido do usuário em 20/10/2026): a tela abre e depois
//                                             pede o saldo das peças da página aberta; só esses códigos vão para o SAP
//                                             (Servicos/Sap.cs). Nada é gravado. Coluna só aparece com "Sap:Ativo".
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.SpareParts;

public class IndexModel(AppDb db, Auditoria auditoria, Sap sap, ILogger<IndexModel> log) : MsPage
{
    protected override string Modulo => "spareparts";
    const int PorPagina = 50;
    public bool SapLigado => sap.Ligado;   // mostra a coluna "Saldo SAP"

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int? Editar { get; set; }   // peça aberta no quadro do alto
    [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;

    public List<SparePart> Lista { get; set; } = new();
    public int Total { get; set; }
    public int Paginas => Math.Max(1, (int)Math.Ceiling(Total / (double)PorPagina));
    public SparePart? Editando { get; set; }
    public List<string> TiposDeMaquina { get; set; } = new();   // lista do campo Equipamento: nomes das máquinas, sem repetir

    IQueryable<SparePart> Filtrar()
    {
        var area = AreaId;   // só as peças da área escolhida nos cartões
        var q = db.SpareParts.Where(p => p.Ativa && p.CelulaId == area);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var t = Q.Trim().ToUpper();
            q = q.Where(p => p.Codigo.Contains(t) || p.Descricao.ToUpper().Contains(t) || (p.Maquina != null && p.Maquina.ToUpper().Contains(t)));
        }
        return q.OrderBy(p => p.Codigo);
    }

    public async Task OnGetAsync()
    {
        var q = Filtrar();
        Total = await q.CountAsync();
        Pagina = Math.Clamp(Pagina, 1, Paginas);
        Lista = await q.Skip((Pagina - 1) * PorPagina).Take(PorPagina).ToListAsync();
        var area = AreaId;   // peça aberta e tipos de máquina: só os da área escolhida nos cartões
        if (Editar != null) Editando = await db.SpareParts.FirstOrDefaultAsync(p => p.Id == Editar && p.Ativa && p.CelulaId == area);
        if (PodeEditar) TiposDeMaquina = await db.Equipamentos.Where(e => area == null || e.CelulaId == area).Select(e => e.Nome).Distinct().OrderBy(n => n).ToListAsync();
    }

    // Quadro do alto: sem id = peça nova; com id = altera a peça (código, descrição e equipamento).
    public async Task<IActionResult> OnPostSalvarAsync(int? id, string? codigo, string? descricao, string? maquina)
    {
        var cod = (codigo ?? "").Trim().ToUpper();
        var desc = (descricao ?? "").Trim().ToUpper();
        var maq = string.IsNullOrWhiteSpace(maquina) ? null : maquina.Trim().ToUpper();
        if (cod == "") { Erro("Preencha o código."); return RedirectToPage(new { Q, editar = id, Pagina }); }
        if (desc == "") { Erro("Preencha a descrição."); return RedirectToPage(new { Q, editar = id, Pagina }); }

        SparePart? p;
        var osAtualizadas = 0;
        var area = AreaId;   // a peça é da área escolhida nos cartões; o mesmo código em outra área é outra peça
        if (id is int pid)
        {
            p = await db.SpareParts.FirstOrDefaultAsync(x => x.Id == pid && x.CelulaId == area);
            if (p == null) { Erro("Esta peça não é da área escolhida."); return RedirectToPage(new { Q, Pagina }); }
            var antes = (p.Codigo, p.Descricao, p.Maquina);
            if (cod != p.Codigo)
            {
                // O código é único na área (inclusive das peças removidas, que voltam se o código for cadastrado de novo).
                if (await db.SpareParts.AnyAsync(x => x.Codigo == cod && x.CelulaId == area && x.Id != pid)) { Erro($"Já existe uma peça com o código {cod}."); return RedirectToPage(new { Q, editar = id, Pagina }); }
                // As OS ligam a peça pelo código: troca nelas também (só nas OS das máquinas desta área), para o
                // histórico de uso continuar nesta peça.
                var codigoAntigo = p.Codigo;
                foreach (var po in await db.Pecas.Where(x => x.Codigo == codigoAntigo
                    && db.Ordens.Any(o => o.Id == x.OrdemServicoId && o.Equipamento!.CelulaId == area)).ToListAsync()) { po.Codigo = cod; osAtualizadas++; }
                p.Codigo = cod;
            }
            p.Descricao = desc;
            p.Maquina = maq;
            var mudou = Auditoria.Mudancas(("Código", antes.Codigo, p.Codigo), ("Descrição", antes.Descricao, p.Descricao),
                ("Equipamento", antes.Maquina, p.Maquina));
            if (mudou != "") auditoria.Registrar("SparePart", p.Id, "Alterada", mudou);
            await db.SaveChangesAsync();
        }
        else
        {
            var existente = await db.SpareParts.Where(x => x.Codigo == cod && x.CelulaId == area).OrderByDescending(x => x.Ativa).FirstOrDefaultAsync();
            if (existente is { Ativa: true }) { Erro($"Já existe uma peça com o código {cod}."); return RedirectToPage(new { Q, Pagina }); }
            // Código de uma peça removida (inativa) desta área: ela volta, com o histórico de uso que já tinha.
            p = existente ?? new SparePart { CelulaId = area, Codigo = cod, CriadaEm = DateTime.Now };
            p.Descricao = desc;
            p.Maquina = maq;
            p.Ativa = true;
            if (existente == null) db.SpareParts.Add(p);
            await db.SaveChangesAsync();
            auditoria.Registrar("SparePart", p.Id, existente == null ? "Criada" : "Reativada",
                $"{p.Codigo} · {p.Descricao}{(maq != null ? $" · {maq}" : "")}");
            await db.SaveChangesAsync();
        }
        Ok(osAtualizadas == 0 ? $"Peça {p.Codigo} salva." : $"Peça {p.Codigo} salva. O código novo também foi trocado em {osAtualizadas} peça(s) lançada(s) em OS.");
        return RedirectToPage(new { Q, Pagina });   // volta na mesma página da lista
    }

    // Coluna "Saldo SAP" — a tela manda os ids das peças da página aberta; o SAP recebe só os códigos delas (da área
    // escolhida). Resposta: { saldos: { id: { texto, zero } } } ou { erro } quando o SAP não respondeu.
    public async Task<IActionResult> OnGetSaldoSapAsync(int[] ids, CancellationToken ct)
    {
        if (!sap.Ligado) return new JsonResult(new { erro = "SAP não configurado" });
        var area = AreaId;
        var pecas = await db.SpareParts.Where(p => ids.Contains(p.Id) && p.Ativa && p.CelulaId == area)
            .Select(p => new { p.Id, p.Codigo }).Take(PorPagina).ToListAsync(ct);
        var nomeArea = area == null ? null : await db.Celulas.Where(c => c.Id == area).Select(c => c.Nome).FirstOrDefaultAsync(ct);
        try
        {
            var saldos = await sap.SaldosAsync(pecas.Select(p => p.Codigo), nomeArea, ct);
            return new JsonResult(new
            {
                saldos = pecas.Where(p => saldos.ContainsKey(p.Codigo.Trim().ToUpperInvariant())).ToDictionary(p => p.Id.ToString(), p =>
                {
                    var s = saldos[p.Codigo.Trim().ToUpperInvariant()];
                    return new { texto = $"{s.Quantidade:0.###}{(s.Unidade != null ? " " + s.Unidade : "")}", zero = s.Quantidade <= 0 };
                }),
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            log.LogWarning(ex, "Saldo SAP: consulta falhou");
            return new JsonResult(new { erro = "SAP indisponível" });
        }
    }

    public IActionResult OnGetModelo() => File(Excel.ModeloSpareParts(), Excel.Mime, "modelo-spare-parts.xlsx");

    // Importar Excel — código novo vira peça nova; código que já existe é atualizado (e volta a ficar ativo se tinha
    // sido removido). Equipamento é opcional: sem a coluna, não muda. Coluna Quantidade (planilhas antigas) é ignorada.
    public async Task<IActionResult> OnPostImportarAsync(IFormFile? arquivo)
    {
        var linhas = LerPlanilha(arquivo);
        if (linhas == null) return RedirectToPage(new { Q });
        if (linhas.Count == 0) { Erro("A planilha está vazia: preencha as linhas abaixo do cabeçalho."); return RedirectToPage(new { Q }); }
        if (!linhas[0].Tem("Código") || !linhas[0].Tem("Descrição"))
        {
            Erro("A planilha não tem as colunas Código e Descrição. Use a planilha do botão \"Baixar modelo\" (ou a da Extração).");
            return RedirectToPage(new { Q });
        }
        var area = AreaId;   // a planilha entra na área escolhida nos cartões; as peças das outras áreas não mudam
        var pecas = (await db.SpareParts.Where(p => p.CelulaId == area).ToListAsync())
            .GroupBy(p => p.Codigo).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Ativa).First());
        var erros = new List<string>();
        var novas = new List<SparePart>();
        // Para o Histórico: como cada peça que já existia estava antes da planilha.
        var antes = new Dictionary<SparePart, (string Descricao, string? Maquina, bool Ativa)>();
        foreach (var l in linhas)
        {
            var codigo = l["Código"].ToUpper();
            var desc = l["Descrição"].ToUpper();
            if (codigo == "" || desc == "") { erros.Add($"Linha {l.Numero}: preencha código e descrição"); continue; }

            if (!pecas.TryGetValue(codigo, out var p))
            {
                p = new SparePart { CelulaId = area, Codigo = codigo, CriadaEm = DateTime.Now };
                db.SpareParts.Add(p);
                pecas[codigo] = p;
                novas.Add(p);
            }
            else if (!novas.Contains(p) && !antes.ContainsKey(p)) antes[p] = (p.Descricao, p.Maquina, p.Ativa);
            p.Descricao = desc;
            if (l.Tem("Equipamento")) p.Maquina = l["Equipamento"] == "" ? null : l["Equipamento"].ToUpper();
            p.Ativa = true;
        }
        await db.SaveChangesAsync();

        // Histórico: as criadas pela planilha e, nas que já existiam, só o que mudou (campo: antes → depois).
        foreach (var p in novas)
            auditoria.Registrar("SparePart", p.Id, "Criada pelo Excel", $"{p.Codigo} · {p.Descricao}{(p.Maquina != null ? $" · {p.Maquina}" : "")}");
        foreach (var (p, a) in antes)
        {
            var mudou = Auditoria.Mudancas(("Descrição", a.Descricao, p.Descricao), ("Equipamento", a.Maquina, p.Maquina));
            if (!a.Ativa) auditoria.Registrar("SparePart", p.Id, "Reativada pelo Excel", mudou == "" ? $"{p.Codigo} · {p.Descricao}" : mudou);
            else if (mudou != "") auditoria.Registrar("SparePart", p.Id, "Alterada pelo Excel", mudou);
        }
        await db.SaveChangesAsync();
        Resumo($"{novas.Count} peça(s) criada(s) e {antes.Count} atualizada(s) pelo Excel.", erros);
        return RedirectToPage(new { Q });
    }

    // Extração — as peças da lista (código, descrição, equipamento) e o histórico de uso delas nas OS (usos e usos
    // desfeitos por conclusão retornada; "Qtd. na OS" é a quantidade lançada na OS, não estoque).
    public async Task<IActionResult> OnGetExtracaoAsync()
    {
        Lista = await Filtrar().ToListAsync();
        var ids = Lista.Select(p => p.Id).ToList();
        var usos = await db.MovimentosPeca.Include(m => m.SparePart).Include(m => m.OrdemServico).Include(m => m.Equipamento)
            .Where(m => ids.Contains(m.SparePartId) && (m.Tipo == TipoMovimento.UsoOs || m.Tipo == TipoMovimento.Estorno))
            .OrderByDescending(m => m.Quando).ToListAsync();
        var bytes = Excel.Planilha(
            ("Peças", Excel.ColunasSpareParts, Lista.Select(p => new object?[] { p.Codigo, p.Descricao, p.Maquina })),
            ("Uso em OS", ["Data", "Código", "Descrição", "Registro", "Qtd. na OS", "OS", "Equipamento", "Quem", "Observação"],
                usos.Select(m => new object?[]
                {
                    m.Quando, m.SparePart?.Codigo, m.SparePart?.Descricao, m.Tipo.Nome(), Math.Abs(m.Quantidade),
                    m.OrdemServico?.Numero, m.Equipamento?.Descricao, m.Usuario, m.Observacao,
                })));
        return File(bytes, Excel.Mime, $"spare-parts-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    // Remover selecionados — peça nunca usada é apagada; já usada em OS fica inativa (some da lista e das sugestões da
    // OS, mas o histórico fica; cadastrar o mesmo código de novo traz ela de volta).
    // Com "marcar todas" (todos), todas as peças da lista pesquisada, de todas as páginas.
    // Só remove peça da área escolhida nos cartões: a peça de mesmo código em outra área continua lá.
    public async Task<IActionResult> OnPostRemoverAsync(int[] ids, bool todos)
    {
        if (todos) ids = await Filtrar().Select(p => p.Id).ToArrayAsync();
        var area = AreaId;
        var lista = await db.SpareParts.Where(p => ids.Contains(p.Id) && p.CelulaId == area).ToListAsync();
        var comHistorico = await db.MovimentosPeca.Where(m => ids.Contains(m.SparePartId) && (m.Tipo == TipoMovimento.UsoOs || m.Tipo == TipoMovimento.Estorno))
            .Select(m => m.SparePartId).Distinct().ToListAsync();
        int apagadas = 0, inativadas = 0;
        foreach (var p in lista)
        {
            if (comHistorico.Contains(p.Id)) { p.Ativa = false; inativadas++; auditoria.Registrar("SparePart", p.Id, "Inativada", $"{p.Codigo} · {p.Descricao}"); }
            else
            {
                db.MovimentosPeca.RemoveRange(db.MovimentosPeca.Where(m => m.SparePartId == p.Id)); // só ajustes de quantidade
                db.SpareParts.Remove(p);
                apagadas++;
                auditoria.Registrar("SparePart", p.Id, "Removida", $"{p.Codigo} · {p.Descricao}");
            }
        }
        await db.SaveChangesAsync();
        Ok(inativadas == 0 ? $"{apagadas} peça(s) removida(s)."
            : $"{apagadas} peça(s) removida(s) e {inativadas} inativada(s) (já foram usadas em OS: o histórico fica guardado).");
        return RedirectToPage(new { Q, Pagina });
    }
}
