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
//   6. Atualização automática do banco ........ bloco "using (var scope ...)": acrescenta colunas novas em bancos
//      antigos. Criou um campo novo em Modelos/Entidades.cs? Acrescente aqui o ALTER TABLE dele.
//   7. Página de erro ......................... UseExceptionHandler("/Erro")
// Porta/endereço: iniciar.bat usa 5090; o VS Code usa Properties/launchSettings.json.
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
builder.Services.AddSingleton<Anexos>();           // arquivos da NRE (dados/anexos) — Servicos/Anexos.cs
builder.Services.AddSingleton<Manual>();           // slide do fluxo da tela Manual (dados/manual) — Servicos/Manual.cs
builder.Services.AddSingleton<LogosArea>();        // logo de cada área nos cards (dados/logos-areas) — Servicos/LogosArea.cs
builder.Services.AddHostedService<LimpezaFotos>();
builder.Services.AddSingleton<Rede>();            // login pela rede (AD) — Servicos/Rede.cs
builder.Services.AddSingleton<TentativasLogin>();  // pausa depois de várias senhas erradas
builder.Services.AddSingleton<Email>();            // envio de e-mail (Outlook deste PC ou SMTP) — "Email" no appsettings.json
builder.Services.AddHostedService<AvisosPorEmail>(); // avisos por e-mail: aprovação na hora + resumo diário (Servicos/Avisos.cs)

