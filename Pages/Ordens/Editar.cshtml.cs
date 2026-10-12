// ============================================================================
// TELA: ORDENS DE SERVIÇO — executar/editar a OS (lógica)  ·  endereço /Ordens/Editar/{id}  ·  visual em Editar.cshtml
// É aqui que o técnico marca o checklist, lança peças e fotos e conclui (Regra 04: com ao menos 1 tarefa marcada,
// Salvar leva o ciclo para a Finalização e a OS pula para a próxima data).
// ONDE MEXER:
//   - Carregar a OS na tela ............... OnGetAsync
//   - Salvar / concluir ................... OnPostAsync (validações, peças, fotos, conclusão)
//   - Peças (código, peça, quantidade) .... classe PecaInput · sugestões do Spare Parts: Catalogo (CarregarListas)
//                                           · ao concluir, o uso fica registrado no Spare Parts (Regras.Concluir → Servicos/Estoque.cs)
//   - Só foto de hoje ..................... bloco "Imagens antes/depois" (hora da foto: Servicos/Fotos.cs → HoraDaFotoAsync)
//   - Fotos obrigatórias para concluir .... faltaFoto (pelo menos 1 de Antes e 1 de Depois; a tela avisa antes)
//   - Remover a OS ........................ OnPostRemoverAsync
//   - Salvar e abrir NRE .................. acao "nre": guarda o andamento (não conclui) e abre /Nre/Editar?os=id
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using MS.Servicos;

namespace MS.Pages.Ordens;

// Várias fotos de celular num Salvar só (3 a 8 MB cada): o padrão de 30 MB por envio recusava a partir de umas 5.
// ValueCountLimit repete o do Program.cs (este atributo troca as regras do formulário só desta tela).
[RequestSizeLimit(300_000_000)]
[RequestFormLimits(MultipartBodyLengthLimit = 300_000_000, ValueCountLimit = 100_000)]
public class EditarModel(AppDb db, Regras regras, Auditoria auditoria, Fotos fotos) : MsPage
{
    protected override string Modulo => "ordens";

    public class PecaInput
    {
        public string? Codigo { get; set; }
        public string? Descricao { get; set; }
        // Texto: o <input type=number> sempre envia com ponto, e a cultura pt-BR leria "1.5" como 15.
        public string? Quantidade { get; set; }

        public decimal Valor() =>
            decimal.TryParse(Quantidade?.Replace(',', '.'), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var q) && q > 0 ? q : 1;
    }

    [BindProperty] public OrdemServico Os { get; set; } = new();
    [BindProperty] public List<int> Concluidas { get; set; } = new();
    [BindProperty] public List<PecaInput> PecasInput { get; set; } = new();
    [BindProperty] public List<IFormFile> FotosAntes { get; set; } = new();
    [BindProperty] public List<IFormFile> FotosDepois { get; set; } = new();
    // Data do arquivo de cada foto, informada pelo navegador na mesma ordem (vale quando a foto não tem EXIF).
    [BindProperty] public List<string> FotosAntesHora { get; set; } = new();
    [BindProperty] public List<string> FotosDepoisHora { get; set; } = new();
    [BindProperty] public List<int> RemoverImagens { get; set; } = new();
    [BindProperty] public string? TarefasNovas { get; set; }
    [BindProperty] public string? AbaAtiva { get; set; }
    [BindProperty] public DateTime? DataManutencao { get; set; }
    [BindProperty] public DateTime? ProximaManutencao { get; set; }

    public List<Equipamento> Equipamentos { get; set; } = new();
    public List<Usuario> Tecnicos { get; set; } = new();
    public List<Usuario> Coordenadores { get; set; } = new();
    public List<Modelos.Nre> NresDaOs { get; set; } = new();   // NRE abertas a partir desta OS
    // Spare Parts: peças sugeridas no campo Código de "Peças utilizadas" (com a descrição).
    public record ItemCatalogo(string Codigo, string Descricao);
    public List<ItemCatalogo> Catalogo { get; set; } = new();
    public bool Novo => Os.Id == 0;

