# Версия приложения в UI — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Показать под надписью CONTROL номер версии и дату сборки; номер хранится в одном файле `VERSION` и меняется вручную перед публикацией.

**Architecture:** Файл `VERSION` в корне репо — единственный источник правды. Vite читает его при сборке и вшивает константы (`__APP_VERSION__`, `__BUILD_DATE__`, `__GIT_COMMIT__`) через `define`; чистая функция `formatAppVersion` превращает их в подпись и подсказку. MSBuild читает тот же файл в `<Version>` через `Directory.Build.props`; `build.ps1` использует его как тег Docker-образов.

**Tech Stack:** Vue 3 + Vite, Vitest, MSBuild (.NET 9), PowerShell.

**Спецификация:** [docs/superpowers/specs/2026-09-29-app-version-design.md](../specs/2026-09-29-app-version-design.md)

## Global Constraints

- Формат номера — SemVer `MAJOR.MINOR.PATCH`, стартовое значение `0.9.0`, без префикса `v` в файле.
- Все читающие стороны делают `trim` содержимого `VERSION`.
- Версия показывается **только** в `DefaultLayout.vue`; `MobileLayout.vue` и `LoginView.vue` не трогаем.
- Подпись: `v0.9.0 · ДД.ММ.ГГГГ` (дата в поясе браузера); в `npm run dev` — `v0.9.0 · dev`; подсказка `commit <hash>` или пустая.
- `deploy/pzp/build-and-save.ps1`: тег `pilot` не меняется, только печать версии.
- `build.ps1`: теги `:<версия>` и `:latest`, пушатся оба.
- Работа в ветке `feature/app-version`; прямой push в `master` запрещён.

---

### Task 1: `VERSION` + `Directory.Build.props`

**Files:**
- Create: `VERSION`
- Create: `Directory.Build.props`

**Interfaces:**
- Produces: файл `VERSION` (корень репо) с содержимым `0.9.0\n` — его читают Task 3 (Vite) и Task 4 (скрипты).

- [ ] **Step 1: Создать `VERSION`**

Содержимое (одна строка + перевод строки):

```
0.9.0
```

- [ ] **Step 2: Создать `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <!-- Единый номер версии продукта; меняется вручную перед публикацией (см. CLAUDE.md) -->
    <Version>$([System.IO.File]::ReadAllText('$(MSBuildThisFileDirectory)VERSION').Trim())</Version>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Проверить, что ни один проект не задаёт свою версию**

Run: `git grep -n -E "<(Version|AssemblyVersion|FileVersion|VersionPrefix)>" -- "*.csproj"`
Expected: пустой вывод.

- [ ] **Step 4: Собрать решение и прогнать unit-тесты**

Run: `dotnet build Wintime.Control.sln -c Debug` → Expected: `Build succeeded`, 0 ошибок.
Run: `dotnet test Wintime.Control.Tests.Unit` → Expected: все тесты зелёные.

- [ ] **Step 5: Проверить, что версия попала в сборку**

Run (PowerShell): `[System.Reflection.AssemblyName]::GetAssemblyName((Resolve-Path "Wintime.Control.API/bin/Debug/net9.0/Wintime.Control.API.dll")).Version`
Expected: `Major 0, Minor 9, Build 0, Revision 0`.

- [ ] **Step 6: Commit**

```bash
git add VERSION Directory.Build.props
git commit -m "build: single VERSION file drives .NET assembly version"
```

---

### Task 2: `formatAppVersion` (чистая функция, TDD)

**Files:**
- Create: `Wintime-Control-Frontend/src/utils/appVersion.js`
- Test: `Wintime-Control-Frontend/src/utils/__tests__/appVersion.spec.js`

**Interfaces:**
- Produces: `formatAppVersion({ version: string, buildDate: string, commit: string }) → { label: string, tooltip: string }`. `buildDate` — ISO-строка или `''` (dev); `commit` — короткий хэш или `''`.

- [ ] **Step 1: Написать падающий тест**

```js
import { describe, it, expect } from 'vitest'
import { formatAppVersion } from '../appVersion'

