// ============================================================================
// ENTIDADES — o formato de cada registro guardado no banco (uma classe = uma tabela).
//   Celula · Usuario · Equipamento · PlanoManutencao (Cronograma) + TarefaPlano
//   · OrdemServico + TarefaOS / PecaOS / Imagem · Finalizacao (ciclo concluído)
//   · SparePart + MovimentoPeca (estoque de peças) · WorkOrderJms (histórico do JMS antigo) · Historico · PermissaoGrupo
// Listas fixas (enum): Grupo, Periodicidade (o número é a quantidade de dias do ciclo), StatusOS,
// StatusRevisao, StatusEquipamento, TipoMovimento.
// ONDE MEXER:
//   - Campo novo ............................... propriedade na classe + ALTER TABLE em Program.cs
//   - Nome que aparece na tela de cada opção ... Modelos/Textos.cs
//   - ATENÇÃO: não troque os números dos enums — eles estão gravados no banco.
// ============================================================================
namespace MS.Modelos;

// Ehs (14/10/2026): o pessoal de EHS — edita só as tarefas de NR do plano (Servicos/Ehs.cs).
public enum Grupo { Visitante = 1, Auditor = 2, Tecnico = 3, Coordenador = 4, Administrador = 5, Ehs = 6 }

// O valor de cada periodicidade é o número de dias do ciclo (Regra 03).
public enum Periodicidade { Diaria = 1, Semanal = 7, Quinzenal = 15, Mensal = 30, Trimestral = 90, Semestral = 180, Anual = 365 }

public enum Prioridade { Baixa = 1, Media = 2, Alta = 3, Urgente = 4 }

// EmAndamento (2) não é mais usado: a OS fica Aberta até ser concluída. O valor fica reservado no banco.
public enum StatusOS { Aberta = 1, EmAndamento = 2, Concluida = 3 }

// Situação do prazo da OS (coluna Status da lista de Ordens no computador): Atrasada, Atenção (vence em até 7 dias) ou Em dia.
// Não é gravada no banco: é calculada na hora por OrdemServico.Situacao.
public enum SituacaoPrazo { EmDia, Atencao, Atrasada }

public enum StatusRevisao { Aguardando = 1, Aprovado = 2, Retornado = 3 }

// Parado (2) foi retirado das opções; o valor fica reservado para não alterar os números salvos no banco.
public enum StatusEquipamento { Operando = 1, Parado = 2, EmManutencao = 3, Inativo = 4 }

public enum TipoImagem { Antes = 1, Depois = 2 }

// Spare Parts: o que aconteceu com o estoque da peça. A quantidade do movimento tem sinal (+ entrou, − saiu).
public enum TipoMovimento { Entrada = 1, Retirada = 2, UsoOs = 3, Ajuste = 4, Estorno = 5 }

// Célula (cliente: SAMSUNG, AVELL, ISS...). Na tela, nas planilhas e nas mensagens o nome é "Área" (desde 02/10/2026);
// no código e no banco continua Celula / CelulaId / tabela Celulas / endereço /Celulas.
public class Celula
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    // Logo do cliente da área (opcional, pedido do usuário em 13/10/2026): PNG em dados/fotos ("logo-....png"),
    // mostrado nos cartões do alto do Dashboard e aberto por /?handler=Logo&id={id}. Sem logo, o cartão mostra o nome.
    public string? Logo { get; set; }
}

