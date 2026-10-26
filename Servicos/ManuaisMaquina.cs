// ============================================================================
// MANUAL DA MÁQUINA — o PDF do manual de cada equipamento, na pasta dados/manuais (pedido do usuário em 19/10/2026).
// Anexado no cadastro do Equipamento (aba Manual); abre no visualizador do MS (Shared/_VisorManual.cshtml +
// wwwroot/js/manual.js) na OS, já na página da tarefa, e no Cronograma, onde o coordenador escolhe essa página.
// Não é apagado sozinho: sai quando é trocado ou quando o equipamento é removido.
// ONDE MEXER:
//   - Tamanho máximo do PDF ........... TamanhoMaximo (o envio inteiro: [RequestSizeLimit] em Pages/Equipamentos/Editar.cshtml.cs)
//   - Quem abre o arquivo ............. OnGetManualAsync em Equipamentos/Editar, Cronograma/Editar e Ordens/Editar
// ============================================================================
namespace MS.Servicos;

public class ManuaisMaquina(IWebHostEnvironment env)
{
    public const long TamanhoMaximo = 500L * 1024 * 1024;   // 500 MB (era 150; aumentado a pedido em 19/10/2026): manual com muita foto é grande

    public string Pasta
    {
        get
        {
            var p = Path.Combine(env.ContentRootPath, "dados", "manuais");
            Directory.CreateDirectory(p);
            return p;
        }
    }

    public string Caminho(string arquivo) => Path.Combine(Pasta, Path.GetFileName(arquivo));

    public long Tamanho(string arquivo) => File.Exists(Caminho(arquivo)) ? new FileInfo(Caminho(arquivo)).Length : 0;

    // Tamanho para a tela: "850 KB", "12.5 MB".
    public static string TamanhoTexto(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024:0.#} MB" : $"{Math.Max(1, bytes / 1024)} KB";

    // Guarda o PDF com um nome próprio (o original fica no banco). Devolve o nome guardado ou, se recusou, o motivo.
    public async Task<(string? Arquivo, string? Erro)> SalvarAsync(IFormFile f)
    {
        var nome = Path.GetFileName(f.FileName);
        if (f.Length == 0) return (null, $"{nome}: arquivo vazio.");
        if (f.Length > TamanhoMaximo) return (null, $"{nome}: maior que {TamanhoMaximo / 1024 / 1024} MB.");
        // Confere o começo do arquivo ("%PDF"), não só a extensão.
        var cabecalho = new byte[4];
        await using (var s = f.OpenReadStream())
            if (await s.ReadAsync(cabecalho) < 4 || System.Text.Encoding.ASCII.GetString(cabecalho) != "%PDF")
                return (null, $"{nome}: não é um PDF. Anexe o manual em PDF.");
        var guardado = $"manual-{Guid.NewGuid():N}.pdf";
        await using var destino = File.Create(Caminho(guardado));
        await f.CopyToAsync(destino);
        return (guardado, null);
    }

    public void Apagar(string arquivo)
    {
        var c = Caminho(arquivo);
        if (File.Exists(c)) File.Delete(c);
    }
}