// ---------- HTTPS (cadeado para quem vem pela rede — Servicos/Https.cs; "Https" no appsettings.json) ----------
// Mantém o endereço do --urls (localhost = só este PC; 0.0.0.0 = rede) e acrescenta a porta do https.
var certificado = Https.Certificado(builder.Configuration, builder.Environment.ContentRootPath);
var portaHttps = builder.Configuration.GetValue("Https:Porta", 5453);
if (certificado != null)
{
    var url = (builder.Configuration["urls"] ?? "http://localhost:5090").Split(';')[0].Trim();
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
    // IRF (nº das máquinas que vieram da Samsung) em bancos criados antes dele.
    if (!colunasEq.Contains("Irf"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Equipamentos\" ADD COLUMN \"Irf\" TEXT NULL");
    // Manutenção preditiva (peças com vida útil, aba do equipamento) em bancos criados antes dela.
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "PecasPreditivas" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_PecasPreditivas" PRIMARY KEY AUTOINCREMENT,
            "EquipamentoId" INTEGER NOT NULL, "Nome" TEXT NOT NULL, "VidaUtil" INTEGER NOT NULL, "Unidade" INTEGER NOT NULL,
            "InstaladaEm" TEXT NOT NULL,
            CONSTRAINT "FK_PecasPreditivas_Equipamentos_EquipamentoId" FOREIGN KEY ("EquipamentoId") REFERENCES "Equipamentos" ("Id") ON DELETE CASCADE);
        CREATE INDEX IF NOT EXISTS "IX_PecasPreditivas_EquipamentoId" ON "PecasPreditivas" ("EquipamentoId");
        """);
    // Login pela rede (AD): quem já existia com login que não é matrícula (admin, contas de demonstração)
    // vira conta local e continua entrando com a senha do sistema; matrículas entram pela rede.
    if (!db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Usuarios')").ToList().Contains("ContaLocal"))
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Usuarios\" ADD COLUMN \"ContaLocal\" INTEGER NOT NULL DEFAULT 0");
        db.Database.ExecuteSqlRaw("UPDATE \"Usuarios\" SET \"ContaLocal\" = 1 WHERE \"Login\" GLOB '*[^0-9]*'");
    }
    // "Receber os avisos por e-mail" (cadastro do usuário) em bancos criados antes dele: todo mundo continua recebendo.
    if (!db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Usuarios')").ToList().Contains("RecebeEmail"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Usuarios\" ADD COLUMN \"RecebeEmail\" INTEGER NOT NULL DEFAULT 1");
    // Cards escondidos na tela "Escolha a área" em bancos criados antes dela: vazio = todas as áreas aparecem.
    if (!db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Usuarios')").ToList().Contains("CardsEscondidos"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Usuarios\" ADD COLUMN \"CardsEscondidos\" TEXT NULL");
    // Código da peça e hora em que a foto foi tirada, em bancos criados antes deles.
    if (!db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Pecas')").ToList().Contains("Codigo"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Pecas\" ADD COLUMN \"Codigo\" TEXT NULL");
    if (!db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Imagens')").ToList().Contains("TiradaEm"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Imagens\" ADD COLUMN \"TiradaEm\" TEXT NULL");
    // Tabelas da NRE em bancos criados antes dela (mesmo formato que o EnsureCreated cria num banco novo).
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "Nres" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_Nres" PRIMARY KEY AUTOINCREMENT,
            "Numero" TEXT NOT NULL, "Maquina" TEXT NULL, "CelulaId" INTEGER NULL, "EquipamentoId" INTEGER NULL, "OrdemServicoId" INTEGER NULL,
            "Tipo" INTEGER NOT NULL, "Descricao" TEXT NOT NULL, "PedidoCompra" TEXT NULL,
            "Status" INTEGER NOT NULL, "SolicitanteId" INTEGER NULL, "CriadaEm" TEXT NOT NULL,
            "AnalisadaPorId" INTEGER NULL, "AnalisadaEm" TEXT NULL, "MotivoRecusa" TEXT NULL, "ConcluidaEm" TEXT NULL,
            CONSTRAINT "FK_Nres_Celulas_CelulaId" FOREIGN KEY ("CelulaId") REFERENCES "Celulas" ("Id") ON DELETE SET NULL,
            CONSTRAINT "FK_Nres_Equipamentos_EquipamentoId" FOREIGN KEY ("EquipamentoId") REFERENCES "Equipamentos" ("Id") ON DELETE RESTRICT,
            CONSTRAINT "FK_Nres_Ordens_OrdemServicoId" FOREIGN KEY ("OrdemServicoId") REFERENCES "Ordens" ("Id") ON DELETE SET NULL,
            CONSTRAINT "FK_Nres_Usuarios_SolicitanteId" FOREIGN KEY ("SolicitanteId") REFERENCES "Usuarios" ("Id") ON DELETE SET NULL,
            CONSTRAINT "FK_Nres_Usuarios_AnalisadaPorId" FOREIGN KEY ("AnalisadaPorId") REFERENCES "Usuarios" ("Id") ON DELETE SET NULL);
        """);
    // NRE ligada ao TIPO de máquina (Maquina) e à célula; a máquina exata ficou opcional. Tabela antiga (EquipamentoId
    // obrigatório, sem Maquina/CelulaId): o SQLite não muda coluna, então a tabela é refeita com os mesmos dados (as chaves
    // estrangeiras ficam desligadas durante a troca para não apagar itens e arquivos das NRE).
    if (!db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Nres')").ToList().Contains("Maquina"))
    {
        db.Database.OpenConnection();
        try
        {
            db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF");
            using var troca = db.Database.BeginTransaction();
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE "Nres_novo" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Nres" PRIMARY KEY AUTOINCREMENT,
                    "Numero" TEXT NOT NULL, "Maquina" TEXT NULL, "CelulaId" INTEGER NULL, "EquipamentoId" INTEGER NULL, "OrdemServicoId" INTEGER NULL,
                    "Tipo" INTEGER NOT NULL, "Descricao" TEXT NOT NULL, "Fornecedor" TEXT NULL, "PedidoCompra" TEXT NULL,
                    "Status" INTEGER NOT NULL, "SolicitanteId" INTEGER NULL, "CriadaEm" TEXT NOT NULL,
                    "AnalisadaPorId" INTEGER NULL, "AnalisadaEm" TEXT NULL, "MotivoRecusa" TEXT NULL, "ConcluidaEm" TEXT NULL,
                    CONSTRAINT "FK_Nres_Celulas_CelulaId" FOREIGN KEY ("CelulaId") REFERENCES "Celulas" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_Nres_Equipamentos_EquipamentoId" FOREIGN KEY ("EquipamentoId") REFERENCES "Equipamentos" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_Nres_Ordens_OrdemServicoId" FOREIGN KEY ("OrdemServicoId") REFERENCES "Ordens" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_Nres_Usuarios_SolicitanteId" FOREIGN KEY ("SolicitanteId") REFERENCES "Usuarios" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_Nres_Usuarios_AnalisadaPorId" FOREIGN KEY ("AnalisadaPorId") REFERENCES "Usuarios" ("Id") ON DELETE SET NULL);
                INSERT INTO "Nres_novo" ("Id", "Numero", "Maquina", "CelulaId", "EquipamentoId", "OrdemServicoId", "Tipo", "Descricao", "Fornecedor",
                    "PedidoCompra", "Status", "SolicitanteId", "CriadaEm", "AnalisadaPorId", "AnalisadaEm", "MotivoRecusa", "ConcluidaEm")
                SELECT n."Id", n."Numero", e."Nome", e."CelulaId", n."EquipamentoId", n."OrdemServicoId", n."Tipo", n."Descricao", n."Fornecedor",
                    n."PedidoCompra", n."Status", n."SolicitanteId", n."CriadaEm", n."AnalisadaPorId", n."AnalisadaEm", n."MotivoRecusa", n."ConcluidaEm"
                FROM "Nres" n LEFT JOIN "Equipamentos" e ON e."Id" = n."EquipamentoId";
                DROP TABLE "Nres";
                ALTER TABLE "Nres_novo" RENAME TO "Nres";
                """);
            troca.Commit();
        }
        finally
        {
            db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON");
            db.Database.CloseConnection();
        }
    }
    // Campos da NRE que vieram depois (título, modelo, NRE do cliente, aprovação, PO, evidência, fechamento) em bancos
    // criados antes deles. Colunas antigas sem uso: NotaFiscal, Invoice, Di e Fornecedor (foram para os blocos do
    // Detalhado, ver abaixo), Taxas e ValorGasto (o Resumo agora é calculado).
    var colunasNre = db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Nres')").ToList();
    if (!colunasNre.Contains("Titulo"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Nres\" ADD COLUMN \"Titulo\" TEXT NULL");
    if (!colunasNre.Contains("Modelo"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Nres\" ADD COLUMN \"Modelo\" TEXT NULL");
    if (!colunasNre.Contains("CodigoNre"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Nres\" ADD COLUMN \"CodigoNre\" TEXT NULL");
    if (!colunasNre.Contains("ValorAprovado"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Nres\" ADD COLUMN \"ValorAprovado\" REAL NULL");
    if (!colunasNre.Contains("PedidoCompra"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Nres\" ADD COLUMN \"PedidoCompra\" TEXT NULL");
    if (!colunasNre.Contains("AprovadaClienteEm"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Nres\" ADD COLUMN \"AprovadaClienteEm\" TEXT NULL");
    if (!colunasNre.Contains("Aprovador"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Nres\" ADD COLUMN \"Aprovador\" TEXT NULL");
    if (!colunasNre.Contains("AprovadoUsd"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Nres\" ADD COLUMN \"AprovadoUsd\" REAL NULL");
    if (!colunasNre.Contains("PtaxAprovacao"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Nres\" ADD COLUMN \"PtaxAprovacao\" REAL NULL");
    if (!colunasNre.Contains("Evidencia"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Nres\" ADD COLUMN \"Evidencia\" TEXT NULL");
    if (!colunasNre.Contains("TaxaPercentual"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Nres\" ADD COLUMN \"TaxaPercentual\" REAL NULL");
    if (!colunasNre.Contains("CustoAbsorvido"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Nres\" ADD COLUMN \"CustoAbsorvido\" REAL NULL");
    if (!colunasNre.Contains("MotivoDiferenca"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Nres\" ADD COLUMN \"MotivoDiferenca\" TEXT NULL");
    if (!colunasNre.Contains("CobradaEm"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Nres\" ADD COLUMN \"CobradaEm\" TEXT NULL");
    db.Database.ExecuteSqlRaw("""
        CREATE INDEX IF NOT EXISTS "IX_Nres_CelulaId" ON "Nres" ("CelulaId");
        CREATE INDEX IF NOT EXISTS "IX_Nres_EquipamentoId" ON "Nres" ("EquipamentoId");
        CREATE INDEX IF NOT EXISTS "IX_Nres_OrdemServicoId" ON "Nres" ("OrdemServicoId");
        CREATE INDEX IF NOT EXISTS "IX_Nres_SolicitanteId" ON "Nres" ("SolicitanteId");
        CREATE INDEX IF NOT EXISTS "IX_Nres_AnalisadaPorId" ON "Nres" ("AnalisadaPorId");
        CREATE TABLE IF NOT EXISTS "NreRemessas" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_NreRemessas" PRIMARY KEY AUTOINCREMENT,
            "NreId" INTEGER NOT NULL, "Ordem" INTEGER NOT NULL, "Tipo" INTEGER NOT NULL,
            "Di" TEXT NULL, "Invoice" TEXT NULL, "NotaFiscal" TEXT NULL, "Fornecedor" TEXT NULL,
            "Ptax" REAL NULL, "Frete" REAL NOT NULL, "Seguro" REAL NOT NULL,
            CONSTRAINT "FK_NreRemessas_Nres_NreId" FOREIGN KEY ("NreId") REFERENCES "Nres" ("Id") ON DELETE CASCADE);
        CREATE INDEX IF NOT EXISTS "IX_NreRemessas_NreId" ON "NreRemessas" ("NreId");
        CREATE TABLE IF NOT EXISTS "NreItens" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_NreItens" PRIMARY KEY AUTOINCREMENT,
            "NreId" INTEGER NOT NULL, "RemessaId" INTEGER NULL, "Codigo" TEXT NULL, "Descricao" TEXT NOT NULL,
            "Quantidade" REAL NOT NULL, "ValorUnitario" REAL NOT NULL, "ValorUnitarioUsd" REAL NULL,
            CONSTRAINT "FK_NreItens_Nres_NreId" FOREIGN KEY ("NreId") REFERENCES "Nres" ("Id") ON DELETE CASCADE,
            CONSTRAINT "FK_NreItens_NreRemessas_RemessaId" FOREIGN KEY ("RemessaId") REFERENCES "NreRemessas" ("Id") ON DELETE CASCADE);
        CREATE INDEX IF NOT EXISTS "IX_NreItens_NreId" ON "NreItens" ("NreId");
        CREATE TABLE IF NOT EXISTS "NreAnexos" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_NreAnexos" PRIMARY KEY AUTOINCREMENT,
            "NreId" INTEGER NOT NULL, "Tipo" INTEGER NOT NULL DEFAULT 0, "Nome" TEXT NOT NULL, "Arquivo" TEXT NOT NULL, "Tamanho" INTEGER NOT NULL,
            "EnviadoEm" TEXT NOT NULL, "EnviadoPor" TEXT NOT NULL,
            CONSTRAINT "FK_NreAnexos_Nres_NreId" FOREIGN KEY ("NreId") REFERENCES "Nres" ("Id") ON DELETE CASCADE);
        CREATE INDEX IF NOT EXISTS "IX_NreAnexos_NreId" ON "NreAnexos" ("NreId");
        """);
    // Detalhado da NRE (blocos de DI / compra nacional) em bancos criados antes dele: item com o bloco e o valor em US$,
    // arquivo com o tipo (Invoice, DI, NF, outro).
    var colunasItemNre = db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('NreItens')").ToList();
    if (!colunasItemNre.Contains("RemessaId"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"NreItens\" ADD COLUMN \"RemessaId\" INTEGER NULL");
    if (!colunasItemNre.Contains("ValorUnitarioUsd"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"NreItens\" ADD COLUMN \"ValorUnitarioUsd\" REAL NULL");
    db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS \"IX_NreItens_RemessaId\" ON \"NreItens\" (\"RemessaId\")");
    if (!db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('NreAnexos')").ToList().Contains("Tipo"))
        db.Database.ExecuteSqlRaw("ALTER TABLE \"NreAnexos\" ADD COLUMN \"Tipo\" INTEGER NOT NULL DEFAULT 0");
    // Itens de NRE de antes do Detalhado (sem bloco): cada NRE ganha um bloco com eles — DI se a NRE tinha DI ou invoice,
    // senão compra nacional — levando a NF, a invoice, a DI e o fornecedor que ficavam na própria NRE.
    var nresSemBloco = db.Database.SqlQueryRaw<int>("SELECT DISTINCT \"NreId\" AS Value FROM \"NreItens\" WHERE \"RemessaId\" IS NULL").ToList();
    if (nresSemBloco.Count > 0)
    {
        string? Antigo(string coluna, string sql, int id) =>
            colunasNre.Contains(coluna) ? db.Database.SqlQueryRaw<string>(sql, id).AsEnumerable().FirstOrDefault() : null;
        foreach (var nreId in nresSemBloco)
        {
            var di = Antigo("Di", "SELECT \"Di\" AS Value FROM \"Nres\" WHERE \"Id\" = {0}", nreId);
            var invoice = Antigo("Invoice", "SELECT \"Invoice\" AS Value FROM \"Nres\" WHERE \"Id\" = {0}", nreId);
            var bloco = new MS.Modelos.NreRemessa
            {
                NreId = nreId,
                Tipo = string.IsNullOrWhiteSpace(di) && string.IsNullOrWhiteSpace(invoice) ? MS.Modelos.TipoRemessa.Nacional : MS.Modelos.TipoRemessa.Importacao,
                Di = di, Invoice = invoice,
                NotaFiscal = Antigo("NotaFiscal", "SELECT \"NotaFiscal\" AS Value FROM \"Nres\" WHERE \"Id\" = {0}", nreId),
                Fornecedor = Antigo("Fornecedor", "SELECT \"Fornecedor\" AS Value FROM \"Nres\" WHERE \"Id\" = {0}", nreId),
            };
            db.NreRemessas.Add(bloco);
            db.SaveChanges();
            db.Database.ExecuteSql($"UPDATE \"NreItens\" SET \"RemessaId\" = {bloco.Id} WHERE \"NreId\" = {nreId} AND \"RemessaId\" IS NULL");
        }
    }
    // Tabelas do Spare Parts (estoque de peças) em bancos criados antes dele.
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "SpareParts" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_SpareParts" PRIMARY KEY AUTOINCREMENT,
            "Codigo" TEXT NOT NULL, "Descricao" TEXT NOT NULL, "Local" TEXT NULL,
            "Estoque" REAL NOT NULL, "EstoqueMinimo" REAL NULL, "Ativa" INTEGER NOT NULL, "CriadaEm" TEXT NOT NULL);
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_SpareParts_Codigo" ON "SpareParts" ("Codigo");
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
