// ============================================================================
// TELA: CRONOGRAMA — cadastro do plano (lógica)  ·  endereço /Cronograma/Editar/{id}  ·  visual em Editar.cshtml
// Salvar o plano cria ou atualiza a OS dele (Regra 01), com o checklist do plano (Regra 02).
// ONDE MEXER:
//   - Carregar o plano na tela ............ OnGetAsync
//   - Validações e salvar ................. OnPostAsync (mensagens "Selecione o equipamento" etc.)
//   - Ordem de trabalho (= número da OS) .. OnPostAsync → regras.OrdemTrabalhoAsync
//   - Remover o plano ..................... OnPostRemoverAsync
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Cronograma;

public class EditarModel(AppDb db, Regras regras, Auditoria auditoria) : MsPage
{
    protected override string Modulo => "planos";

    [BindProperty] public PlanoManutencao Plano { get; set; } = new();
    [BindProperty] public List<string> Tarefas { get; set; } = new();
    [BindProperty] public string? AbaAtiva { get; set; }
    [BindProperty] public DateTime? DataManutencao { get; set; } // prazo da OS do plano
    [BindProperty] public DateTime? ProximaManutencao { get; set; } // preenchida pela periodicidade, mas editável

    public List<Equipamento> Equipamentos { get; set; } = new();
    public string Local { get; set; } = "";
    public string Predio { get; set; } = "";
    public string CodigoEquip { get; set; } = "";

    async Task Carregar()
    {
        // Só as máquinas da área escolhida nos cards (Servicos/AreaEscolhida.cs) + a do plano aberto.
        var area = AreaEscolhida.Ler(HttpContext);
        Equipamentos = await db.Equipamentos.Where(e => area == null || e.CelulaId == area || e.Id == Plano.EquipamentoId)
            .OrderBy(e => e.Codigo).ToListAsync();
        var eq = Equipamentos.FirstOrDefault(e => e.Id == Plano.EquipamentoId);
        Local = eq?.Localizacao ?? "";
        Predio = eq == null ? "" : eq.Predio == "" ? "-" : eq.Predio; // vem do cadastro do equipamento
        CodigoEquip = eq?.Codigo ?? "";
    }

    public async Task<IActionResult> OnGetAsync(int? id, string? aba)
    {
        if (id != null)
        {
            var p = await db.Planos.Include(x => x.Tarefas).FirstOrDefaultAsync(x => x.Id == id);
            if (p == null) return NotFound();
            Plano = p;
            Tarefas = p.Tarefas.OrderBy(t => t.Ordem).Select(t => t.Descricao).ToList();
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

    public async Task<IActionResult> OnPostAsync(int? id, string acao)
    {
        Tarefas = Tarefas.Select(t => t.Trim()).Where(t => t != "").ToList();
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

        plano.EquipamentoId = Plano.EquipamentoId;
        // Plano mudou de equipamento: a OS dele vai junto (as finalizações antigas continuam com o equipamento de antes).
        if (!novo && equipamentoAntes != plano.EquipamentoId)
            foreach (var os in await OsDoPlano(plano.Id).ToListAsync())
                os.EquipamentoId = plano.EquipamentoId;
        plano.Nome = Plano.Nome;
        plano.Descricao = (Plano.Descricao ?? "").Trim(); // descrição em branco chega como null
        plano.Periodicidade = Plano.Periodicidade;
        plano.LembreteDias = Plano.LembreteDias is int dias ? Math.Clamp(dias, 0, 365) : null;
        // Regra 01: Ordem de trabalho = número da OS; o mesmo para o equipamento + nome do plano (todas as periodicidades).
        var ordemAntes = plano.OrdemTrabalho;
        plano.OrdemTrabalho = await regras.OrdemTrabalhoAsync(plano);
        if (!novo && ordemAntes != plano.OrdemTrabalho)
            foreach (var os in await OsDoPlano(plano.Id).Where(o => o.Numero == ordemAntes).ToListAsync())
                os.Numero = plano.OrdemTrabalho; // a OS acompanha a Ordem de trabalho

        var mudouTarefas = tarefasAntes != string.Join("|", Tarefas);
        if (mudouTarefas)
        {
            db.TarefasPlano.RemoveRange(plano.Tarefas);
            plano.Tarefas = Tarefas.Select((t, i) => new TarefaPlano { Descricao = t, Ordem = i }).ToList();
        }
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
            var mudou = Auditoria.Mudancas(
                ("Equipamento", eqAntes?.Descricao, eq?.Descricao),
                ("Nome do plano", nomeAntes, plano.Nome),
                ("Periodicidade", periodoAntes.Nome(), plano.Periodicidade.Nome()),
                ("Ordem de trabalho", ordemAntes, plano.OrdemTrabalho),
                ("Descrição", descricaoAntes, plano.Descricao),
                ("Lembrete", lembrete(lembreteAntes), lembrete(plano.LembreteDias)),
                ("Tarefas", tarefasAntes.Replace("|", "; "), string.Join("; ", Tarefas)));
            if (mudou != "") auditoria.Registrar("Plano", plano.Id, "Alterado", mudou);
        }
        await db.SaveChangesAsync();

        return acao switch
        {
            "outro" => RedirectToPage("Editar", new { id = (int?)null }),
            "continuar" => RedirectToPage("Editar", new { id = plano.Id, aba = Request.Form["abaAtiva"].ToString() }),
            _ => RedirectToPage("Index"),
        };
    }

    // OS do plano que ainda não foi concluída (a que o plano gerou).
    IQueryable<OrdemServico> OsDoPlano(int planoId) =>
        db.Ordens.Where(o => o.PlanoId == planoId && o.Status != StatusOS.Concluida).OrderBy(o => o.Id);

    public async Task<IActionResult> OnPostRemoverAsync(int id)
    {
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
