// ============================================================================
// TELA: EQUIPAMENTOS — cadastro (lógica)  ·  endereço /Equipamentos/Editar/{id}  ·  visual em Editar.cshtml
// O código (prefixo do prédio + sigla + número, ex.: PRDADS00113) é gerado pelo sistema no cadastro e não muda depois (Servicos/Regras.cs).
// ONDE MEXER:
//   - Carregar na tela .................... OnGetAsync
//   - Prévia do código enquanto digita .... OnGetCodigoAsync
//   - Validações e salvar ................. OnPostAsync
//   - O que o Histórico mostra ............ MudancasEquipamento (campo: antes → depois)
//   - Remover ............................. OnPostRemoverAsync (com plano ou OS não remove)
//   - Foto da máquina ..................... FotoNova / RemoverFoto (vão junto com o Salvar; reduzida a 1600px por
//                                           Servicos/Fotos.cs) · mostrar a foto: OnGetFotoAsync (?handler=Foto)
//   - Manual da máquina (PDF, aba Manual) . ManualNovo (vai junto com o Salvar; troca o anterior; Servicos/ManuaisMaquina.cs)
//                                           · abrir: OnGetManualAsync (?handler=Manual)
//   - Manual nas outras de mesmo nome ..... OnPostAsync: o manual novo vai sozinho para todas com o mesmo Nome (ex.: NXT),
//                                           um PDF só; o arquivo só é apagado quando nenhuma usa (ApagarManualSemUsoAsync)
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Equipamentos;

// O manual em PDF pode ser grande (até 500 MB desde 19/10/2026, pedido do usuário): o padrão de 30 MB por envio recusaria.
// O envio inteiro tem folga para a foto e os campos. ValueCountLimit repete o do Program.cs (este atributo troca as
// regras do formulário só desta tela).
[RequestSizeLimit(560_000_000)]
[RequestFormLimits(MultipartBodyLengthLimit = 560_000_000, ValueCountLimit = 100_000)]
public class EditarModel(AppDb db, Auditoria auditoria, Regras regras, Fotos fotos, ManuaisMaquina manuais) : MsPage
{
    protected override string Modulo => "equipamentos";

    [BindProperty] public Equipamento Equip { get; set; } = new();
    [BindProperty] public IFormFile? FotoNova { get; set; }   // foto da máquina escolhida agora (troca a anterior)
    [BindProperty] public bool RemoverFoto { get; set; }
    [BindProperty] public IFormFile? ManualNovo { get; set; } // manual em PDF escolhido agora (troca o anterior)
    [BindProperty] public string? AbaAtiva { get; set; }
    public long TamanhoManual => Equip.Manual == null ? 0 : manuais.Tamanho(Equip.Manual);
    public List<Celula> Celulas { get; set; } = new();
    public List<string> Localizacoes { get; set; } = new();
    public List<string> Predios { get; set; } = new();

    async Task Carregar()
    {
        Celulas = await db.Celulas.OrderBy(c => c.Nome).ToListAsync();
        // Sugestões de localização: só as da área da máquina (ou da área escolhida nos cartões). Prédio é o mesmo para todas.
        var area = Equip.CelulaId ?? AreaId;
        Localizacoes = await db.Equipamentos.Where(e => area == null || e.CelulaId == area).Select(e => e.Localizacao).Distinct().OrderBy(x => x).ToListAsync();
        Predios = await db.Equipamentos.Where(e => e.Predio != "").Select(e => e.Predio).Distinct().OrderBy(x => x).ToListAsync();
    }

    public async Task<IActionResult> OnGetAsync(int? id, string? aba)
    {
        if (id != null)
        {
            var e = await db.Equipamentos.FindAsync(id);
            if (e == null) return NotFound();
            Equip = e;
        }
        else Equip.CelulaId = AreaId;   // máquina nova: já vem na área escolhida nos cartões
        AbaAtiva = aba;
        await Carregar();
        return Page();
    }