describe('formatAppVersion', () => {
  it('сборка: номер и дата ДД.ММ.ГГГГ', () => {
    // полдень UTC — одна и та же календарная дата в любом поясе от -11 до +11
    const r = formatAppVersion({ version: '0.9.0', buildDate: '2026-09-29T12:00:00Z', commit: 'abc1234' })
    expect(r.label).toBe('v0.9.0 · 29.09.2026')
    expect(r.tooltip).toBe('commit abc1234')
  })

  it('dev-режим: пустая дата → «dev»', () => {
    const r = formatAppVersion({ version: '0.9.0', buildDate: '', commit: 'abc1234' })
    expect(r.label).toBe('v0.9.0 · dev')
  })

  it('без git: пустой хэш → пустая подсказка', () => {
    const r = formatAppVersion({ version: '0.9.0', buildDate: '2026-01-05T12:00:00Z', commit: '' })
    expect(r.label).toBe('v0.9.0 · 05.01.2026')
    expect(r.tooltip).toBe('')
  })
})
```

- [ ] **Step 2: Запустить тест — должен упасть**

Run: `cd Wintime-Control-Frontend; npx vitest run src/utils/__tests__/appVersion.spec.js`
Expected: FAIL — `Failed to resolve import "../appVersion"`.

- [ ] **Step 3: Реализация**

```js
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
```

- [ ] **Step 4: Запустить тест — должен пройти**

Run: `npx vitest run src/utils/__tests__/appVersion.spec.js`
Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add Wintime-Control-Frontend/src/utils/appVersion.js Wintime-Control-Frontend/src/utils/__tests__/appVersion.spec.js
git commit -m "feat(ui): formatAppVersion helper"
```

---

### Task 3: Вшивание констант в Vite + отображение под CONTROL

**Files:**
- Modify: `Wintime-Control-Frontend/vite.config.js` (весь файл)
- Create: `Wintime-Control-Frontend/src/utils/buildInfo.js`
- Modify: `Wintime-Control-Frontend/src/layouts/DefaultLayout.vue:9-11` (блок логотипа) + `<script setup>`

**Interfaces:**
- Consumes: `VERSION` (Task 1), `formatAppVersion` (Task 2).
- Produces: `appVersion: { label, tooltip }` из `@/utils/buildInfo`.

`buildInfo.js` — отдельный модуль, потому что Vitest использует свой `vitest.config.js` без этих констант; тесты его не импортируют.

- [ ] **Step 1: Переписать `vite.config.js`**

```js
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
```

- [ ] **Step 2: Создать `src/utils/buildInfo.js`**

```js
/* global __APP_VERSION__, __BUILD_DATE__, __GIT_COMMIT__ */
// Константы вшиваются Vite (define в vite.config.js) при сборке.
import { formatAppVersion } from './appVersion'

export const appVersion = formatAppVersion({
  version: __APP_VERSION__,
  buildDate: __BUILD_DATE__,
  commit: __GIT_COMMIT__,
})
```

- [ ] **Step 3: Изменить блок логотипа в `DefaultLayout.vue`**

Было (строки 9-11):

```html
      <div class="h-16 flex-shrink-0 flex items-center justify-center border-b border-gray-200">
        <div class="text-xl font-bold text-primary-700">CONTROL</div>
      </div>
```

Стало:

```html
      <div class="h-16 flex-shrink-0 flex flex-col items-center justify-center border-b border-gray-200">
        <div class="text-xl font-bold text-primary-700 leading-tight">CONTROL</div>
        <div class="text-xs text-gray-400 leading-tight" :title="appVersion.tooltip">{{ appVersion.label }}</div>
      </div>
```

В `<script setup>` рядом с остальными импортами добавить:

```js
import { appVersion } from '@/utils/buildInfo'
```

- [ ] **Step 4: Прогнать все фронт-тесты**

Run: `cd Wintime-Control-Frontend; npm test`
Expected: все зелёные (в т.ч. 3 новых из Task 2).

- [ ] **Step 5: Production-сборка содержит версию**

