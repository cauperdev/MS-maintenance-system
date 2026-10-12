// ============================================================================
// BASE DE TODAS AS TELAS — toda página do sistema herda daqui (": MsPage").
//   - Bloqueia quem não tem permissão (Regra 07) ....... OnPageHandlerExecuting
//   - Mensagem verde / vermelha no topo da tela ........ Ok("...") / Erro("...")
//   - Aviso amarelo em lista (ex.: resumo da importação) Avisar / Resumo (desenho em Shared/_Aviso.cshtml)
//   - Ler a planilha enviada em "Importar Excel" ....... LerPlanilha / LerArquivo
//   - Quem está logado ................................. Grupo, UsuarioId, PodeEditar, Ve("modulo"), Pode("modulo")
//   - Filtro de área = área escolhida nos cards ........ OnPageHandlerExecutionAsync (telas com ": IFiltroArea")
// ============================================================================
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MS.Modelos;

namespace MS.Servicos;

// Aviso amarelo em lista (ex.: resumo de importação): título + grupos de itens.
public record GrupoAviso(string Titulo, List<string> Itens);
public record Aviso(string Titulo, List<GrupoAviso> Grupos, string? Dica = null);

// Base das páginas do sistema: aplica a Regra 07 (acesso pelo grupo).
public abstract class MsPage : PageModel
{
    protected abstract string Modulo { get; }

    public Grupo Grupo => Enum.TryParse<Grupo>(User.FindFirstValue(ClaimTypes.Role), out var g) ? g : Grupo.Visitante;
    public int UsuarioId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    public bool PodeEditar => Acesso.PodeEditar(Grupo, Modulo);
    public bool Pode(string modulo) => Acesso.PodeEditar(Grupo, modulo);
    public bool Ve(string modulo) => Acesso.PodeVer(Grupo, modulo);

    // Área escolhida nos cards (Servicos/AreaEscolhida.cs): é o filtro de área da tela, sempre (as telas não têm mais
    // o campo "Área"; o que vier no endereço é ignorado). Sem área escolhida, a tela mostra todas.
    public override Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext ctx, PageHandlerExecutionDelegate next)
    {
        if (this is IFiltroArea tela) tela.CelulaId = AreaEscolhida.Ler(HttpContext);
        return base.OnPageHandlerExecutionAsync(ctx, next);
    }

    public override void OnPageHandlerExecuting(PageHandlerExecutingContext ctx)
    {
        if (!Acesso.PodeVer(Grupo, Modulo))
        {
            ctx.Result = RedirectToPage("/Conta/Negado");
            return;
        }
        if (HttpMethods.IsPost(Request.Method) && !PodeEditar)
            ctx.Result = RedirectToPage("/Conta/Negado");
    }

    protected void Ok(string msg) => TempData["ok"] = msg;
    protected void Erro(string msg) => TempData["erro"] = msg;

    // Importar Excel: lê a planilha enviada; em caso de problema já deixa a mensagem e devolve null.
    protected List<Excel.LinhaPlanilha>? LerPlanilha(IFormFile? arquivo) => LerArquivo(arquivo, Excel.LerTabela);

    protected T? LerArquivo<T>(IFormFile? arquivo, Func<Stream, T> ler) where T : class
    {
        if (arquivo == null || arquivo.Length == 0) { Erro("Escolha a planilha preenchida."); return null; }
        try
        {
            using var s = arquivo.OpenReadStream();
            return ler(s);
        }
        catch (Exception)
        {
            Erro("Não foi possível ler o arquivo. Use a planilha do botão \"Baixar modelo\" (.xlsx).");
            return null;
        }
    }

    // O aviso viaja num cookie até a próxima tela: cada grupo mostra no máximo 40 itens (o resto vira "… e mais N").
    protected void Avisar(Aviso aviso)
    {
        const int max = 40;
        var grupos = aviso.Grupos.Select(g => g.Itens.Count <= max ? g
            : g with { Itens = [.. g.Itens.Take(max), $"… e mais {g.Itens.Count - max}"] }).ToList();
        TempData["aviso"] = JsonSerializer.Serialize(aviso with { Grupos = grupos });
    }

    // Mensagem final da importação: sucesso, ou sucesso + linhas com problema.
    protected void Resumo(string msg, List<string> erros)
    {
        if (erros.Count == 0) Ok(msg);
        else Avisar(new(msg, [new($"{erros.Count} linha(s) com problema:", erros)]));
    }
}
