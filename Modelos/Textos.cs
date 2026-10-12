// ============================================================================
// TEXTOS DAS OPÇÕES — como cada opção fixa aparece na tela (ex.: Periodicidade.Semanal → "Semanal").
// ONDE MEXER:
//   - Nome exibido (grupo, periodicidade, status) ...... funções Nome(...)
//   - Cor de cada status (classe do estilos.css) ...... funções Css(...) e Etiqueta(...) (cartões da NRE)
//   - Formato de data e hora ........................... função Data(...)
//   - Formato de dinheiro (R$ 1.234,56) ................ função Reais(...) · em dólar: Dolares(...) · PTAX: Ptax(...)
//   - Status de equipamento que aparecem nas listas .... StatusEquipamentoOpcoes
// ============================================================================
namespace MS.Modelos;

// Nomes de exibição em português para os enums.
public static class Textos
{
    public static string Nome(this Grupo g) => g switch
    {
        Grupo.Visitante => "Visitante",
        Grupo.Auditor => "Auditor",
        Grupo.Tecnico => "Técnico",
        Grupo.Coordenador => "Coordenador",
        _ => "Administrador",
    };

    public static string Nome(this Periodicidade p) => p switch
    {
        Periodicidade.Diaria => "Diária",
        Periodicidade.Semanal => "Semanal",
        Periodicidade.Quinzenal => "Quinzenal",
        Periodicidade.Mensal => "Mensal",
        Periodicidade.Trimestral => "Trimestral",
        Periodicidade.Semestral => "Semestral",
        _ => "Anual",
    };

    public static string Nome(this Prioridade p) => p switch
    {
        Prioridade.Baixa => "Baixa",
        Prioridade.Media => "Média",
        Prioridade.Alta => "Alta",
        _ => "Urgente",
    };

    public static string Nome(this StatusOS s) => s switch
    {
        StatusOS.Aberta => "Aberta",
        StatusOS.EmAndamento => "Em andamento",
        _ => "Concluída",
    };

    public static string Nome(this SituacaoPrazo s) => s switch
    {
        SituacaoPrazo.EmDia => "Em dia",
        SituacaoPrazo.Atencao => "Atenção",
        _ => "Atrasada",
    };

    public static string Nome(this StatusRevisao s) => s switch
    {
        StatusRevisao.Aguardando => "Aguardando revisão",
        StatusRevisao.Aprovado => "Aprovado",
        _ => "Retornado",
    };

    public static string Nome(this StatusEquipamento s) => s switch
    {
        StatusEquipamento.Operando => "Operando",
        StatusEquipamento.Parado => "Parado",
        StatusEquipamento.EmManutencao => "Em manutenção",
        _ => "Inativo",
    };

    // Manutenção preditiva: unidade da vida útil no singular ou no plural ("1 semana", "3 meses").
    public static string Nome(this UnidadeVida u, int quantidade = 2) => u switch
    {
        UnidadeVida.Dias => quantidade == 1 ? "dia" : "dias",
        UnidadeVida.Semanas => quantidade == 1 ? "semana" : "semanas",
        UnidadeVida.Meses => quantidade == 1 ? "mês" : "meses",
        _ => quantidade == 1 ? "ano" : "anos",
    };
    public static string Vida(this PecaPreditiva p) => $"{p.VidaUtil} {p.Unidade.Nome(p.VidaUtil)}";
    // Situação da peça na manutenção preditiva: como a do prazo da OS, mas a vencida é "Trocar".
    public static string NomePeca(this SituacaoPrazo s) => s == SituacaoPrazo.Atrasada ? "Trocar" : s.Nome();

    // Status de equipamento que aparecem nas telas (sem "Parado").
    public static readonly StatusEquipamento[] StatusEquipamentoOpcoes =
        [StatusEquipamento.Operando, StatusEquipamento.EmManutencao, StatusEquipamento.Inativo];

    public static string Css(this StatusOS s) => s switch
    {
        StatusOS.Aberta => "st-aberta",
        StatusOS.EmAndamento => "st-andamento",
        _ => "st-concluida",
    };

    public static string Css(this SituacaoPrazo s) => s switch
    {
        SituacaoPrazo.EmDia => "st-ok",
        SituacaoPrazo.Atencao => "st-atencao",
        _ => "st-atrasada",
    };

    public static string Css(this StatusRevisao s) => s switch
    {
        StatusRevisao.Aguardando => "st-andamento",
        StatusRevisao.Aprovado => "st-ok",
        _ => "st-atrasada",
    };

    public static string Nome(this TipoNre t) => t switch
    {
        TipoNre.Peca => "Peça / material",
        TipoNre.Servico => "Serviço externo",
        TipoNre.Ferramental => "Ferramental / dispositivo",
        TipoNre.Melhoria => "Melhoria / modificação",
        _ => "Outro",
    };

    // Os nomes seguem a planilha de NRE do cliente (Aguardando Chegada, Cobrado do Cliente...).
    public static string Nome(this StatusNre s) => s switch
    {
        StatusNre.Aberta => "Aguardando aprovação",
        StatusNre.Aprovada => "Aprovada",
        StatusNre.EmAndamento => "Aguardando chegada",
        StatusNre.Cobrada => "Cobrada do cliente",
        StatusNre.Concluida => "Concluída",
        _ => "Recusada",
    };

    public static string Css(this StatusNre s) => s switch
    {
        StatusNre.Aberta or StatusNre.Cobrada => "st-andamento",
        StatusNre.Aprovada or StatusNre.EmAndamento => "st-aberta",
        StatusNre.Concluida => "st-ok",
        _ => "st-atrasada",
    };

    // Cor da etiqueta do cartão no celular: amarela aguardando, azul em curso, verde concluída, vermelha recusada.
    public static string Etiqueta(this StatusNre s) => s switch
    {
        StatusNre.Aberta => "atencao",
        StatusNre.Aprovada or StatusNre.EmAndamento or StatusNre.Cobrada => "neutro",
        StatusNre.Concluida => "em-dia",
        _ => "atrasada",
    };

    public static string Nome(this TipoRemessa t) => t == TipoRemessa.Importacao ? "Importação (DI)" : "Compra nacional (NF)";

    public static string Nome(this TipoAnexoNre t) => t switch
    {
        TipoAnexoNre.Invoice => "Invoice",
        TipoAnexoNre.Di => "DI",
        TipoAnexoNre.NotaFiscal => "NF",
        _ => "Outro",
    };

    // Dólar: "US$ 1.234,56" · PTAX: "5,3498" ou "5,55" (2 a 4 casas, sem separador de milhar).
    public static string Dolares(this decimal v) => $"US$ {v:N2}";
    public static string Ptax(this decimal v) => v.ToString("0.00##");

    public static string Reais(this decimal v) => $"R$ {v:N2}";

    // Spare Parts: tipo de movimento da peça e a quantidade (sem casas decimais à toa: 2 em vez de 2,00).
    public static string Nome(this TipoMovimento t) => t switch
    {
        TipoMovimento.Entrada => "Entrada",
        TipoMovimento.Retirada => "Retirada",
        TipoMovimento.UsoOs => "Uso em OS",
        TipoMovimento.Ajuste => "Ajuste de inventário",
        _ => "Uso desfeito (conclusão retornada)",
    };

    public static string Qtd(this decimal v) => v.ToString("0.##");

    public static string Data(this DateTime d) => d.ToString("dd/MM/yyyy HH:mm");
    public static string Data(this DateTime? d) => d?.ToString("dd/MM/yyyy HH:mm") ?? "—";

    public static readonly string[] Meses =
        { "Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho", "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro" };
}
