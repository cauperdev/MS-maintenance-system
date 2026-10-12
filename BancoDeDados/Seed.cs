// ============================================================================
// DADOS INICIAIS — só roda quando o banco está VAZIO (a primeira vez que o sistema liga).
// Cria usuários de demonstração, células, equipamentos, planos e OS de exemplo. Não mexe em bancos que já têm dados.
// ONDE MEXER:
//   - Usuários e senhas iniciais ............... linhas U("login", "senha", "Nome", "Sobrenome", Grupo, célula)
//     (admin / admin123 · coordenador, tecnico, tecnico2, auditor, visitante / 123456)
// ============================================================================
using Microsoft.AspNetCore.Identity;
using MS.Modelos;
using MS.Servicos;

namespace MS.BancoDeDados;

// Dados iniciais de demonstração (só roda com o banco vazio).
public static class Seed
{
    public static void Run(AppDb db)
    {
        if (db.Usuarios.Any()) return;

        var samsung = new Celula { Nome = "Samsung" };
        var avell = new Celula { Nome = "Avell" };
        db.Celulas.AddRange(samsung, avell);
        db.SaveChanges();

        var hasher = new PasswordHasher<Usuario>();
        Usuario U(string login, string senha, string nome, string sobrenome, Grupo g, Celula c)
        {
            // Contas de demonstração e o admin são locais (senha do sistema), não da rede (AD).
            var u = new Usuario { Login = login, Email = $"{login}@ms.local", PrimeiroNome = nome, UltimoNome = sobrenome, Grupo = g, CelulaId = c.Id, ContaLocal = true };
            u.SenhaHash = hasher.HashPassword(u, senha);
            return u;
        }
        var admin = U("admin", "admin123", "Christhian", "Cauper", Grupo.Administrador, samsung);
        var coord = U("coordenador", "123456", "Coordenador", "Demo", Grupo.Coordenador, samsung);
        var tec1 = U("tecnico", "123456", "Técnico", "Demo", Grupo.Tecnico, avell);
        var tec2 = U("tecnico2", "123456", "Segundo", "Técnico", Grupo.Tecnico, samsung);
        db.Usuarios.AddRange(admin, coord, tec1, tec2,
            U("auditor", "123456", "Auditor", "Demo", Grupo.Auditor, samsung),
            U("visitante", "123456", "Visitante", "Demo", Grupo.Visitante, samsung));
        db.SaveChanges();

        var equipamentos = new[]
        {
            ("MTXSHU01973", "SHUTTLER", "LINHA JUTAÍ", avell),
            ("MTXADS01656", "ADESIVADORA", "SMT LINE 02", samsung),
            ("MTXADS01675", "ADESIVADORA", "SMT LINE 02", samsung),
            ("MTXAOI01750", "AOI", "SMT LINE 01", samsung),
            ("MTXAOI01725", "AOI", "SMT LINE 01", samsung),
            ("MTXARC01974", "AR CONDICIONADO DA PRINTER", "SMT LINE 01", samsung),
            ("MTXCHA01955", "CHAMBER", "SMT LINE 02", samsung),
            ("MTXCONV01635", "CONVEYOR", "LINHA JUTAÍ", avell),
            ("MTXCONV01733", "CONVEYOR", "LINHA JUTAÍ", avell),
            ("MTXPNT01704", "PARAFUSADEIRA", "LINHA JUTAÍ", avell),
            ("MTXPTD01975", "PRINTER", "SMT LINE 01", samsung),
            ("MTXCHA01916", "CHAMBER", "SMT LINE 01", samsung),
        }.Select(e => new Equipamento { Codigo = e.Item1, Nome = e.Item2, Predio = "PRÉDIO 1", Localizacao = e.Item3, CelulaId = e.Item4.Id }).ToList();
        equipamentos[6].Status = StatusEquipamento.EmManutencao;
        equipamentos[10].Status = StatusEquipamento.Inativo;
        db.Equipamentos.AddRange(equipamentos);
        db.SaveChanges();

        var checklists = new Dictionary<string, string[]>
        {
            ["SHUTTLER"] = ["LIMPEZA DA ESTRUTURA", "LIMPEZA DE SENSORES", "VERIFICAÇÃO DE POLIAS", "VERIFICAÇÃO DE BELT", "VERIFICAÇÃO DA PNEUMÁTICA", "VERIFICAR BOTÕES DE FUNCIONAMENTO", "VERIFICAR E LUBRIFICAR O EIXO DE ABERTURA"],
            ["CONVEYOR"] = ["Conveyor: Verificar o desgaste e conexão da Belt", "Conveyor: Verificar a condição de rotação do rolo"],
            ["AOI"] = ["Limpeza das lentes da câmera", "Verificar iluminação", "Calibração com placa padrão"],
            ["ADESIVADORA"] = ["Limpeza do bico dosador", "Verificar pressão de ar", "Verificar nível do adesivo"],
            ["CHAMBER"] = ["Verificar vedação da porta", "Conferir temperatura com termômetro padrão"],
        };
        string[] Tarefas(string nome) => checklists.GetValueOrDefault(nome, ["Inspeção visual geral", "Limpeza externa", "Verificar ruídos anormais"]);

        var rnd = new Random(42);
        var agora = DateTime.Now;
        var osSeq = 0;
        var tipos = new[] { Periodicidade.Semanal, Periodicidade.Mensal, Periodicidade.Trimestral };
        foreach (var eq in equipamentos)
        {
            var numero = $"OS-{agora.Year}-{++osSeq:0000}"; // Ordem de trabalho = número da OS, o mesmo nas periodicidades
            foreach (var tipo in tipos.Take(eq.Nome == "SHUTTLER" ? 3 : 2))
            {
                var plano = new PlanoManutencao
                {
                    EquipamentoId = eq.Id,
                    Nome = eq.Nome,
                    OrdemTrabalho = numero,
                    Descricao = $"Preventiva {tipo.Nome().ToLower()} — {eq.Nome}",
                    Periodicidade = tipo,
                    Tarefas = Tarefas(eq.Nome).Select((t, i) => new TarefaPlano { Descricao = t, Ordem = i }).ToList(),
                };
                db.Planos.Add(plano);
                db.SaveChanges();

                var abertura = agora.AddDays(-rnd.Next(0, (int)tipo + 20)).Date.AddHours(10).AddMinutes(39);
                var os = new OrdemServico
                {
                    Numero = numero,
                    EquipamentoId = eq.Id,
                    PlanoId = plano.Id,
                    Tipo = tipo,
                    Prioridade = (Prioridade)rnd.Next(1, 5),
                    DataAbertura = abertura,
                    DataConclusao = Regras.Prazo(abertura, tipo),
                    Descricao = plano.Descricao,
                    Tarefas = Regras.CopiarTarefas(plano),
                };
                if (rnd.Next(4) == 0)
                {
                    os.Status = StatusOS.Aberta;
                    os.TecnicoId = tec1.Id;
                    os.CoordenadorId = coord.Id;
                    os.Tarefas[0].Concluida = true;
                }
                db.Ordens.Add(os);
                db.SaveChanges();

                // Alguns ciclos já concluídos aguardando / aprovados.
                if (rnd.Next(3) == 0)
                {
                    var ini = abertura.AddDays(-(int)tipo);
                    var aprovada = rnd.Next(2) == 0;
                    db.Finalizacoes.Add(new Finalizacao
                    {
                        OrdemServicoId = os.Id,
                        NumeroOS = os.Numero,
                        EquipamentoId = eq.Id,
                        Tipo = tipo,
                        Descricao = plano.Descricao,
                        TecnicoId = tec2.Id,
                        CoordenadorId = coord.Id,
                        DataAbertura = ini,
                        DataConclusao = Regras.Prazo(ini, tipo),
                        ConcluidaEm = abertura,
                        Checklist = string.Join("\n", plano.Tarefas.Select(t => $"[x] {t.Descricao}")),
                        StatusRevisao = aprovada ? StatusRevisao.Aprovado : StatusRevisao.Aguardando,
                        AprovadaEm = aprovada ? abertura.AddHours(3) : null,
                        AprovadaPorId = aprovada ? coord.Id : null,
                    });
                    db.SaveChanges();
                }
            }
        }
    }
}
