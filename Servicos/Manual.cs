// ============================================================================
// MANUAL — SLIDE DO FLUXO DO SISTEMA (tela Manual, Pages/Manual). Guarda o arquivo enviado em dados/manual e as
// imagens de cada slide, que a tela mostra uma a uma (abre no celular também).
//   - PowerPoint (.pptx/.ppt) ... vira uma imagem por slide pelo PowerPoint deste computador (como o Outlook dos
//                                 e-mails); slide oculto não entra. Sem PowerPoint instalado: envie em PDF ou imagem.
//   - Imagem (.png/.jpg) ........ vira o slide único.
//   - PDF ....................... a tela mostra o próprio PDF (o navegador do celular pode só baixar).
// Enviar outro arquivo substitui o anterior; se a conversão falhar, o slide anterior continua.
// ONDE MEXER:
//   - Tipos aceitos e tamanho ...... Extensoes / TamanhoMaximo
//   - Largura das imagens (1920) ... Largura
//   - Conversão do PowerPoint ...... ExportarPowerPoint
// ============================================================================
namespace MS.Servicos;

public class Manual(IWebHostEnvironment env)
{
    public static readonly string[] Extensoes = [".pptx", ".ppt", ".pdf", ".png", ".jpg", ".jpeg"];
    public const long TamanhoMaximo = 50L * 1024 * 1024;   // 50 MB
    const int Largura = 1920;
    static readonly SemaphoreSlim umPorVez = new(1, 1);

    string Pasta => Path.Combine(env.ContentRootPath, "dados", "manual");
    string PastaSlide => Path.Combine(Pasta, "slide");

    // Imagens dos slides, em ordem (1.png, 2.png...).
    public List<string> Slides() =>
        !Directory.Exists(PastaSlide) ? [] : Directory.GetFiles(PastaSlide)
            .Select(Path.GetFileName).OfType<string>()
            .Where(n => int.TryParse(Path.GetFileNameWithoutExtension(n), out _) && Path.GetExtension(n).ToLower() is ".png" or ".jpg" or ".jpeg")
            .OrderBy(n => int.Parse(Path.GetFileNameWithoutExtension(n))).ToList();

    public string? CaminhoSlide(int numero) =>
        Slides().FirstOrDefault(n => Path.GetFileNameWithoutExtension(n) == numero.ToString()) is string n ? Path.Combine(PastaSlide, n) : null;

    // Arquivo enviado (para baixar) e o nome que ele tinha.
    public (string Caminho, string Nome)? Original()
    {
        if (!Directory.Exists(PastaSlide)) return null;
        var arq = Directory.GetFiles(PastaSlide, "original.*").FirstOrDefault();
        if (arq == null) return null;
        var nome = File.Exists(Path.Combine(PastaSlide, "nome.txt")) ? File.ReadAllText(Path.Combine(PastaSlide, "nome.txt")).Trim() : "";
        return (arq, nome == "" ? Path.GetFileName(arq) : nome);
    }

    // Quando o slide foi trocado (vai no endereço das imagens para o navegador não mostrar a antiga).
    public long Versao() => Directory.Exists(PastaSlide) ? Directory.GetLastWriteTimeUtc(PastaSlide).Ticks : 0;

    // Troca o slide pelo arquivo enviado. Devolve o motivo da recusa (ou null se deu certo).
    public async Task<string?> TrocarAsync(IFormFile f)
    {
        var ext = Path.GetExtension(f.FileName).ToLowerInvariant();
        if (!Extensoes.Contains(ext)) return $"Tipo de arquivo não aceito. Envie {string.Join(", ", Extensoes)}.";
        if (f.Length == 0) return "O arquivo está vazio.";
        if (f.Length > TamanhoMaximo) return $"Arquivo maior que {TamanhoMaximo / 1024 / 1024} MB.";

        await umPorVez.WaitAsync();
        var novo = Path.Combine(Pasta, "slide-novo");
        try
        {
            if (Directory.Exists(novo)) Directory.Delete(novo, true);
            Directory.CreateDirectory(novo);
            var original = Path.Combine(novo, "original" + ext);
            await using (var s = File.Create(original)) await f.CopyToAsync(s);
            await File.WriteAllTextAsync(Path.Combine(novo, "nome.txt"), Path.GetFileName(f.FileName));

            if (ext is ".pptx" or ".ppt")
            {
                var erro = await Task.Run(() => ExportarPowerPoint(original, novo));
                if (erro != null) return erro;
            }
            else if (ext is ".png" or ".jpg" or ".jpeg") File.Copy(original, Path.Combine(novo, "1" + ext));

            // Deu certo: o novo slide entra no lugar do anterior.
            if (Directory.Exists(PastaSlide)) Directory.Delete(PastaSlide, true);
            Directory.Move(novo, PastaSlide);
            Directory.SetLastWriteTimeUtc(PastaSlide, DateTime.UtcNow);
            return null;
        }
        finally
        {
            if (Directory.Exists(novo)) try { Directory.Delete(novo, true); } catch { }
            umPorVez.Release();
        }
    }

    // PowerPoint deste computador: abre escondido, salva cada slide visível como imagem e fecha. Se o PowerPoint já
    // estava aberto com outras apresentações, ele continua aberto.
    static string? ExportarPowerPoint(string arquivo, string pasta)
    {
        string? erro = null;
        var t = new Thread(() =>
        {
            dynamic? app = null, apresentacao = null;
            try
            {
                var tipo = Type.GetTypeFromProgID("PowerPoint.Application");
                if (tipo == null) { erro = "Este computador não tem o PowerPoint instalado. Envie o slide em PDF ou como imagem (PNG/JPG)."; return; }
                app = Activator.CreateInstance(tipo)!;
                apresentacao = app.Presentations.Open(arquivo, -1, 0, 0); // somente leitura, sem título, sem janela
                double w = Convert.ToDouble((object)apresentacao.PageSetup.SlideWidth), h = Convert.ToDouble((object)apresentacao.PageSetup.SlideHeight);
                var altura = (int)Math.Round(Largura * h / w);
                int total = apresentacao.Slides.Count, numero = 0;
                for (var i = 1; i <= total; i++)
                {
                    dynamic slide = apresentacao.Slides.Item(i);
                    if ((int)slide.SlideShowTransition.Hidden == -1) continue; // slide oculto
                    slide.Export(Path.Combine(pasta, $"{++numero}.png"), "PNG", Largura, altura);
                }
                if (numero == 0) erro = "A apresentação não tem slides visíveis.";
            }
            catch (Exception e) { erro = $"Não foi possível abrir a apresentação no PowerPoint ({e.Message})."; }
            finally
            {
                try { apresentacao?.Close(); } catch { }
                try { if (app != null && (int)app.Presentations.Count == 0) app.Quit(); } catch { }
            }
        });
        t.SetApartmentState(ApartmentState.STA); // o PowerPoint pede thread STA
        t.Start();
        if (!t.Join(TimeSpan.FromMinutes(3))) return "O PowerPoint não respondeu em 3 minutos. Tente de novo ou envie em PDF.";
        return erro;
    }
}
