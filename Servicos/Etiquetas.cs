// ============================================================================
// ETIQUETAS ELETRÔNICAS (e-paper ESL-37BWRY) — a etiqueta colada na máquina mostra a manutenção de data mais próxima.
// Liga/desliga e configurações: bloco "Etiquetas" do appsettings.json (ligado em 16/10/2026).
//
// COMO FUNCIONA
//   1. A máquina é ligada à etiqueta pelo campo Etiqueta do cadastro (o código de barras da tela dela, ex.: 1750107D).
//   2. De MinutosConferencia em MinutosConferencia (e sempre que o sistema liga, porque o PC dorme à noite) o MS
//      procura, para cada máquina com etiqueta, a OS NÃO CONCLUÍDA de MENOR data — qualquer periodicidade, junto:
//      semanal 20/10 e trimestral 17/10 → mostra a TRIMESTRAL de 17/10 (pedido do usuário em 14/10/2026).
//   3. A cor sai dessa data (ver Cor): branco = em dia, amarelo = perto, vermelho = chegou o dia ou passou.
//   4. O MS manda só os DADOS da máquina para a nuvem do fornecedor (product/create), para CADA loja da lista
//      "Lojas"; quem desenha é o modelo (template) da nuvem, escolhido pelo Scene. Desenhar fica só como prévia na
//      tela de Equipamentos.
//   5. Só manda o que mudou: os dados de cada etiqueta são guardados por loja (enviadas); dado igual não é
//      reenviado. Cada atualização gasta bateria da etiqueta e leva vários segundos de rádio.
//
// VOLTAR AO BRANCO: a etiqueta só fica branca quando a OS é CONCLUÍDA (o técnico lê o QR, faz as atividades e põe as
// fotos de antes e depois). Não é o relógio que limpa o vermelho — é a conclusão, que muda a OS de menor data.
//
// ONDE MEXER:
//   - Quantos dias antes fica amarela ......... "DiasAmarelo" no appsettings.json
//   - De quanto em quanto tempo confere ....... "MinutosConferencia" no appsettings.json
//   - Desenho da etiqueta ..................... Desenhar (medidas em Tela)
//   - Regra da cor ............................ Cor
//   - Envio para a nuvem ...................... EtiquetasEletronicas.ChamarAsync
//   - Número do Scene de cada cor ............. "Modelos" no appsettings.json
//   - Lojas da nuvem (uma por antena/linha) ... "Lojas" no appsettings.json (ver abaixo)
//
// LOJAS DA NUVEM (appsettings.json → "Etiquetas" → "Lojas") — vale na hora, sem reiniciar nem recompilar.
//   Cada loja da nuvem tem as suas antenas, as suas etiquetas e os seus modelos. O MS manda TODAS as máquinas com
//   etiqueta para TODAS as lojas ligadas; cada etiqueta mostra a máquina na loja em que ela estiver cadastrada.
//   Para pôr uma loja nova, acrescente uma linha na lista (com vírgula entre as linhas):
//       { "Codigo": "20248750", "Nome": "LINHA 3" }
//   Codigo ..... o Branch code da loja (NÃO o número que aparece no endereço da página do painel). Obrigatório.
//   Nome ....... só para aparecer no log. Opcional.
//   Ativo ...... false = a loja fica na lista, mas não recebe nada. Opcional (sem ele, a loja está ligada).
//   Chave ...... a chave da aba API da loja, se for diferente da "Chave" geral. Opcional.
//   Api ........ o nome da aba API da loja, se não for o "Api" geral ("default"). Opcional.
//   Endereco ... o endereço da nuvem, se a loja estiver em outro servidor. Opcional.
//   No painel da nuvem, a loja nova também precisa: dos 3 modelos (branco, amarelo e vermelho, com o Scene de
//   "Modelos" — os modelos são de cada loja), da lista de etiquetas dela e da antena com o código dela na tela
//   Setting. Loja nova recebe todas as máquinas na primeira rodada; loja fora do ar ou que recusa tudo (código ou
//   chave errados) não atrapalha as outras: fica 10 minutos parada e tenta de novo.
//   O formato antigo ("Loja": "12345678", uma loja só) continua valendo quando não existe "Lojas".
// ============================================================================
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MS.BancoDeDados;
using MS.Modelos;
using SkiaSharp;

namespace MS.Servicos;

public enum CorEtiqueta { Branco, Amarelo, Vermelho }

