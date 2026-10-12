// ============================================================================
// ENTIDADES — o formato de cada registro guardado no banco (uma classe = uma tabela).
//   Celula · Usuario · Equipamento + PecaPreditiva (vida útil das peças) · PlanoManutencao (Cronograma) + TarefaPlano
//   · OrdemServico + TarefaOS / PecaOS / Imagem · Finalizacao (ciclo concluído) · Nre + NreRemessa + NreItem + NreAnexo
//   · SparePart + MovimentoPeca (estoque de peças) · WorkOrderJms (histórico do JMS antigo) · Historico · PermissaoGrupo
// Listas fixas (enum): Grupo, Periodicidade (o número é a quantidade de dias do ciclo), StatusOS,
// StatusRevisao, StatusEquipamento, UnidadeVida, TipoNre, StatusNre, TipoRemessa, TipoAnexoNre, TipoMovimento.
// ONDE MEXER:
//   - Campo novo ............................... propriedade na classe + ALTER TABLE em Program.cs
//   - Nome que aparece na tela de cada opção ... Modelos/Textos.cs
//   - ATENÇÃO: não troque os números dos enums — eles estão gravados no banco.
// ============================================================================
namespace MS.Modelos;

public enum Grupo { Visitante = 1, Auditor = 2, Tecnico = 3, Coordenador = 4, Administrador = 5 }

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

// Manutenção preditiva: em que a vida útil da peça é contada (ex.: BELT dura 3 Meses; outra dura 1 Semana).
public enum UnidadeVida { Dias = 1, Semanas = 2, Meses = 3, Anos = 4 }

public enum TipoImagem { Antes = 1, Depois = 2 }

// Spare Parts: o que aconteceu com o estoque da peça. A quantidade do movimento tem sinal (+ entrou, − saiu).
public enum TipoMovimento { Entrada = 1, Retirada = 2, UsoOs = 3, Ajuste = 4, Estorno = 5 }

// NRE (custo não recorrente cobrado do cliente): o que está sendo pedido e em que pé está.
// Ordem do fluxo: Aberta → Aprovada → EmAndamento ("Aguardando chegada") → Cobrada (do cliente) → Concluida; ou Recusada.
public enum TipoNre { Peca = 1, Servico = 2, Ferramental = 3, Melhoria = 4, Outro = 5 }
public enum StatusNre { Aberta = 1, Aprovada = 2, EmAndamento = 3, Concluida = 4, Recusada = 5, Cobrada = 6 }
// Bloco do Detalhado da NRE: uma DI (importação, itens em US$ × PTAX da DI) ou uma compra nacional (NF, itens em R$).
public enum TipoRemessa { Importacao = 1, Nacional = 2 }
// Tipo do arquivo anexado à NRE (aba Arquivos, agrupada por tipo).
public enum TipoAnexoNre { Outro = 0, Invoice = 1, Di = 2, NotaFiscal = 3 }

// Célula (cliente: SAMSUNG, AVELL, ISS...). Na tela, nas planilhas e nas mensagens o nome é "Área" (desde 02/10/2026);
// no código e no banco continua Celula / CelulaId / tabela Celulas / endereço /Celulas.
public class Celula
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
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
    // Avisos por e-mail (Servicos/Avisos.cs): desmarcado = não recebe nenhum e-mail automático (o aviso não vai para outra pessoa).
    public bool RecebeEmail { get; set; } = true;
    // Login pela rede (AD): a matrícula entra com a senha do Windows. Conta local (ex.: admin de emergência,
    // contas de demonstração) entra com a senha guardada no sistema (SenhaHash).
    public bool ContaLocal { get; set; }
    // Cards da tela "Escolha a área" (Pages/EscolherArea) que a pessoa escondeu com o ×: ids das áreas separados por
    // vírgula. null = todas as áreas aparecem. Área criada depois aparece sozinha.
    public string? CardsEscondidos { get; set; }

    public string NomeCompleto => $"{PrimeiroNome} {UltimoNome}".Trim();
}

public class Equipamento
{
    public int Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Nome { get; set; } = "";
    public string? Modelo { get; set; }
    public string Predio { get; set; } = "";        // prédio onde a máquina fica (obrigatório no cadastro)
    public string Localizacao { get; set; } = "";
    public string? Irf { get; set; }                // nº do IRF: só nas máquinas que vieram da Samsung (vazio = não veio)
    public StatusEquipamento Status { get; set; } = StatusEquipamento.Operando;
    public int? CelulaId { get; set; }
    public Celula? Celula { get; set; }

    public string Descricao => $"{Codigo} - {Nome}";
}

