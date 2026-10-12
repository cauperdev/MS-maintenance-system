// ============================================================================
// LOGO DAS ÁREAS — imagem que aparece no card da área (tela "Escolha a área", Pages/EscolherArea).
// Fica na pasta de fotos, em dados/fotos/logos-areas/<id da área>.png: a imagem enviada é reduzida (até 600 px) e
// gravada em PNG (mantém o fundo transparente). Gravar outra substitui. Não usa o banco, então a limpeza das
// fotos de 90 dias (Servicos/Fotos.cs, só apaga as fotos das OS registradas no banco) não mexe nas logos.
// Enviar / trocar / remover a logo: tela Áreas (clique na área). Remover a área apaga a logo dela.
// ONDE MEXER: tipos aceitos e tamanho → Extensoes / TamanhoMaximo · tamanho guardado → LadoMaximo
// ============================================================================
using SkiaSharp;

namespace MS.Servicos;

public class LogosArea(IWebHostEnvironment env)
{
    public static readonly string[] Extensoes = [".png", ".jpg", ".jpeg", ".webp"];
    public const long TamanhoMaximo = 5L * 1024 * 1024;   // 5 MB
    const int LadoMaximo = 600;

    string Pasta => Path.Combine(env.ContentRootPath, "dados", "fotos", "logos-areas");
    string Arquivo(int areaId) => Path.Combine(Pasta, $"{areaId}.png");

    public string? Caminho(int areaId) => File.Exists(Arquivo(areaId)) ? Arquivo(areaId) : null;

    // Quando a logo foi trocada (vai no endereço da imagem para o navegador não mostrar a antiga); null = sem logo.
    public long? Versao(int areaId) => Caminho(areaId) is string p ? File.GetLastWriteTimeUtc(p).Ticks : null;

    // Grava a logo enviada. Devolve o motivo da recusa (ou null se deu certo).
    public async Task<string?> SalvarAsync(int areaId, IFormFile f)
    {
        if (!Extensoes.Contains(Path.GetExtension(f.FileName).ToLowerInvariant())) return "Logo: envie uma imagem PNG, JPG ou WEBP.";
        if (f.Length == 0) return "Logo: o arquivo está vazio.";
        if (f.Length > TamanhoMaximo) return $"Logo: imagem maior que {TamanhoMaximo / 1024 / 1024} MB.";

        using var entrada = new MemoryStream();
        await f.CopyToAsync(entrada);
        entrada.Position = 0;
        using var original = SKBitmap.Decode(entrada);
        if (original == null) return "Logo: não foi possível ler a imagem.";

        var escala = Math.Min(1.0, (double)LadoMaximo / Math.Max(original.Width, original.Height));
        int w = Math.Max(1, (int)(original.Width * escala)), h = Math.Max(1, (int)(original.Height * escala));
        using var reduzida = escala < 1
            ? original.Resize(new SKImageInfo(w, h), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            : original.Copy();
        using var img = SKImage.FromBitmap(reduzida);
        using var dados = img.Encode(SKEncodedImageFormat.Png, 100);

        Directory.CreateDirectory(Pasta);
        await File.WriteAllBytesAsync(Arquivo(areaId), dados.ToArray());
        return null;
    }

    public void Apagar(int areaId)
    {
        if (File.Exists(Arquivo(areaId))) File.Delete(Arquivo(areaId));
    }
}