Run: `npm run build`, затем `Select-String -Path ../Wintime.Control.API/wwwroot/assets/*.js -Pattern '0\.9\.0' -List | Select-Object -First 1`
Expected: сборка успешна, найдено совпадение.

Примечание: `wwwroot` — артефакт сборки; не коммитить, если он в `.gitignore` (проверить `git status`).

- [ ] **Step 6: Визуальная проверка в dev**

Run: `npm run dev`, открыть http://localhost:3000, войти.
Expected: под CONTROL — `v0.9.0 · dev`, при наведении — `commit <hash>`; высота шапки меню не изменилась. `/login` и мобильный layout — без версии.

- [ ] **Step 7: Commit**

```bash
git add Wintime-Control-Frontend/vite.config.js Wintime-Control-Frontend/src/utils/buildInfo.js Wintime-Control-Frontend/src/layouts/DefaultLayout.vue
git commit -m "feat(ui): show app version and build date under CONTROL logo"
```

---

### Task 4: Скрипты публикации + CLAUDE.md

**Files:**
- Modify: `build.ps1` (param-блок, шаги 4-5)
- Modify: `deploy/pzp/build-and-save.ps1` (после вычисления `$ControlRoot`)
- Modify: `CLAUDE.md` (раздел «Команды»)

**Interfaces:**
- Consumes: `VERSION` (Task 1).

- [ ] **Step 1: `build.ps1` — тег из `VERSION`, два тега на образ**

Заменить param-блок и начало:

```powershell
param(
    # По умолчанию — номер из файла VERSION в корне репо
    [string]$Tag = (Get-Content (Join-Path $PSScriptRoot "VERSION") -Raw).Trim(),
    [string]$Registry = "ghcr.io/a-v-kalabuhov/control"
)

$ErrorActionPreference = "Stop"

Write-Host "==> Version: $Tag"
```

Заменить шаги 4 и 5:

```powershell
# 4. Docker build (версия + latest)
Write-Host "==> Building Docker images..."
docker build -f Wintime.Control.API/Dockerfile.prod -t "$Registry/api:$Tag" -t "$Registry/api:latest" .
docker build -f Wintime.Control.Emulator/Dockerfile.prod -t "$Registry/emulator:$Tag" -t "$Registry/emulator:latest" .

# 5. Push
Write-Host "==> Pushing to GHCR..."
docker push "$Registry/api:$Tag"
docker push "$Registry/api:latest"
docker push "$Registry/emulator:$Tag"
docker push "$Registry/emulator:latest"
```

- [ ] **Step 2: `deploy/pzp/build-and-save.ps1` — печать версии**

Сразу после строки `$ConnRoot = ...` добавить:

```powershell
$Version     = (Get-Content (Join-Path $ControlRoot "VERSION") -Raw).Trim()
Write-Host "==> Version: $Version"
```

Тег `pilot` не трогать.

- [ ] **Step 3: Синтаксическая проверка скриптов (без запуска сборки)**

Run (PowerShell):
```powershell
foreach ($f in 'build.ps1','deploy/pzp/build-and-save.ps1') { $e=$null; [void][System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $f), [ref]$null, [ref]$e); "$f : $($e.Count) errors" }
```
Expected: `0 errors` для обоих.

Run: `(Get-Content VERSION -Raw).Trim()` → Expected: `0.9.0`.

- [ ] **Step 4: CLAUDE.md**

В блоке «Команды» после строки `docker-compose up   # PostgreSQL + Mosquitto + API` (внутри блока кода) добавить:

```powershell

.\build.ps1          # публикация образов; ПЕРЕД ней поднять номер в VERSION (SemVer)
```

- [ ] **Step 5: Commit**

```bash
git add build.ps1 deploy/pzp/build-and-save.ps1 CLAUDE.md
git commit -m "build: tag images with VERSION, print version in pilot build"
```

---

### Финал

- [ ] `dotnet build Wintime.Control.sln` и `npm test` — зелёные.
- [ ] Push ветки `feature/app-version`, PR в `master` через `gh pr create`.
