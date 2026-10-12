// ============================================================================
// TELA: MANUAL (lógica)  ·  endereço /Manual  ·  visual em Index.cshtml (texto do manual em _Conteudo.cshtml)
// Duas abas: "Fluxo do sistema" (slide enviado pelo administrador, mostrado slide a slide) e "Manual de uso"
// (como usar cada tela, com todas as regras). Todos os grupos veem; enviar o slide é do Editar (tela Grupos).
// ONDE MEXER:
//   - Enviar / trocar o slide ............ OnPostEnviarAsync (conversão e arquivos: Servicos/Manual.cs)
//   - Imagem de cada slide ............... OnGetSlide
//   - Baixar o arquivo enviado ........... OnGetOriginal (ver=true abre o PDF na tela)
//   - Texto do manual .................... _Conteudo.cshtml (regras que mudam com o sistema vêm do próprio código)
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using MS.BancoDeDados;
using MS.Servicos;

namespace MS.Pages.Manual;

// PowerPoint com muitas imagens passa fácil dos 30 MB padrão de um envio.
[RequestSizeLimit(60_000_000)]
[RequestFormLimits(MultipartBodyLengthLimit = 60_000_000)]
public class IndexModel(AppDb db, Servicos.Manual manual, Auditoria auditoria) : MsPage
{
    protected override string Modulo => "manual";

    [BindProperty(SupportsGet = true)] public string? Aba { get; set; }   // "manual" abre direto na aba Manual de uso

    public List<string> Slides { get; set; } = new();
    public (string Caminho, string Nome)? Original { get; set; }
    public long Versao { get; set; }
    public bool EhPdf => Original is { } o && Path.GetExtension(o.Caminho).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public void OnGet()
    {
        Slides = manual.Slides();
        Original = manual.Original();
        Versao = manual.Versao();
    }

    public IActionResult OnGetSlide(int n) =>
        manual.CaminhoSlide(n) is string c ? PhysicalFile(c, Tipo(c)) : NotFound();

    public IActionResult OnGetOriginal(bool ver = false)
    {
        if (manual.Original() is not { } o) return NotFound();
        return ver ? PhysicalFile(o.Caminho, Tipo(o.Caminho)) : PhysicalFile(o.Caminho, Tipo(o.Caminho), o.Nome);
    }

    public async Task<IActionResult> OnPostEnviarAsync(IFormFile? arquivo)
    {
        if (arquivo == null || arquivo.Length == 0) { Erro("Escolha o arquivo do slide."); return RedirectToPage(); }
        if (await manual.TrocarAsync(arquivo) is string erro) { Erro(erro); return RedirectToPage(); }
        var slides = manual.Slides().Count;
        auditoria.Registrar("Manual", 0, "Slide do fluxo trocado", $"{Path.GetFileName(arquivo.FileName)}{(slides > 0 ? $" · {slides} slide(s)" : "")}");
        await db.SaveChangesAsync();
        Ok(slides > 0 ? $"Slide do fluxo atualizado: {slides} slide(s)." : "Slide do fluxo atualizado.");
        return RedirectToPage();
    }

    static string Tipo(string caminho) =>
        new FileExtensionContentTypeProvider().TryGetContentType(caminho, out var t) ? t : "application/octet-stream";
}