    async Task CarregarListas()
    {
        // Só as máquinas da área escolhida nos cards (Servicos/AreaEscolhida.cs) + a da OS aberta.
        var area = AreaEscolhida.Ler(HttpContext);
        Equipamentos = await db.Equipamentos.Where(e => area == null || e.CelulaId == area || e.Id == Os.EquipamentoId)
            .OrderBy(e => e.Codigo).ToListAsync();
        Tecnicos = await db.Usuarios.Where(u => u.Ativo && u.Grupo == Grupo.Tecnico).OrderBy(u => u.PrimeiroNome).ToListAsync();
        Coordenadores = await db.Usuarios.Where(u => u.Ativo && (u.Grupo == Grupo.Coordenador || u.Grupo == Grupo.Administrador)).OrderBy(u => u.PrimeiroNome).ToListAsync();
        Catalogo = (await db.SpareParts.Where(p => p.Ativa).OrderBy(p => p.Codigo).ToListAsync())
            .Select(p => new ItemCatalogo(p.Codigo, p.Descricao)).ToList();
    }

    Task<OrdemServico?> Buscar(int id) => db.Ordens
        .Include(o => o.Equipamento).Include(o => o.Plano)
        .Include(o => o.Tarefas).Include(o => o.Pecas).Include(o => o.Imagens)
        .FirstOrDefaultAsync(o => o.Id == id);

