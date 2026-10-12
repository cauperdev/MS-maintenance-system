# MS · Gestão de Manutenção

Sistema de planejamento de manutenção preventiva em **C# (ASP.NET Core 10, Razor Pages) + SQLite**, com menu lateral hambúrguer e layout responsivo (computador, tablet e celular). Foi feito a partir dos slides `Regras_MS_PC.pptx` e `Regras_MS_Celular.pptx`.

## Como rodar

- **Pasta `programa`** (não vem nesta cópia): o sistema já compilado, com o .NET embutido. Rode `atualizar-programa.bat` (precisa do .NET 10 SDK) para gerar; sem ela, o `iniciar.bat` roda direto pelo código-fonte com `dotnet run`.
- **Dois cliques em `iniciar.bat`**: liga o sistema só para este computador e abre http://localhost:5090 (não pede administrador nem Firewall).
- **`iniciar-na-rede.bat`**: liga também para a rede, para o celular (mesmo Wi-Fi) abrir `http://IP-DO-PC:5090`. Na primeira vez o Firewall pede permissão de administrador.
- **No VS Code**: `Ctrl+F5` (também na porta 5090).
- **Pelo terminal**: `dotnet run --urls http://0.0.0.0:5090`

O banco (`dados/ms.db`) e as fotos (`dados/fotos`) são criados sozinhos na primeira vez, com dados de demonstração (`BancoDeDados/Seed.cs`).

**Depois de editar:**
- Arquivos `.cs` ou `.cshtml`: feche e ligue o sistema de novo.
- Arquivos `.css` ou `.js`: basta recarregar a página com `Ctrl+F5`.

## Usuários de demonstração

| Usuário | Senha | Grupo |
|---|---|---|
| admin | admin123 | Administrador |
| coordenador | 123456 | Coordenador |
| tecnico / tecnico2 | 123456 | Técnico |
| auditor | 123456 | Auditor |
| visitante | 123456 | Visitante |

Troque essas senhas antes de usar o sistema de verdade.

## Mapa das pastas

```
MS/
├─ iniciar.bat ............ liga o sistema (duplo clique)
├─ Program.cs ............. início do sistema: login, banco, atualização automática do banco
├─ appsettings.json ....... configurações (ex.: "EnderecoSistema" usado nos QR codes)
├─ LEIAME.md .............. este guia
│
├─ BancoDeDados/ .......... conexão com o banco (AppDb.cs) e dados iniciais (Seed.cs)
├─ Modelos/ ............... formato dos registros (Entidades.cs) e nomes exibidos das opções (Textos.cs)
├─ Servicos/ .............. regras e ferramentas usadas por várias telas
│   ├─ Regras.cs .......... as regras dos slides (OS, prazos, conclusão, revisão)
│   ├─ Acesso.cs .......... Regra 07: o que cada grupo vê e edita
│   ├─ Excel.cs ........... Extração, modelos, importação e QR codes
│   ├─ Fotos.cs ........... Regra 06: fotos reduzidas e apagadas após 90 dias
│   ├─ Lembretes.cs ....... sino de lembretes
│   ├─ Auditoria.cs ....... Histórico (quem mudou o quê)
│   ├─ Icones.cs .......... ícones dos botões e do menu
│   └─ MsPage.cs ......... base de todas as telas (permissão, mensagens)
│
├─ Pages/ ................. AS TELAS: uma pasta por item do menu
│   ├─ Index .............. Dashboard
│   ├─ EscolherArea/ ...... cards das áreas (abre depois do login; a área escolhida filtra as telas)
│   ├─ Grupos/, Usuarios/, Celulas/
│   ├─ Equipamentos/, Cronograma/, Ordens/, Finalizacao/, Calendario/
│   ├─ Historico/, Conta/ (Login, Sair, Negado), Erro, Foto
│   └─ Shared/ ............ moldes e pedaços reaproveitados (topo/menu, impressão, cartão de OS, gráficos, sino)
│
├─ wwwroot/ ............... arquivos que o navegador baixa
│   ├─ css/estilos.css .... cores, tamanhos e posições (índice numerado no topo)
│   ├─ js/interacoes.js ... cliques, "marcar todos", confirmações, menu (índice no topo)
│   ├─ js/traducao.js ..... traduções para English / Español / 中文
│   ├─ img/ ............... ícone da marca do sistema (aba do navegador, topo, login e impressão)
│   └─ lib/ ............... Bootstrap e jQuery (bibliotecas prontas: não mexer)
│
├─ dados/ ................. banco ms.db e fotos (criada sozinha ao ligar o sistema; apagar a pasta volta aos dados de demonstração)
└─ Properties/ ............ porta usada pelo VS Code (launchSettings.json)
```

**Cada tela tem 2 arquivos:**
- `.cshtml` é o **visual** (o HTML que aparece);
- `.cshtml.cs` é a **lógica** (busca no banco, salva, valida).

No VS Code, a lógica aparece "dentro" do visual: clique na setinha ao lado do `.cshtml`.

**Nomes dos arquivos de cada tela:**
- `Index` = lista (tela principal da pasta);
- `Editar` = cadastro;
- `Ver` = só leitura;
- `Imprimir` = folha de impressão.

