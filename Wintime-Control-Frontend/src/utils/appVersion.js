// Подпись версии приложения под логотипом. Дата — в поясе браузера.
// demo — демо-стенд (GET /api/app-info): к подписи дописывается «демо-версия».
export function formatAppVersion({ version, buildDate, commit, demo = false }) {
  let date = 'dev'
  if (buildDate) {
    const d = new Date(buildDate)
    const pad = (n) => String(n).padStart(2, '0')
    date = `${pad(d.getDate())}.${pad(d.getMonth() + 1)}.${d.getFullYear()}`
  }
  return {
    label: `v${version} · ${date}${demo ? ' · демо-версия' : ''}`,
    tooltip: commit ? `commit ${commit}` : '',
  }
}
