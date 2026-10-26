// ============================================================================
// TELA: HISTÓRICO de um registro (lógica)  ·  endereço /Historico?entidade=...&id=...
// Mostra quem mudou o quê e quando (gravado por Servicos/Auditoria.cs). Vale a permissão da tela do registro.
// ONDE MEXER: tipos de registro com histórico → lista Entidades
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Servicos;

namespace MS.Pages.Historico;

public class IndexModel(AppDb db) : MsPage
{
    static readonly Dictionary<string, (string Modulo, string Titulo)> Entidades = new()
    {
        ["Celula"] = ("celulas", "Áreas"),
        ["Usuario"] = ("usuarios", "Usuários"),
        ["Equipamento"] = ("equipamentos", "Equipamentos"),
        ["Plano"] = ("planos", "Cronograma"),
        ["OrdemServico"] = ("ordens", "Ordens de serviço"),
        ["Finalizacao"] = ("finalizacao", "Finalização"),
        ["SparePart"] = ("spareparts", "Spare Parts"),
        ["Grupo"] = ("grupos", "Grupos"),
    };

    [BindProperty(SupportsGet = true)] public string Entidade { get; set; } = "";
    [BindProperty(SupportsGet = true)] public int Id { get; set; }

    protected override string Modulo => Entidades.TryGetValue(Entidade, out var e) ? e.Modulo : "";
    public string Titulo => Entidades.TryGetValue(Entidade, out var e) ? e.Titulo : "";
    public List<Modelos.Historico> Lista { get; set; } = new();

    public string Detalhes(Modelos.Historico h) => h.Detalhes;

    public async Task OnGetAsync() =>
        Lista = await db.Historicos.Where(h => h.Entidade == Entidade && h.EntidadeId == Id)
            .OrderByDescending(h => h.Quando).ToListAsync();
}
