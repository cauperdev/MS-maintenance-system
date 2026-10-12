// ============================================================================
// RESUMO DA NRE — as linhas da aba "Summary" da planilha de NRE do cliente: uma por DI / compra nacional do Detalhado
// (sem Detalhado ainda, uma linha com a previsão da aprovação), e a planilha Excel nesse formato.
// Usado em: aba Resumo da NRE (Pages/Nre/Editar) e Extração (Pages/Nre/Index),
// botão "Baixar resumo" da NRE. As contas ficam em Modelos/Entidades.cs (Nre e NreRemessa).
// ============================================================================
using MS.Modelos;

namespace MS.Servicos;

public static class ResumoNre
{
    // Uma linha do resumo. Valores em reais vazios = DI ainda sem PTAX (não dá para converter).
    public record Linha(Nre Nre, string Rotulo, string? Invoice, decimal? AprovadoUsd, decimal FreteSeguroUsd, decimal? Ptax,
        decimal? FinalReais, decimal? Taxas, bool Previsao, bool PtaxPendente)
    {
        public decimal? Total => FinalReais + Taxas;
    }

    public static List<Linha> Linhas(Nre n)
    {
        if (n.TemDetalhe)
            return n.RemessasEmOrdem.Select(r => new Linha(n, r.Rotulo(), r.Invoice, r.ItensUsd(), r.FreteSeguroUsd(), r.Ptax,
                r.PtaxPendente ? null : r.Reais(), r.PtaxPendente ? null : r.Taxas(n.Taxa), false, r.PtaxPendente)).ToList();
        if (n.Previsto() is decimal previsto)
            return [new Linha(n, "Previsão (aprovação)", null, n.AprovadoUsd, 0, n.PtaxAprovacao, previsto, n.TaxasReais(), true, false)];
        return n.AprovadoUsd != null ? [new Linha(n, "Previsão (aprovação)", null, n.AprovadoUsd, 0, n.PtaxAprovacao, null, null, true, false)] : [];
    }

    // Excel: Summary (formato do cliente, com o total no fim) · Detalhado (os itens de cada DI / NF) · NRE (uma linha por NRE).
    public static byte[] Planilha(IEnumerable<Nre> nres)
    {
        var lista = nres.ToList();
        var linhas = lista.SelectMany(Linhas).ToList();
        var summary = linhas.Select(l => new object?[]
        {
            l.Nre.Modelo, l.Nre.CodigoNre, l.Nre.Titulo ?? l.Nre.Numero, l.Rotulo, l.Invoice, l.AprovadoUsd,
            l.FreteSeguroUsd == 0 ? null : l.FreteSeguroUsd, l.Ptax, l.FinalReais, l.Taxas, l.Total, l.Nre.Aprovador, l.Nre.Status.Nome(),
        }).ToList();
        if (linhas.Count > 0)
            summary.Add(["Total", null, $"{lista.Count} NRE", null, null, linhas.Sum(l => l.AprovadoUsd ?? 0), linhas.Sum(l => l.FreteSeguroUsd),
                null, linhas.Sum(l => l.FinalReais ?? 0), linhas.Sum(l => l.Taxas ?? 0), linhas.Sum(l => l.Total ?? 0), null, null]);

        var detalhado = new List<object?[]>();
        foreach (var n in lista)
            foreach (var r in n.RemessasEmOrdem)
            {
                foreach (var i in r.Itens)
                    detalhado.Add([n.Numero, n.Titulo, r.Rotulo(), r.Invoice, r.Ptax, i.Codigo, i.Descricao, i.Quantidade,
                        r.Importacao ? i.ValorUnitarioUsd : null, r.Importacao && i.ValorUnitarioUsd != null ? i.TotalUsd() : null,
                        r.Importacao && i.ValorUnitarioUsd != null ? null : i.ValorUnitario, r.PtaxPendente && i.ValorUnitarioUsd != null ? null : r.ReaisItem(i)]);
                if (r.Importacao && r.Frete > 0)
                    detalhado.Add([n.Numero, n.Titulo, r.Rotulo(), r.Invoice, r.Ptax, null, "Frete", 1m, r.Frete, r.Frete, null, r.Reais(r.Frete)]);
                if (r.Importacao && r.Seguro > 0)
                    detalhado.Add([n.Numero, n.Titulo, r.Rotulo(), r.Invoice, r.Ptax, null, "Seguro", 1m, r.Seguro, r.Seguro, null, r.Reais(r.Seguro)]);
            }

        return Excel.Planilha(
            ("Summary",
                ["Project", "NRE", "Item/Project", "DI / NF", "Invoice", "Approved Value (USD)", "Freight + Insurance (USD)", "Ptax",
                 "Final Amount (BRL)", "Pis/Cofins, CSLL, R&D Tax (BRL)", "Final Amount (BRL) + Taxes", "Approver", "Status"],
                summary),
            ("Detalhado",
                ["NRE", "Título", "DI / NF", "Invoice", "PTAX", "Código", "Descrição", "Quantidade", "US$ unit.", "US$ total", "R$ unit.", "R$ total"],
                detalhado),
            ("NRE",
                ["Número", "Status", "Modelo", "NRE", "Título", "Aprovada em", "Aprovador", "Aprovado (US$)", "Ptax da aprovação",
                 "Final Amount previsto (R$)", "PO", "Evidência", "Custo real (R$ Total NF)", "Taxa (%)", "Taxas (R$)", "Total a cobrar (R$)",
                 "Custo absorvido pela empresa (R$)", "Motivo da diferença", "NF", "Invoice", "DI", "Tipo", "Equipamento", "Área", "Descrição",
                 "Solicitante", "Aberta em", "Cobrada em", "Concluída em"],
                lista.Select(n => new object?[]
                {
                    n.Numero, n.Status.Nome(), n.Modelo, n.CodigoNre, n.Titulo, n.AprovadaClienteEm, n.Aprovador, n.AprovadoUsd, n.PtaxAprovacao,
                    n.ValorAprovado, n.PedidoCompra, n.Evidencia, n.Total(), n.Taxa, n.TaxasReais(), n.TotalCobrar(),
                    n.CustoAbsorvido, n.MotivoDiferenca, n.Juntar(r => r.NotaFiscal), n.Juntar(r => r.Invoice), n.Juntar(r => r.Di),
                    n.Tipo.Nome(), n.Maquina, n.Celula?.Nome, n.Descricao, n.Solicitante?.NomeCompleto, n.CriadaEm, n.CobradaEm, n.ConcluidaEm,
                })));
    }
}