// O que vai aparecer numa etiqueta. É só isto que o desenho precisa saber.
public record ConteudoEtiqueta(
    string Etiqueta,        // código da etiqueta eletrônica (o da tela dela)
    string Nome,
    string Codigo,
    string Localizacao,
    string Area,
    string Link,            // endereço que o QR abre (as OS da máquina)
    CorEtiqueta Cor,
    string? Periodicidade,  // "TRIMESTRAL" — nulo quando a máquina não tem OS aberta
    DateTime? Data);

// Uma loja da nuvem do fornecedor: as suas antenas, etiquetas e modelos, e a sua aba API.
public record LojaEtiquetas(string Codigo, string Nome, string Endereco, string Api, string Chave)
{
    // Endereço completo de uma chamada: https://esl.einktag.net/api/default/product/create
    public string Url(string caminho) => $"{Endereco}/api/{Api}/{caminho}";
    // A mesma loja = mesmo servidor e mesmo código (é por aqui que o MS lembra o que já mandou para ela).
    public string Id => $"{Endereco}|{Codigo}";
    // Como a loja aparece no log: "12345678 (ANTENA 01)".
    public override string ToString() => Nome == "" ? Codigo : $"{Codigo} ({Nome})";
}

// Configurações do bloco "Etiquetas" do appsettings.json.
public class ConfigEtiquetas(IConfiguration config)
{
    public bool Ativo => config.GetValue("Etiquetas:Ativo", false);
    // Endereço, nome da API e chave gerais: valem para toda loja que não tiver os seus.
    public string Endereco => Ou(config["Etiquetas:Endereco"], "").TrimEnd('/');
    // Nome da API na nuvem (aba API da loja — lá está "default"). Entra no meio do endereço.
    public string Api => Ou(config["Etiquetas:Api"], "default");
    public string Chave => Ou(config["Etiquetas:Chave"], "");

