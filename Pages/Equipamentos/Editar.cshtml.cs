// ============================================================================
// TELA: EQUIPAMENTOS — cadastro (lógica)  ·  endereço /Equipamentos/Editar/{id}  ·  visual em Editar.cshtml
// O código (prefixo do prédio + sigla + número, ex.: MTXADS02129) é gerado pelo sistema no cadastro e não muda depois (Servicos/Regras.cs).
// ONDE MEXER:
//   - Carregar na tela .................... OnGetAsync
//   - Prévia do código enquanto digita .... OnGetCodigoAsync
//   - Validações e salvar ................. OnPostAsync
//   - O que o Histórico mostra ............ MudancasEquipamento (campo: antes → depois)
//   - Remover ............................. OnPostRemoverAsync (com plano, OS ou NRE não remove)
//   - NRE desta máquina ................... OnGetAsync (lista Nres) · "Abrir NRE" leva a /Nre/Editar?equipamento=id
//   - Manutenção preditiva (aba) .......... Pecas (o que a tela envia) · SalvarPecasAsync (grava e põe no Histórico)
//                                           · cálculo da data da troca e da situação: Modelos/Entidades.cs → PecaPreditiva
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Equipamentos;

public class EditarModel(AppDb db, Auditoria auditoria, Regras regras) : MsPage
{
    protected override string Modulo => "equipamentos";

    [BindProperty] public Equipamento Equip { get; set; } = new();
    [BindProperty] public List<PecaInput> Pecas { get; set; } = new();   // aba Manutenção preditiva
    [BindProperty] public string? AbaAtiva { get; set; }
    public List<Celula> Celulas { get; set; } = new();
    public List<string> Localizacoes { get; set; } = new();
    public List<string> Predios { get; set; } = new();
    // NRE desta máquina (quem vê a tela NRE): lista e quanto já foi gasto (aprovadas, em execução e concluídas).
    public List<Modelos.Nre> Nres { get; set; } = new();
    public decimal GastoNre => Nres.Where(n => n.Status is StatusNre.Aprovada or StatusNre.EmAndamento or StatusNre.Cobrada or StatusNre.Concluida)
        .Sum(n => n.Total());

    // Uma linha da aba Manutenção preditiva (Id vazio = peça nova; Nome vazio = linha ignorada).
    public class PecaInput
    {
        public int? Id { get; set; }
        public string? Nome { get; set; }
        public int? VidaUtil { get; set; }
        public UnidadeVida Unidade { get; set; } = UnidadeVida.Meses;
        public DateTime? InstaladaEm { get; set; }

        public bool Completa => !string.IsNullOrWhiteSpace(Nome) && VidaUtil > 0 && InstaladaEm != null;
        // Para a tela mostrar a data da troca e a situação (só com a linha completa).
        public PecaPreditiva? Calculo() => Completa
            ? new PecaPreditiva { Nome = Nome!.Trim().ToUpper(), VidaUtil = VidaUtil!.Value, Unidade = Unidade, InstaladaEm = InstaladaEm!.Value.Date }
            : null;
    }

    async Task Carregar()
    {
        Celulas = await db.Celulas.OrderBy(c => c.Nome).ToListAsync();
        // Sugestões de localização e prédio: só das máquinas da área escolhida nos cards (Servicos/AreaEscolhida.cs).
        var area = AreaEscolhida.Ler(HttpContext);
        Localizacoes = await db.Equipamentos.Where(e => area == null || e.CelulaId == area).Select(e => e.Localizacao).Distinct().OrderBy(x => x).ToListAsync();
        Predios = await db.Equipamentos.Where(e => e.Predio != "" && (area == null || e.CelulaId == area)).Select(e => e.Predio).Distinct().OrderBy(x => x).ToListAsync();
    }

    public async Task<IActionResult> OnGetAsync(int? id, string? aba)
    {
        AbaAtiva = aba;
        if (id != null)
        {
            var e = await db.Equipamentos.FindAsync(id);
            if (e == null) return NotFound();
            Equip = e;
            Pecas = await db.PecasPreditivas.Where(p => p.EquipamentoId == e.Id).OrderBy(p => p.Nome)
                .Select(p => new PecaInput { Id = p.Id, Nome = p.Nome, VidaUtil = p.VidaUtil, Unidade = p.Unidade, InstaladaEm = p.InstaladaEm })
                .ToListAsync();
            if (Ve("nre"))
                Nres = await db.Nres.Include(n => n.Remessas).ThenInclude(r => r.Itens).Where(n => n.EquipamentoId == e.Id)
                    .OrderByDescending(n => n.CriadaEm).ToListAsync();
        }
        else Equip.CelulaId = AreaEscolhida.Ler(HttpContext); // máquina nova já vem na área escolhida nos cards
        await Carregar();
        return Page();
    }

