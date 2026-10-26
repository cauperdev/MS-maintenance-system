// ============================================================================
// TELA: CRONOGRAMA — cadastro do plano (lógica)  ·  endereço /Cronograma/Editar/{id}  ·  visual em Editar.cshtml
// Salvar o plano cria ou atualiza a OS dele (Regra 01), com o checklist do plano (Regra 02).
// ONDE MEXER:
//   - Carregar o plano na tela ............ OnGetAsync
//   - Validações e salvar ................. OnPostAsync (mensagens "Selecione o equipamento" etc.)
//   - Ordem de trabalho (= número da OS) .. OnPostAsync → regras.OrdemTrabalhoAsync
//   - Técnico e coordenador responsáveis .. listas em Carregar; a OS do plano acompanha (regras.ResponsaveisAsync)
//   - Remover o plano ..................... OnPostRemoverAsync
//   - Tarefas de NR (EHS) ................. só quem tem "planos_ehs" (EditaNr) mexe nelas; o grupo EHS, que não edita o
//                                           plano, salva só as NR (SalvarSoNrAsync). Quem edita o plano sem "planos_ehs"
//                                           não mexe nas NR: ficam as do banco (JuntarTarefasAsync).
//   - Página do manual de cada tarefa ..... TarefasPagina (uma por tarefa, junto com o texto); escolhida no visualizador
//                                           (wwwroot/js/manual.js) · o PDF da máquina: OnGetManualAsync (?handler=Manual)
//                                           · sem página escolhida: a achada pelo texto (Achadas, OnPostPaginasAsync →
//                                           Servicos/PaginaAutomatica.cs)
//                                           · a OS lê a página daqui (Ordens/Editar → PaginaDaTarefa)
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Cronograma;

public class EditarModel(AppDb db, Regras regras, Auditoria auditoria, ManuaisMaquina manuais, PaginaAutomatica paginaAutomatica) : MsPage
{
    protected override string Modulo => "planos";
    // Tarefas de NR (EHS): só o grupo EHS e o administrador (permissão "planos_ehs" na tela Grupos).
    public bool EditaNr => Pode("planos_ehs");
    protected override bool PodeSalvar => PodeEditar || EditaNr;

    [BindProperty] public PlanoManutencao Plano { get; set; } = new();
    [BindProperty] public List<string> Tarefas { get; set; } = new();
    // Uma por tarefa, na mesma ordem: "EHS" no quadro vermelho ou "-" na tarefa comum (Servicos/Ehs.cs).
    [BindProperty] public List<string?> TarefasNr { get; set; } = new();
    // Uma por tarefa, na mesma ordem: a página do manual ("" = sem página).
    [BindProperty] public List<string?> TarefasPagina { get; set; } = new();
    // Página de cada item de Tarefas (mesma ordem), para a tela.
    public List<int?> Paginas { get; set; } = new();
    public int? Pagina(int i) => i < Paginas.Count ? Paginas[i] : null;
    // Tarefa sem página: a achada pelo texto no manual (Servicos/PaginaAutomatica.cs); grava ao Salvar como as outras.
    public List<PaginaAutomatica.Achada?> Achadas { get; set; } = new();
    public PaginaAutomatica.Achada? Achada(int i) => Pagina(i) == null && i < Achadas.Count ? Achadas[i] : null;
    [BindProperty] public string? AbaAtiva { get; set; }
    [BindProperty] public DateTime? DataManutencao { get; set; } // prazo da OS do plano
    [BindProperty] public DateTime? ProximaManutencao { get; set; } // preenchida pela periodicidade, mas editável

    public List<Equipamento> Equipamentos { get; set; } = new();
    public List<Usuario> Tecnicos { get; set; } = new();
    public List<Usuario> Coordenadores { get; set; } = new();
    public string Local { get; set; } = "";
    public string Predio { get; set; } = "";
    public string CodigoEquip { get; set; } = "";