    // As lojas que recebem as máquinas: só as ligadas e sem repetir. É lido a cada rodada, então loja posta ou
    // tirada do appsettings.json vale na rodada seguinte, sem reiniciar o sistema.
    public List<LojaEtiquetas> Lojas
    {
        get
        {
            var lojas = new List<LojaEtiquetas>();
            var lista = config.GetSection("Etiquetas:Lojas");
            if (lista.Exists())
            {
                foreach (var s in lista.GetChildren())
                {
                    if (bool.TryParse(s["Ativo"], out var ligada) && !ligada) continue;
                    var codigo = Ou(s["Codigo"], "");
                    if (codigo == "") continue;
                    lojas.Add(new LojaEtiquetas(codigo, Ou(s["Nome"], ""), Ou(s["Endereco"], Endereco).TrimEnd('/'),
                        Ou(s["Api"], Api), Ou(s["Chave"], Chave)));
                }
            }
            // Formato antigo: uma loja só, em "Loja".
            else if (Ou(config["Etiquetas:Loja"], "") is var loja && loja != "")
                lojas.Add(new LojaEtiquetas(loja, "", Endereco, Api, Chave));
            return lojas.DistinctBy(l => l.Id, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public int MinutosConferencia => Math.Max(1, config.GetValue("Etiquetas:MinutosConferencia", 30));
    public int DiasAmarelo => Math.Max(0, config.GetValue("Etiquetas:DiasAmarelo", 2));
    public string EnderecoSistema => (config["EnderecoSistema"] ?? "http://localhost:5100").TrimEnd('/');

    // O Scene de cada cor. A nuvem troca o modelo da etiqueta sozinha conforme este valor, então é ele que pinta a
    // etiqueta de branco, amarelo ou vermelho. Pela API o Scene vai como NÚMERO, na ordem da lista da tela deles:
    // Normal = 1, Limit = 5, OOS = 8 (conferidos na nuvem em 16/10/2026).
    public string Modelo(CorEtiqueta cor) => (config[$"Etiquetas:Modelos:{cor}"] ?? "").Trim();

    // O valor do appsettings.json sem espaços nas pontas; em branco ou ausente = o padrão.
    static string Ou(string? valor, string padrao) => string.IsNullOrWhiteSpace(valor) ? padrao : valor.Trim();
}

public static class Etiquetas
{
    // ---------- Medidas da tela da ESL-37BWRY ----------
    // ATENÇÃO: confirmar com o fornecedor. 416 x 240 é o tamanho usual da e-paper de 3,7", mas se a dele for outro
    // a imagem sai esticada. É só trocar aqui.
    public static class Tela
    {
        public const int Largura = 416;
        public const int Altura = 240;
    }

    // As 4 cores da tela. A e-paper não tem meio-tom: cada ponto é um destes quatro, e nada mais.
    static readonly SKColor Branco = new(0xFF, 0xFF, 0xFF);
    static readonly SKColor Preto = new(0x00, 0x00, 0x00);
    static readonly SKColor Vermelho = new(0xFF, 0x00, 0x00);
    static readonly SKColor Amarelo = new(0xFF, 0xFF, 0x00);

    // ---------- DE-PARA: a máquina do MS vira um "produto" na nuvem ----------
    // Os campos são os do cadastro de produto da einktag (print do formulário em 15/10/2026). O modelo (template) da
    // etiqueta é desenhado lá na nuvem e busca estes mesmos nomes (#pn, #f1, ...).
    //   Barcode (#pc) ....... CÓDIGO da máquina — é a chave: é por ele que a etiqueta é ligada à máquina
    //   Product name (#pn) .. NOME
    //   QR Code Url (#pqr) .. o endereço que o QR abre; NUNCA muda (o código da máquina é fixo)
    //   Scene (#promotion) .. a COR: a nuvem troca o modelo sozinha conforme este valor ("Match the Scene in the esl
    //                         template"). Os três nomes ficam em "Etiquetas:Modelos" no appsettings.json.
    //   Customize1..4 ....... LOCALIZAÇÃO, ÁREA, PERIODICIDADE e DATA
    //   Price (#pp) ......... obrigatório no cadastro deles (é um sistema de supermercado); vai 0 e não aparece no modelo.
    public static Dictionary<string, string> Produto(ConteudoEtiqueta c, ConfigEtiquetas cfg) => new()
    {
        ["pc"] = c.Codigo,
        ["pn"] = c.Nome,
        ["pqr"] = c.Link,
        ["promotion"] = cfg.Modelo(c.Cor),
        ["f1"] = c.Localizacao,
        ["f2"] = c.Area,
        ["f3"] = c.Periodicidade ?? "",
        ["f4"] = c.Data is DateTime d ? d.ToString("dd/MM/yyyy") : "",
        ["pp"] = "0",
    };

    // Planilha das máquinas no formato do cadastro de produto da nuvem, para subir de uma vez no Import da tela
    // Product. É o caminho sem API: a planilha faz na mão o que o envio automático vai fazer sozinho depois.
    // O cabeçalho é a etiqueta de cada campo no formulário deles (#pc, #pn, ...). Se o Import pedir outros nomes,
    // é só trocar aqui — a ordem e o conteúdo continuam valendo.
    public static readonly string[] ColunasProduto = ["#pc", "#pn", "#pp", "#pqr", "#promotion", "#f1", "#f2", "#f3", "#f4"];

    public static byte[] Planilha(IEnumerable<ConteudoEtiqueta> etiquetas, ConfigEtiquetas cfg) =>
        Excel.Planilha(("Product", ColunasProduto, etiquetas.Select(c =>
        {
            var p = Produto(c, cfg);
            return ColunasProduto.Select(coluna => (object?)p[coluna.TrimStart('#')]).ToArray();
        })));

    // ---------- A regra da cor ----------
    // Vermelho: chegou o dia da manutenção ou já passou. Amarelo: faltam DiasAmarelo dias ou menos. Branco: o resto.
    // Compara o DIA, não a hora: no próprio dia a OS ainda está no prazo, mas a etiqueta já pede a manutenção.
    public static CorEtiqueta Cor(DateTime? data, DateTime hoje, int diasAmarelo)
    {
        if (data is not DateTime d) return CorEtiqueta.Branco;   // máquina sem OS aberta
        var faltam = (d.Date - hoje.Date).Days;
        return faltam <= 0 ? CorEtiqueta.Vermelho
             : faltam <= diasAmarelo ? CorEtiqueta.Amarelo
             : CorEtiqueta.Branco;
    }

    // ---------- O que cada etiqueta deve mostrar ----------
    // Uma consulta só para todas as máquinas com etiqueta, e uma só para as OS delas (não traz OS de máquina sem
    // etiqueta nem OS já concluída).
    public static async Task<List<ConteudoEtiqueta>> MontarAsync(AppDb db, ConfigEtiquetas cfg, DateTime agora, CancellationToken parar = default)
    {
        var maquinas = await db.Equipamentos.Include(e => e.Celula)
            .Where(e => e.Etiqueta != null && e.Etiqueta != "")
            .ToListAsync(parar);
        if (maquinas.Count == 0) return new();

        var ids = maquinas.Select(e => e.Id).ToList();
        // A OS de MENOR data de cada máquina, sem olhar a periodicidade (a mais próxima é a que a etiqueta mostra).
        var proximas = (await db.Ordens
                .Where(o => ids.Contains(o.EquipamentoId) && o.Status != StatusOS.Concluida)
                .Select(o => new { o.EquipamentoId, o.DataConclusao, o.Tipo })
                .ToListAsync(parar))
            .GroupBy(o => o.EquipamentoId)
            .ToDictionary(g => g.Key, g => g.OrderBy(o => o.DataConclusao).First());

        return maquinas
            .Select(e => Montar(e, proximas.GetValueOrDefault(e.Id) is var os && os != null ? (os.DataConclusao, os.Tipo) : null, cfg, agora))
            .ToList();
    }

    // A etiqueta de UMA máquina — a prévia da tela de Equipamentos. Desenha mesmo que a máquina ainda não tenha
    // etiqueta ligada, para dar para conferir o desenho antes de colar as etiquetas nas máquinas.
    public static async Task<ConteudoEtiqueta?> UmaAsync(AppDb db, ConfigEtiquetas cfg, int equipamentoId, DateTime agora, CancellationToken parar = default)
    {
        var e = await db.Equipamentos.Include(x => x.Celula).FirstOrDefaultAsync(x => x.Id == equipamentoId, parar);
        if (e == null) return null;
        var os = await db.Ordens
            .Where(o => o.EquipamentoId == e.Id && o.Status != StatusOS.Concluida)
            .OrderBy(o => o.DataConclusao)
            .Select(o => new { o.DataConclusao, o.Tipo })
            .FirstOrDefaultAsync(parar);
        return Montar(e, os == null ? null : (os.DataConclusao, os.Tipo), cfg, agora);
    }

    static ConteudoEtiqueta Montar(Equipamento e, (DateTime Data, Periodicidade Tipo)? os, ConfigEtiquetas cfg, DateTime agora) =>
        new(Etiqueta: (e.Etiqueta ?? "").Trim(),
            Nome: e.Nome,
            Codigo: e.Codigo,
            Localizacao: e.Localizacao,
            Area: e.Celula?.Nome ?? "",
            Link: $"{cfg.EnderecoSistema}/Ordens?Q={Uri.EscapeDataString(e.Codigo)}",
            Cor: Cor(os?.Data, agora, cfg.DiasAmarelo),
            Periodicidade: os?.Tipo.Nome().ToUpperInvariant(),
            Data: os?.Data);

    // ---------- O desenho da etiqueta ----------
    // Layout aprovado pelo usuário em 14/10/2026: QR à esquerda; ao lado NOME, CÓDIGO, LOCALIZAÇÃO e ÁREA; embaixo
    // uma faixa com o aviso e a manutenção. QR e dados SEMPRE em fundo branco — a cor só contorna (a moldura e a faixa).
    public static byte[] Desenhar(ConteudoEtiqueta c)
    {
        const int margem = 8, raio = 10;
        var fundo = c.Cor switch { CorEtiqueta.Vermelho => Vermelho, CorEtiqueta.Amarelo => Amarelo, _ => Branco };

        using var bitmap = new SKBitmap(Tela.Largura, Tela.Altura, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(Branco);

        using var tinta = new SKPaint { IsAntialias = false };   // sem suavizar: a e-paper só tem as 4 cores puras

        // Moldura colorida em volta (na branca é só um contorno preto fino, para a etiqueta ter borda).
        var moldura = new SKRoundRect(new SKRect(1, 1, Tela.Largura - 1, Tela.Altura - 1), raio);
        if (c.Cor == CorEtiqueta.Branco)
        {
            tinta.Color = Preto;
            tinta.Style = SKPaintStyle.Stroke;
            tinta.StrokeWidth = 3;
            canvas.DrawRoundRect(moldura, tinta);
        }
        else
        {
            tinta.Color = fundo;
            tinta.Style = SKPaintStyle.Fill;
            canvas.DrawRoundRect(moldura, tinta);
        }

        // Faixa de baixo: o aviso (MANUTENÇÃO / PRÓX. DA DATA... / PRÓXIMA MANUTENÇÃO) e a periodicidade + data.
        var alturaFaixa = c.Cor == CorEtiqueta.Vermelho ? 72 : 60;
        var topoFaixa = Tela.Altura - alturaFaixa - margem;

        // Área branca dos dados: do topo até a faixa.
        tinta.Color = Branco;
        tinta.Style = SKPaintStyle.Fill;
        canvas.DrawRoundRect(new SKRoundRect(new SKRect(margem, margem, Tela.Largura - margem, topoFaixa), raio - 4), tinta);

        // ----- QR code -----
        const int ladoQr = 104;
        var qr = QrPng(c.Link, ladoQr);
        using (var imagemQr = SKBitmap.Decode(qr))
            if (imagemQr != null)
                canvas.DrawBitmap(imagemQr, new SKRect(margem + 8, margem + 8, margem + 8 + ladoQr, margem + 8 + ladoQr),
                    new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));   // Nearest: o QR não pode borrar

        // ----- Dados da máquina, ao lado do QR -----
        var x = margem + 8 + ladoQr + 10;
        var largura = Tela.Largura - margem - 8 - x;
        var y = margem + 26f;
        using var negrito = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), 19);
        using var normal = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal), 14);
        tinta.Color = Preto;