    // Prévia do código na tela de cadastro, enquanto o nome e o prédio são digitados.
    public async Task<IActionResult> OnGetCodigoAsync(string? nome, string? predio) =>
        Content(string.IsNullOrWhiteSpace(nome) || string.IsNullOrWhiteSpace(predio) ? "" : await regras.ProximoCodigoEquipamentoAsync(nome, predio));

    public async Task<IActionResult> OnPostAsync(int? id, string acao)
    {
        Equip.Nome = (Equip.Nome ?? "").Trim().ToUpper();
        Equip.Modelo = string.IsNullOrWhiteSpace(Equip.Modelo) ? null : Equip.Modelo.Trim().ToUpper();
        Equip.Predio = (Equip.Predio ?? "").Trim().ToUpper();
        Equip.Localizacao = (Equip.Localizacao ?? "").Trim().ToUpper();
        Equip.Irf = string.IsNullOrWhiteSpace(Equip.Irf) ? null : Equip.Irf.Trim().ToUpper();
        // O código é do sistema: gerado no cadastro (o da tela é só prévia) e nunca muda depois.
        Equip.Codigo = id is int atual
            ? await db.Equipamentos.Where(e => e.Id == atual).Select(e => e.Codigo).FirstOrDefaultAsync() ?? ""
            : (Equip.Nome == "" || Equip.Predio == "" ? "" : await regras.ProximoCodigoEquipamentoAsync(Equip.Nome, Equip.Predio));
        if (Equip.Nome == "" || Equip.Predio == "" || Equip.Localizacao == "")
            ModelState.AddModelError("", "Preencha nome, prédio e localização.");
        if (await db.Equipamentos.AnyAsync(e => e.Codigo == Equip.Codigo && e.Id != (id ?? 0)))
            ModelState.AddModelError("", $"Já existe um equipamento com o código {Equip.Codigo}.");
        // Manutenção preditiva: linha sem nome é ignorada; com nome, precisa de quanto dura e de quando foi instalada.
        Pecas = Pecas.Where(p => !string.IsNullOrWhiteSpace(p.Nome)).ToList();
        foreach (var p in Pecas.Where(p => !p.Completa))
            ModelState.AddModelError("", $"Manutenção preditiva: preencha quanto a peça {p.Nome!.Trim().ToUpper()} dura e quando foi instalada.");
        foreach (var p in Pecas.Where(p => p.InstaladaEm?.Date > DateTime.Today))
            ModelState.AddModelError("", $"Manutenção preditiva: a data de instalação da peça {p.Nome!.Trim().ToUpper()} não pode ser depois de hoje.");
        if (!ModelState.IsValid) { Equip.Id = id ?? 0; await Carregar(); return Page(); }

        Equipamento e;
        Equipamento? antes = null;
        if (id is int eid)
        {
            e = await db.Equipamentos.FindAsync(eid) ?? throw new InvalidOperationException();
            antes = new Equipamento { Codigo = e.Codigo, Nome = e.Nome, Modelo = e.Modelo, Predio = e.Predio, Localizacao = e.Localizacao, Irf = e.Irf, Status = e.Status, CelulaId = e.CelulaId };
            db.Entry(e).CurrentValues.SetValues(new { Equip.Codigo, Equip.Nome, Equip.Modelo, Equip.Predio, Equip.Localizacao, Equip.Irf, Equip.Status, Equip.CelulaId });
        }
        else
        {
            e = new Equipamento { Codigo = Equip.Codigo, Nome = Equip.Nome, Modelo = Equip.Modelo, Predio = Equip.Predio, Localizacao = Equip.Localizacao, Irf = Equip.Irf, Status = Equip.Status, CelulaId = Equip.CelulaId };
            db.Equipamentos.Add(e);
        }
        await db.SaveChangesAsync();
        var celulas = await db.Celulas.ToDictionaryAsync(c => c.Id, c => c.Nome);
        if (antes == null)
            auditoria.Registrar("Equipamento", e.Id, "Criado",
                $"{e.Descricao}{(e.Modelo != null ? $" · modelo {e.Modelo}" : "")}{(e.Irf != null ? $" · IRF {e.Irf}" : "")} · {e.Predio} · {e.Localizacao} · {e.Status.Nome()}");
        else if (MudancasEquipamento(antes, e, celulas) is { Length: > 0 } mudou)
            auditoria.Registrar("Equipamento", e.Id, "Alterado", mudou); // só o que mudou: "Localização: SMT LINE 02 → LINHA JUTAÍ"
        await SalvarPecasAsync(e.Id);
        await db.SaveChangesAsync();
        Ok($"Equipamento {e.Descricao} salvo.");

        return acao switch
        {
            "outro" => RedirectToPage("Editar", new { id = (int?)null }),
            "continuar" => RedirectToPage("Editar", new { id = e.Id, aba = AbaAtiva }),
            _ => RedirectToPage("Index"),
        };
    }