    async Task Carregar()
    {
        // Só as máquinas da área escolhida nos cartões (e a do próprio plano, se ele for aberto estando em outra área).
        var area = AreaId;
        Equipamentos = await db.Equipamentos.Where(e => area == null || e.CelulaId == area || e.Id == Plano.EquipamentoId)
            .OrderBy(e => e.Codigo).ToListAsync();
        var eq = Equipamentos.FirstOrDefault(e => e.Id == Plano.EquipamentoId);
        Local = eq?.Localizacao ?? "";
        Predio = eq == null ? "" : eq.Predio == "" ? "-" : eq.Predio; // vem do cadastro do equipamento
        CodigoEquip = eq?.Codigo ?? "";
        Achadas = await paginaAutomatica.AcharAsync(eq?.Manual, Tarefas.Select(t => t ?? ""));
        // As mesmas listas da OS: só quem é da área da máquina (sem máquina ainda: a área escolhida nos cartões).
        // Quem já está no plano continua na lista mesmo se foi desativado ou mudou de área depois.
        var areaPessoas = eq?.CelulaId ?? area;
        Tecnicos = await db.Usuarios.Where(u => (u.Ativo && u.Grupo == Grupo.Tecnico && (areaPessoas == null || u.CelulaId == areaPessoas)) || u.Id == Plano.TecnicoId)
            .OrderBy(u => u.PrimeiroNome).ToListAsync();
        Coordenadores = await db.Usuarios.Where(u => (u.Ativo && (u.Grupo == Grupo.Coordenador || u.Grupo == Grupo.Administrador) && (areaPessoas == null || u.CelulaId == areaPessoas)) || u.Id == Plano.CoordenadorId)
            .OrderBy(u => u.PrimeiroNome).ToListAsync();
    }

    public async Task<IActionResult> OnGetAsync(int? id, string? aba)
    {
        if (id != null)
        {
            var p = await db.Planos.Include(x => x.Tarefas).FirstOrDefaultAsync(x => x.Id == id);
            if (p == null) return NotFound();
            Plano = p;
            Tarefas = p.Tarefas.OrderBy(t => t.Ordem).Select(t => t.Descricao).ToList();
            Paginas = p.Tarefas.OrderBy(t => t.Ordem).Select(t => t.Pagina).ToList();
            var osPlano = await OsDoPlano(p.Id).FirstOrDefaultAsync();
            DataManutencao = osPlano?.DataConclusao.Date;
            ProximaManutencao = osPlano != null ? Regras.ProximaManutencao(osPlano).Date : null;
        }
        // Plano novo: a 1ª manutenção já vem calculada pela periodicidade (hoje + período, mín. 7 dias, sem domingo).
        DataManutencao ??= Regras.PrimeiraManutencao(Plano.Periodicidade, DateTime.Today);
        AbaAtiva = aba;
        await Carregar();
        return Page();
    }

    // Tarefas enviadas pela tela → texto gravado (quadro vermelho = "EHS — texto", Servicos/Ehs.cs) e página do manual.
    // TarefasNr: "-" = tarefa comum; "EHS" = quadro vermelho. TarefasPagina: número da página ou "".
    List<(string Texto, int? Pagina)> TarefasEnviadas() =>
        Tarefas.Select((t, i) => (
                Texto: i < TarefasNr.Count && TarefasNr[i] == "EHS" ? Ehs.Gravar(t) : (t ?? "").Trim(),
                Pagina: i < TarefasPagina.Count && int.TryParse(TarefasPagina[i], out var p) && p > 0 ? p : (int?)null))
            .Where(t => t.Texto != "").ToList();

