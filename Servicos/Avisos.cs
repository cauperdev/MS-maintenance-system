// ============================================================================
// AVISOS POR E-MAIL — roda sozinho a cada 5 minutos enquanto o sistema está ligado (envio: Servicos/Email.cs).
//   1. Conclusão aguardando aprovação → e-mail NA HORA para o coordenador (uma vez por conclusão).
//   2. Resumo do dia, uma vez por dia a partir de "Email:HoraResumo" (domingo não manda):
//        técnico ...... OS atrasadas + OS que vencem em até 7 dias (atenção)
//        coordenador .. conclusões aguardando a aprovação dele + OS atrasadas
//   3. Manutenção preditiva — e-mail SEPARADO do resumo das OS, na mesma hora do resumo (domingo não manda) e só quando
//      há novidade: cada peça (aba Manutenção preditiva do equipamento) avisa UMA vez quando fica perto de trocar
//      (Atenção) e UMA vez quando chega a data da troca. Trocou a peça (data de instalação nova) = avisa de novo no
//      próximo ciclo. Recebem os técnicos da área da máquina (área sem técnico: os coordenadores); no dia da troca,
//      também os coordenadores da área. Máquina Inativa não avisa.
// QUEM RECEBE: SÓ gente da MESMA ÁREA da máquina (área no cadastro do usuário) — e-mail de máquina da SAMSUNG nunca
// vai para quem é da AVELL, e vice-versa (pedido do usuário, 2026-10-07). Dentro da área: o técnico / coordenador
// marcado na OS; sem ninguém marcado (ou marcado de outra área, ou sem e-mail), os técnicos / coordenadores da área.
// Área sem técnico → o aviso de atenção vai para o coordenador da área. Área sem ninguém com e-mail, máquina sem área
// ou usuário sem área → o e-mail não sai (não é desviado para outra área). Só usuários ativos com e-mail de verdade
// (não @ms.local).
// Quem desmarcou "Receber os avisos por e-mail" (cadastro do usuário) não recebe nada — e o aviso dele NÃO é desviado
// para outra pessoa: as regras acima continuam contando com ele, só o envio para ele é que não sai (QueRecebem).
// Cada envio fica na tabela EmailsEnviados: o mesmo aviso não sai duas vezes; se falhar, tenta de novo (até 3 vezes).
// ONDE MEXER:
//   - De quanto em quanto tempo verifica ..... ExecuteAsync (5 minutos)
//   - Quem recebe ............................ Tecnicos / Coordenadores
//   - Texto dos e-mails ...................... AprovacoesAsync (aviso de aprovação) · HtmlResumo / AssuntoResumo (resumo)
//                                               · HtmlPreditiva / AssuntoPreditiva (manutenção preditiva)
// ============================================================================
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;

namespace MS.Servicos;

public class AvisosPorEmail(IServiceScopeFactory scopes, Email email, ILogger<AvisosPorEmail> log) : BackgroundService
{
    const int MaxTentativas = 3;   // e-mail que falhou tenta de novo nas próximas rodadas, até 3 vezes
    const int MaxLinhas = 100;     // linhas por quadro do resumo; o resto o técnico vê no sistema
    static readonly SemaphoreSlim acordar = new(0, 1);

    // Técnico concluiu uma OS (Pages/Ordens/Editar): o aviso ao coordenador sai em segundos, sem esperar a rodada.
    public static void Acordar()
    {
        try { acordar.Release(); } catch (SemaphoreFullException) { }
    }

