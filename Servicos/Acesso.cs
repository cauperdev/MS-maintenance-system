// ============================================================================
// REGRA 07 — ACESSO PELO GRUPO: o que cada grupo (Visitante, Auditor, Técnico, Coordenador, Administrador)
// vê e edita. As permissões em uso ficam no banco e são trocadas na tela Grupos; aqui fica o PADRÃO de fábrica.
// ONDE MEXER:
//   - Tela nova no controle de acesso .......... lista Modulos (chave, nome, página inicial, ajuda)
//   - Padrão de fábrica ("Restaurar padrão") ... PadraoVer / PadraoEditar
//   - Tela que abre ao entrar no sistema ....... PrioridadeInicial
//   - Quem só vê finalizações aprovadas ........ SoAprovadas
// Mudar o padrão aqui só vale para bancos novos ou ao apertar "Restaurar padrão" na tela Grupos.
// ============================================================================
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;

namespace MS.Servicos;

// Regra 07 — o que cada pessoa vê depende do grupo.
// As permissões ficam no banco (tela Grupos) e são mantidas em memória para consulta rápida.
public static class Acesso
{
    const Grupo V = Grupo.Visitante, A = Grupo.Auditor, T = Grupo.Tecnico, C = Grupo.Coordenador, ADM = Grupo.Administrador, E = Grupo.Ehs;

    // TemVer/TemEditar: quais caixas fazem sentido para a tela. Pagina: destino ao entrar no sistema.
    public record Modulo(string Chave, string Nome, bool TemVer, bool TemEditar, string? Pagina, string Ajuda = "");

    public static readonly Modulo[] Modulos =
    {
        new("dashboard", "Dashboard", true, true, "/Index", "Editar = exportar os indicadores em Excel"),
        new("grupos", "Grupos", true, true, "/Grupos/Index", "Editar = alterar as permissões dos grupos"),
        new("usuarios", "Usuários", true, true, "/Usuarios/Index"),
        new("celulas", "Áreas", true, true, "/Celulas/Index"),
        new("equipamentos", "Equipamentos", true, true, "/Equipamentos/Index"),
        new("planos", "Cronograma", true, true, "/Cronograma/Index"),
        new("planos_ehs", "Cronograma — tarefas de NR (EHS)", true, true, null, "Editar = criar, alterar e remover as tarefas de NR do plano"),
        new("ordens", "Ordens de serviço", true, true, "/Ordens/Index", "Editar = executar a OS (checklist, peças, fotos, concluir)"),
        new("ordens_gestao", "Ordens de serviço — gestão", true, true, null, "Ver = extração em Excel · Editar = remover OS"),
        new("finalizacao", "Finalização", true, true, "/Finalizacao/Index", "Editar = aprovar ou retornar conclusões"),
        new("spareparts", "Spare Parts", true, true, "/SpareParts/Index", "Editar = cadastrar e remover peças"),
        new("calendario", "Calendário", true, true, "/Calendario/Index"),
        new("jms", "Histórico JMS", true, true, "/HistoricoJms/Index", "Editar = importar e remover Work Orders do JMS antigo"),
        new("manual", "Manual", true, true, "/Manual/Index", "Editar = enviar o slide do fluxo do sistema"),
    };

    // Padrão de fábrica (usado na primeira execução e no botão "Restaurar padrão").
    static readonly Dictionary<string, Grupo[]> PadraoVer = new()
    {
        ["dashboard"] = [V, A, T, C, ADM, E],
        ["grupos"] = [ADM],
        ["usuarios"] = [C, ADM],
        ["celulas"] = [C, ADM],
        ["equipamentos"] = [V, A, T, C, ADM, E],
        ["spareparts"] = [A, T, C, ADM],
        ["planos"] = [A, T, C, ADM, E],
        ["planos_ehs"] = [ADM, E],
        ["ordens"] = [V, A, T, C, ADM, E],
        ["ordens_gestao"] = [A, T, C, ADM],
        ["finalizacao"] = [V, A, C, ADM, E], // Visitante (verificação/OBA), Auditor e EHS só veem; aprovar/retornar é do Editar
        ["calendario"] = [T, C, ADM],
        ["jms"] = [V, A, T, C, ADM],
        ["manual"] = [V, A, T, C, ADM, E],
    };

