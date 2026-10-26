// ============================================================================
// AVISOS POR E-MAIL — roda sozinho a cada 5 minutos enquanto o sistema está ligado (envio: Servicos/Email.cs).
//   1. Conclusão aguardando aprovação → e-mail NA HORA para o coordenador (uma vez por conclusão).
//   2. Resumo do dia, uma vez por dia a partir de "Email:HoraResumo" (domingo não manda):
//        técnico ...... OS atrasadas + OS que vencem em até 7 dias (atenção)
//        coordenador .. conclusões aguardando a aprovação dele + OS atrasadas
// QUEM RECEBE: o técnico / coordenador marcado na OS. Se a OS não tiver (ou ele não receber e-mail), recebem os que
// são da ÁREA da máquina (campo Área do cadastro do usuário). Uma área NUNCA recebe aviso
// de outra (pedido do usuário em 08/10/2026: nada da SAMSUNG para quem é da AVELL e vice-versa) — antes, área sem
// coordenador mandava para todos os coordenadores. Área sem técnico → o aviso vai para o coordenador da área.
// Área sem ninguém → o aviso não sai. Só usuários ativos, com e-mail de verdade (não @ms.local) e com "Receber os
// avisos por e-mail" marcado.
// Cada envio fica na tabela EmailsEnviados: o mesmo aviso não sai duas vezes; se falhar, tenta de novo (até 3 vezes).
// ONDE MEXER:
//   - De quanto em quanto tempo verifica ..... ExecuteAsync (5 minutos)
//   - Quem recebe ............................ Tecnicos / Coordenadores / DaArea (Usuario.CelulaId)
//   - Texto dos e-mails ...................... AprovacoesAsync (aviso de aprovação) · HtmlResumo / AssuntoResumo (resumo)
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
                }
                catch (OperationCanceledException) { return; }
                catch (Exception e) { log.LogError(e, "Avisos por e-mail"); }
            }
            try { await acordar.WaitAsync(TimeSpan.FromMinutes(5), parar); } catch (OperationCanceledException) { return; }
        }
    }

    // ---------- Quem recebe ----------

    // Usuários ativos, com e-mail de verdade e com "Receber os avisos por e-mail" marcado.
    static async Task<List<Usuario>> PessoasAsync(AppDb db, CancellationToken parar) =>
        (await db.Usuarios.Where(u => u.Ativo && u.RecebeEmail).ToListAsync(parar)).Where(u => Email.Valido(u.Email)).ToList();

    // A pessoa recebe os avisos desta área? (Só os da área do cadastro dela.)
    static bool DaArea(Usuario u, int? area) => area is int a && u.CelulaId == a;

    // Técnico da OS; sem técnico marcado, os técnicos que recebem os avisos da área da máquina.
    static List<Usuario> Tecnicos(List<Usuario> pessoas, int? tecnicoId, int? area)
    {
        var marcado = pessoas.FirstOrDefault(u => u.Id == tecnicoId);
        if (marcado != null) return [marcado];
        return pessoas.Where(u => u.Grupo == Grupo.Tecnico && DaArea(u, area)).ToList();
    }

    // Coordenador da OS; sem coordenador marcado, os coordenadores que recebem os avisos da área (nunca os de outra área).
    static List<Usuario> Coordenadores(List<Usuario> pessoas, int? coordenadorId, int? area)
    {
        var marcado = pessoas.FirstOrDefault(u => u.Id == coordenadorId);
        if (marcado != null) return [marcado];
        return pessoas.Where(u => u.Grupo == Grupo.Coordenador && DaArea(u, area)).ToList();
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
            var para = Coordenadores(pessoas, f.CoordenadorId, f.Equipamento?.CelulaId);
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

        var celulas = await db.Celulas.ToDictionaryAsync(c => c.Id, c => c.Nome, parar);
        foreach (var r in resumos.Values.OrderBy(r => r.Pessoa.Id))
        {
            var referencia = $"{dia}|{r.Pessoa.Id}";
            if (await JaFoiAsync(db, "resumo", referencia, parar)) continue;
            await EnviarAsync(db, "resumo", referencia, [r.Pessoa], AssuntoResumo(r), HtmlResumo(r, agora, celulas));
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

    string HtmlResumo(Resumo r, DateTime agora, IReadOnlyDictionary<int, string> celulas)
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
        var deQuais = r.Pessoa.CelulaId is int id && celulas.GetValueOrDefault(id) is string area ? $" e recebe os avisos da área {area}" : "";
        var hora = email.HoraResumo.ToString(@"hh\:mm");
        return Moldura($"Resumo do dia — {agora:dd/MM/yyyy}", sb.ToString(),
            $"Você recebe este resumo todo dia às {hora} (menos domingo) porque é {papel}{deQuais}. " +
            "Para não receber mais, peça ao administrador para desmarcar \"Receber os avisos por e-mail\" no seu cadastro.");
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
