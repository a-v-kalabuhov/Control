-- ============================================================
-- Wintime Control — тестовые данные для отчёта «Производительность оборудования»
-- Период   : 2026-06-01 … 2026-09-30 (локальные сутки Europe/Moscow), 4 месяца
-- Состав   : 1 шаблон ТПА, 3 ТПА, 10 ПФ (+10 типов изделий), 2 наладчика, админ
-- Логины   : admin / adj1 / adj2 — пароль Demo@123
-- Сценарии :
--   ТПА-1  надёжный, 24/7, редкие простои; 6 ч без связи 05.08
--   ТПА-2  частые аварии, ночами бывает «Отсутствие оператора»;
--          14.07–16.07 — нет данных (ни одной записи статуса)
--   ТПА-3  пропадания связи 18.06 и 22–23.07; с 10.09 12:00 — без связи
--          и в архиве (IsActive = false)
--   Между заданиями — «Без задания», иногда «Работа без задания».
-- Запуск   : только в отдельной БД WintimeControl_ReportTest со схемой
--            (миграции уже применены). Скрипт перезаписывает все данные.
--   docker exec -i <pg-container> psql -U postgres -d WintimeControl_ReportTest \
--     -v ON_ERROR_STOP=1 < seed_equipment_report.sql
-- ============================================================

DO $$
BEGIN
  IF current_database() <> 'WintimeControl_ReportTest' THEN
    RAISE EXCEPTION 'Скрипт перезаписывает данные — запускать только в WintimeControl_ReportTest (сейчас: %)',
      current_database();
  END IF;
END $$;

TRUNCATE "ImmCycles", "ImmStatusHistory", "Events", "UnplannedRuns", "ShiftTasks", "Orders",
         "Telemetry", "Molds", "ProductTypes", "Imms", "Templates", "DowntimeReasons", "Shifts",
         "Users" CASCADE;

-- ============================================================
-- Справочники
-- ============================================================
INSERT INTO "Templates" ("Id","Name","Manufacturer","Model","Version","Author","JsonConfig","IsActive","CreatedAt","UpdatedAt")
VALUES ('a2000000-0000-0000-0000-000000000001','Haitian Mars III','Haitian','MA III','1.0','Тест','{}',true,
        '2026-01-01 00:00+00','2026-01-01 00:00+00');

INSERT INTO "Imms" ("Id","Name","InventoryNumber","TemplateId","IsActive","CommissioningDate","CreatedAt")
VALUES
  ('b2000000-0000-0000-0000-000000000001','ТПА-1 Haitian MA1600','RT-001','a2000000-0000-0000-0000-000000000001',true, '2024-03-01 00:00+00','2026-01-01 00:00+00'),
  ('b2000000-0000-0000-0000-000000000002','ТПА-2 Haitian MA2500','RT-002','a2000000-0000-0000-0000-000000000001',true, '2023-05-01 00:00+00','2026-01-01 00:00+00'),
  ('b2000000-0000-0000-0000-000000000003','ТПА-3 Haitian MA900', 'RT-003','a2000000-0000-0000-0000-000000000001',false,'2019-09-01 00:00+00','2026-01-01 00:00+00');

INSERT INTO "ProductTypes" ("Id","Article","Name","IsActive","CreatedAt")
SELECT ('e2000000-0000-0000-0000-0000000001' || lpad(n::text, 2, '0'))::uuid, 'ART-' || (100 + n), name, true, '2026-01-01 00:00+00'
FROM (VALUES (1,'Крышка флип-топ 28 мм'),(2,'Колпачок 38 мм'),(3,'Корпус фильтра'),(4,'Ручка ведра'),
             (5,'Ведро 5 л'),(6,'Крышка ведра 5 л'),(7,'Ящик овощной'),(8,'Втулка d20'),
             (9,'Заглушка 20×20'),(10,'Шайба 8 мм')) AS v(n, name);

-- Пулы ПФ: ТПА-1 → ПФ 1–4, ТПА-2 → ПФ 5–7, ТПА-3 → ПФ 8–10
INSERT INTO "Molds" ("Id","FormId","Name","Cavities","PartWeightGrams","RunnerWeightGrams","MaxResourceCycles",
                     "To1Cycles","To2Cycles","IsActive","MoldStatus","ProductTypeId","CreatedAt")
