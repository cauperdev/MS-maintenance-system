// ============================================================================
// HISTÓRICO — grava quem mudou o quê e quando (aparece na tela Histórico de cada registro).
// Uso: auditoria.Registrar("Equipamento", id, "Alterado", "detalhe").
// Alteração: o detalhe traz só os campos que mudaram, de quanto para quanto (Auditoria.Mudancas):
//   "Localização: SMT LINE 02 → LINHA JUTAÍ"
// ============================================================================
using MS.BancoDeDados;
using MS.Modelos;

namespace MS.Servicos;

// Botão "Histórico" — mostra quem mudou o quê e quando.
public class Auditoria(AppDb db, IHttpContextAccessor http)
{
    // Só os campos que mudaram, um por linha: "Campo: antes → depois". Nada mudou: "".
    // Uso: Auditoria.Mudancas(("Localização", antes.Localizacao, e.Localizacao), ("Status", ..., ...))
    public static string Mudancas(params (string Campo, object? Antes, object? Depois)[] campos) =>
        string.Join("\n", campos
            .Select(c => (c.Campo, Antes: Texto(c.Antes), Depois: Texto(c.Depois)))
            .Where(c => c.Antes != c.Depois)
            .Select(c => $"{c.Campo}: {c.Antes} → {c.Depois}"));

    static string Texto(object? v) => v switch
    {
        null => "—",
        string s => string.IsNullOrWhiteSpace(s) ? "—" : s.Trim(),
        bool b => b ? "Sim" : "Não",
        DateTime d => d.TimeOfDay == TimeSpan.Zero ? d.ToString("dd/MM/yyyy") : d.ToString("dd/MM/yyyy HH:mm"),
        decimal m => m.ToString("0.##"),
        _ => v.ToString() ?? "—",
    };

    public void Registrar(string entidade, int id, string acao, string detalhes = "")
    {
        db.Historicos.Add(new Historico
        {
            Entidade = entidade,
            EntidadeId = id,
            Acao = acao,
            Detalhes = detalhes,
            Usuario = http.HttpContext?.User.Identity?.Name ?? "sistema",
            Quando = DateTime.Now,
        });
    }
}