    // Manual da máquina (dados/manuais, fora do wwwroot: só abre com login). O visualizador lê aos pedaços (range).
    public async Task<IActionResult> OnGetManualAsync(int id)
    {
        var arquivo = await db.Equipamentos.Where(e => e.Id == id).Select(e => e.Manual).FirstOrDefaultAsync();
        if (arquivo == null || !System.IO.File.Exists(manuais.Caminho(arquivo))) return NotFound();
        Response.Headers.CacheControl = "private, max-age=86400";   // trocar o manual muda o endereço (&v=arquivo)
        return new PhysicalFileResult(manuais.Caminho(arquivo), "application/pdf") { EnableRangeProcessing = true };
    }

    // O mesmo PDF pode estar em várias máquinas (manual colocado em todas de mesmo nome): só apaga quando nenhuma usa mais.
    async Task ApagarManualSemUsoAsync(string arquivo)
    {
        if (!await db.Equipamentos.AnyAsync(e => e.Manual == arquivo)) manuais.Apagar(arquivo);
    }

    // Foto da máquina (fica em dados/fotos, fora do wwwroot: só abre com login e com acesso a Equipamentos).
    public async Task<IActionResult> OnGetFotoAsync(int id)
    {
        var arquivo = await db.Equipamentos.Where(e => e.Id == id).Select(e => e.Foto).FirstOrDefaultAsync();
        if (arquivo == null || !System.IO.File.Exists(fotos.Caminho(arquivo))) return NotFound();
        Response.Headers.CacheControl = "private, max-age=86400";   // trocar a foto muda o endereço (&v=arquivo)
        return PhysicalFile(fotos.Caminho(arquivo), "image/jpeg");
    }

    // Prévia do código na tela de cadastro, enquanto o nome e o prédio são digitados.
    public async Task<IActionResult> OnGetCodigoAsync(string? nome, string? predio) =>
        Content(string.IsNullOrWhiteSpace(nome) || string.IsNullOrWhiteSpace(predio) ? "" : await regras.ProximoCodigoEquipamentoAsync(nome, predio));