public class Usuario
{
    public int Id { get; set; }
    public string Login { get; set; } = "";
    public string Email { get; set; } = "";
    public string PrimeiroNome { get; set; } = "";
    public string UltimoNome { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public Grupo Grupo { get; set; } = Grupo.Tecnico;
    public int? CelulaId { get; set; }
    public Celula? Celula { get; set; }
    public bool Ativo { get; set; } = true;
    public bool MembroEquipe { get; set; } = true;
    // Login pela rede (AD): a matrícula entra com a senha do Windows. Conta local (ex.: admin de emergência,
    // contas de demonstração) entra com a senha guardada no sistema (SenhaHash).
    public bool ContaLocal { get; set; }
    // Avisos por e-mail (Servicos/Avisos.cs), pedido do usuário em 08/10/2026: RecebeEmail desmarcado = não recebe nenhum
    // e-mail automático. A pessoa recebe os avisos só da área do cadastro (CelulaId): assim um assunto da SAMSUNG não
    // vai para quem é da AVELL. (As caixinhas "Avisos das áreas" saíram da tela de Usuários em 13/10/2026, a pedido do
    // usuário; a coluna AreasAvisos ficou no banco, sem uso.)
    public bool RecebeEmail { get; set; } = true;

    public string NomeCompleto => $"{PrimeiroNome} {UltimoNome}".Trim();
}

public class Equipamento
{
    public int Id { get; set; }
    public string Codigo { get; set; } = "";
    // IRF: código da Samsung nas máquinas que vieram da fábrica dela (para o mapeamento que a Samsung pede). Opcional e
    // único — as outras máquinas ficam em branco. Pedido do usuário em 07/10/2026.
    public string? Irf { get; set; }
    // Etiqueta eletrônica (e-paper) colada na máquina: o código de barras da tela dela (ex.: 1750107D). Liga a etiqueta à
    // máquina para o MS mandar a manutenção de data mais próxima. Opcional e única; entra pelo cadastro ou pelo Excel.
    // Pedido do usuário em 14/10/2026.
    public string? Etiqueta { get; set; }
    public string Nome { get; set; } = "";
    public string? Modelo { get; set; }
    // Número de série da máquina (opcional, todas as áreas). Aparece na lista de Equipamentos e, quando preenchido, na
    // visualização e na impressão da OS e da Finalização. Pedido do usuário em 13/10/2026.
    public string? NumeroSerie { get; set; }
    public string Predio { get; set; } = "";        // prédio onde a máquina fica (obrigatório no cadastro)
    public string Localizacao { get; set; } = "";
    public StatusEquipamento Status { get; set; } = StatusEquipamento.Operando;
    public int? CelulaId { get; set; }
    public Celula? Celula { get; set; }
    // Foto da máquina (opcional, pedido do usuário em 08/10/2026): arquivo em dados/fotos ("equip-....jpg"), aberto
    // por /Equipamentos/Editar/{id}?handler=Foto.
    public string? Foto { get; set; }
    // Manual da máquina em PDF (pedido do usuário em 19/10/2026): arquivo em dados/manuais ("manual-....pdf"), com o
    // nome original, quando e por quem foi anexado. Abre no visualizador do MS (Shared/_VisorManual.cshtml) na OS,
    // no Cronograma (para escolher a página de cada tarefa) e aqui no cadastro.
    public string? Manual { get; set; }
    public string? ManualNome { get; set; }
    public DateTime? ManualEm { get; set; }
    public string? ManualPor { get; set; }

    public string Descricao => $"{Codigo} - {Nome}";
}

// A manutenção preditiva (aba do equipamento, de 08/10/2026) foi retirada em 13/10/2026 a pedido do usuário. A tabela
// PecasPreditivas continua no banco com o que foi cadastrado, mas o sistema não lê nem grava mais nela.

public class PlanoManutencao
{
    public int Id { get; set; }
    public int EquipamentoId { get; set; }
    public Equipamento? Equipamento { get; set; }
    public string Nome { get; set; } = "";
    public string OrdemTrabalho { get; set; } = "";
    public string Descricao { get; set; } = "";
    public Periodicidade Periodicidade { get; set; } = Periodicidade.Mensal;
    public int? LembreteDias { get; set; }       // lembrar N dias antes da data da manutenção (vale para a OS do plano)
    // Técnico e coordenador responsáveis (pedido do usuário em 13/10/2026): escolhidos aqui, no Cronograma; a OS do
    // plano só mostra o que veio daqui (OrdemServico.TecnicoId / CoordenadorId são a cópia, mantida por Regras.ResponsaveisAsync).
    public int? TecnicoId { get; set; }
    public Usuario? Tecnico { get; set; }
    public int? CoordenadorId { get; set; }
    public Usuario? Coordenador { get; set; }
    public List<TarefaPlano> Tarefas { get; set; } = new();
}

public class TarefaPlano
{
    public int Id { get; set; }
    public int PlanoId { get; set; }
    public string Descricao { get; set; } = "";
    public int Ordem { get; set; }
    // Página do manual da máquina para esta tarefa (opcional). A OS não guarda cópia: lê daqui, pelo texto da tarefa,
    // então trocar a página no plano já vale para todas as OS dele.
    public int? Pagina { get; set; }
}

public class OrdemServico
{
    public int Id { get; set; }
    public string Numero { get; set; } = "";
    public int EquipamentoId { get; set; }
    public Equipamento? Equipamento { get; set; }
    public int? PlanoId { get; set; }
    public PlanoManutencao? Plano { get; set; }
    public Periodicidade Tipo { get; set; } = Periodicidade.Mensal;
    public Prioridade Prioridade { get; set; } = Prioridade.Media;
    public StatusOS Status { get; set; } = StatusOS.Aberta;
    public int? TecnicoId { get; set; }
    public Usuario? Tecnico { get; set; }
    public int? CoordenadorId { get; set; }
    public Usuario? Coordenador { get; set; }
    public DateTime DataAbertura { get; set; }
    public DateTime DataConclusao { get; set; } // prazo do ciclo
    public DateTime? DataProxima { get; set; }   // próxima manutenção escolhida pelo usuário (vazio = prazo + período)
    public int? LembreteDias { get; set; }       // lembrar quantos dias antes do prazo (em branco = não lembrar)
    public DateTime? DataLembrete { get; set; } // calculado: prazo − LembreteDias; a partir deste dia a OS aparece no sino
    public string Descricao { get; set; } = "";
    // Conclusão retornada pelo coordenador (Regra 05): o motivo aparece na coluna Retorno da lista de Ordens de serviço
    // (e no cartão do celular) até a OS ser concluída de novo (Regras.Concluir limpa).
    public string? MotivoRetorno { get; set; }
    public DateTime? RetornadaEm { get; set; }
    public string? RetornadaPor { get; set; }
    public List<TarefaOS> Tarefas { get; set; } = new();
    public List<PecaOS> Pecas { get; set; } = new();
    public List<Imagem> Imagens { get; set; } = new();

