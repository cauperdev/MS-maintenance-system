// ============================================================================
// TELA: SPARE PARTS (lógica)  ·  endereço /SpareParts  ·  visual em Index.cshtml  ·  permissão "spareparts"
// Cadastro das peças de reposição — código, descrição, equipamento e quantidade em estoque — tudo numa tela só: o
// quadro do alto adiciona uma peça nova ou altera a peça clicada na lista (?editar=id).
// Equipamento: tipo de máquina (só o nome, ex.: ADESIVADORA; opcional) — a lista são os nomes das máquinas cadastradas,
// sem repetir; dá para digitar outro.
// A quantidade (SparePart.Estoque) só muda aqui: as peças lançadas nas OS são só registro e histórico do código e não
// descontam nada (Servicos/Estoque.cs). Mudar a quantidade aqui grava um "Ajuste de inventário".
// ONDE MEXER:
//   - Lista e pesquisa ...................... Filtrar (só peças ativas; da área dos cards + uso geral; pesquisa por
//                                             código, descrição ou equipamento)
//   - Adicionar / alterar (quadro do alto) .. OnPostSalvarAsync (o código liga a peça às OS: trocar o código troca
//                                             também nas "Peças utilizadas" das OS que usavam o código antigo)
//   - Remover selecionados .................. OnPostRemoverAsync (peça já usada em OS fica inativa, não é apagada)
//   - Planilha do botão Extração ............ OnGetExtracaoAsync (abas Peças e Movimentos)
//   - Planilha do Baixar modelo ............. OnGetModelo (colunas em Servicos/Excel.cs → ColunasSpareParts)
//   - Importar Excel ........................ OnPostImportarAsync (código novo = peça nova; código que existe é atualizado)
// ============================================================================
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.SpareParts;

public class IndexModel(AppDb db, Auditoria auditoria, Estoque estoque) : MsPage, IFiltroArea
{
    protected override string Modulo => "spareparts";

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int? Editar { get; set; }   // peça aberta no quadro do alto

    public List<SparePart> Lista { get; set; } = new();
    public SparePart? Editando { get; set; }
    public List<string> TiposDeMaquina { get; set; } = new();   // lista do campo Equipamento: nomes das máquinas, sem repetir

    // Área escolhida nos cards (Servicos/AreaEscolhida.cs): somem só as peças de máquinas de OUTRAS áreas; peças sem
    // equipamento ou de equipamento que não está cadastrado aparecem em todas.
    public int? CelulaId { get; set; }
    IQueryable<string> MaquinasDaArea() => db.Equipamentos.Where(e => CelulaId == null || e.CelulaId == CelulaId).Select(e => e.Nome.ToUpper());