    static readonly Dictionary<string, Grupo[]> PadraoEditar = new()
    {
        ["dashboard"] = [T, C, ADM],
        ["grupos"] = [ADM],
        ["usuarios"] = [C, ADM],
        ["celulas"] = [C, ADM],
        ["equipamentos"] = [T, C, ADM],
        ["spareparts"] = [T, C, ADM],
        ["planos"] = [T, C, ADM],
        ["planos_ehs"] = [ADM, E],      // só o EHS (e o administrador) mexe nas tarefas de NR; os outros só marcam na OS
        ["ordens"] = [T, C, ADM],
        ["ordens_gestao"] = [T, C, ADM],
        ["finalizacao"] = [C, ADM],
        ["calendario"] = [C, ADM],
        ["jms"] = [C, ADM],             // só consulta para os outros; importar/remover é do coordenador
        ["manual"] = [ADM],             // todos leem; trocar o slide do fluxo é do administrador
    };

    public static (bool Ver, bool Editar) Padrao(Grupo g, string modulo) =>
        (PadraoVer.GetValueOrDefault(modulo)?.Contains(g) == true, PadraoEditar.GetValueOrDefault(modulo)?.Contains(g) == true);

    static volatile Dictionary<(Grupo, string), (bool Ver, bool Editar)> atual =
        Enum.GetValues<Grupo>().SelectMany(g => Modulos.Select(m => (g, m.Chave)))
            .ToDictionary(k => k, k => Padrao(k.g, k.Chave));

    // Administrador nunca perde o acesso a Grupos — senão ninguém conseguiria desfazer.
    public static bool Travado(Grupo g, string modulo) => g == ADM && modulo == "grupos";

    public static bool PodeVer(Grupo g, string modulo) =>
        Travado(g, modulo) || atual.TryGetValue((g, modulo), out var p) && p.Ver;

    public static bool PodeEditar(Grupo g, string modulo) =>
        Travado(g, modulo) || atual.TryGetValue((g, modulo), out var p) && p.Editar;

    // Cria a tabela se o banco for de uma versão anterior, grava o padrão na primeira vez e carrega.
    public static void Carregar(AppDb db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "Permissoes" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Permissoes" PRIMARY KEY AUTOINCREMENT,
                "Grupo" INTEGER NOT NULL, "Modulo" TEXT NOT NULL, "Ver" INTEGER NOT NULL, "Editar" INTEGER NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Permissoes_Grupo_Modulo" ON "Permissoes" ("Grupo", "Modulo");
            """);
        var existentes = db.Permissoes.ToList();
        foreach (var g in Enum.GetValues<Grupo>())
            foreach (var m in Modulos.Where(m => !existentes.Any(p => p.Grupo == g && p.Modulo == m.Chave)))
            {
                var (ver, editar) = Padrao(g, m.Chave);
                db.Permissoes.Add(new PermissaoGrupo { Grupo = g, Modulo = m.Chave, Ver = ver, Editar = editar });
            }
        // Editar inclui Ver (linhas antigas de telas que não tinham a caixinha Ver ficaram com Ver desmarcado).
        foreach (var p in existentes.Where(p => p.Editar && !p.Ver)) p.Ver = true;
        db.SaveChanges();
        Recarregar(db);
    }

    public static void Recarregar(AppDb db) =>
        atual = db.Permissoes.AsNoTracking().ToList().ToDictionary(p => (p.Grupo, p.Modulo), p => (p.Ver, p.Editar));

    // Visitante e Auditor só veem as finalizações aprovadas.
    public static bool SoAprovadas(Grupo g) => g is V or A;

    // Tela ao entrar: a primeira desta lista que o grupo pode ver.
    static readonly string[] PrioridadeInicial =
        ["dashboard", "ordens", "finalizacao", "calendario", "planos", "equipamentos", "usuarios", "celulas", "grupos"];

    public static string PaginaInicial(Grupo g) =>
        PrioridadeInicial.Select(k => Modulos.First(m => m.Chave == k))
            .FirstOrDefault(m => PodeVer(g, m.Chave))?.Pagina ?? "/Conta/Negado";

    public static string Descricao(Grupo g)
    {
        var ver = Modulos.Count(m => m.TemVer && PodeVer(g, m.Chave));
        var editar = Modulos.Count(m => m.TemEditar && PodeEditar(g, m.Chave));
        return ver == 0 ? "Sem acesso" : $"Vê {ver} tela(s) · edita {editar}";
    }
}