SELECT ('c2000000-0000-0000-0000-0000000001' || lpad(n::text, 2, '0'))::uuid, 'PF-' || (100 + n), name, cav, pw, rw,
       1000000, 400000, 800000, true, 0,
       ('e2000000-0000-0000-0000-0000000001' || lpad(n::text, 2, '0'))::uuid, '2026-01-01 00:00+00'
FROM (VALUES (1,'Крышка флип-топ 28 мм', 8,  3.2, 4.0),
             (2,'Колпачок 38 мм',       12,  4.1, 6.0),
             (3,'Корпус фильтра',        2, 38.0, 9.0),
             (4,'Ручка ведра',           4,  6.5, 5.0),
             (5,'Ведро 5 л',             1,210.0,12.0),
             (6,'Крышка ведра 5 л',      1, 95.0, 8.0),
             (7,'Ящик овощной',          1,820.0,25.0),
             (8,'Втулка d20',           32,  1.9, 7.0),
             (9,'Заглушка 20×20',       24,  1.5, 5.0),
             (10,'Шайба 8 мм',          48,  0.4, 3.0)) AS v(n, name, cav, pw, rw);

INSERT INTO "DowntimeReasons" ("Id","Name","Type","IsActive","CreatedAt")
VALUES
  ('d2000000-0000-0000-0000-000000000001','Неисправность оборудования','Emergency',true,'2026-01-01 00:00+00'),
  ('d2000000-0000-0000-0000-000000000002','Неисправность пресс-формы', 'Emergency',true,'2026-01-01 00:00+00'),
  ('d2000000-0000-0000-0000-000000000003','Ожидание сырья',            'Planned',  true,'2026-01-01 00:00+00'),
  ('d2000000-0000-0000-0000-000000000004','Отсутствие оператора',      'Planned',  true,'2026-01-01 00:00+00'),
  ('d2000000-0000-0000-0000-000000000005','Подналадка технологии',     'Planned',  true,'2026-01-01 00:00+00');

-- Две 12-часовые смены 08:00–20:00 и 20:00–08:00 по Москве
INSERT INTO "Shifts" ("Id","StartMinutes","DurationMinutes","BreakStartMinutes","BreakDurationMinutes","TimeZoneId","CreatedAt")
VALUES (gen_random_uuid(),  480, 720,  720, 30, 'Europe/Moscow', '2026-01-01 00:00+00'),
       (gen_random_uuid(), 1200, 720,    0, 30, 'Europe/Moscow', '2026-01-01 00:00+00');

-- Пароль всех пользователей: Demo@123 (Identity V3, PBKDF2-SHA256, 100 000 итераций)
INSERT INTO "Users" ("Id","FullName","Role","EmployeeId","IsActive","UserName","NormalizedUserName","Email","NormalizedEmail",
                     "EmailConfirmed","PasswordHash","SecurityStamp","ConcurrencyStamp",
                     "PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount")
SELECT id, fio, role, emp, true, login, upper(login), login || '@test.local', upper(login || '@test.local'), true,
       'AQAAAAEAAYagAAAAEI80oht8Xp0POmstjkwfegtuSbDirfOYzpBrtROf7w8kGYzBTu7MQxOl2fRSXRA8Dg==',
       gen_random_uuid()::text, gen_random_uuid()::text, false, false, true, 0
FROM (VALUES ('f2000000-0000-0000-0000-000000000001','Администратор',  0,'T-001','admin'),
             ('f2000000-0000-0000-0000-000000000002','Олег Сидоров',    2,'T-002','adj1'),
             ('f2000000-0000-0000-0000-000000000003','Павел Гусев',     2,'T-003','adj2')) AS v(id, fio, role, emp, login);

-- ============================================================
-- Вспомогательные функции (живут только в этой сессии)
-- ============================================================
CREATE FUNCTION pg_temp.lt(p text) RETURNS timestamptz LANGUAGE sql IMMUTABLE AS
$$ SELECT p::timestamp AT TIME ZONE 'Europe/Moscow' $$;

CREATE FUNCTION pg_temp.rmin(lo int, hi int) RETURNS interval LANGUAGE sql VOLATILE AS
$$ SELECT ((lo + random() * (hi - lo))::int) * interval '1 minute' $$;

