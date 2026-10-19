/* global __APP_VERSION__, __BUILD_DATE__, __GIT_COMMIT__ */
// Константы вшиваются Vite (define в vite.config.js) при сборке.
import { formatAppVersion } from './appVersion'

const build = {
  version: __APP_VERSION__,
  buildDate: __BUILD_DATE__,
  commit: __GIT_COMMIT__,
}

// Подпись версии; demo — признак демо-стенда из стора appInfo.
export const appVersionFor = (demo) => formatAppVersion({ ...build, demo })