    public async Task<IActionResult> OnGetAsync(int? id, string? aba)
    {
        if (id != null)
        {
            var os = await Buscar(id.Value);
            if (os == null) return NotFound();
            Os = os;
            if (Ve("nre")) NresDaOs = await db.Nres.Where(n => n.OrdemServicoId == os.Id).OrderBy(n => n.CriadaEm).ToListAsync();
        }
        else
        {
            Os.DataAbertura = DateTime.Now;
            Os.DataConclusao = Regras.Prazo(Os.DataAbertura, Os.Tipo);
        }
        AbaAtiva = aba;
        await CarregarListas();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id, string acao)
    {
        var agora = DateTime.Now;
        OrdemServico os;
        if (id is int oid)
        {
            os = await Buscar(oid) ?? throw new InvalidOperationException();
        }
        else
        {
            if (Os.EquipamentoId == 0)
            {
                ModelState.Remove("Os.EquipamentoId"); // fica só a mensagem abaixo, em português
                ModelState.AddModelError("", "Selecione o equipamento.");
                await CarregarListas();
                return Page();
            }
            // OS à mão: se o equipamento tem plano com o mesmo tipo, nasce com as tarefas do plano (Regra 02).
            var plano = await db.Planos.Include(p => p.Tarefas)
                .FirstOrDefaultAsync(p => p.EquipamentoId == Os.EquipamentoId && p.Periodicidade == Os.Tipo);
            os = new OrdemServico
            {
                // OS de plano usa a Ordem de trabalho dele como número (se estiver livre).
                Numero = plano != null ? await regras.NumeroDaOsDoPlanoAsync(plano, agora) : await regras.ProximoNumeroOsAsync(agora),
                EquipamentoId = Os.EquipamentoId,
                PlanoId = plano?.Id,
                Tipo = Os.Tipo,
                DataAbertura = agora,
                LembreteDias = plano?.LembreteDias,
                Tarefas = plano != null
                    ? Regras.CopiarTarefas(plano)
                    : (TarefasNovas ?? "").Split('\n').Select(t => t.Trim()).Where(t => t != "")
                        .Select((t, i) => new TarefaOS { Descricao = t, Ordem = i }).ToList(),
            };
            if (string.IsNullOrWhiteSpace(Os.Descricao) && plano != null) Os.Descricao = plano.Descricao;
            db.Ordens.Add(os);
        }

        var statusAntes = os.Status;
        // Para o Histórico: como a OS estava (só o que mudou aparece).
        var antes = (os.TecnicoId, os.CoordenadorId, os.Descricao, os.Tipo, Data: os.DataConclusao.Date,
            Pecas: string.Join("; ", os.Pecas.Select(Regras.LinhaPeca)));
        // Tipo só muda em OS sem plano; o prazo acompanha (Regra 03).
        if (os.PlanoId == null && os.Tipo != Os.Tipo) os.Tipo = Os.Tipo;
        // Data da manutenção: na OS avulsa (ou nova) é escolhida aqui; na OS de plano, no Cronograma.
        if (id == null || os.PlanoId == null)
        {
            if (DataManutencao is DateTime dm) Regras.DefinirDataManutencao(os, dm, ProximaManutencao);
            else os.DataConclusao = Regras.Prazo(os.DataAbertura, os.Tipo);
        }
        os.TecnicoId = Os.TecnicoId;
        os.CoordenadorId = Os.CoordenadorId;
        os.Descricao = (Os.Descricao ?? "").Trim();

        // Checklist: o texto é travado, só se marca Concluída (Regra 02).
        foreach (var t in os.Tarefas) t.Concluida = t.Id != 0 && Concluidas.Contains(t.Id);

        // Peças utilizadas (código e peça; a linha vale se tiver um dos dois).
        db.Pecas.RemoveRange(os.Pecas);
        os.Pecas = PecasInput.Where(p => !string.IsNullOrWhiteSpace(p.Codigo) || !string.IsNullOrWhiteSpace(p.Descricao))
            .Select(p => new PecaOS
            {
                Codigo = string.IsNullOrWhiteSpace(p.Codigo) ? null : p.Codigo.Trim().ToUpper(),
                Descricao = (p.Descricao ?? "").Trim(),
                Quantidade = p.Valor(),
            }).ToList();

        // Imagens antes/depois (Regra 06 — reduzidas a 1600px). Só vale foto tirada hoje: a tela já recusa as outras,
        // e aqui confere de novo pela data gravada na foto.
        regras.RemoverImagens(os.Imagens.Where(i => RemoverImagens.Contains(i.Id)).ToList());
        var recusadas = new List<string>();
        foreach (var (lista, horas, tipo) in new[] { (FotosAntes, FotosAntesHora, TipoImagem.Antes), (FotosDepois, FotosDepoisHora, TipoImagem.Depois) })
            for (var i = 0; i < lista.Count; i++)
            {
                var tirada = await Fotos.HoraDaFotoAsync(lista[i], i < horas.Count ? horas[i] : null, agora);
                if (tirada.Date != agora.Date) { recusadas.Add(tirada.ToString("dd/MM/yyyy HH:mm")); continue; }
                if (await fotos.SalvarAsync(lista[i]) is string arq)
                    os.Imagens.Add(new Imagem { Tipo = tipo, Arquivo = arq, CriadaEm = agora, TiradaEm = tirada });
            }
        if (recusadas.Count > 0)
            Erro($"{recusadas.Count} foto(s) não foram salvas: só vale foto tirada hoje. Tiradas em: {string.Join(", ", recusadas)}.");

        // Lembrete: os dias vêm do plano (Cronograma); o aviso acompanha a data da manutenção.
        Regras.AtualizarLembrete(os);

        // Status definido pelo sistema (o campo na tela é só leitura):
        //   Salvar com pelo menos uma tarefa marcada → Concluída (vai para a Finalização) e a OS reinicia;
        //   sem nenhuma marcada, ou em "Salvar e continuar editando", só guarda o andamento.
        //   OS sem tarefas é concluída pelo botão "Concluir OS". "Atrasada" vem da data da manutenção.
        var concluir = id != null && (os.Tarefas.Count > 0
            ? acao is not ("continuar" or "nre") && os.Tarefas.Any(t => t.Concluida)
            : acao == "concluir");
        // Fotos obrigatórias: só conclui com pelo menos uma foto de Antes e uma de Depois. Faltou: guarda o andamento,
        // não conclui e volta para a aba Imagens (a tela já avisa antes de enviar — Editar.cshtml).
        var fotosFicam = os.Imagens.Where(i => !RemoverImagens.Contains(i.Id)).ToList();
        var faltaFoto = concluir && (!fotosFicam.Any(i => i.Tipo == TipoImagem.Antes) || !fotosFicam.Any(i => i.Tipo == TipoImagem.Depois));
        if (faltaFoto)
        {
            concluir = false;
            acao = "fotos";
            Erro("Para concluir a OS, tire pelo menos uma foto de Antes e uma de Depois. O andamento foi salvo.");
        }
        os.Status = concluir ? StatusOS.Concluida
            : StatusOS.Aberta;
        // Técnico que começou a marcar tarefas numa OS sem técnico vira o responsável.
        if (os.TecnicoId == null && Grupo == Grupo.Tecnico && os.Tarefas.Any(t => t.Concluida)) os.TecnicoId = UsuarioId;

        await db.SaveChangesAsync();

        if (os.Status == StatusOS.Concluida)
        {
            // Regra 04 — concluiu, reiniciou: o ciclo vai para a Finalização e a mesma OS segue com a data do próximo.
            var dataCiclo = os.DataConclusao;
            var fin = regras.Concluir(os);
            await db.SaveChangesAsync();
            auditoria.Registrar("OrdemServico", os.Id, "Concluída",
                $"Ciclo de {dataCiclo:dd/MM/yyyy} foi para a Finalização (#{fin.Id}). Nova data da manutenção: {os.DataConclusao:dd/MM/yyyy}");
            auditoria.Registrar("Finalizacao", fin.Id, "Criada", $"Conclusão da {os.Numero} aguardando revisão");
            await db.SaveChangesAsync();
            AvisosPorEmail.Acordar(); // e-mail para o coordenador aprovar sai em segundos (Servicos/Avisos.cs)
            Ok($"{os.Numero}: o ciclo de {dataCiclo:dd/MM/yyyy} foi para a Finalização. A OS continua na lista com a nova data da manutenção: {os.DataConclusao:dd/MM/yyyy}.");
        }
        else
        {
            var andamento = $"Status: {os.Status.Nome()} · tarefas {os.Tarefas.Count(t => t.Concluida)}/{os.Tarefas.Count}" +
                (statusAntes != os.Status && id != null ? $" (antes: {statusAntes.Nome()})" : "");
            if (id == null)
                auditoria.Registrar("OrdemServico", os.Id, "Criada",
                    andamento + (os.Pecas.Count > 0 ? $" · peças: {string.Join(", ", os.Pecas.Select(Regras.LinhaPeca))}" : ""));
            else
            {
                // Andamento + só os campos que mudaram, de quanto para quanto.
                var ids = new[] { antes.TecnicoId, antes.CoordenadorId, os.TecnicoId, os.CoordenadorId }.OfType<int>().ToList();
                var nomes = await db.Usuarios.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.NomeCompleto);
                string? Nome(int? uid) => uid is int x ? nomes.GetValueOrDefault(x) : null;
                var mudou = Auditoria.Mudancas(
                    ("Técnico responsável", Nome(antes.TecnicoId), Nome(os.TecnicoId)),
                    ("Coordenador responsável", Nome(antes.CoordenadorId), Nome(os.CoordenadorId)),
                    ("Descrição", antes.Descricao, os.Descricao),
                    ("Periodicidade", antes.Tipo.Nome(), os.Tipo.Nome()),
                    ("Data da manutenção", antes.Data, os.DataConclusao.Date),
                    ("Peças", antes.Pecas, string.Join("; ", os.Pecas.Select(Regras.LinhaPeca))));
                auditoria.Registrar("OrdemServico", os.Id, "Alterada", andamento + (mudou == "" ? "" : "\n" + mudou));
            }
            await db.SaveChangesAsync();
            Ok($"{os.Numero} salva.");
        }