    // Cada um mexe só no que pode: as tarefas comuns vêm da tela para quem edita o plano, as de NR para quem tem "planos_ehs";
    // o resto fica como está no banco (os campos travados nem chegam no envio). NR sempre por último. A página do manual
    // anda junto com o texto da tarefa.
    async Task<List<(string Texto, int? Pagina)>> JuntarTarefasAsync(int? id)
    {
        var enviadas = TarefasEnviadas();
        var gravadas = id == null ? [] : (await db.TarefasPlano.Where(t => t.PlanoId == id).OrderBy(t => t.Ordem)
            .Select(t => new { t.Descricao, t.Pagina }).ToListAsync()).Select(t => (Texto: t.Descricao, t.Pagina)).ToList();
        var comuns = (PodeEditar ? enviadas : gravadas).Where(t => !Ehs.EhNr(t.Texto));
        var nrs = (EditaNr ? enviadas : gravadas).Where(t => Ehs.EhNr(t.Texto));
        return comuns.Concat(nrs).ToList();
    }

    // Grava as tarefas no plano: texto mudou → checklist novo; só a página mudou → acerta a página nas mesmas tarefas.
    // Devolve se o TEXTO mudou (é o que mexe no checklist das OS) e as linhas do Histórico das páginas.
    (bool MudouTexto, string Paginas) GravarTarefas(PlanoManutencao plano, List<(string Texto, int? Pagina)> tarefas)
    {
        var antes = plano.Tarefas.OrderBy(t => t.Ordem).ToList();
        var paginaAntes = antes.GroupBy(t => t.Descricao, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Pagina, StringComparer.OrdinalIgnoreCase);
        var mudouTexto = !antes.Select(t => t.Descricao).SequenceEqual(tarefas.Select(t => t.Texto));
        if (mudouTexto)
        {
            db.TarefasPlano.RemoveRange(plano.Tarefas);
            plano.Tarefas = tarefas.Select((t, i) => new TarefaPlano { Descricao = t.Texto, Ordem = i, Pagina = t.Pagina }).ToList();
        }
        else
            for (var i = 0; i < antes.Count; i++) antes[i].Pagina = tarefas[i].Pagina;
        // Histórico: uma linha por tarefa cuja página mudou ("Página do manual — TROCA DO FILTRO: 37 → 16").
        var paginas = Auditoria.Mudancas(tarefas.DistinctBy(t => t.Texto)
            .Select(t => ($"Página do manual — {Ehs.Ler(t.Texto).Texto}", (object?)paginaAntes.GetValueOrDefault(t.Texto), (object?)t.Pagina))
            .ToArray());
        return (mudouTexto, paginas);
    }

    // Manual da máquina escolhida no plano, para o coordenador achar a página de cada tarefa.
    public async Task<IActionResult> OnGetManualAsync(int equipamento)
    {
        var arquivo = await db.Equipamentos.Where(e => e.Id == equipamento).Select(e => e.Manual).FirstOrDefaultAsync();
        if (arquivo == null || !System.IO.File.Exists(manuais.Caminho(arquivo))) return NotFound();
        Response.Headers.CacheControl = "private, max-age=86400";   // trocar o manual muda o endereço (&v=arquivo)
        return new PhysicalFileResult(manuais.Caminho(arquivo), "application/pdf") { EnableRangeProcessing = true };
    }

    // Página achada pelo texto de cada tarefa (a tela pede ao digitar uma tarefa ou trocar o equipamento — manual.js).
    public async Task<IActionResult> OnPostPaginasAsync(int equipamento, List<string> textos)
    {
        var arquivo = await db.Equipamentos.Where(e => e.Id == equipamento).Select(e => e.Manual).FirstOrDefaultAsync();
        var achadas = await paginaAutomatica.AcharAsync(arquivo, textos.Select(t => t ?? ""));
        return new JsonResult(achadas.Select(a => a == null ? null : new { pagina = a.Pagina, palavras = a.Palavras }));
    }

