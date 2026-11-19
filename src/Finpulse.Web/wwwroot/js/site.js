document.querySelectorAll('form[data-confirm]').forEach(form => form.addEventListener('submit', event => {
  if (!window.confirm(form.dataset.confirm)) event.preventDefault();
}));
document.querySelectorAll('form').forEach(form => form.addEventListener('submit', event => {
  if (!event.defaultPrevented && form.checkValidity()) {
    const button = event.submitter;
    if (button) { button.disabled = true; button.textContent = button.dataset.pendingLabel || 'Guardando…'; }
  }
}));
const typeSelect = document.querySelector('[data-category-type]');
if (typeSelect) {
  const category = document.getElementById('CategoryId');
  const options = Array.from(category.options).map(option => option.cloneNode(true));
  const sync = () => {
    const previous = category.value;
    category.replaceChildren(...options.filter(option => option.dataset.kind === typeSelect.value).map(option => option.cloneNode(true)));
    if (Array.from(category.options).some(option => option.value === previous)) category.value = previous;
  };
  typeSelect.addEventListener('change', sync); sync();
}
const errors = document.querySelector('.validation-summary-errors');
if (errors) errors.focus();
document.querySelectorAll('[data-fin-chart]').forEach(canvas => {
  if (!window.Chart) return;
  const config = JSON.parse(canvas.dataset.finChart);
  new Chart(canvas, {
    type: 'line',
    data: { labels: config.labels, datasets: config.series.map((series, i) => ({
      label: series.label, data: series.values, borderColor: ['#bde89b', '#b7bfa4', '#8dbbc3'][i % 3],
      backgroundColor: i === 0 ? 'rgba(189,232,155,.07)' : 'transparent', fill: i === 0,
      tension: .25, borderWidth: 2, pointRadius: config.labels.length > 12 ? 0 : 3, pointHoverRadius: 5,
      borderDash: i === 1 ? [5, 4] : []
    })) },
    options: {
      maintainAspectRatio: false,
      animation: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? false : { duration: 300 },
      interaction: { intersect: false, mode: 'index' },
      plugins: { legend: { position: 'bottom', labels: { color: '#c2d1c5', usePointStyle: true, padding: 20 } },
        tooltip: { callbacks: { label: context => context.dataset.label + ': Bs ' + Number(context.parsed.y).toLocaleString('es-BO', { minimumFractionDigits: 2 }) } } },
      scales: { x: { grid: { display: false }, ticks: { color: '#a6b8ad', maxTicksLimit: 8 } },
        y: { grid: { color: '#30463a' }, ticks: { color: '#a6b8ad', maxTicksLimit: 5 } } }
    }
  });
});