    public async Task<IActionResult> OnPostAsync(int? id, string acao)
    {
        Equip.Nome = (Equip.Nome ?? "").Trim().ToUpper();
        Equip.Modelo = string.IsNullOrWhiteSpace(Equip.Modelo) ? null : Equip.Modelo.Trim().ToUpper();
        Equip.Irf = string.IsNullOrWhiteSpace(Equip.Irf) ? null : Equip.Irf.Trim().ToUpper();
        Equip.Etiqueta = string.IsNullOrWhiteSpace(Equip.Etiqueta) ? null : Equip.Etiqueta.Trim().ToUpper();
        Equip.NumeroSerie = string.IsNullOrWhiteSpace(Equip.NumeroSerie) ? null : Equip.NumeroSerie.Trim().ToUpper();
        Equip.Predio = (Equip.Predio ?? "").Trim().ToUpper();
        Equip.Localizacao = (Equip.Localizacao ?? "").Trim().ToUpper();
        // O código é do sistema: gerado no cadastro (o da tela é só prévia) e nunca muda depois.
        Equip.Codigo = id is int atual
            ? await db.Equipamentos.Where(e => e.Id == atual).Select(e => e.Codigo).FirstOrDefaultAsync() ?? ""
            : (Equip.Nome == "" || Equip.Predio == "" ? "" : await regras.ProximoCodigoEquipamentoAsync(Equip.Nome, Equip.Predio));
        if (Equip.Nome == "" || Equip.Predio == "" || Equip.Localizacao == "")
            ModelState.AddModelError("", "Preencha nome, prédio e localização.");
        if (await db.Equipamentos.AnyAsync(e => e.Codigo == Equip.Codigo && e.Id != (id ?? 0)))
            ModelState.AddModelError("", $"Já existe um equipamento com o código {Equip.Codigo}.");
        if (Equip.Irf != null && await db.Equipamentos.FirstOrDefaultAsync(e => e.Irf == Equip.Irf && e.Id != (id ?? 0)) is { } dono)
            ModelState.AddModelError("", $"O IRF {Equip.Irf} já está na máquina {dono.Descricao}.");
        if (Equip.Etiqueta != null && await db.Equipamentos.FirstOrDefaultAsync(e => e.Etiqueta == Equip.Etiqueta && e.Id != (id ?? 0)) is { } donoEtq)
            ModelState.AddModelError("", $"A etiqueta {Equip.Etiqueta} já está na máquina {donoEtq.Descricao}.");
        // Foto da máquina escolhida agora: reduzida e gravada antes de salvar o cadastro (arquivo que não é imagem = erro).
        string? novaFoto = null;
        if (ModelState.IsValid && FotoNova is { Length: > 0 })
        {
            try { novaFoto = await fotos.SalvarAsync(FotoNova, "equip-"); } catch (Exception) { }
            if (novaFoto == null) ModelState.AddModelError("", "Não foi possível ler a foto da máquina. Escolha uma imagem (JPG ou PNG).");
        }
        // Manual em PDF escolhido agora: gravado antes de salvar o cadastro (arquivo que não é PDF = erro).
        string? novoManual = null;
        if (ModelState.IsValid && ManualNovo is { Length: > 0 })
        {
            var (arquivo, erro) = await manuais.SalvarAsync(ManualNovo);
            if (erro != null) ModelState.AddModelError("", erro);
            novoManual = arquivo;
        }
        if (!ModelState.IsValid)
        {
            if (novaFoto != null) fotos.Apagar(novaFoto);
            if (novoManual != null) manuais.Apagar(novoManual);
            Equip.Id = id ?? 0;
            var salvo = id == null ? null : await db.Equipamentos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            (Equip.Foto, Equip.Manual, Equip.ManualNome, Equip.ManualEm, Equip.ManualPor) = (salvo?.Foto, salvo?.Manual, salvo?.ManualNome, salvo?.ManualEm, salvo?.ManualPor);
            await Carregar(); return Page();
        }

        Equipamento e;
        Equipamento? antes = null;
        if (id is int eid)
        {
            e = await db.Equipamentos.FindAsync(eid) ?? throw new InvalidOperationException();
            antes = new Equipamento { Codigo = e.Codigo, Irf = e.Irf, Etiqueta = e.Etiqueta, Nome = e.Nome, Modelo = e.Modelo, NumeroSerie = e.NumeroSerie, Predio = e.Predio, Localizacao = e.Localizacao, Status = e.Status, CelulaId = e.CelulaId };
            db.Entry(e).CurrentValues.SetValues(new { Equip.Codigo, Equip.Irf, Equip.Etiqueta, Equip.Nome, Equip.Modelo, Equip.NumeroSerie, Equip.Predio, Equip.Localizacao, Equip.Status, Equip.CelulaId });
        }
        else
        {
            e = new Equipamento { Codigo = Equip.Codigo, Irf = Equip.Irf, Etiqueta = Equip.Etiqueta, Nome = Equip.Nome, Modelo = Equip.Modelo, NumeroSerie = Equip.NumeroSerie, Predio = Equip.Predio, Localizacao = Equip.Localizacao, Status = Equip.Status, CelulaId = Equip.CelulaId };
            db.Equipamentos.Add(e);
        }
        // Foto: a nova troca a anterior; "Remover foto" tira. O arquivo antigo é apagado depois de salvar.
        var fotoAntiga = e.Foto;
        if (novaFoto != null) e.Foto = novaFoto;
        else if (RemoverFoto) e.Foto = null;
        // Manual: o novo troca o anterior (o arquivo antigo é apagado depois de salvar).
        var manualAntigo = e.Manual;
        if (novoManual != null)
            (e.Manual, e.ManualNome, e.ManualEm, e.ManualPor) = (novoManual, Path.GetFileName(ManualNovo!.FileName), DateTime.Now, User.Identity?.Name);
        // Manual novo vai sozinho para as outras máquinas de mesmo nome (ex.: todas as NXT), sem perguntar (pedido do usuário
        // em 21/10/2026): todas usam o MESMO arquivo (um PDF só, não uma cópia por máquina).
        var outras = new List<(Equipamento Maq, string? Antigo)>();
        if (novoManual != null)
            foreach (var x in await db.Equipamentos.Where(x => x.Nome == e.Nome && x.Id != e.Id).ToListAsync())
            {
                outras.Add((x, x.Manual));
                (x.Manual, x.ManualNome, x.ManualEm, x.ManualPor) = (e.Manual, e.ManualNome, e.ManualEm, e.ManualPor);
            }
        await db.SaveChangesAsync();
        if (fotoAntiga != null && fotoAntiga != e.Foto) fotos.Apagar(fotoAntiga);
        if (fotoAntiga != e.Foto && antes != null)
            auditoria.Registrar("Equipamento", e.Id, "Alterado", e.Foto == null ? "Foto: removida" : fotoAntiga == null ? "Foto: adicionada" : "Foto: trocada");
        foreach (var antigo in outras.Select(o => o.Antigo).Append(manualAntigo).Where(a => a != null && a != e.Manual).Distinct())
            await ApagarManualSemUsoAsync(antigo!);
        if (novoManual != null)
            auditoria.Registrar("Equipamento", e.Id, "Alterado", $"Manual: {(manualAntigo == null ? "anexado" : "trocado")} ({e.ManualNome})");
        foreach (var (x, antigo) in outras)
            auditoria.Registrar("Equipamento", x.Id, "Alterado", $"Manual: {(antigo == null ? "anexado" : "trocado")} ({e.ManualNome}) — o mesmo de {e.Descricao}");
        var celulas = await db.Celulas.ToDictionaryAsync(c => c.Id, c => c.Nome);
        if (antes == null)
            auditoria.Registrar("Equipamento", e.Id, "Criado",
                $"{e.Descricao}{(e.Irf != null ? $" · IRF {e.Irf}" : "")}{(e.Etiqueta != null ? $" · etiqueta {e.Etiqueta}" : "")}{(e.Modelo != null ? $" · modelo {e.Modelo}" : "")}{(e.NumeroSerie != null ? $" · número de série {e.NumeroSerie}" : "")} · {e.Predio} · {e.Localizacao} · {e.Status.Nome()}");
        else if (MudancasEquipamento(antes, e, celulas) is { Length: > 0 } mudou)
            auditoria.Registrar("Equipamento", e.Id, "Alterado", mudou); // só o que mudou: "Localização: SMT LINE 02 → LINHA 03"
        await db.SaveChangesAsync();
        Ok($"Equipamento {e.Descricao} salvo.{(outras.Count > 0 ? $" Manual colocado também em {outras.Count} outra(s) máquina(s) {e.Nome}." : "")}");

        return acao == "outro" ? RedirectToPage("Editar", new { id = (int?)null }) : RedirectToPage("Index");
    }