    public async Task<IActionResult> OnPostAsync(int? id, string acao)
    {
        if (!PodeEditar) return await SalvarSoNrAsync(id, acao);
        var juntas = await JuntarTarefasAsync(id);
        Tarefas = juntas.Select(t => t.Texto).ToList();
        Paginas = juntas.Select(t => t.Pagina).ToList();
        Plano.Nome = (Plano.Nome ?? "").Trim().ToUpper();
        ModelState.Remove("Plano.EquipamentoId"); // equipamento vazio: fica só a mensagem abaixo, em português
        if (Plano.EquipamentoId == 0) ModelState.AddModelError("", "Selecione o equipamento.");
        if (Plano.Nome == "") ModelState.AddModelError("", "Informe o nome do plano.");
        if (DataManutencao is DateTime dm0 && ProximaManutencao is DateTime px0 && px0.Date <= dm0.Date)
            ModelState.AddModelError("", "A próxima manutenção precisa ser depois da data da manutenção.");
        // Um plano por equipamento + nome + periodicidade (senão nasceriam duas OS iguais).
        if (Plano.EquipamentoId != 0 && Plano.Nome != "" && await db.Planos.AnyAsync(p => p.Id != (id ?? 0) &&
                p.EquipamentoId == Plano.EquipamentoId && p.Nome.ToUpper() == Plano.Nome && p.Periodicidade == Plano.Periodicidade))
            ModelState.AddModelError("", $"Este equipamento já tem o plano {Plano.Nome} {Plano.Periodicidade.Nome().ToLower()}. Abra esse plano no Cronograma para alterá-lo.");
        if (!ModelState.IsValid) { Plano.Id = id ?? 0; await Carregar(); return Page(); }

        PlanoManutencao plano;
        var novo = id == null;
        if (novo)
        {
            plano = new PlanoManutencao();
            db.Planos.Add(plano);
        }
        else
        {
            plano = await db.Planos.Include(p => p.Tarefas).FirstOrDefaultAsync(p => p.Id == id) ?? throw new InvalidOperationException();
        }
        var tarefasAntes = string.Join("|", plano.Tarefas.OrderBy(t => t.Ordem).Select(t => t.Descricao));
        var periodoAntes = plano.Periodicidade;
        var lembreteAntes = plano.LembreteDias;
        var equipamentoAntes = plano.EquipamentoId;
        var nomeAntes = plano.Nome;
        var descricaoAntes = plano.Descricao;
        var (tecnicoAntes, coordenadorAntes) = (plano.TecnicoId, plano.CoordenadorId);

        plano.EquipamentoId = Plano.EquipamentoId;
        // Plano mudou de equipamento: a OS dele vai junto (as finalizações antigas continuam com o equipamento de antes).
        if (!novo && equipamentoAntes != plano.EquipamentoId)
            foreach (var os in await OsDoPlano(plano.Id).ToListAsync())
                os.EquipamentoId = plano.EquipamentoId;
        plano.Nome = Plano.Nome;
        plano.Descricao = (Plano.Descricao ?? "").Trim(); // descrição em branco chega como null
        plano.Periodicidade = Plano.Periodicidade;
        plano.LembreteDias = Plano.LembreteDias is int dias ? Math.Clamp(dias, 0, 365) : null;
        plano.TecnicoId = Plano.TecnicoId;
        plano.CoordenadorId = Plano.CoordenadorId;
        // Regra 01: Ordem de trabalho = número da OS; o mesmo para o equipamento + nome do plano (todas as periodicidades).
        var ordemAntes = plano.OrdemTrabalho;
        plano.OrdemTrabalho = await regras.OrdemTrabalhoAsync(plano);
        if (!novo && ordemAntes != plano.OrdemTrabalho)
            foreach (var os in await OsDoPlano(plano.Id).Where(o => o.Numero == ordemAntes).ToListAsync())
                os.Numero = plano.OrdemTrabalho; // a OS acompanha a Ordem de trabalho

        var (mudouTarefas, mudouPaginas) = GravarTarefas(plano, juntas);
        await db.SaveChangesAsync();

        if (novo)
        {
            // Regra 01: o plano gera a OS, com a data da manutenção escolhida como prazo.
            var data = Regras.SemDomingo((DataManutencao ?? Regras.PrimeiraManutencao(plano.Periodicidade, DateTime.Today)).Date);
            var os = await regras.CriarOsDoPlanoAsync(plano, data.AddDays(-(int)plano.Periodicidade) + DateTime.Now.TimeOfDay);
            Regras.DefinirDataManutencao(os, data, ProximaManutencao);
            await db.SaveChangesAsync();
            auditoria.Registrar("OrdemServico", os.Id, "Criada", $"Gerada pelo plano {plano.OrdemTrabalho} · prazo {os.DataConclusao.Data()}");
            Ok($"Plano salvo com a ordem de trabalho {os.Numero}: a OS foi gerada com {os.Tarefas.Count} tarefas.");
        }
        else
        {
            if (mudouTarefas || periodoAntes != plano.Periodicidade) await regras.SincronizarTarefasAsync(plano); // Regra 02
            if (mudouTarefas)
            {
                await db.SaveChangesAsync();
                await regras.SincronizarNrAsync(plano); // tarefas de NR: também as OS já começadas
            }
            // O técnico e o coordenador do plano valem para a OS dele (na OS os dois campos são só leitura).
            await regras.ResponsaveisAsync(plano);
            // O lembrete do plano vale para a OS dele.
            if (lembreteAntes != plano.LembreteDias)
                foreach (var os in await OsDoPlano(plano.Id).ToListAsync())
                {
                    os.LembreteDias = plano.LembreteDias;
                    Regras.AtualizarLembrete(os);
                }
            if (DataManutencao is DateTime data)
                foreach (var os in await OsDoPlano(plano.Id).ToListAsync())
                {
                    var antes = os.DataConclusao.Date;
                    os.Tipo = plano.Periodicidade;
                    Regras.DefinirDataManutencao(os, data, ProximaManutencao);
                    if (antes != os.DataConclusao.Date)
                        auditoria.Registrar("OrdemServico", os.Id, "Prazo reprogramado", $"{antes:dd/MM/yyyy} → {os.DataConclusao:dd/MM/yyyy} (pelo Cronograma)");
                }
            Ok($"Plano {plano.OrdemTrabalho} salvo.");
        }
        var eq = await db.Equipamentos.FindAsync(plano.EquipamentoId);
        if (novo)
            auditoria.Registrar("Plano", plano.Id, "Criado",
                $"{plano.OrdemTrabalho} · {plano.Nome} · {eq?.Codigo} · {plano.Periodicidade.Nome()}\nTarefas: {string.Join("; ", Tarefas)}");
        else
        {
            // Só o que mudou, de quanto para quanto (a data da manutenção fica no histórico da OS: "Prazo reprogramado").
            var eqAntes = equipamentoAntes == plano.EquipamentoId ? eq : await db.Equipamentos.FindAsync(equipamentoAntes);
            var lembrete = (int? d) => d is int n ? $"{n} dia(s) antes" : null;
            var ids = new[] { tecnicoAntes, coordenadorAntes, plano.TecnicoId, plano.CoordenadorId }.OfType<int>().ToList();
            var nomes = await db.Usuarios.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.NomeCompleto);
            string? Quem(int? uid) => uid is int x ? nomes.GetValueOrDefault(x) : null;
            var mudou = Auditoria.Mudancas(
                ("Equipamento", eqAntes?.Descricao, eq?.Descricao),
                ("Nome do plano", nomeAntes, plano.Nome),
                ("Periodicidade", periodoAntes.Nome(), plano.Periodicidade.Nome()),
                ("Ordem de trabalho", ordemAntes, plano.OrdemTrabalho),
                ("Técnico responsável", Quem(tecnicoAntes), Quem(plano.TecnicoId)),
                ("Coordenador responsável", Quem(coordenadorAntes), Quem(plano.CoordenadorId)),
                ("Descrição", descricaoAntes, plano.Descricao),
                ("Lembrete", lembrete(lembreteAntes), lembrete(plano.LembreteDias)),
                ("Tarefas", tarefasAntes.Replace("|", "; "), string.Join("; ", Tarefas)));
            mudou = string.Join("\n", new[] { mudou, mudouPaginas }.Where(m => m != ""));
            if (mudou != "") auditoria.Registrar("Plano", plano.Id, "Alterado", mudou);
            // Na OS do plano os dois campos são só leitura: a troca feita aqui fica também no histórico dela.
            var responsaveis = Auditoria.Mudancas(
                ("Técnico responsável", Quem(tecnicoAntes), Quem(plano.TecnicoId)),
                ("Coordenador responsável", Quem(coordenadorAntes), Quem(plano.CoordenadorId)));
            if (responsaveis != "")
                foreach (var os in await OsDoPlano(plano.Id).ToListAsync())
                    auditoria.Registrar("OrdemServico", os.Id, "Alterada", responsaveis + "\n(pelo Cronograma)");
        }
        await db.SaveChangesAsync();

