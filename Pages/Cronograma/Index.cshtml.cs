// ============================================================================
// TELA: CRONOGRAMA — lista de planos (lógica)  ·  endereço /Cronograma  ·  visual em Index.cshtml  ·  permissão "planos"
// Cada plano (equipamento + nome + periodicidade) gera e mantém a sua OS (Regra 01).
// ONDE MEXER:
//   - Filtros e ordem da lista ............ Filtrar / OnGetAsync
//   - Itens por página (50) ............... PorPagina (páginas como a lista de OS, pedido do usuário em 08/10/2026)
//   - Planilha do botão Extração .......... OnGetExtracaoAsync (aba do cronograma + aba TASKS; todas as páginas)
//   - Planilha do Baixar modelo ........... OnGetModelo (colunas em Servicos/Excel.cs → ColunasPlano)
//   - Importar Excel ...................... OnPostImportarAsync (cria/atualiza planos, tarefas e OS; resumo em aviso)
//                                           · técnico e coordenador pelo nome completo ou matrícula (Pessoa / NomeSimples)
//   - Remover selecionados ................ OnPostRemoverAsync (apaga também as OS dos planos; com "marcar todos",
//                                           todos os planos da lista filtrada, de todas as páginas)
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Cronograma;

public class IndexModel(AppDb db, Regras regras, Auditoria auditoria) : MsPage, IFiltroArea
{
    protected override string Modulo => "planos";
    const int PorPagina = 50;

    [BindProperty(SupportsGet = true)] public string? Local { get; set; }
    [BindProperty(SupportsGet = true)] public int? CelulaId { get; set; }
    [BindProperty(SupportsGet = true)] public Periodicidade? Periodicidade { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;

    public List<string> Localizacoes { get; set; } = new();
    public List<Celula> Celulas { get; set; } = new();
    public List<PlanoManutencao> Lista { get; set; } = new();
    public int Total { get; set; }
    public int Paginas => Math.Max(1, (int)Math.Ceiling(Total / (double)PorPagina));
    // OS de cada plano (cartões do celular/tablet: cor da etiqueta, data da manutenção e "Ver OS").
    public Dictionary<int, OrdemServico> OsDoPlano { get; set; } = new();
    public DateTime Agora { get; } = DateTime.Now;

    IQueryable<PlanoManutencao> Filtrar()
    {
        var q = db.Planos.Include(p => p.Equipamento).Include(p => p.Tarefas).Include(p => p.Tecnico).Include(p => p.Coordenador).AsQueryable();
        if (!string.IsNullOrEmpty(Local)) q = q.Where(p => p.Equipamento!.Localizacao == Local);
        if (CelulaId != null) q = q.Where(p => p.Equipamento!.CelulaId == CelulaId);
        if (Periodicidade != null) q = q.Where(p => p.Periodicidade == Periodicidade);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var t = Q.Trim().ToUpperInvariant();   // texto gravado em maiúscula
            q = q.Where(p => p.OrdemTrabalho.Contains(t) || p.Nome.Contains(t) || p.Equipamento!.Codigo.Contains(t));
        }
        return q.OrderBy(p => p.Equipamento!.Codigo).ThenBy(p => p.Periodicidade);
    }

    public async Task OnGetAsync()
    {
        Localizacoes = await db.Equipamentos.Where(e => CelulaId == null || e.CelulaId == CelulaId)   // só as da área escolhida
            .Select(e => e.Localizacao).Distinct().OrderBy(x => x).ToListAsync();
        Celulas = await db.Celulas.OrderBy(c => c.Nome).ToListAsync();
        var q = Filtrar();
        Total = await q.CountAsync();
        Pagina = Math.Clamp(Pagina, 1, Paginas);
        Lista = await q.Skip((Pagina - 1) * PorPagina).Take(PorPagina).ToListAsync();
        var planos = Lista.Select(p => p.Id).ToList();   // só as OS dos planos desta página
        OsDoPlano = (await db.Ordens.Where(o => o.PlanoId != null && planos.Contains(o.PlanoId.Value))
                .Select(o => new OrdemServico { Id = o.Id, PlanoId = o.PlanoId, DataConclusao = o.DataConclusao, Status = o.Status })
                .ToListAsync())
            .GroupBy(o => o.PlanoId!.Value).ToDictionary(g => g.Key, g => g.OrderBy(o => o.Id).First());
    }