        y = Linha(canvas, tinta, negrito, "NOME: " + c.Nome, x, y, largura, 22);
        y = Linha(canvas, tinta, normal, "CÓDIGO: " + c.Codigo, x, y + 4, largura, 17);
        y = Linha(canvas, tinta, normal, "LOCALIZAÇÃO: " + c.Localizacao, x, y, largura, 17);
        Linha(canvas, tinta, normal, "ÁREA: " + c.Area, x, y, largura, 17);

        // ----- Faixa de baixo -----
        var (aviso, tamanhoAviso) = c.Cor switch
        {
            CorEtiqueta.Vermelho => ("MANUTENÇÃO", 34f),
            CorEtiqueta.Amarelo => ("PRÓX. DA DATA DE MANUTENÇÃO", 17f),
            _ => ("PRÓXIMA MANUTENÇÃO", 17f),
        };
        // Na branca a faixa é branca com texto preto; nas outras, o texto fica sobre a própria cor.
        tinta.Color = c.Cor == CorEtiqueta.Vermelho ? Branco : Preto;
        using var fonteAviso = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), tamanhoAviso);
        Centralizado(canvas, tinta, fonteAviso, aviso, topoFaixa + tamanhoAviso + 2);

        if (c.Data is DateTime data)
        {
            using var fonteData = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), 17);
            Centralizado(canvas, tinta, fonteData, $"{c.Periodicidade} · {data:dd/MM}", topoFaixa + alturaFaixa - 8);
        }

        using var imagem = SKImage.FromBitmap(bitmap);
        using var png = imagem.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    // Escreve uma linha; se não couber na largura, vai diminuindo a letra até caber (nome comprido não pode vazar).
    static float Linha(SKCanvas canvas, SKPaint tinta, SKFont fonte, string texto, float x, float y, float largura, float altura)
    {
        var tamanho = fonte.Size;
        while (fonte.MeasureText(texto) > largura && fonte.Size > 8) fonte.Size -= 0.5f;
        canvas.DrawText(texto, x, y, SKTextAlign.Left, fonte, tinta);
        fonte.Size = tamanho;
        return y + altura;
    }

    static void Centralizado(SKCanvas canvas, SKPaint tinta, SKFont fonte, string texto, float y)
    {
        while (fonte.MeasureText(texto) > Tela.Largura - 24 && fonte.Size > 8) fonte.Size -= 0.5f;
        canvas.DrawText(texto, Tela.Largura / 2f, y, SKTextAlign.Center, fonte, tinta);
    }

    // QR do tamanho pedido (o mesmo endereço da planilha de QR codes: /Ordens?Q=código).
    static byte[] QrPng(string link, int lado)
    {
        using var gerador = new QRCoder.QRCodeGenerator();
        using var dados = gerador.CreateQrCode(link, QRCoder.QRCodeGenerator.ECCLevel.M);
        var modulos = dados.ModuleMatrix.Count;                  // quantos quadradinhos o QR tem de lado
        var porModulo = Math.Max(1, lado / Math.Max(1, modulos)); // pixels por quadradinho (inteiro: QR não pode borrar)
        return new QRCoder.PngByteQRCode(dados).GetGraphic(porModulo);
    }
}

