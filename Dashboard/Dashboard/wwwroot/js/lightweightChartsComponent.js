let libraryPromise;

function loadScript(source) {
    return new Promise((resolve, reject) => {
        const existing = document.querySelector(`script[data-lightweight-charts="${source}"]`);
        if (existing) {
            existing.addEventListener('load', resolve, { once: true });
            existing.addEventListener('error', reject, { once: true });
            if (existing.dataset.loaded === 'true') {
                resolve();
            }
            return;
        }

        const script = document.createElement('script');
        script.src = source;
        script.async = true;
        script.dataset.lightweightCharts = source;
        script.addEventListener('load', () => {
            script.dataset.loaded = 'true';
            resolve();
        }, { once: true });
        script.addEventListener('error', reject, { once: true });
        document.head.appendChild(script);
    });
}

async function ensureLibrary() {
    if (window.LightweightCharts) {
        return window.LightweightCharts;
    }

    libraryPromise ??= loadScript('https://unpkg.com/lightweight-charts@4.2.3/dist/lightweight-charts.standalone.development.js');
    await libraryPromise;
    return window.LightweightCharts;
}

function applyRange(state, payload) {
    const rangeFrom = payload?.rangeFrom ?? payload?.RangeFrom;
    const rangeTo = payload?.rangeTo ?? payload?.RangeTo;

    if (!payload || rangeFrom == null || rangeTo == null) {
        state.chart.timeScale().fitContent();
        return;
    }

    state.chart.timeScale().setVisibleLogicalRange({
        from: rangeFrom,
        to: rangeTo
    });
}

function createSeries(state, definition) {
    const options = {
        title: definition.title ?? definition.Title
    };

    switch (definition.type ?? definition.Type) {
        case 'candlestick':
            Object.assign(options, {
                upColor: definition.upColor ?? definition.UpColor,
                downColor: definition.downColor ?? definition.DownColor,
                borderUpColor: definition.borderUpColor ?? definition.BorderUpColor,
                borderDownColor: definition.borderDownColor ?? definition.BorderDownColor,
                wickUpColor: definition.wickUpColor ?? definition.WickUpColor,
                wickDownColor: definition.wickDownColor ?? definition.WickDownColor
            });
            return state.chart.addCandlestickSeries(options);
        case 'area':
            Object.assign(options, {
                lineColor: definition.lineColor ?? definition.LineColor,
                topColor: definition.topColor ?? definition.TopColor,
                bottomColor: definition.bottomColor ?? definition.BottomColor
            });
            return state.chart.addAreaSeries(options);
        default:
            Object.assign(options, {
                color: definition.color ?? definition.Color
            });
            return state.chart.addLineSeries(options);
    }
}

function toLabels(payload) {
    return new Map(Object.entries(payload?.labels ?? payload?.Labels ?? {}));
}

function normalizeSeriesDefinitions(payload) {
    return payload?.series ?? payload?.Series ?? [];
}

function normalizeTime(value) {
    if (value == null) {
        return null;
    }

    const time = Number(value);
    return Number.isFinite(time) ? time : null;
}

function normalizeLinePoint(point) {
    const time = normalizeTime(point?.time ?? point?.Time);
    const value = Number(point?.value ?? point?.Value);

    if (time == null || Number.isFinite(value) === false) {
        return null;
    }

    return { time, value };
}

function normalizeCandlePoint(point) {
    const time = normalizeTime(point?.time ?? point?.Time);
    const open = Number(point?.open ?? point?.Open);
    const high = Number(point?.high ?? point?.High);
    const low = Number(point?.low ?? point?.Low);
    const close = Number(point?.close ?? point?.Close);

    if (time == null || [open, high, low, close].some(value => Number.isFinite(value) === false)) {
        return null;
    }

    return { time, open, high, low, close };
}

function normalizeData(definition) {
    const source = definition?.data ?? definition?.Data ?? [];

    if (definition?.type === 'candlestick' || definition?.Type === 'candlestick') {
        return source.map(normalizeCandlePoint).filter(Boolean);
    }

    return source.map(normalizeLinePoint).filter(Boolean);
}

export async function createChart(element) {
    const lightweightCharts = await ensureLibrary();
    const state = {
        chart: null,
        library: lightweightCharts,
        disposed: false,
        labels: new Map(),
        series: new Map(),
        resizeObserver: null
    };
    const chart = lightweightCharts.createChart(element, {
        autoSize: true,
        layout: {
            background: { color: '#ffffff' },
            textColor: '#1f2937'
        },
        grid: {
            vertLines: { color: '#e5e7eb' },
            horzLines: { color: '#e5e7eb' }
        },
        rightPriceScale: {
            borderColor: '#d1d5db'
        },
        timeScale: {
            borderColor: '#d1d5db',
            timeVisible: true,
            secondsVisible: true,
            tickMarkFormatter: (time) => {
                return state.labels?.get(String(time)) ?? '';
            }
        },
        crosshair: {
            mode: lightweightCharts.CrosshairMode.Normal
        }
    });

    state.chart = chart;
    state.resizeObserver = new ResizeObserver(() => {
            chart.resize(element.clientWidth, element.clientHeight);
        });

    state.resizeObserver.observe(element);
    return state;
}

export function setChartData(state, payload) {
    if (!state || !state.chart || state.disposed) {
        return;
    }

    state.labels = toLabels(payload);

    const definitions = normalizeSeriesDefinitions(payload);
    const keys = new Set(definitions.map(series => series.key ?? series.Key));

    for (const [key, series] of state.series.entries()) {
        if (!keys.has(key)) {
            state.chart.removeSeries(series);
            state.series.delete(key);
        }
    }

    for (const definition of definitions) {
        const key = definition.key ?? definition.Key;
        let series = state.series.get(key);
        if (!series) {
            series = createSeries(state, definition);
            state.series.set(key, series);
        }

        series.setData(normalizeData(definition));
    }

    applyRange(state, payload);
}

export function clearChart(state) {
    if (!state || !state.chart || state.disposed) {
        return;
    }

    for (const series of state.series.values()) {
        state.chart.removeSeries(series);
    }

    state.series.clear();
    state.labels = new Map();
    state.chart.timeScale().fitContent();
}

export function disposeChart(state) {
    if (!state || state.disposed) {
        return;
    }

    state.disposed = true;
    state.resizeObserver?.disconnect();

    try {
        state.chart?.remove();
    }
    catch {
    }
}
