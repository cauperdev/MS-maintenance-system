// ============================================================================
// INÍCIO DO SISTEMA — primeiro arquivo que roda quando o MS liga.
// ONDE MEXER:
//   1. Idioma e formato de datas/números ...... cultura "pt-BR" (logo abaixo)
//   2. Quem precisa de login .................. AddRazorPages (sem login só as telas Conta/Login e Erro)
//   3. Onde fica o banco de dados ............. "dados/ms.db" (ou ConnectionStrings:Ms no appsettings.json)
//   4. Tempo que o login dura (12 h) .......... AddCookie → ExpireTimeSpan
//   5. Serviços do sistema .................... AddScoped/AddSingleton (Regras, Auditoria, Fotos, Rede...)
//      Login pela rede (AD) ................... "AD" no appsettings.json (Servicos/Rede.cs)
//      HTTPS (cadeado) ........................ "Https" no appsettings.json (Servicos/Https.cs)
//      Avisos por e-mail ...................... "Email" no appsettings.json (Servicos/Email.cs e Servicos/Avisos.cs)
//      Saldo SAP no Spare Parts ............... "Sap" no appsettings.json (Servicos/Sap.cs)
//   6. Atualização automática do banco ........ bloco "using (var scope ...)": acrescenta colunas novas em bancos
//      antigos. Criou um campo novo em Modelos/Entidades.cs? Acrescente aqui o ALTER TABLE dele.
//   7. Página de erro ......................... UseExceptionHandler("/Erro")
// Porta/endereço: iniciar.bat usa 5100; o VS Code usa Properties/launchSettings.json.
// ============================================================================
using System.Globalization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Servicos;

// ---------- 1. IDIOMA E FORMATO (pt-BR: datas dd/mm/aaaa, vírgula nos decimais) ----------
var cultura = new CultureInfo("pt-BR");
CultureInfo.DefaultThreadCurrentCulture = cultura;
CultureInfo.DefaultThreadCurrentUICulture = cultura;

var builder = WebApplication.CreateBuilder(args);

// ---------- 2. TELAS E LOGIN (todas as telas pedem login, menos Login e Erro) ----------
builder.Services.AddRazorPages(o =>
    {
        o.Conventions.AuthorizeFolder("/");
        o.Conventions.AllowAnonymousToPage("/Conta/Login");
        o.Conventions.AllowAnonymousToPage("/Erro");
    })
    .AddMvcOptions(o =>
    {
        o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
        // Voltar de um registro para a lista com o mesmo filtro (Servicos/VoltaParaLista.cs).
        o.Filters.Add<MS.Servicos.VoltaParaLista>();
        // Mensagens automáticas de campo inválido em português (o padrão do ASP.NET é em inglês).
        var m = o.ModelBindingMessageProvider;
        m.SetValueMustNotBeNullAccessor(_ => "Preencha este campo.");
        m.SetMissingBindRequiredValueAccessor(campo => $"Preencha o campo {campo}.");
        m.SetMissingKeyOrValueAccessor(() => "Preencha este campo.");
        m.SetMissingRequestBodyRequiredValueAccessor(() => "Preencha os campos do formulário.");
        m.SetAttemptedValueIsInvalidAccessor((valor, campo) => $"O valor \"{valor}\" não é válido para {campo}.");
        m.SetNonPropertyAttemptedValueIsInvalidAccessor(valor => $"O valor \"{valor}\" não é válido.");
        m.SetUnknownValueIsInvalidAccessor(campo => $"Valor inválido em {campo}.");
        m.SetNonPropertyUnknownValueIsInvalidAccessor(() => "Valor inválido.");
        m.SetValueIsInvalidAccessor(valor => $"O valor \"{valor}\" não é válido.");
        m.SetValueMustBeANumberAccessor(campo => $"{campo} precisa ser um número.");
        m.SetNonPropertyValueMustBeANumberAccessor(() => "Use um número.");
    });

// ---------- 3. BANCO DE DADOS ----------
builder.Services.AddDbContext<AppDb>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Ms") ?? "Data Source=dados/ms.db"));

// ---------- 4. SESSÃO DO LOGIN (cookie de 12 h, renovado enquanto a pessoa usa) ----------
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Conta/Login";
        o.AccessDeniedPath = "/Conta/Negado";
        o.ExpireTimeSpan = TimeSpan.FromHours(12);
        o.SlidingExpiration = true;
    });