// ---------- Rodando sozinho ----------
// Confere as datas ao ligar o sistema e depois de MinutosConferencia em MinutosConferencia. Acordar() é chamado
// quando uma OS é concluída, para a etiqueta voltar ao branco na hora, sem esperar a rodada.
public class EtiquetasEletronicas(IServiceScopeFactory scopes, ConfigEtiquetas cfg, IHttpClientFactory http,
    ILogger<EtiquetasEletronicas> log) : BackgroundService
{
    static readonly SemaphoreSlim acordar = new(0, 1);

    // O técnico concluiu uma OS: a etiqueta daquela máquina é conferida em segundos.
    public static void Acordar()
    {
        try { acordar.Release(); } catch (SemaphoreFullException) { }
    }

    // Resumo do que foi mandado para cada etiqueta, separado por loja (Id da loja → etiqueta → resumo): dado igual
    // não é reenviado. Cada atualização gasta bateria da etiqueta e tempo de rádio da base, então a rodada só fala da
    // máquina que mudou de verdade. Loja nova começa vazia, então recebe todas as máquinas na primeira rodada.
    readonly Dictionary<string, Dictionary<string, string>> enviadas = new(StringComparer.OrdinalIgnoreCase);

    // Loja que recusou tudo (código, chave ou nome da API errados) fica parada até esta hora, para não encher o log a
    // cada rodada. Mudou qualquer coisa da loja no appsettings.json, ela tenta na hora (é outro registro).
    readonly Dictionary<LojaEtiquetas, DateTime> parada = new();
    static readonly TimeSpan TempoParada = TimeSpan.FromMinutes(10);
    bool avisouSemLoja;

    enum Resposta { Ok, Recusou, SemConexao }

    protected override async Task ExecuteAsync(CancellationToken parar)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), parar); } catch (OperationCanceledException) { return; }
        while (!parar.IsCancellationRequested)
        {
            if (cfg.Ativo)
            {
                try { await RodadaAsync(parar); }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { log.LogError(ex, "Falha ao atualizar as etiquetas eletrônicas."); }
            }
            // Espera a próxima rodada, mas acorda antes se alguém concluiu uma OS.
            try { await acordar.WaitAsync(TimeSpan.FromMinutes(cfg.MinutosConferencia), parar); }
            catch (OperationCanceledException) { return; }
        }
    }

    async Task RodadaAsync(CancellationToken parar)
    {
        var lojas = cfg.Lojas;
        if (lojas.Count == 0)
        {
            if (!avisouSemLoja) log.LogWarning("Etiquetas: ligadas, mas nenhuma loja em \"Etiquetas:Lojas\" no appsettings.json.");
            avisouSemLoja = true;
            return;
        }
        avisouSemLoja = false;

        // Loja tirada do appsettings.json: esquece o que foi mandado para ela (se voltar, recebe tudo de novo).
        foreach (var id in enviadas.Keys.Except(lojas.Select(l => l.Id), StringComparer.OrdinalIgnoreCase).ToList())
            enviadas.Remove(id);
        foreach (var l in parada.Keys.Except(lojas).ToList())
            parada.Remove(l);

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var lista = await Etiquetas.MontarAsync(db, cfg, DateTime.Now, parar);
        foreach (var loja in lojas)
        {
            if (parar.IsCancellationRequested) return;
            if (parada.TryGetValue(loja, out var ate) && DateTime.Now < ate) continue;
            parada.Remove(loja);
            await MandarParaAsync(loja, lista, parar);
        }
    }

    // Manda para uma loja as máquinas que mudaram desde o último envio a ela. Loja fora do ar ou que recusa tudo para
    // só ela; as outras lojas continuam recebendo na mesma rodada.
    async Task MandarParaAsync(LojaEtiquetas loja, List<ConteudoEtiqueta> lista, CancellationToken parar)
    {
        if (!enviadas.TryGetValue(loja.Id, out var jaFoi))
            enviadas[loja.Id] = jaFoi = new(StringComparer.OrdinalIgnoreCase);
        int mudaram = 0, recusadas = 0;
        foreach (var c in lista)
        {
            if (parar.IsCancellationRequested) return;
            var campos = Etiquetas.Produto(c, cfg);
            var resumo = string.Join("|", campos.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));
            if (jaFoi.GetValueOrDefault(c.Etiqueta) == resumo) continue;   // nada mudou nessa máquina
            // product/create também ATUALIZA: "when the product code exists in system already, it's will be updated".
            var r = await ChamarAsync(loja, "product/create", campos, parar);
            if (r == Resposta.Ok)
            {
                jaFoi[c.Etiqueta] = resumo;
                mudaram++;
            }
            // Sem internet ou nuvem fora: não adianta tentar as outras máquinas agora; a próxima rodada tenta de novo.
            else if (r == Resposta.SemConexao) break;
            // As 3 primeiras recusadas e nenhuma aceita: é a loja que está errada, não a máquina.
            else if (++recusadas >= 3 && mudaram == 0)
            {
                parada[loja] = DateTime.Now + TempoParada;
                log.LogWarning("Etiquetas: a loja {Loja} recusou tudo. Confira Codigo, Chave e Api dela no appsettings.json. " +
                    "Tenta de novo em {Minutos} minutos.", loja, TempoParada.TotalMinutes);
                break;
            }
        }
        // As etiquetas das máquinas que mudaram entraram na fila de atualização da nuvem; bind_task manda a base
        // empurrar para elas. Uma chamada só por loja no fim da rodada, não uma por máquina.
        if (mudaram > 0)
        {
            log.LogInformation("Etiquetas: loja {Loja}: {Quantas} máquina(s) atualizada(s); mandando a nuvem atualizar as etiquetas.",
                loja, mudaram);
            await ChamarAsync(loja, "esl/bind_task", new Dictionary<string, string>(), parar);
        }
    }

    // ---------- A chamada à nuvem ----------
    // Documentação: https://esl.einktag.net/docs/index.html (PRODUCT GROUP e ESL GROUP).
    // Todo endereço é POST em JSON e toda chamada leva store_code (a loja), sign (a chave da aba API) e
    // is_base64 = "0" (os valores vão em texto puro, sem codificar). A nuvem responde error_code = 0 quando deu certo.
    async Task<Resposta> ChamarAsync(LojaEtiquetas loja, string caminho, Dictionary<string, string> campos, CancellationToken parar)
    {
        var corpo = new Dictionary<string, string>(campos)
        {
            ["store_code"] = loja.Codigo,
            ["is_base64"] = "0",
            ["sign"] = loja.Chave,
        };
        try
        {
            using var cliente = http.CreateClient();
            cliente.Timeout = TimeSpan.FromSeconds(30);
            using var resposta = await cliente.PostAsJsonAsync(loja.Url(caminho), corpo, parar);
            var texto = await resposta.Content.ReadAsStringAsync(parar);
            if (!resposta.IsSuccessStatusCode)
            {
                log.LogWarning("Etiquetas: loja {Loja}: {Caminho} respondeu {Codigo}. {Resposta}",
                    loja, caminho, (int)resposta.StatusCode, Curto(texto));
                return Resposta.Recusou;
            }
            if (ErroDaNuvem(texto) is string erro)
            {
                log.LogWarning("Etiquetas: loja {Loja}: {Caminho} recusou. {Erro}", loja, caminho, erro);
                return Resposta.Recusou;
            }
            return Resposta.Ok;
        }
        // Internet fora, nuvem fora, nome que não resolve: não marca como enviado, então a próxima rodada tenta de novo.
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !parar.IsCancellationRequested)
        {
            log.LogWarning("Etiquetas: loja {Loja}: não foi possível falar com a nuvem em {Caminho} ({Erro}). Tenta na próxima rodada.",
                loja, caminho, ex.Message);
            return Resposta.SemConexao;
        }
    }

    // null = deu certo. Senão, o que a nuvem respondeu, para aparecer no log.
    static string? ErroDaNuvem(string texto)
    {
        try
        {
            using var doc = JsonDocument.Parse(texto);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Curto(texto);
            if (!doc.RootElement.TryGetProperty("error_code", out var codigo)) return Curto(texto);
            return codigo.ToString() == "0" ? null : Curto(texto);
        }
        catch (JsonException) { return Curto(texto); }
    }

    static string Curto(string texto) => texto.Length <= 300 ? texto : texto[..300] + "…";
}