        return acao switch
        {
            "outro" => RedirectToPage("Editar", new { id = (int?)null }),
            "continuar" => RedirectToPage("Editar", new { id = os.Id, aba = Request.Form["abaAtiva"].ToString() }),
            "fotos" => RedirectToPage("Editar", new { id = os.Id, aba = "#aba-imagens" }),   // faltou foto para concluir
            // Salvar e abrir NRE: a NRE nasce com esta máquina e as peças que acabaram de ser salvas.
            "nre" when Ve("nre") && Pode("nre") => RedirectToPage("/Nre/Editar", new { os = os.Id }),
            _ => RedirectToPage("Index"),
        };
    }

    public async Task<IActionResult> OnPostRemoverAsync(int id)
    {
        if (!Pode("ordens_gestao")) return RedirectToPage("/Conta/Negado");
        var os = await Buscar(id);
        if (os != null)
        {
            var imgsFin = await db.Imagens.Where(i => db.Finalizacoes.Any(f => f.Id == i.FinalizacaoId && f.OrdemServicoId == id)).ToListAsync();
            foreach (var img in os.Imagens.Concat(imgsFin)) fotos.Apagar(img.Arquivo);
            db.Ordens.Remove(os);
            auditoria.Registrar("OrdemServico", id, "Removida", os.Numero);
            await db.SaveChangesAsync();
            Ok($"{os.Numero} removida.");
        }
        return RedirectToPage("Index");
    }
}