        return acao == "outro" ? RedirectToPage("Editar", new { id = (int?)null }) : RedirectToPage("Index");
    }

    // Grupo EHS (edita só as tarefas de NR): o resto do plano chega travado e fica como está no banco.
    async Task<IActionResult> SalvarSoNrAsync(int? id, string acao)
    {
        var plano = id == null ? null : await db.Planos.Include(p => p.Tarefas).FirstOrDefaultAsync(p => p.Id == id);
        if (plano == null) return RedirectToPage("/Conta/Negado"); // plano novo é de quem edita o Cronograma
        var tarefas = await JuntarTarefasAsync(id);
        var tarefasAntes = plano.Tarefas.OrderBy(t => t.Ordem).Select(t => t.Descricao).ToList();
        var (mudouTexto, mudouPaginas) = GravarTarefas(plano, tarefas);
        if (mudouTexto || mudouPaginas != "")
        {
            await db.SaveChangesAsync();
            var ordens = mudouTexto ? await regras.SincronizarNrAsync(plano) : 0;
            var mudou = string.Join("\n", new[] { Auditoria.Mudancas(("Tarefas", string.Join("; ", tarefasAntes), string.Join("; ", tarefas.Select(t => t.Texto)))), mudouPaginas }.Where(m => m != ""));
            auditoria.Registrar("Plano", plano.Id, "Alterado",
                mudou + (ordens > 0 ? $"\n(tarefas de NR pelo EHS; {ordens} OS aberta(s) atualizada(s))" : "\n(tarefas de NR pelo EHS)"));
            await db.SaveChangesAsync();
        }
        Ok($"Tarefas de NR do plano {plano.OrdemTrabalho} salvas.");
        return RedirectToPage("Index");
    }

    // OS do plano que ainda não foi concluída (a que o plano gerou).
    IQueryable<OrdemServico> OsDoPlano(int planoId) =>
        db.Ordens.Where(o => o.PlanoId == planoId && o.Status != StatusOS.Concluida).OrderBy(o => o.Id);

    public async Task<IActionResult> OnPostRemoverAsync(int id)
    {
        if (!PodeEditar) return RedirectToPage("/Conta/Negado"); // o EHS salva as NR, mas não remove plano
        var p = await db.Planos.FindAsync(id);
        if (p != null)
        {
            // As OS do plano (com as finalizações) saem junto, para não sobrar OS solta.
            var osRemovidas = await regras.RemoverOrdensAsync(db.Ordens.Where(o => o.PlanoId == id));
            db.Planos.Remove(p);
            auditoria.Registrar("Plano", id, "Removido", $"{p.OrdemTrabalho} · {p.Nome}");
            await db.SaveChangesAsync();
            Ok($"Plano {p.OrdemTrabalho} removido, junto com {osRemovidas} OS.");
        }
        return RedirectToPage("Index");
    }
}
