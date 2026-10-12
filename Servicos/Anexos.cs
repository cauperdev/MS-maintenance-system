// ============================================================================
// ANEXOS DA NRE — guarda os arquivos (PO, orçamento, nota fiscal...) na pasta dados/anexos.
// Diferente das fotos das OS, NÃO são apagados sozinhos: só saem quando alguém remove na tela da NRE
// (ou quando a NRE inteira é removida).
// ONDE MEXER:
//   - Tipos de arquivo aceitos ........ Extensoes
//   - Tamanho máximo de cada arquivo .. TamanhoMaximo (o envio inteiro: [RequestSizeLimit] em Pages/Nre/Editar.cshtml.cs)
// ============================================================================
namespace MS.Servicos;

public class Anexos(IWebHostEnvironment env)
{
    public static readonly string[] Extensoes =
        [".pdf", ".xlsx", ".xls", ".xlsm", ".csv", ".docx", ".doc", ".pptx", ".png", ".jpg", ".jpeg", ".msg", ".eml", ".txt", ".zip"];
    public const long TamanhoMaximo = 25L * 1024 * 1024;   // 25 MB por arquivo

    public string Pasta
    {
        get
        {
            var p = Path.Combine(env.ContentRootPath, "dados", "anexos");
            Directory.CreateDirectory(p);
            return p;
        }
    }

    public string Caminho(string arquivo) => Path.Combine(Pasta, Path.GetFileName(arquivo));

    // Guarda o arquivo com um nome próprio (o original fica no banco). Devolve o nome guardado ou o motivo da recusa.
    public async Task<(string? Arquivo, string? Erro)> SalvarAsync(IFormFile f)
    {
        var nome = Path.GetFileName(f.FileName);
        var ext = Path.GetExtension(nome).ToLowerInvariant();
        if (f.Length == 0) return (null, $"{nome}: arquivo vazio");
        if (!Extensoes.Contains(ext)) return (null, $"{nome}: tipo de arquivo não aceito");
        if (f.Length > TamanhoMaximo) return (null, $"{nome}: maior que {TamanhoMaximo / 1024 / 1024} MB");
        var guardado = $"{Guid.NewGuid():N}{ext}";
        await using var destino = File.Create(Caminho(guardado));
        await f.CopyToAsync(destino);
        return (guardado, null);
    }

    public void Apagar(string arquivo)
    {
        var c = Caminho(arquivo);
        if (File.Exists(c)) File.Delete(c);
    }

    // PDF e imagem abrem no navegador; o resto baixa.
    public static (string Tipo, bool Abrir) TipoDoArquivo(string nome) => Path.GetExtension(nome).ToLowerInvariant() switch
    {
        ".pdf" => ("application/pdf", true),
        ".png" => ("image/png", true),
        ".jpg" or ".jpeg" => ("image/jpeg", true),
        ".txt" => ("text/plain; charset=utf-8", true),
        ".xlsx" or ".xlsm" => ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", false),
        ".xls" => ("application/vnd.ms-excel", false),
        ".csv" => ("text/csv", false),
        ".docx" => ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", false),
        ".doc" => ("application/msword", false),
        ".pptx" => ("application/vnd.openxmlformats-officedocument.presentationml.presentation", false),
        ".msg" => ("application/vnd.ms-outlook", false),
        ".eml" => ("message/rfc822", false),
        ".zip" => ("application/zip", false),
        _ => ("application/octet-stream", false),
    };

    public static string Tamanho(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024:0.#} MB" : $"{Math.Max(1, bytes / 1024)} KB";
}