    // Atrasada só a partir do dia seguinte à data da manutenção (compara o dia, não a hora: no próprio dia ainda está no prazo).
    public bool Atrasada(DateTime agora) => Status != StatusOS.Concluida && DataConclusao.Date < agora.Date;

    // Atenção = faltam até DiasAtencao dias para a data da manutenção.
    public const int DiasAtencao = 7;
    public SituacaoPrazo Situacao(DateTime agora) =>
        Atrasada(agora) ? SituacaoPrazo.Atrasada
        : (DataConclusao.Date - agora.Date).Days <= DiasAtencao ? SituacaoPrazo.Atencao
        : SituacaoPrazo.EmDia;
}

public class TarefaOS
{
    public int Id { get; set; }
    public int OrdemServicoId { get; set; }
    public string Descricao { get; set; } = "";
    public bool Concluida { get; set; }
    public int Ordem { get; set; }
}

public class PecaOS
{
    public int Id { get; set; }
    public int OrdemServicoId { get; set; }
    public string? Codigo { get; set; }
    public string Descricao { get; set; } = "";
    public decimal Quantidade { get; set; } = 1;
}

public class Imagem
{
    public int Id { get; set; }
    public int? OrdemServicoId { get; set; }
    public int? FinalizacaoId { get; set; }
    public TipoImagem Tipo { get; set; }
    public string Arquivo { get; set; } = "";
    public DateTime CriadaEm { get; set; }   // quando foi enviada
    public DateTime? TiradaEm { get; set; }  // quando foi tirada (data gravada na foto); fotos antigas não têm

