(() => {
    // ===== Utilidades de fecha =====
    const toYMD = d => d.toISOString().slice(0, 10);
    const fromYMD = s => new Date(s + 'T00:00:00');
    const addDays = (d, n) => { const x = new Date(d); x.setDate(x.getDate() + n); return x; };
    const startOfMonth = d => new Date(d.getFullYear(), d.getMonth(), 1);
    const endOfMonth = d => new Date(d.getFullYear(), d.getMonth() + 1, 0);
    const diffDays = (a, b) => Math.round((new Date(toYMD(b)) - new Date(toYMD(a))) / 86400000) + 1;
    const inferGran = (f, t) => { const d = diffDays(f, t); if (d <= 14) return 'day'; if (d <= 90) return 'week'; return 'month'; };

    // ===== Estado inicial =====
    const cfg = window.REPORTS_CFG || {};
    const urls = cfg.urls || {};
    let range = { from: fromYMD(cfg.from), to: fromYMD(cfg.to) };

    // ===== Inicializa DateRangePicker (UN SOLO INPUT) =====
    $(function () {
        moment.locale('es');

        const $input = $('#dateRange');
        $input.daterangepicker({
            startDate: moment(cfg.from, 'YYYY-MM-DD'),
            endDate: moment(cfg.to, 'YYYY-MM-DD'),
            maxDate: moment(),               // opcional: no permitir futuro
            autoApply: true,                   // aplica sin botón extra
            opens: 'left',
            drops: 'auto',
            linkedCalendars: true,
            showCustomRangeLabel: true,
            locale: {
                format: 'YYYY-MM-DD',
                separator: ' a ',
                applyLabel: 'Aplicar',
                cancelLabel: 'Cancelar',
                customRangeLabel: 'Personalizado',
                daysOfWeek: ['Do', 'Lu', 'Ma', 'Mi', 'Ju', 'Vi', 'Sa'],
                monthNames: ['enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio', 'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre'],
                firstDay: 1
            },
            ranges: {
                'Hoy': [moment(), moment()],
                'Ayer': [moment().subtract(1, 'day'), moment().subtract(1, 'day')],
                'Últimos 7 días': [moment().subtract(6, 'day'), moment()],
                'Últimos 30 días': [moment().subtract(29, 'day'), moment()],
                'Este mes': [moment().startOf('month'), moment().endOf('month')],
                'Mes anterior': [
                    moment().subtract(1, 'month').startOf('month'),
                    moment().subtract(1, 'month').endOf('month')
                ]
            }
        }, function (start, end /*, label */) {
            // Callback al aplicar/cambiar
            range.from = start.toDate();
            range.to = end.toDate();
            renderAll();
        });

        // Primer render en cuanto esté listo
        renderAll();
    });

    // ===== Chart.js =====
    Chart.defaults.responsive = true;
    Chart.defaults.maintainAspectRatio = false;
    Chart.defaults.animation = false;
    Chart.defaults.font.family = "'Poppins', system-ui, -apple-system, Segoe UI, Roboto, Ubuntu, Cantarell, Arial";
    const V900 = 'rgb(49,3,83)', V600 = 'rgb(168,96,224)';

    const lineCtx = document.getElementById('lineMensajes').getContext('2d');
    const barCtx = document.getElementById('barCierresAgente').getContext('2d');
    let lineChart, barChart;

    function buildLine(labels, data) {
        if (lineChart) lineChart.destroy();
        const grad = lineCtx.createLinearGradient(0, 0, 0, 300);
        grad.addColorStop(0, 'rgba(168,96,224,.25)');
        grad.addColorStop(1, 'rgba(205,171,230,.03)');
        lineChart = new Chart(lineCtx, {
            type: 'line',
            data: { labels, datasets: [{ label: 'Mensajes', data, borderWidth: 2, borderColor: V600, backgroundColor: grad, fill: true, tension: .35, pointRadius: 2, pointHoverRadius: 4 }] },
            options: {
                resizeDelay: 200, interaction: { mode: 'index', intersect: false },
                plugins: { legend: { labels: { color: V900 } } },
                scales: { x: { ticks: { color: V900 } }, y: { beginAtZero: true, ticks: { precision: 0, color: V900 }, grid: { color: 'rgba(49,3,83,.08)' } } }
            }
        });
    }
    function buildBar(labels, data) {
        if (barChart) barChart.destroy();
        barChart = new Chart(barCtx, {
            type: 'bar',
            data: { labels, datasets: [{ label: 'Cierres (agente)', data, backgroundColor: data.map(() => 'rgba(181,126,220,.65)'), borderColor: data.map(() => V600), borderWidth: 1.2, borderRadius: 6 }] },
            options: {
                resizeDelay: 200, interaction: { mode: 'index', intersect: false },
                plugins: { legend: { labels: { color: V900 } } },
                scales: { x: { ticks: { color: V900 } }, y: { beginAtZero: true, ticks: { precision: 0, color: V900 }, grid: { color: 'rgba(49,3,83,.08)' } } }
            }
        });
    }
    function drawClientes(rows) {
        const tb = document.getElementById('tablaClientes'); tb.innerHTML = '';
        (rows || []).forEach(r => {
            const tr = document.createElement('tr');
            tr.innerHTML = `<td>${r.name}</td><td class="text-end">${(r.count || 0).toLocaleString('es-CR')}</td>`;
            tb.appendChild(tr);
        });
    }

    // ===== Render (consume tus endpoints) =====
    async function renderAll() {
        const gran = (function (f, t) { const d = diffDays(f, t); if (d <= 14) return 'day'; if (d <= 90) return 'week'; return 'month'; })(range.from, range.to);
        const qs = `from=${toYMD(range.from)}&to=${toYMD(range.to)}`;
        const safe = (p, fb) => p.then(r => r.ok ? r.json() : fb).catch(() => fb);

        const [kpis, series, closures, top] = await Promise.all([
            safe(fetch(`${urls.kpis}?${qs}`), { totalMessages: 0, agentClosures: 0, newClients: 0 }),
            safe(fetch(`${urls.series}?${qs}&groupBy=${gran}`), { granularity: gran, points: [] }),
            safe(fetch(`${urls.closures}?${qs}`), []),
            safe(fetch(`${urls.topclients}?${qs}&take=10`), [])
        ]);

        document.getElementById('kpiMensajes').textContent = (kpis.totalMessages || 0).toLocaleString('es-CR');
        document.getElementById('kpiCierres').textContent = (kpis.agentClosures || 0).toLocaleString('es-CR');
        document.getElementById('kpiClientesNuevos').textContent = (kpis.newClients || 0).toLocaleString('es-CR');
        const txt = `${toYMD(range.from)} a ${toYMD(range.to)}`;
        document.getElementById('kpiMensajesRange').textContent = txt;
        document.getElementById('kpiCierresRange').textContent = txt;
        const kc = document.getElementById('kpiClientesRange'); if (kc) kc.textContent = txt;

        buildLine((series.points || []).map(p => p.label), (series.points || []).map(p => p.value));
        buildBar((closures || []).map(x => x.name), (closures || []).map(x => x.count));
        drawClientes(top || []);
    }
})();