CREATE FUNCTION pg_temp.st(p_imm uuid, p_status text, a timestamptz, b timestamptz) RETURNS void LANGUAGE sql AS
$$ INSERT INTO "ImmStatusHistory" ("ImmId","Status","ChangedAt","EndedAt")
   SELECT p_imm, p_status, a, b WHERE b > a $$;

CREATE FUNCTION pg_temp.ev(p_imm uuid, p_task uuid, p_adj text, p_reason int, a timestamptz, b timestamptz) RETURNS void LANGUAGE sql AS
$$ INSERT INTO "Events" ("Id","ImmId","EventType","ReasonId","ReasonName","StartTime","EndTime","PersonnelId","TaskId","IsAuto","CreatedAt")
   SELECT gen_random_uuid(), p_imm, 0, r."Id", r."Name", a, b, p_adj, p_task, true, a
   FROM "DowntimeReasons" r
   WHERE r."Id" = ('d2000000-0000-0000-0000-00000000000' || p_reason)::uuid AND b > a $$;

-- Циклы подряд в [a, b); возвращает новый счётчик номеров циклов
CREATE FUNCTION pg_temp.cyc(p_imm uuid, p_task uuid, p_mold uuid, p_cav int, p_ct int,
                            a timestamptz, b timestamptz, p_no int) RETURNS int LANGUAGE plpgsql AS
$$
DECLARE n int := floor(extract(epoch FROM (b - a)) / p_ct)::int;
BEGIN
  IF n <= 0 THEN RETURN p_no; END IF;
  INSERT INTO "ImmCycles" ("Id","ImmId","TaskId","MoldId","StartTime","EndTime","DurationSeconds","IsSuccessful",
                           "Cavities","InjectionDurationMs","InjectionStartTime","Cushion","CycleNumber","CreatedAt")
  SELECT gen_random_uuid(), p_imm, p_task, p_mold,
         a + (g * p_ct) * interval '1 second',
         a + (g * p_ct + d) * interval '1 second',
         d, random() > 0.005, p_cav,
         1500 + (random() * 1500)::int,
         a + (g * p_ct + 1) * interval '1 second',
         round((3 + random() * 3)::numeric, 2),
         p_no + g + 1,
         a + (g * p_ct + d) * interval '1 second'
  FROM (SELECT g, p_ct - (random() * 2)::int AS d FROM generate_series(0, n - 1) g) s;
  RETURN p_no + n;
END $$;

-- Окна без связи (status = 'Offline') и без данных (status = NULL): в окно задания не заходят
CREATE TEMP TABLE _win (imm int, a timestamptz, b timestamptz, status text);
INSERT INTO _win VALUES
  (1, pg_temp.lt('2026-08-05 10:00'), pg_temp.lt('2026-08-05 16:00'), 'Offline'),
  (2, pg_temp.lt('2026-07-14 00:00'), pg_temp.lt('2026-07-17 00:00'), NULL),
  (3, pg_temp.lt('2026-06-18 00:00'), pg_temp.lt('2026-06-19 00:00'), 'Offline'),
  (3, pg_temp.lt('2026-07-22 08:00'), pg_temp.lt('2026-07-23 20:00'), 'Offline'),
  (3, pg_temp.lt('2026-09-10 12:00'), pg_temp.lt('2026-10-01 00:00'), 'Offline');

-- ============================================================
-- Генерация таймлайна
-- ============================================================
DO $$
DECLARE
  p_start timestamptz := pg_temp.lt('2026-06-01 00:00');
  p_end   timestamptz := pg_temp.lt('2026-10-01 00:00');

  imm_ids uuid[] := ARRAY['b2000000-0000-0000-0000-000000000001','b2000000-0000-0000-0000-000000000002',
                          'b2000000-0000-0000-0000-000000000003']::uuid[];
  mold_from int[] := ARRAY[1, 5, 8];
  mold_to   int[] := ARRAY[4, 7, 10];
  mold_cav  int[] := ARRAY[8, 12, 2, 4, 1, 1, 1, 32, 24, 48];
  mold_ct   int[] := ARRAY[18, 22, 45, 30, 38, 32, 55, 20, 16, 15];
  -- Надёжность: доля коротких остановок (без простоя) и порог «авария» (остальное — долгий простой)
  p_short   float[] := ARRAY[0.80, 0.55, 0.70];
  p_alarm   float[] := ARRAY[0.97, 0.90, 0.95];
  adj_ids text[] := ARRAY['f2000000-0000-0000-0000-000000000002','f2000000-0000-0000-0000-000000000003'];

  i int; k int; cno int; r float;
  v_imm uuid; v_mold uuid; v_task uuid; v_adj text; last_k int;
  cur timestamptz; lim timestamptz; g_end timestamptz; u_a timestamptz; u_b timestamptz;
  s_end timestamptz; r_end timestamptz; t timestamptz; b_end timestamptz; x_end timestamptz;
  w_a timestamptz; w_b timestamptz; w_st text;
  loc timestamp; nk date; last_night date;