// Listas grandes: "marcar todos" envia um campo por linha (ex.: 1.293 planos), e o padrão do ASP.NET
// recusa formulários com mais de 1.024 campos (erro 400).
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.ValueCountLimit = 100_000);

// ---------- 5. SERVIÇOS DO SISTEMA (pasta Servicos) ----------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Auditoria>();
builder.Services.AddScoped<Regras>();
builder.Services.AddScoped<Estoque>();             // Spare Parts — histórico de uso das peças nas OS (Servicos/Estoque.cs)
builder.Services.AddSingleton<Fotos>();
builder.Services.AddSingleton<ManuaisMaquina>();   // manual em PDF de cada máquina (dados/manuais) — Servicos/ManuaisMaquina.cs
builder.Services.AddSingleton<PaginaAutomatica>(); // página do manual achada pelo texto da tarefa — Servicos/PaginaAutomatica.cs
builder.Services.AddSingleton<Manual>();           // slide do fluxo da tela Manual (dados/manual) — Servicos/Manual.cs
builder.Services.AddSingleton<Rede>();            // login pela rede (AD) — Servicos/Rede.cs
builder.Services.AddSingleton<TentativasLogin>();  // pausa depois de várias senhas erradas
builder.Services.AddSingleton<Email>();            // envio de e-mail (Outlook deste PC ou SMTP) — "Email" no appsettings.json
builder.Services.AddHostedService<AvisosPorEmail>(); // avisos por e-mail: aprovação na hora + resumo diário (Servicos/Avisos.cs)
builder.Services.AddHttpClient();                    // chamadas à nuvem das etiquetas eletrônicas
builder.Services.AddSingleton<ConfigEtiquetas>();    // etiquetas eletrônicas (e-paper) — "Etiquetas" no appsettings.json
builder.Services.AddHostedService<EtiquetasEletronicas>(); // desenha e manda a manutenção mais próxima (Servicos/Etiquetas.cs)
builder.Services.AddSingleton<Sap>();                // saldo no SAP dos códigos do Spare Parts, só leitura — "Sap" no appsettings.json

// ---------- HTTPS (cadeado para quem vem pela rede — Servicos/Https.cs; "Https" no appsettings.json) ----------
// Mantém o endereço do --urls (localhost = só este PC; 0.0.0.0 = rede) e acrescenta a porta do https.
var certificado = Https.Certificado(builder.Configuration, builder.Environment.ContentRootPath);
var portaHttps = builder.Configuration.GetValue("Https:Porta", 5463);
if (certificado != null)
{
    var url = (builder.Configuration["urls"] ?? "http://localhost:5100").Split(';')[0].Trim();
    var naRede = url.Contains("0.0.0.0") || url.Contains("://*") || url.Contains("://+");
    var portaHttp = new Uri(System.Text.RegularExpressions.Regex.Replace(url, @"://(0\.0\.0\.0|\*|\+)", "://localhost")).Port;
    builder.WebHost.ConfigureKestrel(k =>
    {
        if (naRede) { k.ListenAnyIP(portaHttp); k.ListenAnyIP(portaHttps, l => l.UseHttps(certificado)); }
        else { k.ListenLocalhost(portaHttp); k.ListenLocalhost(portaHttps, l => l.UseHttps(certificado)); }
    });
}

var app = builder.Build();

