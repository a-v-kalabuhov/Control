import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { resolve } from 'path'
import { readFileSync } from 'fs'
import { execSync } from 'child_process'

// Версия продукта — из VERSION в корне репо (меняется вручную перед публикацией)
const appVersion = readFileSync(resolve(__dirname, '../VERSION'), 'utf-8').trim()

function gitCommit() {
  try {
    return execSync('git rev-parse --short HEAD', { cwd: __dirname, stdio: ['ignore', 'pipe', 'ignore'] })
      .toString().trim()
  } catch {
    return ''
  }
}

export default defineConfig(({ command }) => ({
  plugins: [vue()],
  define: {
    __APP_VERSION__: JSON.stringify(appVersion),
    // В dev-сервере даты нет — UI покажет «dev»
    __BUILD_DATE__: JSON.stringify(command === 'build' ? new Date().toISOString() : ''),
    __GIT_COMMIT__: JSON.stringify(gitCommit()),
  },
  resolve: {
    alias: {
      '@': resolve(__dirname, 'src')
    },
  },
  build: {
    outDir: '../Wintime.Control.API/wwwroot',
    emptyOutDir: true
  },
  server: {
    port: 3000,
    proxy: {
      '/api': {
        target: 'https://localhost:5001',
        changeOrigin: true,
        secure: false
      }
    }
  }
}))