    protected override async Task ExecuteAsync(CancellationToken parar)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), parar); } catch (OperationCanceledException) { return; }
        while (!parar.IsCancellationRequested)
        {
            if (email.Ativo)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                    await AprovacoesAsync(db, parar);
                    await ResumoDoDiaAsync(db, parar);
                    await PreditivaAsync(db, parar);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception e) { log.LogError(e, "Avisos por e-mail"); }
            }
            try { await acordar.WaitAsync(TimeSpan.FromMinutes(5), parar); } catch (OperationCanceledException) { return; }
        }
    }

    // ---------- Quem recebe ----------

    // Usuários ativos com e-mail de verdade.
    static async Task<List<Usuario>> PessoasAsync(AppDb db, CancellationToken parar) =>
        (await db.Usuarios.Include(u => u.Celula).Where(u => u.Ativo).ToListAsync(parar)).Where(u => Email.Valido(u.Email)).ToList();

    // Tira quem desmarcou "Receber os avisos por e-mail" — só na hora de mandar, para o aviso não ir parar em outra pessoa.
    static List<Usuario> QueRecebem(IEnumerable<Usuario> pessoas) => pessoas.Where(u => u.RecebeEmail).ToList();

    // Só quem é da mesma área da máquina; máquina sem área: ninguém.
    static List<Usuario> DaArea(List<Usuario> pessoas, int? area) =>
        area == null ? [] : pessoas.Where(u => u.CelulaId == area).ToList();

    // Técnico da OS (se for da área da máquina); senão, os técnicos da área.
    static List<Usuario> Tecnicos(List<Usuario> pessoas, int? tecnicoId, int? area)
    {
        var daArea = DaArea(pessoas, area);
        var marcado = daArea.FirstOrDefault(u => u.Id == tecnicoId);
        if (marcado != null) return [marcado];
        return daArea.Where(u => u.Grupo == Grupo.Tecnico).ToList();
    }

    // Coordenador da OS (se for da área da máquina); senão, os coordenadores da área. Área sem coordenador: ninguém.
    static List<Usuario> Coordenadores(List<Usuario> pessoas, int? coordenadorId, int? area)
    {
        var daArea = DaArea(pessoas, area);
        var marcado = daArea.FirstOrDefault(u => u.Id == coordenadorId);
        if (marcado != null) return [marcado];
        return daArea.Where(u => u.Grupo == Grupo.Coordenador).ToList();
    }

    // ---------- Registro (não repete) ----------

    // Já saiu com sucesso, ou já falhou 3 vezes? Modo teste e envio de verdade contam separado.
    async Task<bool> JaFoiAsync(AppDb db, string tipo, string referencia, CancellationToken parar)
    {
        var teste = email.EmTeste;
        var resultados = await db.EmailsEnviados.Where(e => e.Tipo == tipo && e.Referencia == referencia && e.Teste == teste)
            .Select(e => e.Ok).ToListAsync(parar);
        return resultados.Contains(true) || resultados.Count >= MaxTentativas;
    }

    async Task EnviarAsync(AppDb db, string tipo, string referencia, List<Usuario> para, string assunto, string html)
    {
        var enderecos = para.Select(u => u.Email.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var registro = new EmailEnviado
        {
            EnviadoEm = DateTime.Now, Tipo = tipo, Referencia = referencia, Para = string.Join("; ", enderecos),
            Assunto = assunto, Teste = email.EmTeste,
        };
        try
        {
            await email.EnviarAsync(enderecos, assunto, html);
            registro.Ok = true;
        }
        catch (Exception e)
        {
            registro.Erro = e.Message;
            log.LogWarning(e, "Aviso por e-mail não enviado: {Assunto}", assunto);
        }
        db.EmailsEnviados.Add(registro);
        await db.SaveChangesAsync();
    }

    // ---------- 1. Conclusão aguardando aprovação (na hora) ----------

    async Task AprovacoesAsync(AppDb db, CancellationToken parar)
    {
        var pendentes = await db.Finalizacoes.Include(f => f.Equipamento).Include(f => f.Tecnico)
            .Where(f => f.StatusRevisao == StatusRevisao.Aguardando).OrderBy(f => f.ConcluidaEm).ToListAsync(parar);
        if (pendentes.Count == 0) return;
        var pessoas = await PessoasAsync(db, parar);
        foreach (var f in pendentes)
        {
            var referencia = f.Id.ToString();
            if (await JaFoiAsync(db, "aprovacao", referencia, parar)) continue;
            var para = QueRecebem(Coordenadores(pessoas, f.CoordenadorId, f.Equipamento?.CelulaId));
            if (para.Count == 0) continue;
            var tecnico = H(f.Tecnico?.NomeCompleto ?? "O técnico");
            var maquina = H(f.Equipamento?.Descricao ?? "-") + (f.Equipamento != null ? " · " + H(f.Equipamento.Localizacao) : "");
            var atraso = f.ConcluidaEm.Date > f.DataConclusao.Date ? " <b style=\"color:#BE123C\">(feita com atraso)</b>" : "";
            var corpo = $"""
                <p>{Ola(para)}</p>
                <p><b>{tecnico}</b> concluiu a <b>{H(f.NumeroOS)}</b> — {maquina} — em {f.ConcluidaEm:dd/MM/yyyy} às {f.ConcluidaEm:HH:mm}.</p>
                <p>Data da manutenção: {f.DataConclusao:dd/MM/yyyy}{atraso}.</p>
                <p>A conclusão está <b>aguardando a sua aprovação</b>.</p>
                {Botao("Revisar e aprovar", $"/Finalizacao/Ver/{f.Id}")}
                """;
            await EnviarAsync(db, "aprovacao", referencia, para, $"MS Manutenção — {f.NumeroOS} concluída, aguardando sua aprovação",
                Moldura("Conclusão aguardando aprovação", corpo, "Você recebe este aviso porque é o coordenador responsável por esta manutenção."));
        }
    }

    // ---------- 2. Resumo do dia ----------

    sealed class Resumo(Usuario pessoa)
    {
        public Usuario Pessoa { get; } = pessoa;
        public List<OrdemServico> Atrasadas { get; } = new();
        public List<OrdemServico> Atencao { get; } = new();
        public List<Finalizacao> Aprovacoes { get; } = new();
    }

    async Task ResumoDoDiaAsync(AppDb db, CancellationToken parar)
    {
        var agora = DateTime.Now;
        if (agora.TimeOfDay < email.HoraResumo || agora.DayOfWeek == DayOfWeek.Sunday) return;
        var dia = agora.ToString("yyyy-MM-dd");
        var pessoas = await PessoasAsync(db, parar);
        // Atrasadas + as que vencem em até 7 dias (mesma regra da lista de OS: OrdemServico.Situacao).
        var limite = agora.Date.AddDays(OrdemServico.DiasAtencao + 1);
        var ordens = (await db.Ordens.Include(o => o.Equipamento)
                .Where(o => o.Status != StatusOS.Concluida && o.DataConclusao < limite).ToListAsync(parar))
            .OrderBy(o => o.DataConclusao).ThenBy(o => o.Numero).ToList();
        var aprovacoes = await db.Finalizacoes.Include(f => f.Equipamento).Include(f => f.Tecnico)
            .Where(f => f.StatusRevisao == StatusRevisao.Aguardando).OrderBy(f => f.ConcluidaEm).ToListAsync(parar);

        var resumos = new Dictionary<int, Resumo>();
        Resumo De(Usuario u) => resumos.TryGetValue(u.Id, out var r) ? r : resumos[u.Id] = new Resumo(u);
        foreach (var o in ordens)
        {
            var area = o.Equipamento?.CelulaId;
            var atrasada = o.Atrasada(agora);
            var tecnicos = Tecnicos(pessoas, o.TecnicoId, area);
            // Área sem técnico: alguém precisa saber — o aviso vai para o coordenador.
            foreach (var p in tecnicos.Count > 0 ? tecnicos : Coordenadores(pessoas, o.CoordenadorId, area))
                (atrasada ? De(p).Atrasadas : De(p).Atencao).Add(o);
            if (atrasada)
                foreach (var c in Coordenadores(pessoas, o.CoordenadorId, area))
                    if (!De(c).Atrasadas.Contains(o)) De(c).Atrasadas.Add(o);
        }
        foreach (var f in aprovacoes)
            foreach (var c in Coordenadores(pessoas, f.CoordenadorId, f.Equipamento?.CelulaId)) De(c).Aprovacoes.Add(f);

        foreach (var r in resumos.Values.Where(r => r.Pessoa.RecebeEmail).OrderBy(r => r.Pessoa.Id))
        {
            var referencia = $"{dia}|{r.Pessoa.Id}";
            if (await JaFoiAsync(db, "resumo", referencia, parar)) continue;
            await EnviarAsync(db, "resumo", referencia, [r.Pessoa], AssuntoResumo(r), HtmlResumo(r, agora));
        }
    }

    static string AssuntoResumo(Resumo r)
    {
        var partes = new List<string>();
        if (r.Aprovacoes.Count > 0) partes.Add(r.Aprovacoes.Count == 1 ? "1 conclusão aguardando sua aprovação" : $"{r.Aprovacoes.Count} conclusões aguardando sua aprovação");
        if (r.Atrasadas.Count > 0) partes.Add(r.Atrasadas.Count == 1 ? "1 OS atrasada" : $"{r.Atrasadas.Count} OS atrasadas");
        if (r.Atencao.Count > 0) partes.Add(r.Atencao.Count == 1 ? "1 OS vence em até 7 dias" : $"{r.Atencao.Count} OS vencem em até 7 dias");
        return "MS Manutenção — " + string.Join(" · ", partes);
    }

    string HtmlResumo(Resumo r, DateTime agora)
    {
        var sb = new StringBuilder();
        sb.Append($"<p>Olá, {H(Primeiro(r.Pessoa))}. Este é o resumo das manutenções de {agora:dd/MM/yyyy}.</p>");
        if (r.Aprovacoes.Count > 0)
            sb.Append(Quadro("Aguardando sua aprovação", "#6D28D9", ["OS", "Equipamento", "Técnico", "Concluída em", ""],
                r.Aprovacoes.Select(f => new[]
                {
                    Link(f.NumeroOS, $"/Finalizacao/Ver/{f.Id}"), H(f.Equipamento?.Descricao ?? "-"), H(f.Tecnico?.NomeCompleto ?? "-"),
                    f.ConcluidaEm.ToString("dd/MM/yyyy HH:mm"), Link("Revisar", $"/Finalizacao/Ver/{f.Id}"),
                }), r.Aprovacoes.Count));
        if (r.Atrasadas.Count > 0)
            sb.Append(Quadro("Atrasadas", "#BE123C", ["OS", "Equipamento", "Localização", "Data da manutenção", "Atraso"],
                r.Atrasadas.Select(o => new[]
                {
                    Link(o.Numero, $"/Ordens/Editar/{o.Id}"), H(o.Equipamento?.Descricao ?? "-"), H(o.Equipamento?.Localizacao ?? "-"),
                    o.DataConclusao.ToString("dd/MM/yyyy"), Dias((agora.Date - o.DataConclusao.Date).Days),
                }), r.Atrasadas.Count));
        if (r.Atencao.Count > 0)
            sb.Append(Quadro("Vencem em até 7 dias (atenção)", "#C2410C", ["OS", "Equipamento", "Localização", "Data da manutenção", "Faltam"],
                r.Atencao.Select(o => new[]
                {
                    Link(o.Numero, $"/Ordens/Editar/{o.Id}"), H(o.Equipamento?.Descricao ?? "-"), H(o.Equipamento?.Localizacao ?? "-"),
                    o.DataConclusao.ToString("dd/MM/yyyy"), Faltam((o.DataConclusao.Date - agora.Date).Days),
                }), r.Atencao.Count));
        sb.Append(Botao("Abrir o sistema", "/Ordens"));
        var papel = r.Pessoa.Grupo switch { Grupo.Tecnico => "técnico", Grupo.Coordenador => "coordenador", _ => "responsável" };
        var area = r.Pessoa.Celula?.Nome is string a ? $" da área {a}" : "";
        var hora = email.HoraResumo.ToString(@"hh\:mm");
        return Moldura($"Resumo do dia — {agora:dd/MM/yyyy}", sb.ToString(),
            $"Você recebe este resumo todo dia às {hora} (menos domingo) porque é {papel}{area}.");
    }

    // ---------- 3. Manutenção preditiva (e-mail separado do resumo das OS) ----------

    // Um aviso = uma peça, num ciclo (data de instalação), numa situação (Atenção ou Trocar), para uma pessoa.
    // Cada pessoa recebe UM e-mail com todas as peças novas dela; cada peça fica registrada em EmailsEnviados
    // (Tipo "preditiva", Referencia "peça|instalada|atencao ou trocar|usuário") para não avisar de novo.
    sealed record AvisoPeca(PecaPreditiva Peca, bool Trocar, string Referencia);

    async Task PreditivaAsync(AppDb db, CancellationToken parar)
    {
        var agora = DateTime.Now;
        if (agora.TimeOfDay < email.HoraResumo || agora.DayOfWeek == DayOfWeek.Sunday) return;
        var hoje = agora.Date;
        var pecas = (await db.PecasPreditivas.Include(p => p.Equipamento).ToListAsync(parar))
            .Where(p => p.Equipamento != null && p.Equipamento.Status != StatusEquipamento.Inativo && p.Situacao(hoje) != SituacaoPrazo.EmDia)
            .OrderBy(p => p.TrocarEm).ThenBy(p => p.Equipamento!.Codigo).ThenBy(p => p.Nome).ToList();
        if (pecas.Count == 0) return;
        var pessoas = await PessoasAsync(db, parar);

        var avisos = new Dictionary<Usuario, List<AvisoPeca>>();
        foreach (var p in pecas)
        {
            var area = p.Equipamento!.CelulaId;
            var trocar = p.Situacao(hoje) == SituacaoPrazo.Atrasada;
            var tecnicos = Tecnicos(pessoas, null, area);
            var para = tecnicos.Count > 0 ? tecnicos : Coordenadores(pessoas, null, area);
            if (trocar) para = para.Union(Coordenadores(pessoas, null, area)).ToList();
            foreach (var u in QueRecebem(para))
            {
                var referencia = $"{p.Id}|{p.InstaladaEm:yyyy-MM-dd}|{(trocar ? "trocar" : "atencao")}|{u.Id}";
                if (await JaFoiAsync(db, "preditiva", referencia, parar)) continue;
                if (!avisos.TryGetValue(u, out var lista)) avisos[u] = lista = new();
                lista.Add(new AvisoPeca(p, trocar, referencia));
            }
        }

        foreach (var (pessoa, lista) in avisos.OrderBy(a => a.Key.Id))
        {
            var assunto = AssuntoPreditiva(lista);
            var html = HtmlPreditiva(pessoa, lista, hoje);
            string? erro = null;
            try { await email.EnviarAsync([pessoa.Email.Trim()], assunto, html); }
            catch (Exception e)
            {
                erro = e.Message;
                log.LogWarning(e, "Aviso por e-mail não enviado: {Assunto}", assunto);
            }
            foreach (var a in lista)
                db.EmailsEnviados.Add(new EmailEnviado
                {
                    EnviadoEm = DateTime.Now, Tipo = "preditiva", Referencia = a.Referencia, Para = pessoa.Email.Trim(),
                    Assunto = assunto, Teste = email.EmTeste, Ok = erro == null, Erro = erro,
                });
            await db.SaveChangesAsync(parar);
        }
    }

    static string AssuntoPreditiva(List<AvisoPeca> lista)
    {
        int trocar = lista.Count(a => a.Trocar), perto = lista.Count - trocar;
        var partes = new List<string>();
        if (trocar > 0) partes.Add(trocar == 1 ? "1 peça para trocar" : $"{trocar} peças para trocar");
        if (perto > 0) partes.Add(perto == 1 ? "1 peça perto de trocar" : $"{perto} peças perto de trocar");
        return "MS Manutenção — Preditiva: " + string.Join(" · ", partes);
    }

    string HtmlPreditiva(Usuario pessoa, List<AvisoPeca> lista, DateTime hoje)
    {
        string[] Linha(AvisoPeca a, string prazo) =>
        [
            Link(a.Peca.Equipamento!.Descricao, $"/Equipamentos/Editar/{a.Peca.EquipamentoId}?aba=%23aba-preditiva"),
            H(a.Peca.Equipamento.Localizacao), $"<b>{H(a.Peca.Nome)}</b>", H(a.Peca.Vida()), a.Peca.TrocarEm.ToString("dd/MM/yyyy"), prazo,
        ];
        var trocar = lista.Where(a => a.Trocar).ToList();
        var perto = lista.Where(a => !a.Trocar).ToList();
        var sb = new StringBuilder();
        sb.Append($"<p>Olá, {H(Primeiro(pessoa))}. Estas peças da manutenção preditiva das máquinas precisam da sua atenção.</p>");
        if (trocar.Count > 0)
            sb.Append(Quadro("Chegou a hora de trocar", "#BE123C", ["Equipamento", "Localização", "Peça", "Dura", "Trocar em", "Atraso"],
                trocar.Select(a => Linha(a, a.Peca.DiasRestantes(hoje) is var d && d < 0 ? Dias(-d) : "hoje")), trocar.Count));
        if (perto.Count > 0)
            sb.Append(Quadro($"Perto de trocar (já usou {PecaPreditiva.AtencaoPercentual}% da vida útil)", "#C2410C",
                ["Equipamento", "Localização", "Peça", "Dura", "Trocar em", "Faltam"],
                perto.Select(a => Linha(a, Faltam(a.Peca.DiasRestantes(hoje)))), perto.Count));
        sb.Append("<p style=\"font-size:13px\">Depois de trocar, abra a máquina no sistema, aba <b>Manutenção preditiva</b>, e clique em <b>Trocada hoje</b>.</p>");
        sb.Append(Botao("Abrir Equipamentos", "/Equipamentos"));
        var papel = pessoa.Grupo switch { Grupo.Tecnico => "técnico", Grupo.Coordenador => "coordenador", _ => "responsável" };
        var area = pessoa.Celula?.Nome is string ar ? $" da área {ar}" : "";
        return Moldura("Manutenção preditiva — peças para trocar", sb.ToString(),
            $"Você recebe este aviso porque é {papel}{area}. Cada peça avisa uma vez quando fica perto de trocar e outra no dia da troca. É separado do resumo das OS.");
    }

    // ---------- Peças do e-mail (estilo inline: o Outlook ignora CSS de fora) ----------

    static string H(string? texto) => WebUtility.HtmlEncode(texto ?? "");

    static string Primeiro(Usuario u) =>
        CultureInfo.GetCultureInfo("pt-BR").TextInfo.ToTitleCase((u.PrimeiroNome is { Length: > 0 } n ? n : u.Login).ToLowerInvariant());

    static string Ola(List<Usuario> para) => para.Count == 1 ? $"Olá, {H(Primeiro(para[0]))}." : "Olá.";

    static string Dias(int n) => n == 1 ? "1 dia" : $"{n} dias";

    static string Faltam(int n) => n <= 0 ? "hoje" : n == 1 ? "amanhã" : $"{n} dias";

    string Link(string texto, string caminho) =>
        $"<a href=\"{H(email.EnderecoSistema + caminho)}\" style=\"color:#6D28D9;font-weight:700;text-decoration:none\">{H(texto)}</a>";

    string Botao(string texto, string caminho) =>
        $"<p style=\"margin:22px 0 6px\"><a href=\"{H(email.EnderecoSistema + caminho)}\" style=\"background:#6D28D9;color:#ffffff;padding:10px 18px;border-radius:4px;text-decoration:none;font-weight:700;display:inline-block\">{H(texto)}</a></p>";

    static string Quadro(string titulo, string cor, string[] cabecalho, IEnumerable<string[]> linhas, int total)
    {
        var sb = new StringBuilder();
        sb.Append($"<h3 style=\"margin:22px 0 8px;font-size:15px;color:{cor}\">{H(titulo)} ({total})</h3>");
        sb.Append("<table cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse;width:100%;font-size:13px\"><tr>");
        foreach (var c in cabecalho)
            sb.Append($"<th align=\"left\" style=\"border-bottom:2px solid #E0DFE8;color:#6D28D9;white-space:nowrap\">{H(c)}</th>");
        sb.Append("</tr>");
        var n = 0;
        foreach (var linha in linhas.Take(MaxLinhas))
        {
            sb.Append($"<tr style=\"background:{(n++ % 2 == 1 ? "#F4F4F8" : "#ffffff")}\">");
            foreach (var c in linha) sb.Append($"<td style=\"border-bottom:1px solid #e9ecef\">{c}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</table>");
        if (total > MaxLinhas) sb.Append($"<p style=\"font-size:12px;color:#52525B\">… e mais {total - MaxLinhas}. Veja todas no sistema.</p>");
        return sb.ToString();
    }

    static string Moldura(string titulo, string corpo, string rodape) => $"""
        <div style="font-family:Roboto,'Segoe UI',Arial,sans-serif;color:#27272A;font-size:14px;max-width:780px">
          <div style="background:#1A1740;padding:14px 18px;border-bottom:4px solid #A78BFA">
            <span style="color:#ffffff;font-weight:700;font-size:20px">MS</span>
            <span style="color:#A78BFA;font-size:15px;margin-left:6px">Gestão de Manutenção</span>
          </div>
          <div style="padding:16px 4px 4px">
            <h2 style="color:#6D28D9;font-weight:400;font-size:20px;margin:0 0 12px">{H(titulo)}</h2>
            {corpo}
          </div>
          <div style="border-top:1px solid #E0DFE8;margin-top:18px;padding:10px 4px;font-size:12px;color:#52525B">
            {H(rodape)} Este é um aviso automático do MS — Gestão de Manutenção.
          </div>
        </div>
        """;
}