    // Extração — as 4 primeiras colunas são as do modelo e as atividades vão na aba TASKS (pode editar e importar de volta).
    public async Task<IActionResult> OnGetExtracaoAsync()
    {
        var lista = await Filtrar().ToListAsync();
        var bytes = Excel.PlanilhaCronograma(("Cronograma",
            // Colunas da tela primeiro; depois as que a importação usa (achadas pelo nome, a ordem não importa).
            ["Ordem de serviço", "Nome do plano", "Prédio", "Equipamento", "Localização", "Periodicidade",
             "Código do equipamento", "Descrição do serviço", "Lembrar antes do prazo (dias)", "Técnico responsável", "Coordenador responsável"],
            lista.Select(p => new object?[]
            {
                p.OrdemTrabalho, p.Nome, p.Equipamento?.Predio, p.Equipamento?.Descricao, p.Equipamento?.Localizacao, p.Periodicidade.Nome(),
                p.Equipamento?.Codigo, p.Descricao, p.LembreteDias, p.Tecnico?.NomeCompleto, p.Coordenador?.NomeCompleto,
            })),
            // Um bloco por máquina + periodicidade (com as atividades do primeiro plano de cada grupo).
            lista.Where(p => p.Equipamento != null && p.Tarefas.Count > 0)
                .GroupBy(p => Excel.TituloTask(p.Equipamento!.Nome, p.Periodicidade))
                .Select(g => (g.Key, g.First().Tarefas.OrderBy(t => t.Ordem).Select(t => t.Descricao))));
        return File(bytes, Excel.Mime, $"cronograma-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    public IActionResult OnGetModelo() => File(Excel.ModeloPlanos(), Excel.Mime, "modelo-cronograma.xlsx");

    // Importar Excel — mesmo equipamento + nome + periodicidade atualiza o plano; senão cria (com SM e OS, Regra 01).
    // As atividades vêm da aba TASKS: plano Semanal da ADESIVADORA usa o bloco "ADESIVADORA - SEMANAL".
    public async Task<IActionResult> OnPostImportarAsync(IFormFile? arquivo)
    {
        var planilha = LerArquivo(arquivo, Excel.LerCronograma);
        if (planilha == null) return RedirectToPage();
        var (linhas, tasks) = (planilha.Linhas, planilha.Tasks);
        if (linhas.Count == 0) { Erro("A planilha está vazia: preencha as linhas abaixo do cabeçalho."); return RedirectToPage(); }
        if (!linhas[0].Tem("Código do equipamento") || !linhas[0].Tem("Nome do plano") || !linhas[0].Tem("Periodicidade"))
        {
            Erro("A planilha não tem as colunas Código do equipamento, Nome do plano e Periodicidade. Use a planilha do botão \"Baixar modelo\" (ou a da Extração).");
            return RedirectToPage();
        }

        // Tudo numa transação só: uma gravação no disco no fim, em vez de várias por plano (importação grande fica rápida).
        await using var transacao = await db.Database.BeginTransactionAsync();
        var equips = await db.Equipamentos.ToDictionaryAsync(e => e.Codigo);
        // Técnico e coordenador responsáveis: pelo nome completo (sem acento e sem maiúscula) ou pela matrícula, entre os ativos.
        var pessoas = await db.Usuarios.AsNoTracking().ToListAsync();
        Usuario? Pessoa(string texto, params Grupo[] grupos) => texto == "" ? null : pessoas.FirstOrDefault(u => u.Ativo && grupos.Contains(u.Grupo) &&
            (NomeSimples(u.NomeCompleto) == NomeSimples(texto) || u.Login.Equals(texto, StringComparison.OrdinalIgnoreCase)));
        string? NomeDe(int? usuarioId) => pessoas.FirstOrDefault(u => u.Id == usuarioId)?.NomeCompleto;
        var editaNr = Pode("planos_ehs");
        var erros = new List<string>();
        var faltaTasks = new Dictionary<string, int>(); // bloco da aba TASKS que não existe → nº de planos sem atividades
        int criados = 0, atualizados = 0;
        foreach (var l in linhas)
        {
            if (!equips.TryGetValue(l["Código do equipamento"].ToUpper(), out var eq)) { erros.Add($"Linha {l.Numero}: equipamento {l["Código do equipamento"]} não cadastrado"); continue; }
            var nome = l["Nome do plano"].ToUpper();
            if (nome == "") { erros.Add($"Linha {l.Numero}: informe o nome do plano"); continue; }
            // Periodicidade por extenso ou sigla; várias juntas ("M/TM") viram um plano para cada uma,
            // todos do mesmo equipamento (mesmo código) e com a mesma Ordem de trabalho (número da OS).
            var (periodos, invalidas) = Excel.LerPeriodicidades(l["Periodicidade"]);
            if (invalidas.Count > 0) erros.Add($"Linha {l.Numero}: periodicidade inválida ({string.Join(", ", invalidas)})");
            else if (periodos.Count == 0) erros.Add($"Linha {l.Numero}: informe a periodicidade");
            // Lembrete em dias (coluna opcional: planilha sem ela não mexe no lembrete já cadastrado).
            var temLembrete = l.Tem("Lembrar antes do prazo");
            var textoLembrete = l["Lembrar antes do prazo"];
            int? lembrete = int.TryParse(new string(textoLembrete.TakeWhile(char.IsDigit).ToArray()), out var dias) ? Math.Clamp(dias, 0, 365) : null;
            if (textoLembrete != "" && lembrete == null)
                erros.Add($"Linha {l.Numero}: lembrete inválido ({textoLembrete}); use o número de dias");
            // Técnico e coordenador (colunas opcionais). Em branco, ou nome que não foi achado, não mexe nos que o plano já tem.
            var tecnico = Pessoa(l["Técnico responsável"], Grupo.Tecnico);
            if (l["Técnico responsável"] != "" && tecnico == null)
                erros.Add($"Linha {l.Numero}: técnico {l["Técnico responsável"]} não encontrado; use o nome completo ou a matrícula de um técnico ativo");
            var coordenador = Pessoa(l["Coordenador responsável"], Grupo.Coordenador, Grupo.Administrador);
            if (l["Coordenador responsável"] != "" && coordenador == null)
                erros.Add($"Linha {l.Numero}: coordenador {l["Coordenador responsável"]} não encontrado; use o nome completo ou a matrícula de um coordenador ativo");

            foreach (var periodo in periodos)
            {
                // Bloco com o nome do equipamento; se não houver, tenta com o nome do plano.
                var tarefas = tasks == null ? null
                    : Excel.TarefasDe(tasks, eq.Nome, periodo) ?? Excel.TarefasDe(tasks, nome, periodo);
                if (tasks != null && tarefas == null)
                {
                    var bloco = Excel.TituloTask(eq.Nome, periodo);
                    faltaTasks[bloco] = faltaTasks.GetValueOrDefault(bloco) + 1;
                }
                tarefas ??= [];

                var plano = await db.Planos.Include(p => p.Tarefas)
                    .FirstOrDefaultAsync(p => p.EquipamentoId == eq.Id && p.Nome.ToUpper() == nome && p.Periodicidade == periodo);
                var daPlanilha = tarefas.Count > 0; // sem bloco na aba TASKS: as tarefas do plano ficam como estão
                // Tarefas de NR são do EHS (permissão "planos_ehs"): para os outros, as da planilha são ignoradas e as do plano
                // ficam (a Extração leva as NR; reimportar a mesma planilha não muda nada). NR sempre por último.
                if (!editaNr)
                    tarefas = [.. tarefas.Where(t => !Ehs.EhNr(t)),
                               .. (plano?.Tarefas.OrderBy(t => t.Ordem).Select(t => t.Descricao) ?? []).Where(Ehs.EhNr)];
                else
                    tarefas = [.. tarefas.OrderBy(Ehs.EhNr)];
                if (plano == null)
                {
                    plano = new PlanoManutencao
                    {
                        EquipamentoId = eq.Id, Nome = nome, Periodicidade = periodo, Descricao = l["Descrição do serviço"],
                        LembreteDias = lembrete, TecnicoId = tecnico?.Id, CoordenadorId = coordenador?.Id,
                        Tarefas = tarefas.Select((t, i) => new TarefaPlano { Descricao = t, Ordem = i }).ToList(),
                    };
                    plano.OrdemTrabalho = await regras.OrdemTrabalhoAsync(plano); // Regra 01: mesmo número para o equipamento + nome
                    db.Planos.Add(plano);
                    await db.SaveChangesAsync();
                    var os = await regras.CriarOsDoPlanoAsync(plano);
                    await db.SaveChangesAsync();
                    auditoria.Registrar("Plano", plano.Id, "Criado", $"Importado do Excel · ordem de trabalho {os.Numero}");
                    criados++;
                }
                else
                {
                    var descricaoAntes = plano.Descricao;
                    var lembreteAntes = plano.LembreteDias;
                    var tarefasAntes = string.Join("; ", plano.Tarefas.OrderBy(t => t.Ordem).Select(t => t.Descricao));
                    // Planilha sem a coluna de descrição não apaga a descrição já cadastrada.
                    if (l.Tem("Descrição do serviço")) plano.Descricao = l["Descrição do serviço"];
                    if (temLembrete && plano.LembreteDias != lembrete)
                    {
                        plano.LembreteDias = lembrete;
                        // A OS do plano passa a lembrar com a nova antecedência.
                        foreach (var os in await db.Ordens.Where(o => o.PlanoId == plano.Id).ToListAsync())
                        {
                            os.LembreteDias = lembrete;
                            Regras.AtualizarLembrete(os);
                        }
                    }
                    var (tecnicoAntes, coordenadorAntes) = (plano.TecnicoId, plano.CoordenadorId);
                    plano.TecnicoId = tecnico?.Id ?? plano.TecnicoId;
                    plano.CoordenadorId = coordenador?.Id ?? plano.CoordenadorId;
                    // A OS do plano passa a mostrar o técnico e o coordenador novos.
                    if ((tecnicoAntes, coordenadorAntes) != (plano.TecnicoId, plano.CoordenadorId)) await regras.ResponsaveisAsync(plano);
                    if (daPlanilha && string.Join("|", plano.Tarefas.OrderBy(t => t.Ordem).Select(t => t.Descricao)) != string.Join("|", tarefas))
                    {
                        // A página do manual continua na tarefa que a planilha manteve (a planilha não tem página).
                        var paginas = plano.Tarefas.GroupBy(t => t.Descricao, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(g => g.Key, g => g.First().Pagina, StringComparer.OrdinalIgnoreCase);
                        db.TarefasPlano.RemoveRange(plano.Tarefas);
                        plano.Tarefas = tarefas.Select((t, i) => new TarefaPlano { Descricao = t, Ordem = i, Pagina = paginas.GetValueOrDefault(t) }).ToList();
                        await db.SaveChangesAsync();
                        await regras.SincronizarTarefasAsync(plano); // Regra 02: OS ainda não iniciadas recebem o novo checklist
                        await db.SaveChangesAsync();
                        await regras.SincronizarNrAsync(plano);       // tarefas de NR: também as OS já começadas
                    }
                    // Histórico: só o que a planilha mudou, de quanto para quanto.
                    var lembreteTexto = (int? d) => d is int n ? $"{n} dia(s) antes" : null;
                    var mudou = Auditoria.Mudancas(
                        ("Descrição", descricaoAntes, plano.Descricao),
                        ("Lembrete", lembreteTexto(lembreteAntes), lembreteTexto(plano.LembreteDias)),
                        ("Técnico responsável", NomeDe(tecnicoAntes), NomeDe(plano.TecnicoId)),
                        ("Coordenador responsável", NomeDe(coordenadorAntes), NomeDe(plano.CoordenadorId)),
                        ("Tarefas", tarefasAntes, string.Join("; ", plano.Tarefas.OrderBy(t => t.Ordem).Select(t => t.Descricao))));
                    if (mudou != "") auditoria.Registrar("Plano", plano.Id, "Alterado pelo Excel", mudou);
                    atualizados++;
                }
                await db.SaveChangesAsync();
                // Plano já gravado: solta da memória, senão cada gravação seguinte reconfere tudo o que já foi importado.
                db.ChangeTracker.Clear();
            }
        }
        await transacao.CommitAsync();

        // Resumo: os planos sem atividades vêm agrupados pelo bloco que falta na aba TASKS (um item por bloco, não por linha).
        var msg = $"{criados} plano(s) criado(s) e {atualizados} atualizado(s) pelo Excel.";
        var grupos = new List<GrupoAviso>();
        if (tasks == null)
            grupos.Add(new($"A planilha não tem a aba {Excel.AbaTasks}, então os planos ficaram sem atividades.", []));
        if (faltaTasks.Count > 0)
            grupos.Add(new($"{faltaTasks.Values.Sum()} plano(s) ficaram sem atividades porque estes blocos não existem na aba {Excel.AbaTasks}:",
                faltaTasks.OrderByDescending(b => b.Value).ThenBy(b => b.Key).Select(b => $"{b.Key} — {b.Value} plano(s)").ToList()));
        if (erros.Count > 0)
            grupos.Add(new($"{erros.Count} linha(s) com problema:", erros));
        if (grupos.Count == 0) Ok(msg);
        else Avisar(new(msg, grupos, faltaTasks.Count == 0 ? null
            : $"Como resolver: crie esses blocos na aba {Excel.AbaTasks} (título = nome do equipamento + periodicidade, ex.: NXT - SEMANAL) " +
              "e importe a mesma planilha de novo. Nada é duplicado: os planos que já existem só recebem as atividades."));
        return RedirectToPage();
    }

    // "José  da Silva" = "JOSE DA SILVA": sem acento, sem maiúscula e com um espaço só entre as palavras.
    static string NomeSimples(string s) => System.Text.RegularExpressions.Regex.Replace(Excel.Normalizar(s), @"\s+", " ");

    // Remover selecionados — apaga os planos e as OS deles (com as finalizações), para não sobrar OS solta.
    // Com "marcar todos" (todos), todos os planos da lista filtrada, de todas as páginas.
    public async Task<IActionResult> OnPostRemoverAsync(int[] ids, bool todos)
    {
        if (todos) ids = await Filtrar().Select(p => p.Id).ToArrayAsync();
        var lista = await db.Planos.Where(p => ids.Contains(p.Id)).ToListAsync();
        var osRemovidas = await regras.RemoverOrdensAsync(db.Ordens.Where(o => o.PlanoId != null && ids.Contains(o.PlanoId.Value)));
        foreach (var p in lista) auditoria.Registrar("Plano", p.Id, "Removido", $"{p.OrdemTrabalho} · {p.Nome}");
        db.Planos.RemoveRange(lista);
        await db.SaveChangesAsync();
        Ok($"{lista.Count} plano(s) removido(s), junto com {osRemovidas} OS.");
        return RedirectToPage(new { Local, CelulaId, Periodicidade, Q });
    }
}
