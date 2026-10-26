// ============================================================================
// MANUAL DA MÁQUINA — o visualizador do PDF dentro do MS (janela em Pages/Shared/_VisorManual.cshtml).
// Pedido do usuário em 19/10/2026. Usa o pdf.js (wwwroot/lib/pdfjs), carregado só na primeira vez que o manual abre.
//   - OS e Equipamento: botão com a classe "abrir-manual" e data-manual-url (o PDF), data-manual-pag (a página, opcional),
//     data-manual-nome (o nome do arquivo) e data-manual-voltar (dica do X de fechar; padrão "Voltar para a OS").
//   - Cronograma: botão "escolher-pag" dentro da tarefa (.tarefa-plano, com o campo escondido TarefasPagina); o manual
//     vem de data-manual-url do quadro mais próximo. Abre para escolher: "Usar esta página" / "Tirar a página" (embaixo)
//     ou o X do alto (cancela).
//   - Cronograma, PÁGINA AUTOMÁTICA (pedido do usuário em 19/10/2026): a tarefa sem página recebe sozinha a página
//     onde o manual fala das palavras do texto dela (achada no servidor: Servicos/PaginaAutomatica.cs). A tela já chega
//     com elas; ao digitar uma tarefa ou trocar o equipamento, paginasAutomaticas pede de novo. Grava ao Salvar o plano.
//     Nunca troca a página que já estava gravada nem a escolhida no visualizador; "Tirar a página" deixa a tarefa sem
//     página até recarregar a tela.
// ONDE MEXER:
//   - Botão 📖 da tarefa (desenho) ........ botaoPagina (tamanho: estilos.css, .btn-livro)
//   - Nitidez no celular ................. NITIDEZ_MAXIMA
// ============================================================================
(function () {
    const visor = document.getElementById('visorManual');
    if (!visor) return;
    const $ = id => document.getElementById(id);
    const NITIDEZ_MAXIMA = 2.5;   // desenha até 2,5× a tela (celular com tela de alta densidade fica nítido)
    const t = s => window.msT ? msT(s) : s;

    let pdfjs = null, doc = null, urlAberta = null, pagina = 1, total = 0;
    let resposta = null;          // o que o "escolher" devolve ao fechar
    let desenhando = null, vez = 0;
    // Zoom: o do próprio celular (pinça na tela; pedido do usuário em 19/10/2026 — sem botões de zoom no visualizador).
    // Para não borrar ao aumentar, a página é desenhada com o dobro dos pixels da tela (até PIXELS_MAXIMO).
    const FOLGA_ZOOM = 2;
    const PIXELS_MAXIMO = 16e6;   // canvas maior que isso trava celular: perde nitidez em vez de travar

    // pdf.js só na primeira abertura (a OS no celular não baixa 1 MB à toa).
    function carregarPdfJs() {
        if (pdfjs) return Promise.resolve(pdfjs);
        return new Promise((ok, falha) => {
            const s = document.createElement('script');
            s.src = '/lib/pdfjs/pdf.min.js';
            s.onload = () => {
                window.pdfjsLib.GlobalWorkerOptions.workerSrc = '/lib/pdfjs/pdf.worker.min.js';
                pdfjs = window.pdfjsLib;
                ok(pdfjs);
            };
            s.onerror = () => falha(new Error('pdf.js'));
            document.head.appendChild(s);
        });
    }

    async function abrirDocumento(url) {
        if (doc && urlAberta === url) return;
        await carregarPdfJs();
        if (doc) { doc.destroy(); doc = null; urlAberta = null; }
        doc = await pdfjs.getDocument({ url, withCredentials: true }).promise;
        urlAberta = url;
        total = doc.numPages;
    }

    function mensagem(texto) {
        $('visorMsg').textContent = texto || '';
        $('visorMsg').hidden = !texto;
        $('visorCanvas').hidden = !!texto;
    }

    // Desenha a página atual na largura da janela. Se pedirem outra página no meio, a anterior é abandonada.
    // manter = true (janela mudou de tamanho): o ponto que estava no meio da tela continua no meio.
    async function desenhar(manter) {
        if (!doc) return;
        pagina = Math.min(Math.max(1, pagina | 0), total);
        $('visorPagina').value = pagina;
        $('visorPagina').max = total;
        $('visorTotal').textContent = total;
        $('visorAnterior').disabled = pagina <= 1;
        $('visorProxima').disabled = pagina >= total;
        const minha = ++vez;
        const pag = await doc.getPage(pagina);
        if (minha !== vez) return;
        const area = $('visorArea');
        const estilo = getComputedStyle(area);
        const largura = Math.max(200, area.clientWidth - parseFloat(estilo.paddingLeft) - parseFloat(estilo.paddingRight));
        const escala = largura / pag.getViewport({ scale: 1 }).width;
        const base = pag.getViewport({ scale: escala });
        const nitidez = Math.min(Math.min(window.devicePixelRatio || 1, NITIDEZ_MAXIMA) * FOLGA_ZOOM, Math.sqrt(PIXELS_MAXIMO / (base.width * base.height)));
        const vp = pag.getViewport({ scale: escala * nitidez });
        const canvas = $('visorCanvas');
        // Ponto do meio da tela, em fração da página (ao girar o celular a leitura não "pula").
        const meioX = (area.scrollLeft + area.clientWidth / 2) / Math.max(1, area.scrollWidth);
        const meioY = (area.scrollTop + area.clientHeight / 2) / Math.max(1, area.scrollHeight);
        if (desenhando) desenhando.cancel();
        canvas.style.transform = '';
        canvas.width = Math.floor(vp.width);
        canvas.height = Math.floor(vp.height);
        canvas.style.width = Math.floor(base.width) + 'px';
        canvas.style.height = Math.floor(base.height) + 'px';
        if (manter) {
            area.scrollLeft = meioX * area.scrollWidth - area.clientWidth / 2;
            area.scrollTop = meioY * area.scrollHeight - area.clientHeight / 2;
        } else {
            area.scrollTop = 0;
            area.scrollLeft = 0;
        }
        desenhando = pag.render({ canvasContext: canvas.getContext('2d'), viewport: vp });
        try { await desenhando.promise; }
        catch (e) { if (e?.name !== 'RenderingCancelledException') throw e; }
    }

    // Linha discreta embaixo do nome: de que a página fala / dica (escolher) ou "Aberto na página n". Vazia = some.
    let escolhendo = false;
    function dica(texto) {
        $('visorDica').textContent = texto || '';
        $('visorDica').hidden = !texto;
    }
    const DICA_ESCOLHER = () => t('Vá até a página da tarefa e toque em Usar esta página.');

    function irPara(n) {
        pagina = n;
        dica(escolhendo ? DICA_ESCOLHER() : '');   // a página mudou: "fala de" / "aberto na página" não valem mais
        desenhar();
    }

    // Abre o manual. escolher = true: modo do Cronograma (devolve { usar: página } / { tirar: true } / null).
    // achada: as palavras da tarefa que deram a página automática (aparece na linha discreta do alto).
    // voltar: dica do X (padrão "Voltar para a OS"; no Cronograma, "Cancelar").
    function abrir({ url, nome, pagina: inicial, escolher = false, temPagina = false, voltar, achada }) {
        escolhendo = escolher;
        $('visorNome').textContent = nome || t('Manual');
        $('visorFechar').title = escolher ? t('Cancelar') : (voltar || t('Voltar para a OS'));
        $('visorAcoes').hidden = !escolher;
        $('visorTirar').hidden = !(escolher && temPagina);
        dica(escolher ? (achada ? t('O manual fala de:') + ' ' + achada : DICA_ESCOLHER())
            : inicial > 1 ? t('Aberto na página') + ' ' + inicial : '');
        visor.hidden = false;
        document.body.classList.add('visor-aberto');
        mensagem(t('Abrindo o manual…'));
        pagina = inicial || 1;
        abrirDocumento(url)
            .then(() => { mensagem(''); return desenhar(); })
            .catch(() => mensagem(t('Não foi possível abrir o manual. Confira se o arquivo ainda está no cadastro do equipamento.')));
        return new Promise(ok => { resposta = ok; });
    }

    function fechar(valor) {
        visor.hidden = true;
        document.body.classList.remove('visor-aberto');
        if (resposta) { const r = resposta; resposta = null; r(valor ?? null); }
    }

    $('visorAnterior').addEventListener('click', () => irPara(pagina - 1));
    $('visorProxima').addEventListener('click', () => irPara(pagina + 1));
    $('visorPagina').addEventListener('change', e => irPara(parseInt(e.target.value, 10) || 1));
    $('visorPagina').addEventListener('focus', e => e.target.select());   // tocar e já digitar a página
    $('visorFechar').addEventListener('click', () => fechar(null));
    $('visorUsar').addEventListener('click', () => fechar({ usar: pagina }));
    $('visorTirar').addEventListener('click', () => fechar({ tirar: true }));
    visor.addEventListener('click', e => { if (e.target === visor) fechar(null); });   // clique fora da janela
    document.addEventListener('keydown', e => {
        if (visor.hidden) return;
        if (e.key === 'Escape') fechar(null);
        else if (e.target.id !== 'visorPagina' && e.key === 'ArrowRight') irPara(pagina + 1);
        else if (e.target.id !== 'visorPagina' && e.key === 'ArrowLeft') irPara(pagina - 1);
    });
    let espera;
    window.addEventListener('resize', () => {
        if (visor.hidden) return;
        clearTimeout(espera);
        espera = setTimeout(() => desenhar(true), 200);
    });

    // O botão 📖 da tarefa no plano: apagado ("+ pág") sem página; "p.37" com página.
    // achada = palavras da tarefa que deram a página automática (só muda a dica do botão).
    function botaoPagina(botao, pag, achada) {
        botao.classList.toggle('sem', !pag);
        botao.querySelector('span').textContent = pag ? `p.${pag}` : t('+ pág');
        botao.title = !pag ? t('Escolher a página do manual')
            : achada ? t('Página {n}: onde o manual fala de {p} (clique para conferir ou trocar)').replace('{n}', pag).replace('{p}', achada)
            : t('Página {n} do manual (clique para trocar)').replace('{n}', pag);
    }

    // ---- PÁGINA AUTOMÁTICA (Cronograma) -------------------------------------------------------------------------
    // Quem acha a página é o servidor (Servicos/PaginaAutomatica.cs): a tela já chega com elas; aqui só se pede de novo
    // quando o texto de uma tarefa muda ou o equipamento do plano é trocado (Cronograma/Editar → OnPostPaginasAsync).
    async function paginasAutomaticas(quadro, so) {
        if (!quadro) return;
        const equipamento = quadro.dataset.equipamento;
        const alvo = [...(so ? [so] : quadro.querySelectorAll('.tarefa-plano'))].filter(tarefa => {
            const campo = tarefa.querySelector('input[name=TarefasPagina]');
            // Gravada ou escolhida à mão: não mexe.
            return campo && tarefa.querySelector('.escolher-pag') && !((campo.value && campo.dataset.auto !== '1') || campo.dataset.auto === '0');
        });
        if (!alvo.length) return;
        const corpo = new FormData();
        corpo.append('equipamento', equipamento || '0');
        for (const tarefa of alvo) corpo.append('textos', tarefa.querySelector('textarea')?.value ?? '');
        corpo.append('__RequestVerificationToken', document.querySelector('input[name=__RequestVerificationToken]')?.value ?? '');
        let achadas;
        try {
            const r = await fetch(location.pathname + '?handler=Paginas', { method: 'POST', body: corpo, credentials: 'same-origin' });
            if (!r.ok) return;
            achadas = await r.json();
        } catch { return; }
        if (quadro.dataset.equipamento !== equipamento) return;   // trocaram de máquina no meio: a chamada nova resolve
        alvo.forEach((tarefa, i) => {
            const campo = tarefa.querySelector('input[name=TarefasPagina]');
            const achou = achadas[i];
            campo.value = achou?.pagina ?? '';
            if (achou) campo.dataset.auto = '1'; else delete campo.dataset.auto;
            campo.dataset.achada = achou?.palavras ?? '';
            botaoPagina(tarefa.querySelector('.escolher-pag'), achou?.pagina, achou?.palavras);
        });
    }
    const quadroTarefas = () => document.querySelector('#aba-tarefas[data-equipamento]');
    // Texto da tarefa mudou (ao sair do campo): procura de novo, se a página dela for automática ou não houver.
    document.addEventListener('change', e => {
        const tarefa = e.target.closest?.('.tarefa-plano');
        if (tarefa && e.target.tagName === 'TEXTAREA') paginasAutomaticas(tarefa.closest('[data-equipamento]'), tarefa);
    });

    document.addEventListener('click', e => {
        // OS / Equipamento: abre o manual (na página da tarefa, se houver). Não marca a tarefa da linha.
        const abrirBtn = e.target.closest('.abrir-manual');
        if (abrirBtn) {
            e.preventDefault();
            e.stopPropagation();
            abrir({
                url: abrirBtn.dataset.manualUrl,
                nome: abrirBtn.dataset.manualNome,
                pagina: parseInt(abrirBtn.dataset.manualPag, 10) || 1,
                voltar: abrirBtn.dataset.manualVoltar,
            });
            return;
        }
        // Cronograma: escolher a página da tarefa no próprio manual.
        const escolherBtn = e.target.closest('.escolher-pag');
        if (escolherBtn) {
            e.preventDefault();
            const quadro = escolherBtn.closest('[data-manual-url]');
            const campo = escolherBtn.closest('.tarefa-plano')?.querySelector('input[name=TarefasPagina]');
            if (!quadro?.dataset.manualUrl || !campo) return;
            const atual = parseInt(campo.value, 10) || null;
            const achada = campo.dataset.auto === '1' ? campo.dataset.achada : null;
            abrir({ url: quadro.dataset.manualUrl, nome: quadro.dataset.manualNome, pagina: atual || 1, escolher: true, temPagina: !!atual, achada })
                .then(r => {
                    // Escolhida ou tirada à mão: a página automática não mexe mais nesta tarefa.
                    if (r?.usar) { campo.value = r.usar; campo.dataset.auto = '0'; botaoPagina(escolherBtn, r.usar); }
                    else if (r?.tirar) { campo.value = ''; campo.dataset.auto = '0'; botaoPagina(escolherBtn, null); }
                });
        }
    });

    // paginasAutomaticas: o Cronograma chama de novo ao trocar o equipamento do plano (outro manual).
    window.MsManual = { abrir, paginasAutomaticas: () => paginasAutomaticas(quadroTarefas()) };
})();