// Manutenção preditiva (aba do cadastro do equipamento): peça que se gasta com o tempo e tem vida útil conhecida
// (ex.: BELT dura 3 meses). Trocar em = instalada em + vida útil. A situação é calculada na hora:
// Em dia · Atenção (já usou 80% da vida útil) · Trocar (chegou a data da troca).
public class PecaPreditiva
{
    public const int AtencaoPercentual = 80;   // a partir de quanto % da vida usada a peça fica em Atenção

    public int Id { get; set; }
    public int EquipamentoId { get; set; }
    public Equipamento? Equipamento { get; set; }
    public string Nome { get; set; } = "";             // ex.: BELT
    public int VidaUtil { get; set; }                  // quanto dura, na Unidade (ex.: 3)
    public UnidadeVida Unidade { get; set; } = UnidadeVida.Meses;
    public DateTime InstaladaEm { get; set; }          // instalação ou última troca

    public DateTime TrocarEm => Unidade switch
    {
        UnidadeVida.Dias => InstaladaEm.AddDays(VidaUtil),
        UnidadeVida.Semanas => InstaladaEm.AddDays(VidaUtil * 7),
        UnidadeVida.Meses => InstaladaEm.AddMonths(VidaUtil),
        _ => InstaladaEm.AddYears(VidaUtil),
    };
    public int DiasRestantes(DateTime hoje) => (TrocarEm.Date - hoje.Date).Days;
    // Quanto da vida útil já foi usada: 0 a 100 (passa de 100 quando a troca atrasou).
    public int PercentualUsado(DateTime hoje)
    {
        var vida = (TrocarEm.Date - InstaladaEm.Date).TotalDays;
        var usado = Math.Max(0, (hoje.Date - InstaladaEm.Date).TotalDays);
        return vida <= 0 ? 100 : (int)Math.Floor(usado * 100 / vida);
    }
    public SituacaoPrazo Situacao(DateTime hoje) =>
        hoje.Date >= TrocarEm.Date ? SituacaoPrazo.Atrasada
        : PercentualUsado(hoje) >= AtencaoPercentual ? SituacaoPrazo.Atencao : SituacaoPrazo.EmDia;
}

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
    public List<TarefaPlano> Tarefas { get; set; } = new();
}

public class TarefaPlano
{
    public int Id { get; set; }
    public int PlanoId { get; set; }
    public string Descricao { get; set; } = "";
    public int Ordem { get; set; }
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
// Local, Estoque e EstoqueMinimo não aparecem mais nas telas (controle de estoque tirado a pedido do usuário);
// ficam no banco caso queiram voltar a usar.
public class SparePart
{
    public int Id { get; set; }
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

// NRE — pedido de gasto não recorrente (peça, serviço, ferramental...), cobrado do cliente (a célula). Ligada a um tipo
// de máquina (Maquina, texto) ou a nenhum; a máquina exata (EquipamentoId) só quando veio de uma OS ou do cadastro dela.
// Fluxo: Aberta (aguardando aprovação) → Aprovada → Aguardando chegada → Cobrada do cliente → Concluída; ou Recusada.
// Como as planilhas de NRE da Samsung: o Detalhado (Remessas: cada DI ou compra nacional, com os itens) é o que se digita;
// o Resumo (aprovado US$, frete + seguro, Final Amount, taxas, total a cobrar — a aba "Summary") sai dele, calculado aqui.
public class Nre
{
    public const decimal TaxaPadrao = 9.3871094945943m;   // % Pis/Cofins, CSLL, R&D — a da fórmula da planilha azul (=G3*9.3871094945943%); o Excel mostra "9,3871%"

