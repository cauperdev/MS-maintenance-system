// ============================================================================
// PÁGINA AUTOMÁTICA DO MANUAL (pedido do usuário em 19/10/2026: "detecta pelo texto da atividade e já mostra a pág").
// Acha a página do manual (PDF da máquina, Servicos/ManuaisMaquina.cs) que mais fala das palavras do texto da tarefa:
// "LIMPEZA DAS LENTES DAS CÂMERAS" → a página que descreve as câmeras. É a página da PEÇA, não uma instrução de
// limpeza (o manual pode nem ter plano de manutenção).
// Usado no Cronograma (tarefa sem página escolhida: Pages/Cronograma/Editar → OnPostPaginas, o 📖 já mostra "p.8")
// e na OS (tarefa sem página no plano: Pages/Ordens/Editar → PaginaDaTarefa). A página escolhida à mão vale sempre.
// Regra: cada palavra vale mais quanto MENOS páginas a citam ("DPANEL" em 2 páginas pesa muito; "PLACA" em todas, quase
// nada); ganha a página com a maior soma (empate: a mais adiante). Sem acento, sem plural e pelas 5 primeiras letras
// (LANES = LANE, CÂMERAS = CÂMERA). Testado no manual da EIPE-IA: 10 de 13 tarefas na página certa.
// MANUAL EM INGLÊS (pedido do usuário em 20/10/2026): cada palavra da tarefa procura também a tradução do dicionário
// (Servicos/GlossarioManual.cs: AQUECIMENTO → HEATER/HEATING) e vale a melhor das duas. A palavra no TÍTULO da página
// (alto da folha, onde fica o nome da seção) vale PesoTitulo vezes mais, e a página que cobre mais palavras da tarefa
// ganha um bônus (Cobertura) — assim a seção do procedimento vence a página que só cita a peça. Testado com as páginas
// conferidas à mão (forno Centurion, SP1, SCM, Koh Young, YJ Link): de 23 para 63 de 104 tarefas na página certa.
// LÊ A ATIVIDADE INTEIRA (pedido do usuário em 21/10/2026: "não quero que leia só algumas palavras, quero que leia a
// atividade e mostre a página onde ela aparece"): não vale mais a soma das palavras espalhadas pela folha, e sim o
// TRECHO da página (cerca de 25 palavras) que junta mais palavras da tarefa, com bônus quando as vizinhas da tarefa
// também estão juntas na página. Ex.: "UNIDADE DE DESCARTE, TESOURA, CHAVES ALLEN…" ia para a p.10 (palavras soltas
// pela folha) e agora vai para a p.6, onde a lista está escrita junta.
// PDF escaneado (só imagem) ou com o texto embaralhado (manuais da DEK) não tem texto: nenhuma página.
// ONDE MEXER:
//   - Palavras que não contam ..... PalavrasGenericas  · tamanho da raiz: Raiz · nota mínima: NotaMinima
//   - Português → inglês .......... Servicos/GlossarioManual.cs
//   - Título da página ............ FracaoTitulo (parte de cima da folha) · PesoTitulo · Cobertura
//   - Trecho ...................... JanelaMinima · PalavrasPorConceito · DistanciaPar · Sequencia · PesoPagina
//   - Texto de cada página ........ LerManual (PdfPig; guardado na memória por arquivo — trocar o manual muda o nome)
// ============================================================================
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace MS.Servicos;

public class PaginaAutomatica(ManuaisMaquina manuais, ILogger<PaginaAutomatica> log)
{
    public record Achada(int Pagina, string Palavras);

    const int Raiz = 5;
    const double NotaMinima = 1;   // só uma palavra que aparece em quase todas as páginas não basta
    const double FracaoTitulo = 0.3;   // o "título" é o terço de cima da folha
    const double PesoTitulo = 2;
    const double Cobertura = 1;        // página com todas as palavras da tarefa vale até o dobro
    // TRECHO (21/10/2026): valores escolhidos testando as 1.788 tarefas que já tinham página (50,6% → 56,3% certas).
    const int JanelaMinima = 25;         // tamanho mínimo do trecho (palavras da página)
    const int PalavrasPorConceito = 3;   // o trecho cresce com o tamanho da tarefa
    const int DistanciaPar = 3;          // "juntas" = até 3 palavras de distância
    const double Sequencia = 2;          // trecho com as palavras vizinhas da tarefa também juntas vale até o triplo
    const double PesoPagina = 0;         // quanto a folha inteira ainda conta (com mais, piorou no teste)
    static readonly HashSet<string> PalavrasGenericas = new((
        "DE DA DO DAS DOS EM NA NO NAS NOS COM POR PARA SEM QUE UMA " +
        "VERIFICAR VERIFICACAO VERIFIQUE LIMPEZA LIMPAR TESTAR TESTE CHECAR INSPECIONAR INSPECAO AJUSTAR AJUSTE " +
        "TROCAR TROCA REAPERTO REAPERTAR FUNCIONAMENTO ESTADO CONDICAO CONDICOES GERAL MAQUINA EQUIPAMENTO").Split(' '));