BEGIN
  PERFORM setseed(0.42);

  FOR i IN 1..3 LOOP
    v_imm := imm_ids[i]; cur := p_start; cno := 100000 * i; last_k := NULL; last_night := NULL;

    WHILE cur < p_end LOOP
      -- Ближайшее окно без связи/данных
      SELECT a, b, status INTO w_a, w_b, w_st FROM _win WHERE imm = i AND b > cur ORDER BY a LIMIT 1;
      IF NOT FOUND THEN w_a := 'infinity'; END IF;
      IF w_a <= cur THEN
        IF w_st IS NOT NULL THEN PERFORM pg_temp.st(v_imm, w_st, cur, LEAST(w_b, p_end)); END IF;
        cur := w_b;
        CONTINUE;
      END IF;
      lim := LEAST(w_a, p_end);

      -- Пауза между заданиями: «Без задания», иногда — «Работа без задания»
      g_end := LEAST(cur + pg_temp.rmin(30, 360), lim);
      IF last_k IS NOT NULL AND random() < 0.2 THEN
        u_a := cur + (g_end - cur) * 0.3;
        u_b := LEAST(u_a + pg_temp.rmin(20, 90), g_end);
        PERFORM pg_temp.st(v_imm, 'Idle', cur, u_a);
        PERFORM pg_temp.st(v_imm, 'Auto', u_a, u_b);
        cno := pg_temp.cyc(v_imm, NULL, ('c2000000-0000-0000-0000-0000000001' || lpad(last_k::text, 2, '0'))::uuid,
                           mold_cav[last_k], mold_ct[last_k], u_a, u_b, cno);
        PERFORM pg_temp.st(v_imm, 'Idle', u_b, g_end);
      ELSE
        PERFORM pg_temp.st(v_imm, CASE WHEN random() < 0.15 THEN 'Manual' ELSE 'Idle' END, cur, g_end);
      END IF;
      cur := g_end;

      IF lim - cur < interval '3 hours' THEN
        PERFORM pg_temp.st(v_imm, 'Idle', cur, lim);
        cur := lim;
        CONTINUE;
      END IF;

      -- Задание: наладка 1–3 ч, работа 16–96 ч
      k      := mold_from[i] + floor(random() * (mold_to[i] - mold_from[i] + 1))::int;
      v_mold := ('c2000000-0000-0000-0000-0000000001' || lpad(k::text, 2, '0'))::uuid;
      v_adj  := adj_ids[1 + floor(random() * 2)::int];
      v_task := gen_random_uuid();
      s_end  := cur + pg_temp.rmin(60, 180);
      r_end  := LEAST(s_end + pg_temp.rmin(16 * 60, 96 * 60), lim);

      INSERT INTO "ShiftTasks" ("Id","ImmId","MoldId","PersonnelId","PlanQuantity","ActualQuantity","ActualMaterialWeightGrams",
                                "Status","WorkMode","PlannedFullCycleSeconds","PlannedInjectionCycleSeconds","DefectQuantity",
                                "PlannedDate","IssuedAt","SetupStartedAt","MoldVerifiedAt","StartedAt","CompletedAt","ClosedAt",
                                "CreatedAt","UpdatedAt")
      VALUES (v_task, v_imm, v_mold, v_adj, 0, 0, 0,
              CASE WHEN r_end < pg_temp.lt('2026-09-25 00:00') THEN 4 ELSE 3 END, 0,
              mold_ct[k], GREATEST(mold_ct[k] / 3, 1), 0,
              date_trunc('day', cur), cur - interval '1 hour', cur, cur + interval '20 minutes', s_end, r_end,
              CASE WHEN r_end < pg_temp.lt('2026-09-25 00:00') THEN r_end + interval '2 hours' END,
              cur - interval '1 day', r_end);

      PERFORM pg_temp.st(v_imm, 'Manual', cur, s_end);
      t := s_end;

      WHILE t < r_end LOOP
        -- ТПА-2: в часть ночей оператора нет до 08:00
        IF i = 2 THEN
          loc := t AT TIME ZONE 'Europe/Moscow';
          IF extract(hour FROM loc) >= 20 OR extract(hour FROM loc) < 8 THEN
            nk := (loc - interval '8 hours')::date;
            IF nk IS DISTINCT FROM last_night THEN
              last_night := nk;
              IF random() < 0.4 THEN
                x_end := LEAST(((nk + 1)::timestamp + interval '8 hours') AT TIME ZONE 'Europe/Moscow', r_end);
                PERFORM pg_temp.st(v_imm, 'Idle', t, x_end);
                PERFORM pg_temp.ev(v_imm, v_task, v_adj, 4, t, x_end);
                t := x_end;
                CONTINUE;
              END IF;
            END IF;
          END IF;
        END IF;

        b_end := LEAST(t + pg_temp.rmin(40, 240), r_end);
        PERFORM pg_temp.st(v_imm, 'Auto', t, b_end);
        cno := pg_temp.cyc(v_imm, v_task, v_mold, mold_cav[k], mold_ct[k], t, b_end, cno);
        t := b_end;
        EXIT WHEN t >= r_end;

        r := random();
        IF r < p_short[i] THEN             -- короткая остановка, простой не фиксируется
          x_end := LEAST(t + pg_temp.rmin(2, 10), r_end);
          PERFORM pg_temp.st(v_imm, 'Idle', t, x_end);
        ELSIF r < p_alarm[i] THEN          -- авария 15–90 мин
          x_end := LEAST(t + pg_temp.rmin(15, 90), r_end);
          PERFORM pg_temp.st(v_imm, 'Alarm', t, x_end);
          PERFORM pg_temp.ev(v_imm, v_task, v_adj, CASE WHEN random() < 0.5 THEN 1 ELSE 2 END, t, x_end);
        ELSE                               -- долгий простой 2–5 ч
          x_end := LEAST(t + pg_temp.rmin(120, 300), r_end);
          PERFORM pg_temp.st(v_imm, 'Idle', t, x_end);
          PERFORM pg_temp.ev(v_imm, v_task, v_adj, CASE WHEN random() < 0.6 THEN 3 ELSE 5 END, t, x_end);
        END IF;
        t := x_end;
      END LOOP;

      last_k := k;
      cur := r_end;
    END LOOP;
  END LOOP;
