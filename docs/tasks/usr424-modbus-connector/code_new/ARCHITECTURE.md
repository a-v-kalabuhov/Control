# UsrConnector — архитектура нижнего слоя коннектора

Read-only съём данных с ТПА BORCHE/KEBA через IoT-шлюз USR (Modbus TCP) и вычисление
состояния машины. Жёсткие инварианты — в `CLAUDE.md`; семантика автомата — в
`STATE_MACHINE.md`; предметная область — в циклограммах.

---

## 1. Слои и поток данных

```
connector.json ──► ConfigLoader ──► ConnectorConfig (+валидация профиля)
                                          │
ConnectorEngine (PeriodicTimer) ──────────┘
   │ каждый тик:
   │ 1. IModbusReader читает ReadBlock'и        ← NModbusReader (Modbus TCP)
   │ 2. RegisterDecoder: сырое → значение        ← STATELESS (scale/offset, invert)
   │ 3. RoleMapper: значения → RoleSnapshot      ← СЕМАНТИЧЕСКИЙ СЛОЙ (роли)
   │ 4. MachineStateMachine: снимок → состояние  ← STATEFUL (циклы, подушки, таймауты)
   ▼
MachineState ──► событие StateUpdated ──► UsrConnector.Host
                                            (консоль сейчас; MQTT/REST — ваш слой)
```

Три уровня абстракции сигнала:

1. **Физический** — адрес регистра, функциональный код, scale/offset. Живёт в конфиге и
   `Pipeline.cs`. Ничего не знает о смысле.
2. **Семантический** — `SignalRole` (`Injection`, `EjectorFwdReached`, `Reject`,
   `InjectionPosition`…). Назначается регистру в конфиге. Прикладная логика видит сигналы
   ТОЛЬКО через роли (`RoleSnapshot`) — замена шлюза/раскладки входов не трогает логику.
3. **Состояние машины** — `MachineState` (`mode`, `cycleCounter`, `cycleCompletion`,
   словарь полей). Вычисляется автоматом. Это ПУБЛИЧНЫЙ КОНТРАКТ с верхним слоем.

Сигналы с ролью `None` прозрачно пробрасываются в `MachineState.Fields` под своим именем —
принцип «коннектор снимает, интерпретируют внешние системы».

## 2. Проекты

| Проект | Содержимое | Зависимости |
|---|---|---|
| `UsrConnector.Core` | конфиг, Modbus-чтение, декодер, роли, автомат, движок | NModbus |
| `UsrConnector.Core.Tests` | исполняемая спецификация автомата + тесты физического слоя | xUnit |
| `UsrConnector.Host` | загрузка конфига, запуск движка, вывод состояния; **сюда** добавится внешний транспорт | Core |

Граница Core/Host физическая: Core не ссылается на Host и не получает транспортных
зависимостей (MQTT/REST). NModbus в Core — протокол опроса устройства, не внешний транспорт.

## 3. Файлы Core

| Файл | Что |
|---|---|
| `Config.cs` | `DeviceConfig`, `RegisterDef` (с полем `Role`), `ConnectorConfig`, профили и их валидация |
| `ConfigLoader.cs` | JSON → конфиг: hex-адреса, enum без учёта регистра, настройки автомата |
| `ModbusReader.cs` | `IModbusReader` (только FC 0x01–0x04) + `NModbusReader` |
| `Pipeline.cs` | `BatchPlanner` (склейка смежных регистров в запросы), `RegisterDecoder` (stateless) |
| `SignalRoles.cs` | enum `SignalRole` + `RoleSnapshot` (вход автомата) |
| `RoleMapper.cs` | сэмплы регистров → `RoleSnapshot` по ролям; None → ExtraFields |
| `MachineState.cs` | контракт: `MachineMode`, `CycleCompletion`, `MachineState`, `WellKnownFields` |
| `StateMachineSettings.cs` | seed, коэффициенты alarm/idle, окно среднего, сброс статистики, порог offline |
| `MachineStateMachine.cs` | автомат (см. STATE_MACHINE.md) |
| `ConnectorEngine.cs` | цикл опроса, оркестровка, событие `StateUpdated` |

## 4. Конфигурация (`connector.json`)

Секции: `device` (host/port/unitId/период/таймаут), `profile`
(`singleNode` / `twoNode`), `stateMachine` (seed, коэффициенты, окно, порог offline),
`registers` (список).

Каждый регистр: `name`, `address` (hex/dec), `access` (`coil`/`discreteInput`/
`holdingRegister`/`inputRegister`), `role`, для аналога `scale`/`offset`/`unit`, для
дискретных опционально `invert` (NC-контакты).

Валидация при загрузке: обязательные роли профиля назначены (`singleNode`: Injection +
EjectorFwdReached; `twoNode`: + InjectionPosition2), роль ≠ None не дублируется, имена
уникальны.

Пример пилота — `UsrConnector.Host/connector.json` (точки съёма и калибровка датчиков —
из CYCLOGRAM_SINGLE_NODE.md §6: сухие контакты KA напрямую, Injection через оптопару,
LS2 0–10 В → мм).

## 5. Профили машин

| Профиль | Обязательные роли | Особенности |
|---|---|---|
| `singleNode` | Injection, EjectorFwdReached | пилот: 4 DI + 2 AI USR |
| `twoNode` | + InjectionPosition2 | 2K: две подушки (минимумы за одно окно цикла); дефицит аналоговых входов — вероятно, второй модуль USR или отказ от MoldPosition |

Роли узла 2 (`Injection2`, `InjectionPosition2`) добавлены в enum; вторая подушка трекается
автоматом в том же окне цикла (узлы 2K работают синхронно — см. CYCLOGRAM_FULL_TWO_NODE.md)
и публикуется полем `cushion2`.

## 6. Обработка ошибок и offline

Любая ошибка чтения = неудачный опрос: движок рвёт соединение, подаёт в автомат
`RoleSnapshot.Disconnected` и на следующем тике переподключается. Offline объявляет автомат
после `OfflineAfterFailedPolls` подряд неудачных опросов (демпфер против ложных
`Interrupted`; рекомендация — суммарный порог сравним с длительностью цикла). Активный цикл
при этом помечается `Interrupted`; восстановление связи → `idle` с пересевом детекторов
фронтов.

## 7. Точки расширения

- **Внешний транспорт** — только Host: подписка на `StateUpdated`, шаблон устройства
  (имя поля → семантический тип), упаковка, MQTT/REST. В `mode=offline` наружу не отправлять.
- **Новые опциональные сигналы** (температуры, чиллер) — строка в конфиге с `role: none`;
  значение появится в `Fields` под именем регистра. Кода не требует.
- **Новая семантика** (сигналы аварии KEBA, когда появится документация оператора) — новая
  роль в `SignalRole` + обработка в автомате + тест + правка STATE_MACHINE.md (синхронно).
- **Несколько машин** — экземпляр `ConnectorEngine` на устройство.
- **Больше входов** — замена USR на модель с 8 DI ничего не меняет: больше строк в конфиге.

## 8. Известные ограничения (осознанные)

- `count > 1` декодируется по первому слову (32-битные значения не нужны текущему набору).
- Автомат «слышит» только назначенные роли; полная валидация порядка фаз по циклограмме —
  возможный будущий слой поверх, не требование.
- Компиляция в среде генерации не проверялась (нет SDK): первый шаг при получении —
  `dotnet build && dotnet test`.
- Наследуемые проверки на месте: фактический TCP-порт USR (регистр 0x1076), удержание
  смыкания (замер DO3 — CYCLOGRAM_SINGLE_NODE.md §8.1), пороги детекции — по реальным данным.