    record Pagina(int Numero, string[] Sequencia, HashSet<string> Raizes, HashSet<string> Titulo);
    // Uma palavra da tarefa: a raiz dela e as das traduções (vale a que a página tiver); Palavra = como aparece na tarefa.
    record Conceito(HashSet<string> Raizes, string Palavra);
    record Indice(List<Pagina> Paginas, Dictionary<string, int> EmQuantas);

    // Um índice por arquivo de manual (o nome muda quando o manual é trocado).
    readonly ConcurrentDictionary<string, Lazy<Task<Indice?>>> indices = new();

    // Página de cada texto (mesma ordem); null = não achou.
    public async Task<List<Achada?>> AcharAsync(string? arquivoManual, IEnumerable<string> textos)
    {
        var indice = arquivoManual == null ? null
            : await indices.GetOrAdd(arquivoManual, a => new Lazy<Task<Indice?>>(() => Task.Run(() => LerManual(a)))).Value;
        return textos.Select(t => indice == null ? null : Achar(indice, t)).ToList();
    }

    Indice? LerManual(string arquivo)
    {
        var caminho = manuais.Caminho(arquivo);
        if (!File.Exists(caminho)) return null;
        try
        {
            var paginas = new List<Pagina>();
            using var pdf = PdfDocument.Open(caminho);
            foreach (var p in pdf.GetPages())
            {
                var palavras = p.GetWords().ToList();
                var texto = string.Join(' ', palavras.Select(w => w.Text));
                var maiusculo = SemAcento(texto);
                // O sumário cita tudo e não é a página certa.
                if (maiusculo.Contains("SUMARIO") || (Regex.IsMatch(maiusculo, "INDICE|CONTENTS") && texto.Contains(".....")))
                    continue;
                // No PDF a altura conta de baixo para cima: o título é o que está acima de (1 − FracaoTitulo) da folha.
                var titulo = string.Join(' ', palavras.Where(w => w.BoundingBox.Top >= p.Height * (1 - FracaoTitulo)).Select(w => w.Text));
                var sequencia = Palavras(texto).Select(x => x.Raiz).ToArray();
                paginas.Add(new Pagina(p.Number, sequencia, sequencia.ToHashSet(),
                    Palavras(titulo).Select(x => x.Raiz).ToHashSet()));
            }
            var emQuantas = new Dictionary<string, int>();
            foreach (var p in paginas)
                foreach (var r in p.Raizes) emQuantas[r] = emQuantas.GetValueOrDefault(r) + 1;
            return new Indice(paginas, emQuantas);
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Página automática: não consegui ler o texto do manual {Arquivo}", arquivo);
            return null;
        }
    }

