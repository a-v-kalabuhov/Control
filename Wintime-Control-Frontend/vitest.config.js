import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'
import { resolve } from 'path'

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: { '@': resolve(__dirname, 'src') },
  },
  test: {
    environment: 'node',
    include: ['src/**/*.spec.js'],
    // Без inline здесь vite-node's SSR-загрузчик ломает CJS/ESM интероп default-экспорта
    // async-validator внутри el-form: `new AsyncValidator(...)` кидает "is not a constructor",
    // form.validate() тихо резолвится в true — валидация в тестах перестаёт что-либо блокировать.
    server: { deps: { inline: ['element-plus', 'async-validator'] } },
  },
})
