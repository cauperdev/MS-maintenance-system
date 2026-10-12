// ============================================================================
// TELA: CÉLULAS (lógica)  ·  endereço /Celulas  ·  visual em Index.cshtml  ·  permissão "celulas"
// Cadastro simples: adicionar, renomear (clicando na célula), logo do card (tela "Escolha a área") e remover as marcadas.
// ONDE MEXER:
//   - Lista ............................... OnGetAsync
//   - Adicionar / salvar nome e logo ...... OnPostSalvarAsync (arquivo da logo: Servicos/LogosArea.cs)
//   - Remover marcadas .................... OnPostRemoverAsync (usuários e equipamentos ficam sem célula)
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Celulas;

public class IndexModel(AppDb db, Auditoria auditoria, LogosArea logos) : MsPage
{
    protected override string Modulo => "celulas";

    public record Linha(int Id, string Nome, int Usuarios, int Equipamentos, long? Logo = null);
    public List<Linha> Lista { get; set; } = new();
    public int EditandoId { get; set; }
    public string EditandoNome { get; set; } = "";
    public bool EditandoTemLogo { get; set; }
    public string[] TiposLogo => LogosArea.Extensoes;

    public async Task OnGetAsync(int? editar)
    {
        Lista = await db.Celulas.OrderBy(c => c.Nome)
            .Select(c => new Linha(c.Id, c.Nome, db.Usuarios.Count(u => u.CelulaId == c.Id), db.Equipamentos.Count(e => e.CelulaId == c.Id)))
            .ToListAsync();
        Lista = Lista.Select(l => l with { Logo = logos.Versao(l.Id) }).ToList();
        if (editar is int id && await db.Celulas.FindAsync(id) is Celula c)
        {
            EditandoId = c.Id;
            EditandoNome = c.Nome;
            EditandoTemLogo = logos.Caminho(c.Id) != null;
        }
    }

    public async Task<IActionResult> OnPostSalvarAsync(int id, string nome, IFormFile? logo, bool removerLogo)
    {
        nome = (nome ?? "").Trim().ToUpper(); // nome da área sempre em maiúscula (como SAMSUNG, AVELL)
        if (nome == "") return RedirectToPage();
        var c = id > 0 ? await db.Celulas.FindAsync(id) : null;
        var nomeAntes = c?.Nome;
        if (c == null) { c = new Celula(); db.Celulas.Add(c); }
        c.Nome = nome;
        await db.SaveChangesAsync();
        if (nomeAntes == null) auditoria.Registrar("Celula", c.Id, "Criado", nome);
        else if (Auditoria.Mudancas(("Nome", nomeAntes, nome)) is { Length: > 0 } mudou) auditoria.Registrar("Celula", c.Id, "Alterado", mudou);
        if (logo is { Length: > 0 })
        {
            if (await logos.SalvarAsync(c.Id, logo) is string recusa) { await db.SaveChangesAsync(); Erro(recusa); return RedirectToPage(new { editar = c.Id }); }
            auditoria.Registrar("Celula", c.Id, "Alterado", $"Logo trocada · {nome}");
        }
        else if (removerLogo && logos.Caminho(c.Id) != null)
        {
            logos.Apagar(c.Id);
            auditoria.Registrar("Celula", c.Id, "Alterado", $"Logo removida · {nome}");
        }
        await db.SaveChangesAsync();
        Ok($"Área {nome} salva.");
        return RedirectToPage();
    }

    // Remove as células marcadas; usuários e equipamentos delas ficam sem célula.
    public async Task<IActionResult> OnPostRemoverAsync(int[] ids)
    {
        var lista = await db.Celulas.Where(c => ids.Contains(c.Id)).ToListAsync();
        if (lista.Count == 0) return RedirectToPage();
        var idsValidos = lista.Select(c => c.Id).ToList();
        await db.Usuarios.Where(u => u.CelulaId != null && idsValidos.Contains(u.CelulaId.Value)).ExecuteUpdateAsync(s => s.SetProperty(u => u.CelulaId, (int?)null));
        await db.Equipamentos.Where(e => e.CelulaId != null && idsValidos.Contains(e.CelulaId.Value)).ExecuteUpdateAsync(s => s.SetProperty(e => e.CelulaId, (int?)null));
        foreach (var c in lista) { auditoria.Registrar("Celula", c.Id, "Removido", c.Nome); logos.Apagar(c.Id); }
        db.Celulas.RemoveRange(lista);
        await db.SaveChangesAsync();
        Ok(lista.Count == 1 ? $"Área {lista[0].Nome} removida." : $"{lista.Count} áreas removidas.");
        return RedirectToPage();
    }
}