    IQueryable<SparePart> Filtrar()
    {
        var q = db.SpareParts.Where(p => p.Ativa);
        if (CelulaId != null)
        {
            var todas = db.Equipamentos.Select(e => e.Nome.ToUpper());
            q = q.Where(p => p.Maquina == null || MaquinasDaArea().Contains(p.Maquina) || !todas.Contains(p.Maquina));
        }
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var t = Q.Trim().ToUpper();
            q = q.Where(p => p.Codigo.Contains(t) || p.Descricao.ToUpper().Contains(t) || (p.Maquina != null && p.Maquina.ToUpper().Contains(t)));
        }
        return q.OrderBy(p => p.Codigo);
    }

    public async Task OnGetAsync()
    {
        Lista = await Filtrar().ToListAsync();
        if (Editar != null) Editando = await db.SpareParts.FirstOrDefaultAsync(p => p.Id == Editar && p.Ativa);
        if (PodeEditar) TiposDeMaquina = await db.Equipamentos.Where(e => CelulaId == null || e.CelulaId == CelulaId).Select(e => e.Nome).Distinct().OrderBy(n => n).ToListAsync();
    }

    // Quantidade digitada: o campo numérico manda com ponto ("1.5"); vírgula também vale. Vazio = null.
    static decimal? Numero(string? s) =>
        decimal.TryParse(s?.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;

    // Quadro do alto: sem id = peça nova; com id = altera a peça (código, descrição, equipamento e quantidade).
    public async Task<IActionResult> OnPostSalvarAsync(int? id, string? codigo, string? descricao, string? maquina, string? quantidade)
    {
        var cod = (codigo ?? "").Trim().ToUpper();
        var desc = (descricao ?? "").Trim().ToUpper();
        var maq = string.IsNullOrWhiteSpace(maquina) ? null : maquina.Trim().ToUpper();
        var qtd = Numero(quantidade);
        if (cod == "") { Erro("Preencha o código."); return RedirectToPage(new { Q, editar = id }); }
        if (!string.IsNullOrWhiteSpace(quantidade) && qtd == null) { Erro("Quantidade inválida."); return RedirectToPage(new { Q, editar = id }); }
        if (desc == "") { Erro("Preencha a descrição."); return RedirectToPage(new { Q, editar = id }); }

        SparePart p;
        var osAtualizadas = 0;
        if (id is int pid)
        {
            p = await db.SpareParts.FindAsync(pid) ?? throw new InvalidOperationException();
            var antes = (p.Codigo, p.Descricao, p.Maquina, p.Estoque);
            if (cod != p.Codigo)
            {
                // O código é único (inclusive das peças removidas, que voltam se o código for cadastrado de novo).
                if (await db.SpareParts.AnyAsync(x => x.Codigo == cod && x.Id != pid)) { Erro($"Já existe uma peça com o código {cod}."); return RedirectToPage(new { Q, editar = id }); }
                // As OS ligam a peça pelo código: troca nelas também, para o histórico de uso continuar nesta peça.
                foreach (var po in await db.Pecas.Where(x => x.Codigo == p.Codigo).ToListAsync()) { po.Codigo = cod; osAtualizadas++; }
                p.Codigo = cod;
            }
            p.Descricao = desc;
            p.Maquina = maq;
            if (qtd is decimal nova && nova != p.Estoque)
                estoque.Movimentar(p, TipoMovimento.Ajuste, nova - p.Estoque, "Quantidade alterada na tela Spare Parts");
            var mudou = Auditoria.Mudancas(("Código", antes.Codigo, p.Codigo), ("Descrição", antes.Descricao, p.Descricao),
                ("Equipamento", antes.Maquina, p.Maquina), ("Quantidade", antes.Estoque.Qtd(), p.Estoque.Qtd()));
            if (mudou != "") auditoria.Registrar("SparePart", p.Id, "Alterada", mudou);
            await db.SaveChangesAsync();
        }
        else
        {
            var existente = await db.SpareParts.FirstOrDefaultAsync(x => x.Codigo == cod);
            if (existente is { Ativa: true }) { Erro($"Já existe uma peça com o código {cod}."); return RedirectToPage(new { Q }); }
            // Código de uma peça removida (inativa): ela volta, com o histórico de uso que já tinha.
            p = existente ?? new SparePart { Codigo = cod, CriadaEm = DateTime.Now };
            p.Descricao = desc;
            p.Maquina = maq;
            p.Ativa = true;
            if (existente == null) db.SpareParts.Add(p);
            if (qtd is decimal inicial && inicial != p.Estoque)
                estoque.Movimentar(p, existente == null ? TipoMovimento.Entrada : TipoMovimento.Ajuste, inicial - p.Estoque, "Quantidade informada no cadastro");
            await db.SaveChangesAsync();
            auditoria.Registrar("SparePart", p.Id, existente == null ? "Criada" : "Reativada",
                $"{p.Codigo} · {p.Descricao}{(maq != null ? $" · {maq}" : "")} · quantidade {p.Estoque.Qtd()}");
            await db.SaveChangesAsync();
        }
        Ok(osAtualizadas == 0 ? $"Peça {p.Codigo} salva." : $"Peça {p.Codigo} salva. O código novo também foi trocado em {osAtualizadas} peça(s) lançada(s) em OS.");
        return RedirectToPage(new { Q });
    }

    public IActionResult OnGetModelo() => File(Excel.ModeloSpareParts(), Excel.Mime, "modelo-spare-parts.xlsx");

    // Importar Excel — código novo vira peça nova; código que já existe é atualizado (e volta a ficar ativo se tinha
    // sido removido). Equipamento e Quantidade são opcionais: sem a coluna, nada muda nelas; quantidade em branco
    // não mexe no estoque; quantidade diferente grava o movimento (entrada na peça nova, ajuste de inventário na que existe).
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
        var pecas = await db.SpareParts.ToDictionaryAsync(p => p.Codigo);
        var erros = new List<string>();
        var novas = new List<SparePart>();
        // Para o Histórico: como cada peça que já existia estava antes da planilha.
        var antes = new Dictionary<SparePart, (string Descricao, string? Maquina, decimal Estoque, bool Ativa)>();
        foreach (var l in linhas)
        {
            var codigo = l["Código"].ToUpper();
            var desc = l["Descrição"].ToUpper();
            if (codigo == "" || desc == "") { erros.Add($"Linha {l.Numero}: preencha código e descrição"); continue; }
            var qtdTexto = l.Tem("Quantidade") ? l["Quantidade"] : "";
            var qtd = Numero(qtdTexto);
            if (qtdTexto != "" && qtd == null) { erros.Add($"Linha {l.Numero} ({codigo}): quantidade \"{qtdTexto}\" inválida"); continue; }

            if (!pecas.TryGetValue(codigo, out var p))
            {
                p = new SparePart { Codigo = codigo, CriadaEm = DateTime.Now };
                db.SpareParts.Add(p);
                pecas[codigo] = p;
                novas.Add(p);
            }
            else if (!novas.Contains(p) && !antes.ContainsKey(p)) antes[p] = (p.Descricao, p.Maquina, p.Estoque, p.Ativa);
            p.Descricao = desc;
            if (l.Tem("Equipamento")) p.Maquina = l["Equipamento"] == "" ? null : l["Equipamento"].ToUpper();
            p.Ativa = true;
            if (qtd is decimal q && q != p.Estoque)
                estoque.Movimentar(p, novas.Contains(p) ? TipoMovimento.Entrada : TipoMovimento.Ajuste, q - p.Estoque, "Quantidade informada na importação do Excel");
        }
        await db.SaveChangesAsync();

        // Histórico: as criadas pela planilha e, nas que já existiam, só o que mudou (campo: antes → depois).
        foreach (var p in novas)
            auditoria.Registrar("SparePart", p.Id, "Criada pelo Excel", $"{p.Codigo} · {p.Descricao}{(p.Maquina != null ? $" · {p.Maquina}" : "")} · quantidade {p.Estoque.Qtd()}");
        foreach (var (p, a) in antes)
        {
            var mudou = Auditoria.Mudancas(("Descrição", a.Descricao, p.Descricao), ("Equipamento", a.Maquina, p.Maquina), ("Quantidade", a.Estoque.Qtd(), p.Estoque.Qtd()));
            if (!a.Ativa) auditoria.Registrar("SparePart", p.Id, "Reativada pelo Excel", mudou == "" ? $"{p.Codigo} · {p.Descricao}" : mudou);
            else if (mudou != "") auditoria.Registrar("SparePart", p.Id, "Alterada pelo Excel", mudou);
        }
        await db.SaveChangesAsync();
        Resumo($"{novas.Count} peça(s) criada(s) e {antes.Count} atualizada(s) pelo Excel.", erros);
        return RedirectToPage(new { Q });
    }

    // Extração — as peças da lista (com a quantidade) e todos os movimentos delas (usos em OS, ajustes, devoluções).
    public async Task<IActionResult> OnGetExtracaoAsync()
    {
        Lista = await Filtrar().ToListAsync();
        var ids = Lista.Select(p => p.Id).ToList();
        var movimentos = await db.MovimentosPeca.Include(m => m.SparePart).Include(m => m.OrdemServico).Include(m => m.Equipamento)
            .Where(m => ids.Contains(m.SparePartId)).OrderByDescending(m => m.Quando).ToListAsync();
        var bytes = Excel.Planilha(
            ("Peças", ["Código", "Descrição", "Equipamento", "Quantidade"], Lista.Select(p => new object?[] { p.Codigo, p.Descricao, p.Maquina, p.Estoque })),
            ("Movimentos", ["Data", "Código", "Descrição", "Movimento", "Quantidade", "Saldo", "OS", "Equipamento", "Quem", "Observação"],
                movimentos.Select(m => new object?[]
                {
                    m.Quando, m.SparePart?.Codigo, m.SparePart?.Descricao, m.Tipo.Nome(), m.Quantidade, m.Saldo,
                    m.OrdemServico?.Numero, m.Equipamento?.Descricao, m.Usuario, m.Observacao,
                })));
        return File(bytes, Excel.Mime, $"spare-parts-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    // Remover selecionados — peça nunca usada é apagada; já usada em OS fica inativa (some da lista e das sugestões da
    // OS, mas o histórico fica; cadastrar o mesmo código de novo traz ela de volta).
    public async Task<IActionResult> OnPostRemoverAsync(int[] ids)
    {
        var lista = await db.SpareParts.Where(p => ids.Contains(p.Id)).ToListAsync();
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
        return RedirectToPage(new { Q });
    }
}