    public int Id { get; set; }
    public string Numero { get; set; } = "";          // NRE-ano-nº
    // Como a planilha de NRE do cliente organiza (lista da tela NRE): Modelo · NRE · Título · NF · Invoice · Valor · DI.
    public string? Titulo { get; set; }               // ex.: Mobile Samsung - SMD Project A075
    public string? Modelo { get; set; }               // modelo do produto do cliente (ex.: A075)
    public string? CodigoNre { get; set; }            // nº da NRE no sistema do cliente (NRE Order, ex.: 122001004722)
    // Aprovação, como na planilha de controle (Approval Date, Approver, Approved Value, Ptax, Final Amount, PO, Evidence).
    public DateTime? AprovadaClienteEm { get; set; }
    public string? Aprovador { get; set; }            // ex.: Sergani/Célio
    public decimal? AprovadoUsd { get; set; }         // valor aprovado em US$ (vazio: a soma dos itens do Detalhado)
    public decimal? PtaxAprovacao { get; set; }       // Ptax usada na aprovação
    public decimal? ValorAprovado { get; set; }       // Final Amount previsto (R$) = AprovadoUsd × PtaxAprovacao (calculado ao salvar); vale enquanto o Detalhado está vazio
    public string? PedidoCompra { get; set; }         // PO (um ou vários)
    public string? Evidencia { get; set; }            // ex.: Pendente
    // Fechamento (fim do Detalhado).
    public decimal? TaxaPercentual { get; set; }      // % das taxas (vazio = TaxaPadrao)
    public decimal? CustoAbsorvido { get; set; }      // custo absorvido pela empresa (R$)
    public string? MotivoDiferenca { get; set; }      // por que o real ficou diferente do aprovado
    public DateTime? CobradaEm { get; set; }          // quando foi marcada como cobrada do cliente
    public string? Maquina { get; set; }              // tipo de máquina (nome, ex.: ADESIVADORA) — opcional
    public int? CelulaId { get; set; }                // cliente que paga
    public Celula? Celula { get; set; }
    public int? EquipamentoId { get; set; }           // máquina exata: só quando a NRE veio de uma OS ou do cadastro da máquina
    public Equipamento? Equipamento { get; set; }
    public int? OrdemServicoId { get; set; }         // OS que originou (opcional)
    public OrdemServico? OrdemServico { get; set; }
    public TipoNre Tipo { get; set; } = TipoNre.Peca;
    public string Descricao { get; set; } = "";      // o que precisa e por quê
    public StatusNre Status { get; set; } = StatusNre.Aberta;
    public int? SolicitanteId { get; set; }
    public Usuario? Solicitante { get; set; }
    public DateTime CriadaEm { get; set; }
    public int? AnalisadaPorId { get; set; }         // quem aprovou ou recusou no sistema
    public Usuario? AnalisadaPor { get; set; }
    public DateTime? AnalisadaEm { get; set; }
    public string? MotivoRecusa { get; set; }
    public DateTime? ConcluidaEm { get; set; }
    public List<NreRemessa> Remessas { get; set; } = new();   // Detalhado: DIs e compras nacionais (cada uma com os itens)
    public List<NreItem> Itens { get; set; } = new();         // todos os itens (os mesmos de Remessas[].Itens)
    public List<NreAnexo> Anexos { get; set; } = new();       // invoices, DIs, NF, aprovação... (ficam até alguém remover)

    public static decimal Centavos(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    public decimal Taxa => TaxaPercentual ?? TaxaPadrao;
    public bool TemDetalhe => Remessas.Count > 0;
    public IEnumerable<NreRemessa> RemessasEmOrdem => Remessas.OrderBy(r => r.Ordem).ThenBy(r => r.Id);
    // Custo real (R$ Total NF): a soma do Detalhado.
    public decimal Total() => Remessas.Sum(r => r.Reais());
    // Final Amount previsto na aprovação: o digitado ou US$ aprovado × Ptax da aprovação.
    public decimal? Previsto() => ValorAprovado ?? (AprovadoUsd * PtaxAprovacao is decimal p ? Centavos(p) : null);
    // Planilha azul (só a aprovação): Pis/Cofins, CSLL, R&D Tax = Final Amount × taxa · Final Amount + Taxes.
    public decimal? TaxasPrevistas() => Previsto() is decimal p ? Centavos(p * Taxa / 100) : null;
    public decimal? TotalPrevisto() => Previsto() + TaxasPrevistas();
    // Resumo (aba Resumo, cartões, lista e Extração). Sem Detalhado ainda, usa o previsto na aprovação.
    public decimal FinalAmount() => TemDetalhe ? Total() : Previsto() ?? 0;
    public decimal TaxasReais() => TemDetalhe ? Remessas.Sum(r => r.Taxas(Taxa)) : Centavos(FinalAmount() * Taxa / 100);
    public decimal TotalCobrar() => FinalAmount() + TaxasReais();       // "VALOR TOTAL DA NF" / "Final Amount + Taxes"
    public decimal? AprovadoUsdResumo() => AprovadoUsd ?? (TemDetalhe ? Remessas.Sum(r => r.ItensUsd()) : null);
    public decimal FreteSeguroUsd() => Remessas.Sum(r => r.FreteSeguroUsd());
    public decimal RealUsd() => Remessas.Sum(r => r.ItensUsd() + r.FreteSeguroUsd());
    // Real − aprovado em US$ (positivo: passou do aprovado). Só com Detalhado e valor aprovado.
    public decimal? DiferencaUsd() => TemDetalhe && AprovadoUsdResumo() is decimal a ? RealUsd() - a : null;
    public bool PtaxPendente => Remessas.Any(r => r.PtaxPendente);
    // Colunas NF / Invoice / DI da lista: juntam as do Detalhado.
    public string? Juntar(Func<NreRemessa, string?> campo) =>
        RemessasEmOrdem.Select(campo).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList() is { Count: > 0 } l ? string.Join(", ", l) : null;
    // Máquina, área, tipo e descrição mudam até concluir, como o resto da NRE (pedido do usuário, 2026-10-07; antes só até aprovar).
    public bool Editavel => Status != StatusNre.Concluida;
}

// Bloco do Detalhado da NRE (como a planilha de detalhe): uma DI com invoice, PTAX, frete e seguro (itens em US$),
// ou uma compra nacional com a NF (itens em R$; a PTAX, se houver, só converte para mostrar em US$ no Resumo).
public class NreRemessa
{
    public int Id { get; set; }
    public int NreId { get; set; }
    public int Ordem { get; set; }
    public TipoRemessa Tipo { get; set; } = TipoRemessa.Importacao;
    public string? Di { get; set; }                   // ex.: 25/2221014-8
    public string? Invoice { get; set; }              // ex.: IMKJAB2509-02
    public string? NotaFiscal { get; set; }           // NF da compra (nacional) — mais de uma, separadas por vírgula
    public string? Fornecedor { get; set; }
    public decimal? Ptax { get; set; }                // PTAX da DI (4 casas, ex.: 5,3498)
    public decimal Frete { get; set; }                // US$
    public decimal Seguro { get; set; }               // US$
    public List<NreItem> Itens { get; set; } = new();

