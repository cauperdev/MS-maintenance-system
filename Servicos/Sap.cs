// ============================================================================
// SALDO NO SAP (só leitura) · usado pela coluna "Saldo SAP" da tela Spare Parts · bloco "Sap" do appsettings.json
// Pedido do usuário em 20/10/2026: "ler o código do material e mostrar o saldo do SAP de forma automática, sem precisar
// de login para entrar no SAP" e "não vai ler todos os materiais do SAP, somente aqueles que tem no meu sistema".
//
// COMO FUNCIONA:
//   - A tela manda os CÓDIGOS DAS PEÇAS DA PÁGINA ABERTA (até 50). O MS pergunta ao SAP só por eles: o filtro da
//     consulta leva cada código pelo nome (Material eq 'X' or Material eq 'Y' ...). Nunca baixa a lista de materiais
//     do SAP e nunca grava saldo no banco (o Spare Parts continua sem estoque: só mostra o número do SAP na hora).
//   - Quem entra no SAP é o MS, com um usuário técnico só de leitura (Usuario/Senha) ou com a conta do Windows deste
//     PC (ContaWindows = true, login único/SSO, sem senha guardada). Quem usa a tela não faz login no SAP.
//   - Leitura pelo serviço padrão do SAP S/4HANA "API_MATERIAL_STOCK_SRV" (OData), conjunto A_MatlStkInAcctMod:
//     soma o estoque de utilização livre (InventoryStockType 01) do centro e do(s) depósito(s) da área.
//   - O saldo fica guardado na memória por MinutosGuardado (padrão 10) para não perguntar ao SAP a cada página.
//   - Material que o SAP não devolve aparece como 0 (o serviço só lista o que tem estoque).
//
// O QUE A TI PRECISA PASSAR (appsettings.json → "Sap"):
//   Servico ......... endereço do serviço, ex.: https://servidor:44300/sap/opu/odata/sap/API_MATERIAL_STOCK_SRV
//   Mandante ........ client do SAP (ex.: 300) — vai como sap-client
//   ContaWindows .... true = entra com a conta do Windows (SSO); false = Usuario + Senha do usuário técnico
//   Centro .......... centro (plant) padrão
//   Areas ........... centro/depósito de cada área do MS, pelo nome da área: "SAMSUNG": { "Deposito": "0001" }
//                     (Deposito aceita vários separados por vírgula; vazio = soma todos os depósitos do centro)
//   ZerosNaFrente ... tamanho do número de material no SAP para código só com números (ex.: 18); 0 = manda como está
// Se o SAP for ECC sem OData, a leitura muda para RFC (SAP .NET Connector) — só este arquivo muda, a tela fica igual.
// ============================================================================
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MS.Servicos;

// Saldo de um código: quantidade livre e unidade (PC, UN...; vazia quando o SAP não devolveu o material).
public record SaldoSap(decimal Quantidade, string? Unidade);

public class Sap
{
    readonly IConfiguration config;
    readonly ILogger<Sap> log;
    readonly HttpClient http;
    // Saldos já consultados: (área, código) → saldo e hora da consulta.
    readonly ConcurrentDictionary<(string Area, string Codigo), (SaldoSap Saldo, DateTime Quando)> guardados = new();

    public Sap(IConfiguration config, ILogger<Sap> log)
    {
        this.config = config;
        this.log = log;
        var handler = new HttpClientHandler();
        if (ContaWindows) { handler.UseDefaultCredentials = true; handler.Credentials = CredentialCache.DefaultCredentials; }
        http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        if (!ContaWindows && Usuario != "")
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Usuario}:{config["Sap:Senha"]}")));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // A coluna só aparece com "Ativo": true e o endereço do serviço preenchido.
    public bool Ligado => config.GetValue("Sap:Ativo", false) && Servico != "";
    string Servico => (config["Sap:Servico"] ?? "").Trim().TrimEnd('/');
    string Mandante => (config["Sap:Mandante"] ?? "").Trim();
    bool ContaWindows => config.GetValue("Sap:ContaWindows", false);
    string Usuario => (config["Sap:Usuario"] ?? "").Trim();
    int ZerosNaFrente => config.GetValue("Sap:ZerosNaFrente", 0);
    int MinutosGuardado => Math.Max(0, config.GetValue("Sap:MinutosGuardado", 10));
    const int CodigosPorConsulta = 20;   // códigos por pergunta ao SAP (endereço não fica comprido demais)