    static Achada? Achar(Indice indice, string texto)
    {
        var conceitos = Conceitos(texto);
        if (conceitos.Count == 0) return null;
        var n = conceitos.Count;
        var total = indice.Paginas.Count;
        // Cada raiz (portuguesa ou tradução) → os conceitos da tarefa que ela representa.
        var deRaiz = new Dictionary<string, List<int>>();
        for (int k = 0; k < n; k++)
            foreach (var r in conceitos[k].Raizes)
                (deRaiz.TryGetValue(r, out var l) ? l : deRaiz[r] = []).Add(k);
        var janela = Math.Max(JanelaMinima, PalavrasPorConceito * n);
        (double Nota, Achada A)? melhor = null;
        foreach (var p in indice.Paginas)
        {
            // Peso de cada palavra da tarefa nesta página: a melhor forma que a página tiver (a portuguesa ou uma
            // tradução), mais rara = mais peso; no título, PesoTitulo vezes.
            var peso = new double[n];
            for (int k = 0; k < n; k++)
                peso[k] = conceitos[k].Raizes.Where(p.Raizes.Contains)
                    .Select(r => Math.Log((double)total / indice.EmQuantas[r]) * (p.Titulo.Contains(r) ? PesoTitulo : 1))
                    .DefaultIfEmpty(0).Max();
            if (peso.All(x => x <= 0)) continue;

            // Onde cada palavra da tarefa aparece na página, na ordem do texto.
            var achadas = new List<(int Pos, int K)>();
            for (int i = 0; i < p.Sequencia.Length; i++)
                if (deRaiz.TryGetValue(p.Sequencia[i], out var ks))
                    foreach (var k in ks) if (peso[k] > 0) achadas.Add((i, k));

            // O TRECHO: o pedaço da página (até `janela` palavras) que junta mais palavras da tarefa.
            var conta = new int[n];
            double soma = 0, notaTrecho = 0;
            int distintas = 0, inicio = 0;
            bool[]? noMelhor = null;
            foreach (var (pos, k) in achadas)
            {
                if (conta[k]++ == 0) { soma += peso[k]; distintas++; }
                while (pos - achadas[inicio].Pos >= janela)
                {
                    var s = achadas[inicio++].K;
                    if (--conta[s] == 0) { soma -= peso[s]; distintas--; }
                }
                var nt = soma * (1 + Cobertura * distintas / n);
                if (nt > notaTrecho) { notaTrecho = nt; noMelhor = conta.Select(c => c > 0).ToArray(); }
            }

            // Palavras vizinhas na tarefa que também estão juntas na página ("FILTRO DE AR" ↔ "air filter").
            var lugares = achadas.ToLookup(a => a.K, a => a.Pos);
            int pares = 0;
            for (int k = 0; k + 1 < n; k++)
                if (lugares[k].Any(a => lugares[k + 1].Any(b => Math.Abs(a - b) <= DistanciaPar))) pares++;
            if (n > 1) notaTrecho *= 1 + Sequencia * pares / (n - 1);

            // A folha inteira conta um pouco (página que fala do assunto todo).
            var naPagina = peso.Count(x => x > 0);
            var nota = notaTrecho + PesoPagina * peso.Sum() * (1 + Cobertura * naPagina / n);

            if (nota >= NotaMinima && (melhor == null || nota >= melhor.Value.Nota))   // empate: a mais adiante
                melhor = (nota, new Achada(p.Numero, string.Join(", ",
                    Enumerable.Range(0, n).Where(k => noMelhor?[k] ?? peso[k] > 0).Select(k => conceitos[k].Palavra))));
        }
        return melhor?.A;
    }

    // Palavras da tarefa sem repetir, cada uma com as traduções do dicionário (Servicos/GlossarioManual.cs).
    static List<Conceito> Conceitos(string texto)
    {
        var lista = new Dictionary<string, Conceito>();
        foreach (Match m in Regex.Matches(texto.ToUpperInvariant(), @"[\p{L}\p{N}]{3,}"))
        {
            var sem = SemAcento(m.Value);
            if (!Regex.IsMatch(sem, "^[A-Z0-9]+$") || PalavrasGenericas.Contains(sem)) continue;
            var raiz = RaizDe(sem);
            if (lista.ContainsKey(raiz)) continue;
            var raizes = new HashSet<string> { raiz };
            foreach (var en in GlossarioManual.Traducoes(sem)) raizes.Add(RaizDe(SemAcento(en)));
            lista[raiz] = new Conceito(raizes, m.Value);
        }
        return lista.Values.ToList();
    }

    static string RaizDe(string semAcento)
    {
        var r = SemPlural(semAcento);
        return r[..Math.Min(Raiz, r.Length)];
    }

    // Palavras de 3+ letras sem as genéricas: (raiz para comparar, palavra como está, para mostrar: CÂMERAS, ROBÔ).
    static IEnumerable<(string Raiz, string Palavra)> Palavras(string texto)
    {
        foreach (Match m in Regex.Matches(texto.ToUpperInvariant(), @"[\p{L}\p{N}]{3,}"))
        {
            var sem = SemAcento(m.Value);
            if (!Regex.IsMatch(sem, "^[A-Z0-9]+$") || PalavrasGenericas.Contains(sem)) continue;
            yield return (RaizDe(sem), m.Value);
        }
    }

    // SENSORES → SENSOR, CONEXOES → CONEX, LANES → LANE (sobram pelo menos 4 letras).
    static string SemPlural(string p)
    {
        foreach (var fim in new[] { "OES", "ES", "S" })
            if (p.EndsWith(fim) && p.Length - fim.Length >= 4) return p[..^fim.Length];
        return p;
    }

    static string SemAcento(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }
}
