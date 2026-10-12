// ============================================================================
// ENVIO DE E-MAIL — usado pelos avisos automáticos (Servicos/Avisos.cs).
// Configuração: seção "Email" do appsettings.json (vale na hora, sem reiniciar o sistema):
//   - Ativo ........ false = não manda nada
//   - Modo ......... "Outlook" = sai pelo Outlook deste computador (conta de quem está logado no Windows; o
//                    sistema e o Outlook precisam estar no mesmo usuário) · "Smtp" = caixa de e-mail de serviço
//                    (dados em "Smtp": servidor, porta, usuário, senha e remetente — pedir à TI)
//   - Teste ........ endereço preenchido = MODO TESTE: todo e-mail vai só para ele, com um aviso de para quem iria.
//                    Apague o endereço ("Teste": "") para mandar de verdade para técnicos e coordenadores.
//   - HoraResumo ... hora do resumo diário (ex.: "07:00")
// ============================================================================
using System.Net;
using System.Net.Mail;

namespace MS.Servicos;

public class Email(IConfiguration config, ILogger<Email> log)
{
    public bool Ativo => config.GetValue("Email:Ativo", false);
    public bool Smtp => string.Equals(config["Email:Modo"], "Smtp", StringComparison.OrdinalIgnoreCase);
    public string? EnderecoTeste => string.IsNullOrWhiteSpace(config["Email:Teste"]) ? null : config["Email:Teste"]!.Trim();
    public bool EmTeste => EnderecoTeste != null;
    public TimeSpan HoraResumo => TimeSpan.TryParse(config["Email:HoraResumo"], out var h) ? h : new TimeSpan(7, 0, 0);
    public string EnderecoSistema => (config["EnderecoSistema"] ?? "http://localhost:5090").TrimEnd('/');

    // E-mail de verdade: tem @ e não é das contas de demonstração (@ms.local).
    public static bool Valido(string? endereco) =>
        !string.IsNullOrWhiteSpace(endereco) && endereco.Contains('@') && !endereco.Trim().EndsWith(".local", StringComparison.OrdinalIgnoreCase);

    // Manda um e-mail em HTML. No modo teste vai só para o endereço de teste, com a lista de quem receberia.
    // Dá erro (exceção) se não conseguiu — quem chama registra e tenta de novo depois.
    public async Task EnviarAsync(IReadOnlyCollection<string> para, string assunto, string html)
    {
        var destino = para;
        if (EnderecoTeste is string teste)
        {
            html = $"""
                <div style="font-family:Roboto,'Segoe UI',Arial,sans-serif;background:#FEF3C7;border:1px solid #FBBF24;border-radius:6px;padding:10px 14px;margin:0 0 16px;font-size:13px;color:#27272A;max-width:752px">
                  <b>MODO TESTE</b> — no envio de verdade este e-mail iria para: <b>{WebUtility.HtmlEncode(string.Join("; ", para))}</b>.<br>
                  Para mandar de verdade, apague o endereço de "Email: Teste" no appsettings.json do sistema.
                </div>
                """ + html;
            assunto = "[TESTE] " + assunto;
            destino = [teste];
        }
        if (Smtp) await EnviarSmtpAsync(destino, assunto, html);
        else await Task.Run(() => EnviarOutlook(destino, assunto, html));
        log.LogInformation("E-mail enviado para {Para}: {Assunto}", string.Join("; ", destino), assunto);
    }

    // Caixa de e-mail de serviço (SMTP com senha, ex.: smtp.office365.com porta 587).
    async Task EnviarSmtpAsync(IReadOnlyCollection<string> para, string assunto, string html)
    {
        var s = config.GetSection("Email:Smtp");
        using var cliente = new SmtpClient(s["Servidor"], s.GetValue("Porta", 587)) { EnableSsl = true };
        if (!string.IsNullOrEmpty(s["Usuario"])) cliente.Credentials = new NetworkCredential(s["Usuario"], s["Senha"]);
        using var msg = new MailMessage { From = new MailAddress(s["Remetente"] is { Length: > 0 } r ? r : s["Usuario"]!), Subject = assunto, Body = html, IsBodyHtml = true };
        foreach (var p in para) msg.To.Add(p);
        await cliente.SendMailAsync(msg);
    }

    // Outlook deste computador (automação do Outlook clássico). Se o Outlook estiver fechado, ele abre escondido,
    // envia e espera a Caixa de Saída esvaziar (até 1 minuto) antes de soltar.
    static void EnviarOutlook(IReadOnlyCollection<string> para, string assunto, string html)
    {
        Exception? erro = null;
        var t = new Thread(() =>
        {
            try
            {
                var tipo = Type.GetTypeFromProgID("Outlook.Application")
                    ?? throw new InvalidOperationException("O Outlook (clássico) não está instalado neste computador.");
                dynamic outlook = Activator.CreateInstance(tipo)!;
                dynamic mapi = outlook.GetNamespace("MAPI");
                dynamic msg = outlook.CreateItem(0); // 0 = e-mail
                msg.To = string.Join("; ", para);
                msg.Subject = assunto;
                msg.HTMLBody = html;
                msg.Send();
                try { mapi.SendAndReceive(false); } catch { /* o Outlook aberto envia sozinho */ }
                dynamic saida = mapi.GetDefaultFolder(4); // 4 = Caixa de Saída
                for (var i = 0; i < 60 && (int)saida.Items.Count > 0; i++) Thread.Sleep(1000);
            }
            catch (Exception e) { erro = e; }
        });
        t.SetApartmentState(ApartmentState.STA); // o Outlook pede thread STA
        t.Start();
        if (!t.Join(TimeSpan.FromMinutes(2))) throw new TimeoutException("O Outlook não respondeu em 2 minutos.");
        if (erro != null) throw erro;
    }
}