END $$;

-- Выпуск заданий — из фактических циклов
UPDATE "ShiftTasks" t
SET "ActualQuantity"            = q.qty,
    "DefectQuantity"            = (q.qty * random() * 0.02)::int,
    "PlanQuantity"              = GREATEST(ceil(q.qty * (0.95 + random() * 0.15) / 100) * 100, 100)::int,
    "ActualMaterialWeightGrams" = round(q.qty * m."PartWeightGrams" + q.n * m."RunnerWeightGrams", 2)
FROM (SELECT "TaskId", count(*) AS n, sum("Cavities") FILTER (WHERE "IsSuccessful") AS qty
      FROM "ImmCycles" WHERE "TaskId" IS NOT NULL GROUP BY "TaskId") q,
     "Molds" m
WHERE q."TaskId" = t."Id" AND m."Id" = t."MoldId";

ANALYZE;

SELECT i."Name",
       (SELECT count(*) FROM "ShiftTasks" t WHERE t."ImmId" = i."Id")       AS tasks,
       (SELECT count(*) FROM "ImmCycles" c WHERE c."ImmId" = i."Id")        AS cycles,
       (SELECT count(*) FROM "ImmStatusHistory" h WHERE h."ImmId" = i."Id") AS statuses,
       (SELECT count(*) FROM "Events" e WHERE e."ImmId" = i."Id")           AS downtimes
FROM "Imms" i ORDER BY i."Name";