    // Data e hora mostradas em cima da foto.
    public DateTime Hora() => TiradaEm ?? CriadaEm;
}

// SPARE PARTS — cadastro das peças de reposição e onde cada uma foi usada. O código é o mesmo que o técnico
// lança em "Peças utilizadas" da OS: ao concluir a OS, o uso fica registrado sozinho (Servicos/Estoque.cs).
// Local, Estoque e EstoqueMinimo não aparecem mais nas telas (controle de estoque tirado a pedido do usuário; a
// quantidade saiu da tela Spare Parts em 08/10/2026 — só histórico do código); ficam no banco caso queiram voltar a usar.
// CADA ÁREA TEM AS SUAS PEÇAS (pedido do usuário em 13/10/2026): a peça é da área (CelulaId) em que foi cadastrada ou da
// área da máquina da OS; o mesmo código pode existir em duas áreas, cada um com o seu histórico. Sem área = null.
public class SparePart
{
    public int Id { get; set; }
    public int? CelulaId { get; set; }              // área (cliente) dona da peça; o código é único dentro da área
    public string Codigo { get; set; } = "";
    public string Descricao { get; set; } = "";
    public string? Local { get; set; }
    public decimal Estoque { get; set; }
    public decimal? EstoqueMinimo { get; set; }
    public string? Maquina { get; set; }            // tipo de máquina da peça (nome, ex.: ADESIVADORA) — opcional
    public bool Ativa { get; set; } = true;         // inativa: some das sugestões da OS, mas o histórico fica
    public DateTime CriadaEm { get; set; }
    public List<MovimentoPeca> Movimentos { get; set; } = new();
}

// Cada entrada, retirada, ajuste ou uso em OS de uma peça (o histórico da peça).
public class MovimentoPeca
{
    public int Id { get; set; }
    public int SparePartId { get; set; }
    public SparePart? SparePart { get; set; }
    public DateTime Quando { get; set; }
    public TipoMovimento Tipo { get; set; }
    public decimal Quantidade { get; set; }         // com sinal: + entrou, − saiu
    public decimal Saldo { get; set; }              // estoque depois do movimento
    public int? OrdemServicoId { get; set; }
    public OrdemServico? OrdemServico { get; set; }
    public int? FinalizacaoId { get; set; }         // conclusão que baixou a peça (para devolver se for retornada)
    public Finalizacao? Finalizacao { get; set; }
    public int? EquipamentoId { get; set; }
    public Equipamento? Equipamento { get; set; }
    public string Usuario { get; set; } = "";
    public string? Observacao { get; set; }
}

// Registro congelado de cada ciclo concluído (Regras 04, 05 e 06).
public class Finalizacao
{
    public int Id { get; set; }
    public int OrdemServicoId { get; set; }
    public OrdemServico? OrdemServico { get; set; }
    public string NumeroOS { get; set; } = "";
    public int EquipamentoId { get; set; }
    public Equipamento? Equipamento { get; set; }
    public Periodicidade Tipo { get; set; }
    public string Descricao { get; set; } = "";
    public int? TecnicoId { get; set; }
    public Usuario? Tecnico { get; set; }
    public int? CoordenadorId { get; set; }
    public Usuario? Coordenador { get; set; }
    public DateTime DataAbertura { get; set; }
    public DateTime DataConclusao { get; set; }
    public DateTime ConcluidaEm { get; set; }
    public string Checklist { get; set; } = "";
    public string Pecas { get; set; } = "";
    public StatusRevisao StatusRevisao { get; set; } = StatusRevisao.Aguardando;
    public DateTime? AprovadaEm { get; set; }
    public int? AprovadaPorId { get; set; }
    public Usuario? AprovadaPor { get; set; }
    public List<Imagem> Imagens { get; set; } = new();
}

// Histórico do JMS antigo: Work Order finalizada no sistema anterior. Só consulta — entra pelo Importar Excel
// (aba JMS da planilha MANUTENÇÃO PREVENTIVA) e não se liga ao cadastro do MS (os códigos do JMS são outros).
public class WorkOrderJms
{
    public int Id { get; set; }
    public string WorkOrder { get; set; } = "";     // WO-9999 (único)
    public string Codigo { get; set; } = "";        // Asset no JMS (ex.: PRDNZS00120)
    public string? Equipamento { get; set; }        // NickName no JMS
    public string? Periodicidade { get; set; }      // Maintenance, já em português (Weekly → Semanal)
    public string? Tipo { get; set; }               // Type, em português (Preventive → Preventiva)
    public DateTime? CriadaEm { get; set; }         // Created Date
    public DateTime? DataManutencao { get; set; }   // Required Date
    public string? Localizacao { get; set; }        // Location
    public string? Celula { get; set; }             // Customer (SAMSUNG, AVELL, ARCON)
    public string? Tecnico { get; set; }            // Technician
    public string? Coordenador { get; set; }        // Coordinator
    public decimal? TempoSugerido { get; set; }     // Suggested Time (horas)
}

public class Historico
{
    public int Id { get; set; }
    public string Entidade { get; set; } = "";
    public int EntidadeId { get; set; }
    public string Usuario { get; set; } = "";
    public string Acao { get; set; } = "";
    public string Detalhes { get; set; } = "";
    public DateTime Quando { get; set; }
}

// Permissão de um grupo numa tela (Regra 07), editada na tela Grupos.
public class PermissaoGrupo
{
    public int Id { get; set; }
    public Grupo Grupo { get; set; }
    public string Modulo { get; set; } = "";
    public bool Ver { get; set; }
    public bool Editar { get; set; }
}

// Registro de cada e-mail de aviso (Servicos/Avisos.cs): não manda o mesmo aviso duas vezes e guarda o erro se falhou.
// Tipo "aprovacao" (Referencia = Id da Finalização) ou "resumo" (Referencia = "aaaa-mm-dd|Id do usuário").
// Teste = enviado no modo teste (só para o endereço de "Email:Teste"); ao ligar o envio de verdade, conta de novo.
public class EmailEnviado
{
    public int Id { get; set; }
    public DateTime EnviadoEm { get; set; }
    public string Tipo { get; set; } = "";
    public string Referencia { get; set; } = "";
    public string Para { get; set; } = "";
    public string Assunto { get; set; } = "";
    public bool Teste { get; set; }
    public bool Ok { get; set; }
    public string? Erro { get; set; }
}
