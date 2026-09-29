/* global __APP_VERSION__, __BUILD_DATE__, __GIT_COMMIT__ */
// Константы вшиваются Vite (define в vite.config.js) при сборке.
import { formatAppVersion } from './appVersion'

export const appVersion = formatAppVersion({
  version: __APP_VERSION__,
  buildDate: __BUILD_DATE__,
  commit: __GIT_COMMIT__,
})