**Todo arquivo começa com um comentário-guia** dizendo o que ele faz, onde aparece e onde mexer.

As pastas `bin` e `obj` são geradas ao compilar e ficam escondidas no VS Code (configuração em `.vscode/settings.json`).

## Quero mudar… vou em…

| Quero mudar | Arquivo | Onde dentro dele |
|---|---|---|
| Uma cor do sistema todo | `wwwroot/css/estilos.css` | seção **1** (Ctrl+F `== 1.`) |
| Visual do celular/tablet | `wwwroot/css/estilos.css` | seção **15** |
| Um texto de botão ou título | o `.cshtml` da tela | procure o texto; depois ajuste a tradução em `traducao.js` |
| Tradução para outro idioma | `wwwroot/js/traducao.js` | dicionário `D` (explicado no topo do arquivo) |
| Um item do menu lateral | `Pages/Shared/_Layout.cshtml` | parte 3 (bloco `<nav>`) |
| Texto do rodapé | `Pages/Shared/_Layout.cshtml` | parte 7 |
| Cabeçalho da impressão (logo, título) | `Pages/Shared/_Impressao.cshtml` | — |
| Colunas de uma lista | `Pages/<Tela>/Index.cshtml` | tabela (computador) e cartões (celular) |
| Colunas da Extração (Excel) | `Pages/<Tela>/Index.cshtml.cs` | `OnGetExtracaoAsync` |
| Colunas do "Baixar modelo" / importação | `Servicos/Excel.cs` | `ColunasEquipamento` / `ColunasPlano` |
| Etiquetas de QR code | `Servicos/Excel.cs` | `QrCodes` |
| Endereço que o QR code abre | `appsettings.json` | `"EnderecoSistema"` (vazio = endereço atual) |
| Como a OS nasce, vence e é concluída | `Servicos/Regras.cs` | índice no topo do arquivo |
| O que cada grupo pode ver/editar | tela **Grupos** do sistema | padrão de fábrica em `Servicos/Acesso.cs` |
| Tela que abre ao entrar | `Servicos/Acesso.cs` | `PrioridadeInicial` |
| Gráficos e números do Dashboard | `Pages/Index.cshtml.cs` | `OnGetAsync` |
| Quais OS aparecem no sino | `Servicos/Lembretes.cs` | `Pendentes` |
| Tamanho/prazo das fotos | `Servicos/Fotos.cs` | `LadoMaximo` / `DiasRetencao` |
| Nome exibido de uma opção (ex.: "Semanal") | `Modelos/Textos.cs` | funções `Nome(...)` |
| Campo novo num cadastro | `Modelos/Entidades.cs` + `Program.cs` (ALTER TABLE) + a tela `Editar` | ver comentário no topo de `Entidades.cs` |
| Usuários e senhas iniciais (banco novo) | `BancoDeDados/Seed.cs` | linhas `U("login", "senha", ...)` |
| Porta do sistema | `iniciar.bat` e `Properties/launchSettings.json` | `5090` |

## As 7 regras e onde estão no código

| # | Regra | Onde |
|---|---|---|
| 01 | O plano gera a OS: a Ordem de trabalho do plano é o número da OS dele (OS-ano-nº), gerado no Cronograma | `Servicos/Regras.cs` → `CriarOsDoPlanoAsync`, `OrdemTrabalhoAsync`, `NumeroDaOsDoPlanoAsync` |
| 02 | O checklist vem do plano. Na OS o texto fica travado e só se marca "Concluída" | `Regras.CopiarTarefas`, `SincronizarTarefasAsync`, `Pages/Ordens/Editar` |
| 03 | Prazo = abertura + período (Diária 1d, Semanal 7d, Quinzenal 15d, Mensal 30d, Trimestral 90d, Semestral 180d, Anual 365d) | `Regras.Prazo`, `ProximaManutencao`, `Pages/Calendario` |
| 04 | Concluiu, reiniciou: Salvar com ao menos 1 tarefa marcada leva o ciclo para a Finalização; a OS continua com o mesmo número, checklist zerado e a próxima data | `Regras.Concluir` |
| 05 | O coordenador aprova ou devolve (Retornar tira da Finalização e a OS volta à data daquele ciclo) | `Regras.Aprovar`, `RetornarAsync`, `Pages/Finalizacao` |
| 06 | Registro com prova: checklist congelado e fotos de antes/depois (até 1600px, apagadas após 90 dias) | `Servicos/Fotos.cs` |
| 07 | O acesso depende do grupo | `Servicos/Acesso.cs`, `Servicos/MsPage.cs`, tela Grupos |

## Telas

Dashboard · Grupos · Usuários · Células · Equipamentos (com QR codes) · Cronograma · Ordens de serviço · Finalização · Calendário · Histórico · Impressão da OS e da finalização.

Excel em cada tela:
- **Equipamentos e Cronograma:** baixar modelo, importar e extração.
- **Ordens de serviço:** só extração, porque as OS nascem do Cronograma.
- **Finalização:** só extração, nas abas Finalização, Checklist e Imagens.