    // Manutenção preditiva: a lista da tela vira a lista do banco (nova entra, alterada muda, a que sumiu da tela sai).
    // Histórico do equipamento: peça adicionada, trocada (data de instalação mais nova), alterada e removida.
    async Task SalvarPecasAsync(int equipamentoId)
    {
        var atuais = await db.PecasPreditivas.Where(p => p.EquipamentoId == equipamentoId).ToListAsync();
        foreach (var i in Pecas)
        {
            var nome = i.Nome!.Trim().ToUpper();
            var instalada = i.InstaladaEm!.Value.Date;
            var p = atuais.FirstOrDefault(a => a.Id == i.Id);
            if (p == null)
            {
                p = new PecaPreditiva { EquipamentoId = equipamentoId, Nome = nome, VidaUtil = i.VidaUtil!.Value, Unidade = i.Unidade, InstaladaEm = instalada };
                db.PecasPreditivas.Add(p);
                auditoria.Registrar("Equipamento", equipamentoId, "Preditiva: peça adicionada",
                    $"{p.Nome} · dura {p.Vida()} · instalada em {p.InstaladaEm:dd/MM/yyyy} · trocar em {p.TrocarEm:dd/MM/yyyy}");
                continue;
            }
            atuais.Remove(p);
            var antes = new PecaPreditiva { Nome = p.Nome, VidaUtil = p.VidaUtil, Unidade = p.Unidade, InstaladaEm = p.InstaladaEm };
            (p.Nome, p.VidaUtil, p.Unidade, p.InstaladaEm) = (nome, i.VidaUtil!.Value, i.Unidade, instalada);
            var trocada = p.InstaladaEm > antes.InstaladaEm;
            if (trocada)
                auditoria.Registrar("Equipamento", equipamentoId, "Preditiva: peça trocada",
                    $"{p.Nome} trocada em {p.InstaladaEm:dd/MM/yyyy} · próxima troca em {p.TrocarEm:dd/MM/yyyy}");
            // Nome, vida útil ou data de instalação corrigida para trás (a troca já foi registrada acima).
            var mudou = Auditoria.Mudancas(("Peça", antes.Nome, p.Nome), ("Dura", antes.Vida(), p.Vida()),
                ("Instalada em", trocada ? null : antes.InstaladaEm, trocada ? null : p.InstaladaEm));
            if (mudou != "") auditoria.Registrar("Equipamento", equipamentoId, $"Preditiva: {p.Nome} alterada", mudou);
        }
        foreach (var p in atuais)
            auditoria.Registrar("Equipamento", equipamentoId, "Preditiva: peça removida", $"{p.Nome} · dura {p.Vida()}");
        db.PecasPreditivas.RemoveRange(atuais);
    }

    // Histórico da alteração: código, nome, modelo, localização, IRF, célula e status — de quanto para quanto.
    // (Também usada pela importação do Excel em Index.cshtml.cs.)
    public static string MudancasEquipamento(Equipamento antes, Equipamento depois, IReadOnlyDictionary<int, string> celulas) =>
        Auditoria.Mudancas(
            ("Código", antes.Codigo, depois.Codigo),
            ("Nome", antes.Nome, depois.Nome),
            ("Modelo", antes.Modelo, depois.Modelo),
            ("Prédio", antes.Predio, depois.Predio),
            ("Localização", antes.Localizacao, depois.Localizacao),
            ("IRF", antes.Irf, depois.Irf),
            ("Área", antes.CelulaId is int a ? celulas.GetValueOrDefault(a) : null, depois.CelulaId is int d ? celulas.GetValueOrDefault(d) : null),
            ("Status", antes.Status.Nome(), depois.Status.Nome()));

    public async Task<IActionResult> OnPostRemoverAsync(int id)
    {
        var e = await db.Equipamentos.FindAsync(id);
        if (e == null) return RedirectToPage("Index");
        if (await db.Planos.AnyAsync(p => p.EquipamentoId == id) || await db.Ordens.AnyAsync(o => o.EquipamentoId == id)
            || await db.Nres.AnyAsync(n => n.EquipamentoId == id))
        {
            Erro("Este equipamento tem planos, ordens de serviço ou NRE. Remova-os antes ou marque o equipamento como Inativo.");
            return RedirectToPage("Editar", new { id });
        }
        db.Equipamentos.Remove(e);
        auditoria.Registrar("Equipamento", id, "Removido", e.Descricao);
        await db.SaveChangesAsync();
        Ok($"Equipamento {e.Descricao} removido.");
        return RedirectToPage("Index");
    }
}