    // Histórico da alteração: código, IRF, etiqueta, nome, modelo, número de série, localização, célula e status — de quanto
    // para quanto. (Também usada pela importação do Excel em Index.cshtml.cs.)
    public static string MudancasEquipamento(Equipamento antes, Equipamento depois, IReadOnlyDictionary<int, string> celulas) =>
        Auditoria.Mudancas(
            ("Código", antes.Codigo, depois.Codigo),
            ("IRF", antes.Irf, depois.Irf),
            ("Etiqueta eletrônica", antes.Etiqueta, depois.Etiqueta),
            ("Nome", antes.Nome, depois.Nome),
            ("Modelo", antes.Modelo, depois.Modelo),
            ("Número de série", antes.NumeroSerie, depois.NumeroSerie),
            ("Prédio", antes.Predio, depois.Predio),
            ("Localização", antes.Localizacao, depois.Localizacao),
            ("Área", antes.CelulaId is int a ? celulas.GetValueOrDefault(a) : null, depois.CelulaId is int d ? celulas.GetValueOrDefault(d) : null),
            ("Status", antes.Status.Nome(), depois.Status.Nome()));

    public async Task<IActionResult> OnPostRemoverAsync(int id)
    {
        var e = await db.Equipamentos.FindAsync(id);
        if (e == null) return RedirectToPage("Index");
        if (await db.Planos.AnyAsync(p => p.EquipamentoId == id) || await db.Ordens.AnyAsync(o => o.EquipamentoId == id))
        {
            Erro("Este equipamento tem planos ou ordens de serviço. Remova-os antes ou marque o equipamento como Inativo.");
            return RedirectToPage("Editar", new { id });
        }
        db.Equipamentos.Remove(e);
        auditoria.Registrar("Equipamento", id, "Removido", e.Descricao);
        await db.SaveChangesAsync();
        if (e.Foto != null) fotos.Apagar(e.Foto);
        if (e.Manual != null) await ApagarManualSemUsoAsync(e.Manual);
        Ok($"Equipamento {e.Descricao} removido.");
        return RedirectToPage("Index");
    }
}
