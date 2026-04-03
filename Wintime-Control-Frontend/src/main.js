import { createApp } from 'vue'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import * as ElementPlusIconsVue from '@element-plus/icons-vue'
import router from './router'
import App from './App.vue'
import './assets/css/main.css'  // ← Теперь файл существует
import dayjs from 'dayjs'
import 'dayjs/locale/ru'

dayjs.locale('ru')

const app = createApp(App)
const pinia = createPinia()

// Регистрируем иконки
for (const [key, component] of Object.entries(ElementPlusIconsVue)) {
  app.component(key, component)
}

app.use(pinia)
app.use(router)
app.use(ElementPlus, { locale: 'ru' })

app.mount('#app')