    public bool Importacao => Tipo == TipoRemessa.Importacao;
    // Importação sem PTAX ainda: os reais dos itens em dólar não dá para calcular.
    public bool PtaxPendente => Importacao && Ptax == null && (Itens.Any(i => i.ValorUnitarioUsd != null) || Frete + Seguro > 0);
    // US$ → R$ pela PTAX da DI, arredondado no centavo em cada linha (como a planilha).
    public decimal? Reais(decimal usd) => Ptax is decimal p ? Nre.Centavos(usd * p) : null;
    public decimal ReaisItem(NreItem i) => Importacao && i.ValorUnitarioUsd != null ? Reais(i.TotalUsd()) ?? 0 : i.Total();
    public decimal Reais() => Itens.Sum(ReaisItem) + (Importacao ? (Reais(Frete) ?? 0) + (Reais(Seguro) ?? 0) : 0);
    public decimal ItensUsd() => Importacao ? Itens.Sum(i => i.TotalUsd())
        : Ptax is decimal p && p > 0 ? Nre.Centavos(Reais() / p) : 0;   // nacional: dólar de referência
    public decimal FreteSeguroUsd() => Importacao ? Frete + Seguro : 0;
    public decimal Taxas(decimal taxa) => Nre.Centavos(Reais() * taxa / 100);
    public string Rotulo() => Importacao ? $"DI {(string.IsNullOrWhiteSpace(Di) ? "(a definir)" : Di)}"
        : $"NF {(string.IsNullOrWhiteSpace(NotaFiscal) ? "(a definir)" : NotaFiscal)}";
}

public class NreItem
{
    public int Id { get; set; }
    public int NreId { get; set; }
    public int? RemessaId { get; set; }               // bloco do Detalhado (DI ou compra nacional)
    public string? Codigo { get; set; }
    public string Descricao { get; set; } = "";
    public decimal Quantidade { get; set; } = 1;
    public decimal ValorUnitario { get; set; }        // R$ (compra nacional e NRE antigas)
    public decimal? ValorUnitarioUsd { get; set; }    // US$ (importação)

    public decimal Total() => Nre.Centavos(Quantidade * ValorUnitario);
    public decimal TotalUsd() => Nre.Centavos(Quantidade * (ValorUnitarioUsd ?? 0));
}

// Arquivo anexado à NRE (guardado em dados/anexos com nome próprio; o nome original fica aqui). Não é apagado
// sozinho, como as fotos das OS: só sai quando alguém remove na tela da NRE.
public class NreAnexo
{
    public int Id { get; set; }
    public int NreId { get; set; }
    public TipoAnexoNre Tipo { get; set; }        // Invoice, DI, NF ou outro (grupos da aba Arquivos)
    public string Nome { get; set; } = "";        // nome original do arquivo (o que aparece na tela)
    public string Arquivo { get; set; } = "";     // nome guardado em dados/anexos
    public long Tamanho { get; set; }
    public DateTime EnviadoEm { get; set; }
    public string EnviadoPor { get; set; } = "";
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
    public string Codigo { get; set; } = "";        // Asset no JMS (ex.: MTXNZS00907)
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
// Tipo "aprovacao" (Referencia = Id da Finalização), "resumo" (Referencia = "aaaa-mm-dd|Id do usuário") ou "preditiva"
// (uma linha por peça avisada: "Id da peça|instalada em|atencao ou trocar|Id do usuário").
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
