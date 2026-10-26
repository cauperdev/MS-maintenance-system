// ============================================================================
// TEXTOS DAS OPÇÕES — como cada opção fixa aparece na tela (ex.: Periodicidade.Semanal → "Semanal").
// ONDE MEXER:
//   - Nome exibido (grupo, periodicidade, status) ...... funções Nome(...)
//   - Cor de cada status (classe do estilos.css) ...... funções Css(...)
//   - Formato de data e hora ........................... função Data(...)
//   - Formato de dinheiro (R$ 1.234,56) ................ função Reais(...)
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
        Grupo.Ehs => "EHS",
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