// ---------- 6. ATUALIZAÇÃO AUTOMÁTICA DO BANCO (roda toda vez que o sistema liga) ----------
Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "dados"));
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    db.Database.EnsureCreated();
    // Modo WAL (21/10/2026): ler e gravar ao mesmo tempo não se atrapalham. No modo antigo, a leitura das etiquetas
    // eletrônicas (ou de outra tela) travava a gravação ("database is locked") e a tela mostrava "Algo deu errado".
    // Fica gravado no arquivo do banco; com outro MS ligado no mesmo banco não troca agora, troca na próxima vez.
    try { db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL"); } catch (Exception) { }
    Seed.Run(db);
    Acesso.Carregar(db);
    // Campos do lembrete em bancos criados antes deles.
    var colunasOs = db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Ordens')").ToList();
    if (!colunasOs.Contains("DataLembrete"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Ordens\" ADD COLUMN \"DataLembrete\" TEXT NULL");
    if (!colunasOs.Contains("DataProxima"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Ordens\" ADD COLUMN \"DataProxima\" TEXT NULL");
    if (!colunasOs.Contains("LembreteDias"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Ordens\" ADD COLUMN \"LembreteDias\" INTEGER NULL");
    // Motivo do retorno da conclusão (aba "Motivo do retorno" da OS) em bancos criados antes dele.
    if (!colunasOs.Contains("MotivoRetorno"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Ordens\" ADD COLUMN \"MotivoRetorno\" TEXT NULL");
    if (!colunasOs.Contains("RetornadaEm"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Ordens\" ADD COLUMN \"RetornadaEm\" TEXT NULL");
    if (!colunasOs.Contains("RetornadaPor"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Ordens\" ADD COLUMN \"RetornadaPor\" TEXT NULL");
    // Modelo do equipamento (opcional) em bancos criados antes dele.
    var colunasEq = db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Equipamentos')").ToList();
    if (!colunasEq.Contains("Modelo"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Equipamentos\" ADD COLUMN \"Modelo\" TEXT NULL");
    // Prédio (obrigatório no cadastro): máquinas antigas ficam em branco até alguém preencher.
    if (!colunasEq.Contains("Predio"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Equipamentos\" ADD COLUMN \"Predio\" TEXT NOT NULL DEFAULT ''");
    // IRF (código da Samsung, opcional): um por máquina; as que não vieram da Samsung ficam em branco.
    if (!colunasEq.Contains("Irf"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Equipamentos\" ADD COLUMN \"Irf\" TEXT NULL");
    db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Equipamentos_Irf\" ON \"Equipamentos\" (\"Irf\") WHERE \"Irf\" IS NOT NULL");
    // Etiqueta eletrônica (opcional): uma por máquina; as que ainda não têm etiqueta ficam em branco.
    if (!colunasEq.Contains("Etiqueta"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Equipamentos\" ADD COLUMN \"Etiqueta\" TEXT NULL");
    db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Equipamentos_Etiqueta\" ON \"Equipamentos\" (\"Etiqueta\") WHERE \"Etiqueta\" IS NOT NULL");
    // Número de série (opcional): as máquinas que já existiam ficam em branco.
    if (!colunasEq.Contains("NumeroSerie"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Equipamentos\" ADD COLUMN \"NumeroSerie\" TEXT NULL");
    // Foto da máquina (opcional): as que já existiam ficam sem foto.
    if (!colunasEq.Contains("Foto"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Equipamentos\" ADD COLUMN \"Foto\" TEXT NULL");
    // Manual da máquina em PDF (opcional): as que já existiam ficam sem manual.
    foreach (var coluna in new[] { "Manual", "ManualNome", "ManualEm", "ManualPor" })
        if (!colunasEq.Contains(coluna))
            db.Database.ExecuteSqlRaw("ALTER TABLE \"Equipamentos\" ADD COLUMN \"" + coluna + "\" TEXT NULL");
    // Página do manual em cada tarefa do plano (opcional).
    if (!db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('TarefasPlano')").ToList().Contains("Pagina"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"TarefasPlano\" ADD COLUMN \"Pagina\" INTEGER NULL");
    // Logo do cliente da área (opcional, cartões do Dashboard): as áreas que já existiam ficam sem logo.
    if (!db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Celulas')").ToList().Contains("Logo"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Celulas\" ADD COLUMN \"Logo\" TEXT NULL");
    // Login pela rede (AD): quem já existia com login que não é matrícula (admin, contas de demonstração)
    // vira conta local e continua entrando com a senha do sistema; matrículas entram pela rede.
    if (!db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Usuarios')").ToList().Contains("ContaLocal"))
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Usuarios\" ADD COLUMN \"ContaLocal\" INTEGER NOT NULL DEFAULT 0");
        db.Database.ExecuteSqlRaw("UPDATE \"Usuarios\" SET \"ContaLocal\" = 1 WHERE \"Login\" GLOB '*[^0-9]*'");
    }
    // Avisos por e-mail por pessoa (Servicos/Avisos.cs): todos continuam recebendo.
    if (!db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Usuarios')").ToList().Contains("RecebeEmail"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Usuarios\" ADD COLUMN \"RecebeEmail\" INTEGER NOT NULL DEFAULT 1");
    // Código da peça e hora em que a foto foi tirada, em bancos criados antes deles.
    if (!db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Pecas')").ToList().Contains("Codigo"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Pecas\" ADD COLUMN \"Codigo\" TEXT NULL");
    if (!db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Imagens')").ToList().Contains("TiradaEm"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Imagens\" ADD COLUMN \"TiradaEm\" TEXT NULL");
    // Tabelas do Spare Parts (estoque de peças) em bancos criados antes dele.
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "SpareParts" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_SpareParts" PRIMARY KEY AUTOINCREMENT,
            "Codigo" TEXT NOT NULL, "Descricao" TEXT NOT NULL, "Local" TEXT NULL,
            "Estoque" REAL NOT NULL, "EstoqueMinimo" REAL NULL, "Ativa" INTEGER NOT NULL, "CriadaEm" TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS "MovimentosPeca" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_MovimentosPeca" PRIMARY KEY AUTOINCREMENT,
            "SparePartId" INTEGER NOT NULL, "Quando" TEXT NOT NULL, "Tipo" INTEGER NOT NULL,
            "Quantidade" REAL NOT NULL, "Saldo" REAL NOT NULL,
            "OrdemServicoId" INTEGER NULL, "FinalizacaoId" INTEGER NULL, "EquipamentoId" INTEGER NULL,
            "Usuario" TEXT NOT NULL, "Observacao" TEXT NULL,
            CONSTRAINT "FK_MovimentosPeca_SpareParts_SparePartId" FOREIGN KEY ("SparePartId") REFERENCES "SpareParts" ("Id") ON DELETE CASCADE,
            CONSTRAINT "FK_MovimentosPeca_Ordens_OrdemServicoId" FOREIGN KEY ("OrdemServicoId") REFERENCES "Ordens" ("Id") ON DELETE SET NULL,
            CONSTRAINT "FK_MovimentosPeca_Finalizacoes_FinalizacaoId" FOREIGN KEY ("FinalizacaoId") REFERENCES "Finalizacoes" ("Id") ON DELETE SET NULL,
            CONSTRAINT "FK_MovimentosPeca_Equipamentos_EquipamentoId" FOREIGN KEY ("EquipamentoId") REFERENCES "Equipamentos" ("Id") ON DELETE SET NULL);
        CREATE INDEX IF NOT EXISTS "IX_MovimentosPeca_SparePartId" ON "MovimentosPeca" ("SparePartId");
        CREATE INDEX IF NOT EXISTS "IX_MovimentosPeca_OrdemServicoId" ON "MovimentosPeca" ("OrdemServicoId");
        CREATE INDEX IF NOT EXISTS "IX_MovimentosPeca_FinalizacaoId" ON "MovimentosPeca" ("FinalizacaoId");
        CREATE INDEX IF NOT EXISTS "IX_MovimentosPeca_EquipamentoId" ON "MovimentosPeca" ("EquipamentoId");
        """);
    // Equipamento da peça (tipo de máquina, opcional) em bancos criados antes dele.
    if (!db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('SpareParts')").ToList().Contains("Maquina"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"SpareParts\" ADD COLUMN \"Maquina\" TEXT NULL");
    // Cada área tem as suas peças (13/10/2026): a peça ganha a área e o código passa a ser único dentro da área.
    // As peças que já existiam ficam na área das máquinas das OS em que foram usadas (sem uso = sem área).
    if (!db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('SpareParts')").ToList().Contains("CelulaId"))
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE \"SpareParts\" ADD COLUMN \"CelulaId\" INTEGER NULL");
        db.Database.ExecuteSqlRaw("UPDATE \"SpareParts\" SET \"CelulaId\" = (SELECT e.\"CelulaId\" FROM \"MovimentosPeca\" m " +
            "JOIN \"Equipamentos\" e ON e.\"Id\" = m.\"EquipamentoId\" WHERE m.\"SparePartId\" = \"SpareParts\".\"Id\" AND e.\"CelulaId\" IS NOT NULL " +
            "GROUP BY e.\"CelulaId\" ORDER BY COUNT(*) DESC LIMIT 1)");
    }
    db.Database.ExecuteSqlRaw("""
        DROP INDEX IF EXISTS "IX_SpareParts_Codigo";
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_SpareParts_CelulaId_Codigo" ON "SpareParts" ("CelulaId", "Codigo");
        """);
    // Tabela do Histórico JMS (Work Orders do JMS antigo) em bancos criados antes dela.
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "WorkOrdersJms" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_WorkOrdersJms" PRIMARY KEY AUTOINCREMENT,
            "WorkOrder" TEXT NOT NULL, "Codigo" TEXT NOT NULL, "Equipamento" TEXT NULL, "Periodicidade" TEXT NULL, "Tipo" TEXT NULL,
            "CriadaEm" TEXT NULL, "DataManutencao" TEXT NULL, "Localizacao" TEXT NULL, "Celula" TEXT NULL,
            "Tecnico" TEXT NULL, "Coordenador" TEXT NULL, "TempoSugerido" REAL NULL);
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_WorkOrdersJms_WorkOrder" ON "WorkOrdersJms" ("WorkOrder");
        CREATE INDEX IF NOT EXISTS "IX_WorkOrdersJms_Codigo" ON "WorkOrdersJms" ("Codigo");
        """);
    // Registro dos avisos por e-mail (Servicos/Avisos.cs) em bancos criados antes dele.
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "EmailsEnviados" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_EmailsEnviados" PRIMARY KEY AUTOINCREMENT,
            "EnviadoEm" TEXT NOT NULL, "Tipo" TEXT NOT NULL, "Referencia" TEXT NOT NULL, "Para" TEXT NOT NULL,
            "Assunto" TEXT NOT NULL, "Teste" INTEGER NOT NULL, "Ok" INTEGER NOT NULL, "Erro" TEXT NULL);
        CREATE INDEX IF NOT EXISTS "IX_EmailsEnviados_Tipo_Referencia" ON "EmailsEnviados" ("Tipo", "Referencia");
        """);
    // Ordem de trabalho = número da OS do plano (antes era SM-nº, compartilhada entre planos do mesmo nome).
    db.Database.ExecuteSqlRaw("UPDATE \"Planos\" SET \"OrdemTrabalho\" = (SELECT o.\"Numero\" FROM \"Ordens\" o WHERE o.\"PlanoId\" = \"Planos\".\"Id\" ORDER BY o.\"Id\" LIMIT 1) " +
        "WHERE \"OrdemTrabalho\" NOT LIKE 'OS-%' AND EXISTS (SELECT 1 FROM \"Ordens\" o WHERE o.\"PlanoId\" = \"Planos\".\"Id\")");
    // Número da OS deixou de ser único: as periodicidades do mesmo equipamento + plano dividem o número.
    var indiceNumero = db.Database.SqlQueryRaw<string>("SELECT sql AS Value FROM sqlite_master WHERE type = 'index' AND name = 'IX_Ordens_Numero'").ToList().FirstOrDefault();
    if (indiceNumero != null && indiceNumero.Contains("UNIQUE"))
    {
        db.Database.ExecuteSqlRaw("DROP INDEX \"IX_Ordens_Numero\"");
        db.Database.ExecuteSqlRaw("CREATE INDEX \"IX_Ordens_Numero\" ON \"Ordens\" (\"Numero\")");
    }
    // Uma Ordem de trabalho por equipamento + nome do plano (a menor do grupo); a OS de cada plano acompanha.
    var gruposDivididos = db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM (SELECT 1 FROM \"Planos\" WHERE \"OrdemTrabalho\" LIKE 'OS-%' " +
        "GROUP BY \"EquipamentoId\", UPPER(\"Nome\") HAVING COUNT(DISTINCT \"OrdemTrabalho\") > 1)").ToList().First();
    if (gruposDivididos > 0)
    {
        db.Database.ExecuteSqlRaw("UPDATE \"Ordens\" SET \"Numero\" = (SELECT MIN(p2.\"OrdemTrabalho\") FROM \"Planos\" p1 JOIN \"Planos\" p2 " +
            "ON p2.\"EquipamentoId\" = p1.\"EquipamentoId\" AND UPPER(p2.\"Nome\") = UPPER(p1.\"Nome\") AND p2.\"OrdemTrabalho\" LIKE 'OS-%' " +
            "WHERE p1.\"Id\" = \"Ordens\".\"PlanoId\") " +
            "WHERE \"PlanoId\" IS NOT NULL AND \"Numero\" = (SELECT p.\"OrdemTrabalho\" FROM \"Planos\" p WHERE p.\"Id\" = \"Ordens\".\"PlanoId\")");
        db.Database.ExecuteSqlRaw("UPDATE \"Planos\" SET \"OrdemTrabalho\" = (SELECT MIN(p2.\"OrdemTrabalho\") FROM \"Planos\" p2 " +
            "WHERE p2.\"EquipamentoId\" = \"Planos\".\"EquipamentoId\" AND UPPER(p2.\"Nome\") = UPPER(\"Planos\".\"Nome\") AND p2.\"OrdemTrabalho\" LIKE 'OS-%') " +
            "WHERE \"OrdemTrabalho\" LIKE 'OS-%'");
    }
    var colunasPlano = db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Planos')").ToList();
    // "Lembrar antes do prazo" passou da OS para o plano: o plano herda o lembrete que a OS dele já tinha.
    if (!colunasPlano.Contains("LembreteDias"))
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Planos\" ADD COLUMN \"LembreteDias\" INTEGER NULL");
        db.Database.ExecuteSqlRaw("UPDATE \"Planos\" SET \"LembreteDias\" = (SELECT o.\"LembreteDias\" FROM \"Ordens\" o " +
            "WHERE o.\"PlanoId\" = \"Planos\".\"Id\" AND o.\"LembreteDias\" IS NOT NULL LIMIT 1)");
    }
    // Técnico e coordenador responsáveis passaram da OS para o plano (13/10/2026): o plano herda os que a OS dele já tinha.
    // (Bancos antigos já têm as duas colunas, vazias, de uma versão anterior: por isso a cópia tem marca própria — user_version 2, abaixo.)
    if (!colunasPlano.Contains("TecnicoId"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Planos\" ADD COLUMN \"TecnicoId\" INTEGER NULL");
    if (!colunasPlano.Contains("CoordenadorId"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Planos\" ADD COLUMN \"CoordenadorId\" INTEGER NULL");
    // Conclusão retornada agora sai da Finalização: as antigas "Retornado" saem e as fotos voltam para a OS.
    var retornadas = db.Finalizacoes.Where(f => f.StatusRevisao == MS.Modelos.StatusRevisao.Retornado).ToList();
    if (retornadas.Count > 0)
    {
        var ids = retornadas.Select(f => f.Id).ToList();
        var osExistentes = db.Ordens.Select(o => o.Id).ToHashSet();
        foreach (var img in db.Imagens.Where(i => i.FinalizacaoId != null && ids.Contains(i.FinalizacaoId.Value)).ToList())
        {
            var osId = retornadas.First(f => f.Id == img.FinalizacaoId).OrdemServicoId;
            img.OrdemServicoId = osExistentes.Contains(osId) ? osId : null;
            img.FinalizacaoId = null;
        }
        db.Finalizacoes.RemoveRange(retornadas);
        db.SaveChanges();
    }
    // "Em andamento" deixou de existir: OS nesse status voltam a "Aberta" (as tarefas marcadas continuam marcadas).
    db.Ordens.Where(o => o.Status == MS.Modelos.StatusOS.EmAndamento)
        .ExecuteUpdate(s => s.SetProperty(o => o.Status, MS.Modelos.StatusOS.Aberta));
    // "Parado" deixou de existir: equipamentos antigos com esse status passam para "Em manutenção".
    db.Equipamentos.Where(e => e.Status == MS.Modelos.StatusEquipamento.Parado)
        .ExecuteUpdate(s => s.SetProperty(e => e.Status, MS.Modelos.StatusEquipamento.EmManutencao));
    // Texto em MAIÚSCULA (pedido do usuário, 2026-10-07): o que já estava gravado passa uma vez só; daí em diante o
    // próprio AppDb grava em maiúscula. A marca de "já feito" é o user_version do SQLite (0 → 1).
    if (db.Database.SqlQueryRaw<int>("SELECT user_version AS Value FROM pragma_user_version").AsEnumerable().First() < 1)
    {
        var linhas = db.PassarTudoParaMaiuscula();
        db.Database.ExecuteSqlRaw("PRAGMA user_version = 1");
        app.Logger.LogInformation("Maiúscula: {Linhas} registros antigos convertidos.", linhas);
    }
    // Técnico e coordenador do plano (acima): cópia, uma vez só, dos que já estavam marcados na OS (user_version 1 → 2).
    if (db.Database.SqlQueryRaw<int>("SELECT user_version AS Value FROM pragma_user_version").AsEnumerable().First() < 2)
    {
        db.Database.ExecuteSqlRaw("UPDATE \"Planos\" SET \"TecnicoId\" = (SELECT o.\"TecnicoId\" FROM \"Ordens\" o " +
            "WHERE o.\"PlanoId\" = \"Planos\".\"Id\" AND o.\"TecnicoId\" IS NOT NULL ORDER BY o.\"Id\" LIMIT 1) WHERE \"TecnicoId\" IS NULL");
        db.Database.ExecuteSqlRaw("UPDATE \"Planos\" SET \"CoordenadorId\" = (SELECT o.\"CoordenadorId\" FROM \"Ordens\" o " +
            "WHERE o.\"PlanoId\" = \"Planos\".\"Id\" AND o.\"CoordenadorId\" IS NOT NULL ORDER BY o.\"Id\" LIMIT 1) WHERE \"CoordenadorId\" IS NULL");
        db.Database.ExecuteSqlRaw("PRAGMA user_version = 2");
    }
    // Tarefas de NR (EHS) viraram tarefa do checklist (pedido do usuário, 2026-10-14): as OS abertas recebem, uma vez só,
    // as NR do plano no fim do checklist, sem desmarcar nada (user_version 2 → 3). Daí em diante o Cronograma mantém.
    if (db.Database.SqlQueryRaw<int>("SELECT user_version AS Value FROM pragma_user_version").AsEnumerable().First() < 3)
    {
        var regras = scope.ServiceProvider.GetRequiredService<MS.Servicos.Regras>();
        var planos = db.Planos.Include(p => p.Tarefas).ToList().Where(p => p.Tarefas.Any(t => MS.Servicos.Ehs.EhNr(t.Descricao))).ToDictionary(p => p.Id);
        var ordens = db.Ordens.Include(o => o.Tarefas)
            .Where(o => o.PlanoId != null && o.Status != MS.Modelos.StatusOS.Concluida).ToList()
            .Where(o => planos.ContainsKey(o.PlanoId!.Value)).ToList();
        var acertadas = ordens.Count(o => regras.AcertarNr(o, planos[o.PlanoId!.Value]));
        db.SaveChanges();
        db.Database.ExecuteSqlRaw("PRAGMA user_version = 3");
        app.Logger.LogInformation("Tarefas de NR: {Ordens} OS abertas receberam as NR do plano.", acertadas);
    }
}

// ---------- 7. PÁGINA DE ERRO E ORDEM DE PROCESSAMENTO ----------
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Erro");

// HTTPS: quem vem pela rede (celular, outro PC) em http é levado para https — a senha nunca viaja aberta.
// Quem abre no próprio PC (localhost) continua em http, sem aviso de certificado.
if (certificado != null)
    app.Use(async (ctx, proximo) =>
    {
        var remoto = ctx.Connection.RemoteIpAddress;
        if (remoto is { IsIPv4MappedToIPv6: true }) remoto = remoto.MapToIPv4();
        if (!ctx.Request.IsHttps && remoto != null && !System.Net.IPAddress.IsLoopback(remoto))
        {
            ctx.Response.Redirect($"https://{ctx.Request.Host.Host}:{portaHttps}{ctx.Request.PathBase}{ctx.Request.Path}{ctx.Request.QueryString}",
                permanent: false, preserveMethod: true);
            return;
        }
        await proximo();
    });

// Endereços antigos (favoritos salvos antes da organização em português) levam para os novos:
// /Planos/... → /Cronograma/...   e   /<Tela>/Edit/... → /<Tela>/Editar/...
app.Use(async (ctx, proximo) =>
{
    var caminho = ctx.Request.Path.Value ?? "";
    var novo = System.Text.RegularExpressions.Regex.Replace(caminho, "^/Planos(?=/|$)", "/Cronograma",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    novo = System.Text.RegularExpressions.Regex.Replace(novo, "^(/[^/]+)/Edit(?=/|$)", "$1/Editar",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (novo != caminho) { ctx.Response.Redirect(novo + ctx.Request.QueryString, permanent: true); return; }
    await proximo();
});

// Endereço que não existe (ou registro apagado, ex.: link de uma OS removida): mostra a tela de erro do sistema.
app.UseStatusCodePagesWithReExecute("/Erro", "?codigo={0}");

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.Run();
