// ============================================================================
// TELAS DE UMA FINALIZAÇÃO (lógica) — três telas usam este arquivo:
//   - VerModel ....... Visualização (só leitura) ...... /Finalizacao/Ver/{id}      · visual em Ver.cshtml
//   - ImprimirModel .. Folha para imprimir ............ /Finalizacao/Imprimir/{id} · visual em Imprimir.cshtml
//   - EditarModel .... Revisão (aprovar / retornar) ... /Finalizacao/Editar/{id}   · visual em Editar.cshtml
// O conteúdo da ficha (campos, checklist, peças, fotos) está em _FinDetalhe.cshtml.
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Finalizacao;

// Visualização / Imprimir / Edit (status da revisão) compartilham o carregamento.
public class VerModel(AppDb db) : MsPage
{
    protected override string Modulo => "finalizacao";
    protected AppDb Db => db;
    public Modelos.Finalizacao Fin { get; set; } = new();

    protected async Task<Modelos.Finalizacao?> Buscar(int id)
    {
        var f = await db.Finalizacoes
            .Include(x => x.Equipamento).Include(x => x.Tecnico).Include(x => x.Coordenador)
            .Include(x => x.AprovadaPor).Include(x => x.Imagens)
            .FirstOrDefaultAsync(x => x.Id == id);
        // Visitante e Auditor só veem as aprovadas.
        if (f != null && Acesso.SoAprovadas(Grupo) && f.StatusRevisao != StatusRevisao.Aprovado) return null;
        return f;
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var f = await Buscar(id);
        if (f == null) return NotFound();
        Fin = f;
        return Page();
    }
}

public class ImprimirModel(AppDb db) : VerModel(db);

public class EditarModel(AppDb db, Regras regras, Auditoria auditoria) : VerModel(db)
{
    [BindProperty] public StatusRevisao StatusRevisao { get; set; }
    [BindProperty] public string? Motivo { get; set; }   // motivo do retorno (obrigatório para Retornado)

    // Status da revisão — aprova (ou devolve) uma conclusão por vez; depois clique em Salvar.
    public async Task<IActionResult> OnPostAsync(int id, string acao)
    {
        var f = await Buscar(id);
        if (f == null) return NotFound();
        if (StatusRevisao == StatusRevisao.Retornado)
        {
            if (string.IsNullOrWhiteSpace(Motivo)) { Erro("Para retornar, escreva o motivo do retorno."); return RedirectToPage("Editar", new { id }); }
            // Retornada sai da Finalização: a OS volta para Ordens de serviço com a data daquele ciclo e o motivo.
            await regras.RetornarAsync(f, Motivo.Trim(), User.Identity?.Name ?? "");
            await Db.SaveChangesAsync();
            Ok($"{f.NumeroOS} retornada: saiu da Finalização e voltou para Ordens de serviço com a data daquele ciclo.");
            return RedirectToPage("Index");
        }
        if (StatusRevisao != f.StatusRevisao)
        {
            switch (StatusRevisao)
            {
                case StatusRevisao.Aprovado: regras.Aprovar(f, UsuarioId); break;
                default:
                    f.StatusRevisao = StatusRevisao.Aguardando;
                    f.AprovadaEm = null;
                    f.AprovadaPorId = null;
                    auditoria.Registrar("Finalizacao", f.Id, "Alterado", "Status da revisão voltou para Aguardando revisão");
                    break;
            }
            await Db.SaveChangesAsync();
        }
        Ok($"Revisão da {f.NumeroOS} salva: {f.StatusRevisao.Nome()}.");
        return acao == "continuar" ? RedirectToPage("Editar", new { id }) : RedirectToPage("Index");
    }
}
