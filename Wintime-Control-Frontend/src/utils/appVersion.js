// Подпись версии приложения под логотипом. Дата — в поясе браузера.
export function formatAppVersion({ version, buildDate, commit }) {
  let date = 'dev'
  if (buildDate) {
    const d = new Date(buildDate)
    const pad = (n) => String(n).padStart(2, '0')
    date = `${pad(d.getDate())}.${pad(d.getMonth() + 1)}.${d.getFullYear()}`
  }
  return {
    label: `v${version} · ${date}`,
    tooltip: commit ? `commit ${commit}` : '',
  }
}
