// ============================================================================
// FOTO — entrega a imagem de uma OS/finalização (endereço /Foto/{id}) só para quem tem permissão.
// As fotos ficam em dados/fotos (fora do wwwroot, para não abrirem sem login). Regras de tamanho: Servicos/Fotos.cs.
// ============================================================================
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages;

// Entrega as fotos respeitando o grupo de quem pede (ficam fora do wwwroot).
public class FotoModel(AppDb db, Fotos fotos) : PageModel
{
    public async Task<IActionResult> OnGetAsync(int id)
    {
        var grupo = Enum.TryParse<Grupo>(User.FindFirstValue(ClaimTypes.Role), out var g) ? g : Grupo.Visitante;
        var img = await db.Imagens.FindAsync(id);
        if (img == null) return NotFound();

        bool permitido;
        if (img.OrdemServicoId != null) permitido = Acesso.PodeVer(grupo, "ordens");
        else
        {
            var fin = await db.Finalizacoes.FindAsync(img.FinalizacaoId);
            permitido = fin != null && Acesso.PodeVer(grupo, "finalizacao") &&
                        (!Acesso.SoAprovadas(grupo) || fin.StatusRevisao == StatusRevisao.Aprovado);
        }
        if (!permitido) return Forbid();

        var caminho = fotos.Caminho(img.Arquivo);
        if (!System.IO.File.Exists(caminho)) return NotFound();
        Response.Headers.CacheControl = "private, max-age=86400";
        return PhysicalFile(caminho, "image/jpeg");
    }
}