    // Centro e depósitos da área (pelo nome da área; sem configuração da área, vale o Centro padrão e todos os depósitos).
    (string Centro, string[] Depositos) LocalDaArea(string? area)
    {
        var sec = config.GetSection("Sap:Areas").GetChildren()
            .FirstOrDefault(s => string.Equals(s.Key, area?.Trim(), StringComparison.OrdinalIgnoreCase));
        var centro = (sec?["Centro"] ?? config["Sap:Centro"] ?? "").Trim();
        var depositos = (sec?["Deposito"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (centro, depositos);
    }

    // Código como o SAP guarda (zeros na frente quando é só número) e a chave para comparar a resposta.
    string NoSap(string codigo) =>
        ZerosNaFrente > 0 && codigo.All(char.IsAsciiDigit) && codigo.Length < ZerosNaFrente ? codigo.PadLeft(ZerosNaFrente, '0') : codigo;
    static string Chave(string material) => material.All(char.IsAsciiDigit) ? material.TrimStart('0') : material.Trim().ToUpperInvariant();

    // Saldo de cada código pedido (só estes códigos vão para o SAP). Lança exceção se o SAP não responder.
    public async Task<Dictionary<string, SaldoSap>> SaldosAsync(IEnumerable<string> codigos, string? area, CancellationToken ct = default)
    {
        var areaChave = (area ?? "").Trim().ToUpperInvariant();
        var lista = codigos.Select(c => c.Trim().ToUpperInvariant()).Where(c => c != "").Distinct().ToList();
        var resultado = new Dictionary<string, SaldoSap>();
        var faltam = new List<string>();
        foreach (var c in lista)
        {
            if (guardados.TryGetValue((areaChave, c), out var g) && DateTime.Now - g.Quando < TimeSpan.FromMinutes(MinutosGuardado))
                resultado[c] = g.Saldo;
            else faltam.Add(c);
        }
        if (faltam.Count == 0) return resultado;

        var (centro, depositos) = LocalDaArea(area);
        foreach (var grupo in faltam.Chunk(CodigosPorConsulta))
        {
            var somados = await ConsultarAsync(grupo, centro, depositos, ct);
            foreach (var c in grupo)
            {
                var saldo = somados.TryGetValue(Chave(NoSap(c)), out var s) ? s : new SaldoSap(0, null);
                resultado[c] = saldo;
                guardados[(areaChave, c)] = (saldo, DateTime.Now);
            }
        }
        return resultado;
    }

    // Uma pergunta ao SAP com até 20 códigos. Devolve o estoque livre somado por material (chave = Chave(material)).
    async Task<Dictionary<string, SaldoSap>> ConsultarAsync(string[] codigos, string centro, string[] depositos, CancellationToken ct)
    {
        static string Texto(string v) => "'" + v.Replace("'", "''") + "'";   // literal do OData (aspas simples dobradas)
        var filtro = new StringBuilder("(" + string.Join(" or ", codigos.Select(c => $"Material eq {Texto(NoSap(c))}")) + ")");
        filtro.Append(" and InventoryStockType eq '01'");
        if (centro != "") filtro.Append($" and Plant eq {Texto(centro)}");
        if (depositos.Length > 0) filtro.Append(" and (" + string.Join(" or ", depositos.Select(d => $"StorageLocation eq {Texto(d)}")) + ")");

        var url = $"{Servico}/A_MatlStkInAcctMod?$format=json"
            + "&$select=Material,StorageLocation,MatlWrhsStkQtyInMatlBaseUnit,MaterialBaseUnit"
            + "&$filter=" + Uri.EscapeDataString(filtro.ToString())
            + (Mandante != "" ? "&sap-client=" + Uri.EscapeDataString(Mandante) : "");

        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogWarning("SAP respondeu {Status} na consulta de saldo", (int)resp.StatusCode);
            throw new HttpRequestException($"SAP respondeu {(int)resp.StatusCode}");
        }
        using var json = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        // OData V2: { "d": { "results": [...] } }; V4: { "value": [...] }.
        var linhas = json.RootElement.TryGetProperty("d", out var d) && d.TryGetProperty("results", out var r) ? r
            : json.RootElement.TryGetProperty("value", out var v) ? v : default;

        var somados = new Dictionary<string, SaldoSap>();
        if (linhas.ValueKind != JsonValueKind.Array) return somados;
        foreach (var l in linhas.EnumerateArray())
        {
            var material = l.TryGetProperty("Material", out var m) ? m.GetString() ?? "" : "";
            if (material == "") continue;
            var qtd = 0m;
            if (l.TryGetProperty("MatlWrhsStkQtyInMatlBaseUnit", out var q))
                qtd = q.ValueKind == JsonValueKind.Number ? q.GetDecimal()
                    : decimal.TryParse(q.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var x) ? x : 0;
            var unidade = l.TryGetProperty("MaterialBaseUnit", out var u) ? u.GetString() : null;
            var chave = Chave(material);
            somados[chave] = somados.TryGetValue(chave, out var antes)
                ? antes with { Quantidade = antes.Quantidade + qtd }
                : new SaldoSap(qtd, unidade);
        }
        return somados;
    }
}
