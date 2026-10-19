-- ============================================================
-- Wintime Control — данные публичного демо (control-demo.ru)
-- Период   : последние _cfg.days суток до t0 = момента загрузки. Ничего позже t0.
-- Состав   : 2 шаблона, 12 ТПА (ТПА-12 в архиве), 15 ПФ, 10 типов изделий (тип 10 в архиве),
--            6 сотрудников сида (без пароля — войти нельзя), заказы, задания, циклы, простои, телеметрия.
-- Логины   : только демо-аккаунты API (admin / manager / adjuster / emulator). Сид их не создаёт;
--            демо-adjuster получает задания (ищется по UserName).
-- Запуск   : свежая БД после первого старта API (миграции + демо-аккаунты), API остановлен — reset-db.sh:
--   docker exec -i wtctrl-demo-postgres psql -U postgres -d WintimeControlDb -v ON_ERROR_STOP=1 -q < seed_demo.sql
-- Параметры: _cfg ниже — days (история, сутки), ppc (точек телеметрии на датчик на цикл, 1..6).
--            Объём при 30/6: ~0,68 млн циклов, ~20 млн строк телеметрии, БД ~4,5 ГБ;
--            загрузка ~12 мин на ПК разработчика, на VPS с 1 ядром ~45–60 мин.
-- ============================================================
SET synchronous_commit = off;

CREATE TEMP TABLE _cfg AS
SELECT t0,
       ((date_trunc('day', t0 AT TIME ZONE 'Europe/Moscow') - days * interval '1 day') AT TIME ZONE 'Europe/Moscow') AS p_start,
       days, ppc
FROM (SELECT date_trunc('minute', now()) AS t0, 30 AS days, 6 AS ppc) s;

DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM "Imms") OR EXISTS (SELECT 1 FROM "ShiftTasks") THEN
    RAISE EXCEPTION 'БД не пустая — сид грузится только в свежую БД (reset-db.sh)';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM "Users" WHERE "UserName" = 'adjuster') THEN
    RAISE EXCEPTION 'Нет демо-аккаунта adjuster — сначала запустите API с DemoMode=true';
  END IF;
END $$;

-- ============================================================
-- Справочники
-- ============================================================
INSERT INTO "Templates" ("Id","Name","Manufacturer","Model","Version","Author","JsonConfig","IsActive","CreatedAt","UpdatedAt")
SELECT v.id::uuid, v.name, v.maker, v.model, '1.0', 'Wintime',
       '{"device_timeout_seconds":30,"sensors":['
       || '{"name":"Температура расплава, °C","field":"MeltTemp","type":"float","threshold":0.5},'
       || '{"name":"Давление впрыска, бар","field":"InjectionPressure","type":"float","threshold":5},'
       || '{"name":"Положение шнека, мм","field":"ScrewPosition","type":"float","threshold":0.5},'
       || '{"name":"Температура формы, °C","field":"MoldTemp","type":"float","threshold":0.2},'
       || '{"name":"Усилие смыкания, кН","field":"ClampForce","type":"float","threshold":10}]}',
       true, c.t0 - interval '90 days', c.t0 - interval '90 days'
FROM _cfg c,
     (VALUES ('a3000000-0000-0000-0000-000000000001','Haitian Mars III','Haitian','MA III'),
             ('a3000000-0000-0000-0000-000000000002','KraussMaffei CX','KraussMaffei','CX')) v(id, name, maker, model);

CREATE TEMP TABLE _imm AS
SELECT n, ('b3000000-0000-0000-0000-0000000000' || lpad(n::text, 2, '0'))::uuid AS id,
       name, tpl, kind, cur_mold, p_short, p_alarm
FROM (VALUES
  (1, 'ТПА-01 Haitian MA1600',      1, 'run',   2,    0.80, 0.97),
  (2, 'ТПА-02 Haitian MA1600',      1, 'run',   4,    0.55, 0.88),
  (3, 'ТПА-03 Haitian MA2500',      1, 'run',   5,    0.75, 0.95),
  (4, 'ТПА-04 Haitian MA2500',      1, 'run',   7,    0.75, 0.95),
  (5, 'ТПА-05 Haitian MA3600',      1, 'setup', 8,    0.75, 0.95),
  (6, 'ТПА-06 Haitian MA1200',      1, 'free',  NULL, 0.80, 0.96),
  (7, 'ТПА-07 Haitian MA1200',      1, 'free',  NULL, 0.75, 0.95),
  (8, 'ТПА-08 Haitian MA900',       1, 'free',  NULL, 0.70, 0.93),
  (9, 'ТПА-09 KraussMaffei CX 160', 2, 'free',  NULL, 0.60, 0.90),
  (10,'ТПА-10 KraussMaffei CX 200', 2, 'free',  NULL, 0.80, 0.97),
  (11,'ТПА-11 KraussMaffei CX 300', 2, 'free',  NULL, 0.75, 0.95),
  (12,'ТПА-12 KraussMaffei CX 80',  2, 'arch',  NULL, 0.75, 0.95)) v(n, name, tpl, kind, cur_mold, p_short, p_alarm);

INSERT INTO "Imms" ("Id","Name","InventoryNumber","TemplateId","IsActive","CommissioningDate","CreatedAt")
SELECT i.id, i.name, 'ИН-' || lpad(i.n::text, 3, '0'),
       ('a3000000-0000-0000-0000-00000000000' || i.tpl)::uuid, i.kind <> 'arch',
       timestamptz '2018-03-01 00:00+00' + i.n * interval '150 days', c.t0 - interval '90 days'
FROM _imm i, _cfg c;

INSERT INTO "ProductTypes" ("Id","Article","Name","IsActive","CreatedAt")
SELECT ('e3000000-0000-0000-0000-0000000000' || lpad(v.n::text, 2, '0'))::uuid, v.art, v.name, v.n <> 10,
       c.t0 - interval '90 days'
FROM _cfg c,
     (VALUES (1,'КР28','Крышка флип-топ 28 мм'),(2,'КП38','Колпачок 38 мм'),(3,'КФ01','Корпус фильтра'),
             (4,'РВ05','Ручка ведра'),(5,'ВД05','Ведро 5 л'),(6,'КВ05','Крышка ведра 5 л'),
             (7,'ЯО01','Ящик овощной'),(8,'ВТ20','Втулка d20'),(9,'ЗГ20','Заглушка 20×20'),
             (10,'ШБ08','Шайба 8 мм')) v(n, art, name);

-- У каждого типа есть изделие «0» (по умолчанию); у части — варианты. Полный артикул ТИП.ЧАСТЬ (ADR-0020).
INSERT INTO "Products" ("Id","ProductTypeId","Article","Name","FullArticle","IsActive","CreatedAt")
SELECT gen_random_uuid(), pt."Id", v.part, v.name, pt."Article" || '.' || v.part, pt."IsActive", pt."CreatedAt"
FROM "ProductTypes" pt
JOIN (VALUES (1,'0','По умолчанию'),(1,'Б','Белая'),(1,'Ч','Чёрная'),
             (2,'0','По умолчанию'),(2,'К','Красный'),
             (3,'0','По умолчанию'),
             (4,'0','По умолчанию'),(4,'С','Серая'),
             (5,'0','По умолчанию'),(6,'0','По умолчанию'),(7,'0','По умолчанию'),
             (8,'0','По умолчанию'),(8,'Н','Натуральная'),
             (9,'0','По умолчанию'),(10,'0','По умолчанию')) v(n, part, name)
  ON pt."Id" = ('e3000000-0000-0000-0000-0000000000' || lpad(v.n::text, 2, '0'))::uuid;

-- ct — длительность цикла, с; inj — время литья, с; pw/rw — вес детали/литника, г
CREATE TEMP TABLE _mold AS
SELECT n, ('c3000000-0000-0000-0000-0000000000' || lpad(n::text, 2, '0'))::uuid AS id,
       ('e3000000-0000-0000-0000-0000000000' || lpad(t::text, 2, '0'))::uuid AS type_id,
       name, cav, ct, inj, pw, rw
FROM (VALUES
  (1, 1, 'Крышка флип-топ 28 мм',         8, 26,  4,   3.2,  4.0),
  (2, 1, 'Крышка флип-топ 28 мм (16 гн.)',16, 30,  4,   3.2,  6.0),
  (3, 2, 'Колпачок 38 мм',               12, 28,  4,   4.1,  6.0),
  (4, 3, 'Корпус фильтра',                2, 48,  8,  38.0,  9.0),
  (5, 4, 'Ручка ведра',                   4, 34,  5,   6.5,  5.0),
  (6, 5, 'Ведро 5 л',                     1, 55,  9, 210.0, 12.0),
  (7, 6, 'Крышка ведра 5 л',              1, 42,  7,  95.0,  8.0),
  (8, 7, 'Ящик овощной',                  1, 70, 12, 820.0, 25.0),
  (9, 8, 'Втулка d20',                   32, 25,  3,   1.9,  7.0),
  (10,9, 'Заглушка 20×20',               24, 24,  3,   1.5,  5.0),
  (11,2, 'Колпачок 38 мм (8 гн.)',        8, 27,  4,   4.1,  5.0),
  (12,3, 'Корпус фильтра (4 гн.)',        4, 52,  8,  38.0, 14.0),
  (13,8, 'Втулка d20 (16 гн.)',          16, 24,  3,   1.9,  5.0),
  (14,9, 'Заглушка 20×20 (32 гн.)',      32, 26,  3,   1.5,  6.0),
  (15,10,'Шайба 8 мм',                   48, 22,  3,   0.4,  3.0)) v(n, t, name, cav, ct, inj, pw, rw);

INSERT INTO "Molds" ("Id","FormId","Name","Cavities","PartWeightGrams","RunnerWeightGrams","MaxResourceCycles",
                     "To1Cycles","To2Cycles","IsActive","MoldStatus","ProductTypeId","CreatedAt")
SELECT m.id, 'ПФ-' || (200 + m.n), m.name, m.cav, m.pw, m.rw, 1000000, 300000, 600000, m.n <> 15, 0, m.type_id,
       c.t0 - interval '90 days'
FROM _mold m, _cfg c;

-- Какие ПФ ставятся на какой ТПА (по ним идёт история)
CREATE TEMP TABLE _pool (imm int, mold int);
INSERT INTO _pool VALUES (1,1),(1,2),(2,3),(2,4),(3,5),(4,6),(4,7),(5,8),(6,9),(6,1),(7,10),(8,11),(8,3),
                         (9,12),(10,13),(10,6),(11,14),(12,15),(12,12);

-- Совместимость (ADR-0018): у ПФ 1–7 список = ТПА их пулов, у остальных пусто (любой ТПА)
INSERT INTO "MoldCompatibleImms" ("MoldId","ImmId")
SELECT DISTINCT m.id, i.id FROM _pool p JOIN _mold m ON m.n = p.mold JOIN _imm i ON i.n = p.imm WHERE p.mold <= 7;

INSERT INTO "DowntimeReasons" ("Id","Name","Type","IsActive","CreatedAt")
SELECT v.id::uuid, v.name, v.type, true, c.t0 - interval '90 days'
FROM _cfg c,
     (VALUES ('d3000000-0000-0000-0000-000000000001','Неисправность оборудования',   'Emergency'),
             ('d3000000-0000-0000-0000-000000000002','Неисправность пресс-формы',    'Emergency'),
             ('d3000000-0000-0000-0000-000000000003','Ожидание сырья',               'Planned'),
             ('d3000000-0000-0000-0000-000000000004','Отсутствие оператора',         'Planned'),
             ('d3000000-0000-0000-0000-000000000005','Подналадка технологии',        'Planned'),
             ('d3000000-0000-0000-0000-000000000006','Смена материала / цвета',      'Planned')) v(id, name, type);

-- Две 12-часовые смены 08:00–20:00 и 20:00–08:00 по Москве
INSERT INTO "Shifts" ("Id","StartMinutes","DurationMinutes","BreakStartMinutes","BreakDurationMinutes","TimeZoneId","CreatedAt")
SELECT gen_random_uuid(), v.s, 720, v.b, 30, 'Europe/Moscow', c.t0 - interval '90 days'
FROM _cfg c, (VALUES (480, 720), (1200, 0)) v(s, b);

UPDATE "PlanningDefaults" SET "SetupMinutes" = 90, "ReleaseBufferFixedMinutes" = 30, "ReleaseBufferPercent" = 10,
                              "UpdatedAt" = (SELECT t0 FROM _cfg)
WHERE "Id" = 1;

-- Сотрудники сида: без пароля (PasswordHash = NULL) — войти под ними нельзя, нужны для отчётов
INSERT INTO "Users" ("Id","FullName","Role","EmployeeId","IsActive","UserName","NormalizedUserName","Email","NormalizedEmail",
                     "EmailConfirmed","PasswordHash","SecurityStamp","ConcurrencyStamp",
                     "PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount")
SELECT v.id, v.fio, v.role, v.emp, true, v.login, upper(v.login),
       v.login || '@control-demo.local', upper(v.login || '@control-demo.local'), true,
       NULL, gen_random_uuid()::text, gen_random_uuid()::text, false, false, true, 0
FROM (VALUES ('f3000000-0000-0000-0000-000000000001','Ковалёв Сергей Петрович',  1,'М-001','kovalev'),
             ('f3000000-0000-0000-0000-000000000002','Сидоров Олег Иванович',    2,'Н-002','sidorov'),
             ('f3000000-0000-0000-0000-000000000003','Гусев Павел Андреевич',    2,'Н-003','gusev'),
             ('f3000000-0000-0000-0000-000000000004','Морозов Илья Викторович',  2,'Н-004','morozov'),
             ('f3000000-0000-0000-0000-000000000005','Лебедев Артём Сергеевич',  2,'Н-005','lebedev'),
             ('f3000000-0000-0000-0000-000000000006','Орлов Денис Николаевич',   2,'Н-006','orlov')) v(id, fio, role, emp, login);

-- Окна без связи (часы от p_start, длительность, ч)
CREATE TEMP TABLE _win AS
SELECT v.imm, c.p_start + v.h * interval '1 hour' AS a, c.p_start + (v.h + v.dur) * interval '1 hour' AS b,
       'Offline'::text AS status
FROM _cfg c, (VALUES (3, 12 * 24 + 10, 4), (7, 20 * 24, 20), (9, 5 * 24 + 14, 3)) v(imm, h, dur);

-- Очередь заданий «на сейчас»: who = 'demo' — демо-adjuster; ago — плановое начало, мин до t0
CREATE TEMP TABLE _queue (imm int, mold int, who text, status int, ago int, shots int);
INSERT INTO _queue VALUES
  (6, 9, 'demo', 1, 300,  800), (6, 1, 'demo', 1, 240, 1200), (7, 10, 'demo', 1, 200,  900),
  (8, 11,'demo', 1, 150, 1500), (9, 12,'demo', 1, 120,  600), (10, 13,'demo', 1,  90, 1000),
  (11,14,'demo', 1,  45,  700),
  (1, 2, 'f3000000-0000-0000-0000-000000000002', 0, 30, 1500),
  (2, 3, 'f3000000-0000-0000-0000-000000000003', 1, 60, 1000),
  (3, 5, 'f3000000-0000-0000-0000-000000000004', 0, 20,  800);

-- ============================================================
-- Вспомогательные функции (живут только в этой сессии)
-- ============================================================
CREATE FUNCTION pg_temp.rmin(lo int, hi int) RETURNS interval LANGUAGE sql VOLATILE AS
$$ SELECT ((lo + random() * (hi - lo))::int) * interval '1 minute' $$;

-- Запись статуса; b = NULL — открытая (текущая) запись
-- Если последняя запись ТПА с тем же статусом заканчивается в a — продлевает её, а не добавляет подряд идущую такую же
CREATE FUNCTION pg_temp.st(p_imm uuid, p_status text, a timestamptz, b timestamptz) RETURNS void LANGUAGE plpgsql AS
$$
DECLARE v_id bigint;
BEGIN
  IF b IS NOT NULL AND b <= a THEN RETURN; END IF;
  SELECT "Id" INTO v_id FROM "ImmStatusHistory"
   WHERE "ImmId" = p_imm AND "ChangedAt" = (SELECT max("ChangedAt") FROM "ImmStatusHistory" WHERE "ImmId" = p_imm)
     AND "Status" = p_status AND "EndedAt" = a;
  IF FOUND THEN
    UPDATE "ImmStatusHistory" SET "EndedAt" = b WHERE "Id" = v_id;
  ELSE
    INSERT INTO "ImmStatusHistory" ("ImmId","Status","ChangedAt","EndedAt") VALUES (p_imm, p_status, a, b);
  END IF;
END $$;

-- Простой; p_reason = NULL — неразмеченный (причину укажет наладчик)
CREATE FUNCTION pg_temp.ev(p_imm uuid, p_task uuid, p_adj text, p_reason int, a timestamptz, b timestamptz)
RETURNS void LANGUAGE sql AS
$$ INSERT INTO "Events" ("Id","ImmId","EventType","ReasonId","ReasonName","StartTime","EndTime","PersonnelId","TaskId","IsAuto","CreatedAt")
   SELECT gen_random_uuid(), p_imm, 0, r."Id", r."Name", a, b, CASE WHEN p_reason IS NULL THEN NULL ELSE p_adj END, p_task, true, a
   FROM (SELECT 1) one
   LEFT JOIN "DowntimeReasons" r ON r."Id" = ('d3000000-0000-0000-0000-00000000000' || p_reason)::uuid
   WHERE b > a $$;

-- Циклы подряд с a, период p_ct + p_gap с, не больше p_max и не дальше b. Возвращает число циклов.
-- PauseDurationMs — от конца предыдущего цикла серии (у первого — NULL, ADR-0017).
CREATE FUNCTION pg_temp.cyc(p_imm uuid, p_task uuid, p_mold uuid, p_cav int, p_ct int, p_inj int, p_gap int,
                            a timestamptz, b timestamptz, p_no int, p_max int) RETURNS int LANGUAGE plpgsql AS
$$
DECLARE n int := LEAST(floor(extract(epoch FROM (b - a)) / (p_ct + p_gap))::int, p_max);
BEGIN
  IF n <= 0 THEN RETURN 0; END IF;
  INSERT INTO "ImmCycles" ("Id","ImmId","TaskId","MoldId","StartTime","EndTime","DurationSeconds","IsSuccessful",
                           "Cavities","InjectionDurationMs","InjectionStartTime","PauseDurationMs","Cushion",
                           "CycleNumber","CreatedAt")
  SELECT gen_random_uuid(), p_imm, p_task, p_mold,
         s.st, s.st + s.d * interval '1 second', s.d, random() > 0.005, p_cav,
         p_inj * 1000 - 200 + (random() * 400)::int,
         s.st + interval '1 second',
         CASE WHEN s.g = 0 THEN NULL ELSE ((p_ct + p_gap - s.prev_d) * 1000)::int END,
         round((3 + random() * 3)::numeric, 2),
         p_no + s.g + 1,
         s.st + s.d * interval '1 second'
  FROM (SELECT g, st, d, lag(d) OVER (ORDER BY g) AS prev_d
        FROM (SELECT g, a + g * (p_ct + p_gap) * interval '1 second' AS st,
                     p_ct - (random() < 0.3)::int AS d
              FROM generate_series(0, n - 1) g) x) s;
  RETURN n;
END $$;

-- Задание. Изделие — случайное из типа ПФ; плановое начало = p_planned (дата + время по часам завода);
-- норматив наладки/буфер — снапшот PlanningDefaults (90 / 30 / 10).
CREATE FUNCTION pg_temp.task(p_imm uuid, p_mold int, p_adj text, p_plan int, p_status int, p_mode int,
                             p_planned timestamptz, p_issued timestamptz, p_setup timestamptz, p_started timestamptz,
                             p_completed timestamptz, p_closed timestamptz) RETURNS uuid LANGUAGE plpgsql AS
$$
DECLARE
  v_id      uuid := gen_random_uuid();
  m         record;
  v_prod    uuid;
  loc       timestamp := p_planned AT TIME ZONE 'Europe/Moscow';
  v_created timestamptz := COALESCE(p_issued, p_planned) - interval '1 day';
BEGIN
  SELECT * INTO m FROM _mold WHERE n = p_mold;
  SELECT "Id" INTO v_prod FROM "Products" WHERE "ProductTypeId" = m.type_id ORDER BY random() LIMIT 1;
  INSERT INTO "ShiftTasks" ("Id","ImmId","MoldId","ProductId","PersonnelId","PlanQuantity","ActualQuantity",
                            "ActualMaterialWeightGrams","Status","WorkMode","PlannedFullCycleSeconds",
                            "PlannedInjectionCycleSeconds","DefectQuantity","PlannedDate","PlannedSetupStartTime",
                            "PlannedSetupMinutes","ReleaseBufferFixedMinutes","ReleaseBufferPercent",
                            "IssuedAt","SetupStartedAt","MoldVerifiedAt","StartedAt","CompletedAt","ClosedAt",
                            "CreatedAt","UpdatedAt")
  VALUES (v_id, p_imm, m.id, v_prod, p_adj, p_plan, 0, 0, p_status, p_mode, m.ct, m.inj, 0,
          loc::date, loc::time, 90, 30, 10,
          p_issued, p_setup,
          CASE WHEN p_setup IS NOT NULL AND random() < 0.8 THEN p_setup + interval '10 minutes' END,
          p_started, p_completed, p_closed, v_created,
          COALESCE(p_closed, p_completed, p_started, p_setup, p_issued, v_created));
  RETURN v_id;
END $$;

-- ============================================================
-- Генерация истории и среза «сейчас»
-- ============================================================
DO $$
DECLARE
  cfg record; im record; m record;
  v_adjuster text; adj text[]; v_adj text;
  n int; cno int; need int; done int; v_plan int; v_shots int; v_gap int; v_mode int; v_period int;
  r float; avail_s float;
  cur timestamptz; lim timestamptz; hist_end timestamptz; g_end timestamptz; u_a timestamptz; u_b timestamptz;
  s_beg timestamptz; s_end timestamptz; t timestamptz; b_end timestamptz; c_end timestamptz; x_end timestamptz;
  v_planned timestamptz;
  w_a timestamptz; w_b timestamptz; w_st text;
  v_task uuid; v_mold int; last_mold int; v_dt_done boolean;
BEGIN
  PERFORM setseed(0.19);
  SELECT * INTO cfg FROM _cfg;
  SELECT "Id" INTO v_adjuster FROM "Users" WHERE "UserName" = 'adjuster';
  adj := ARRAY(SELECT "Id" FROM "Users" WHERE "Id" LIKE 'f3000000%' AND "Role" = 2 ORDER BY "Id") || v_adjuster;

  FOR im IN SELECT * FROM _imm ORDER BY n LOOP
    cur := cfg.p_start; cno := 0; last_mold := NULL;
    hist_end := CASE im.kind
      WHEN 'run'   THEN cfg.t0 - pg_temp.rmin(180, 1200)
      WHEN 'setup' THEN cfg.t0 - interval '40 minutes'
      WHEN 'free'  THEN cfg.t0
      ELSE              cfg.p_start + interval '18 days' END;

    WHILE cur < hist_end LOOP
      -- Ближайшее окно без связи
      SELECT a, b, status INTO w_a, w_b, w_st FROM _win WHERE imm = im.n AND b > cur ORDER BY a LIMIT 1;
      IF NOT FOUND THEN w_a := 'infinity'; END IF;
      IF w_a <= cur THEN
        PERFORM pg_temp.st(im.id, w_st, cur, LEAST(w_b, hist_end));
        cur := LEAST(w_b, hist_end);
        CONTINUE;
      END IF;
      lim := LEAST(w_a, hist_end);

      -- Пауза между заданиями: «Без задания», иногда «Работа без задания»
      g_end := LEAST(cur + pg_temp.rmin(30, 300), lim);
      n := 0;
      IF last_mold IS NOT NULL AND random() < 0.2 THEN
        SELECT * INTO m FROM _mold WHERE _mold.n = last_mold;
        u_a := cur + (g_end - cur) * 0.3;
        -- ПФ не может работать на двух ТПА одновременно
        IF NOT EXISTS (SELECT 1 FROM "ShiftTasks" x
                        WHERE x."MoldId" = m.id AND x."ImmId" <> im.id
                          AND x."SetupStartedAt" < u_a + interval '90 minutes'
                          AND COALESCE(x."CompletedAt", 'infinity') > u_a) THEN
          n := pg_temp.cyc(im.id, NULL, m.id, m.cav, m.ct, m.inj, 1, u_a, LEAST(u_a + pg_temp.rmin(20, 90), g_end), cno, 1000000);
        END IF;
      END IF;
      IF n > 0 THEN
        u_b := u_a + n * (m.ct + 1) * interval '1 second';
        PERFORM pg_temp.st(im.id, 'Idle', cur, u_a);
        PERFORM pg_temp.st(im.id, 'Auto', u_a, u_b);
        INSERT INTO "UnplannedRuns" ("Id","ImmId","StartTime","ClosedAt","CreatedAt")
        VALUES (gen_random_uuid(), im.id, u_a, u_b, u_a);
        PERFORM pg_temp.st(im.id, 'Idle', u_b, g_end);
        cno := cno + n;
      ELSE
        PERFORM pg_temp.st(im.id, 'Idle', cur, g_end);
      END IF;
      cur := g_end;

      IF lim - cur < interval '4 hours' THEN
        PERFORM pg_temp.st(im.id, 'Idle', cur, lim);
        cur := lim;
        CONTINUE;
      END IF;

      -- Задание: план считается из доступного времени, ТПА остановится по плану
      -- ПФ из пула, которая не занята на другом ТПА в ближайшие 6 часов
      SELECT p.mold INTO v_mold FROM _pool p JOIN _mold pm ON pm.n = p.mold
       WHERE p.imm = im.n
         AND NOT EXISTS (SELECT 1 FROM "ShiftTasks" x
                          WHERE x."MoldId" = pm.id AND x."ImmId" <> im.id
                            AND x."SetupStartedAt" < cur + interval '6 hours'
                            AND COALESCE(x."CompletedAt", 'infinity') > cur)
       ORDER BY random() LIMIT 1;
      IF v_mold IS NULL THEN
        PERFORM pg_temp.st(im.id, 'Idle', cur, LEAST(cur + interval '2 hours', lim));
        cur := LEAST(cur + interval '2 hours', lim);
        CONTINUE;
      END IF;
      SELECT * INTO m FROM _mold WHERE _mold.n = v_mold;
      -- задание должно закончиться раньше, чем эта ПФ понадобится на другом ТПА
      lim := LEAST(lim, (SELECT min(x."SetupStartedAt") FROM "ShiftTasks" x
                          WHERE x."MoldId" = m.id AND x."ImmId" <> im.id AND x."SetupStartedAt" >= cur) - interval '1 hour');
      v_mode   := CASE WHEN random() < 0.2 THEN 1 ELSE 0 END;          -- 20% — полуавтомат
      v_gap    := CASE WHEN v_mode = 1 THEN 6 ELSE 1 END;              -- пауза между циклами, с
      v_period := m.ct + v_gap;
      v_adj    := adj[1 + floor(random() * array_length(adj, 1))::int];
      s_beg    := cur;
      s_end    := s_beg + pg_temp.rmin(60, 150);
      avail_s  := extract(epoch FROM (lim - s_end)) - 3600;            -- запас на ожидание наладчика
      v_shots  := floor(LEAST((8 + random() * 52) * 3600, avail_s) * 0.75 / v_period)::int;
      v_plan   := (floor(v_shots * m.cav / 100.0) * 100)::int;
      IF v_plan < 100 THEN
        PERFORM pg_temp.st(im.id, 'Idle', cur, lim);
        cur := lim;
        CONTINUE;
      END IF;
      need := ceil(v_plan::numeric / m.cav)::int;                     -- смыканий до остановки по плану

      v_planned := date_bin('15 minutes', s_beg - pg_temp.rmin(0, 90), cfg.p_start);
      v_task := pg_temp.task(im.id, v_mold, v_adj, v_plan, 3, v_mode, v_planned,
                             v_planned - pg_temp.rmin(60, 720), s_beg, s_end, NULL, NULL);
      PERFORM pg_temp.st(im.id, 'Manual', s_beg, s_end);

      t := s_end; done := 0;
      WHILE done < need AND t < lim LOOP
        b_end := LEAST(t + pg_temp.rmin(40, 240), lim);
        n := pg_temp.cyc(im.id, v_task, m.id, m.cav, m.ct, m.inj, v_gap, t, b_end, cno, need - done);
        EXIT WHEN n = 0;
        c_end := t + n * v_period * interval '1 second';
        PERFORM pg_temp.st(im.id, 'Auto', t, c_end);
        cno := cno + n; done := done + n; t := c_end;
        EXIT WHEN done >= need OR t >= lim;

        r := random();
        IF r < im.p_short THEN                -- короткая остановка, простой не фиксируется
          x_end := LEAST(t + pg_temp.rmin(2, 10), lim);
          PERFORM pg_temp.st(im.id, 'Idle', t, x_end);
        ELSIF r < im.p_alarm THEN             -- авария 15–90 мин
          x_end := LEAST(t + pg_temp.rmin(15, 90), lim);
          PERFORM pg_temp.st(im.id, 'Alarm', t, x_end);
          PERFORM pg_temp.ev(im.id, v_task, v_adj, CASE WHEN random() < 0.5 THEN 1 ELSE 2 END, t, x_end);
        ELSE                                  -- долгий простой 1–3 ч, 20% — без причины
          x_end := LEAST(t + pg_temp.rmin(60, 180), lim);
          PERFORM pg_temp.st(im.id, 'Idle', t, x_end);
          PERFORM pg_temp.ev(im.id, v_task, v_adj,
                             CASE WHEN random() < 0.2 THEN NULL ELSE 3 + floor(random() * 4)::int END, t, x_end);
        END IF;
        t := x_end;
      END LOOP;

      -- Остановка по плану: ТПА стоит, пока наладчик не придёт и не завершит задание
      x_end := LEAST(t + pg_temp.rmin(5, 40), lim);
      PERFORM pg_temp.st(im.id, 'Idle', t, x_end);
      IF x_end - t > interval '15 minutes' THEN
        PERFORM pg_temp.ev(im.id, v_task, v_adj, CASE WHEN random() < 0.5 THEN NULL ELSE 4 END, t, x_end);
      END IF;
      UPDATE "ShiftTasks"
         SET "CompletedAt" = x_end,
             "Status"      = CASE WHEN x_end < cfg.t0 - interval '1 day' THEN 4 ELSE 3 END,
             "ClosedAt"    = CASE WHEN x_end < cfg.t0 - interval '1 day' THEN x_end + pg_temp.rmin(30, 240) END,
             "UpdatedAt"   = x_end
       WHERE "Id" = v_task;
      UPDATE "ShiftTasks" SET "UpdatedAt" = "ClosedAt" WHERE "Id" = v_task AND "ClosedAt" IS NOT NULL;

      last_mold := v_mold;
      cur := x_end;
    END LOOP;

    -- Срез «сейчас»
    IF im.kind = 'arch' THEN
      PERFORM pg_temp.st(im.id, 'Offline', hist_end, NULL);

    ELSIF im.kind = 'free' THEN
      UPDATE "ImmStatusHistory" SET "EndedAt" = NULL
       WHERE "Id" = (SELECT "Id" FROM "ImmStatusHistory" WHERE "ImmId" = im.id ORDER BY "ChangedAt" DESC LIMIT 1);

    ELSIF im.kind = 'setup' THEN
      SELECT * INTO m FROM _mold WHERE _mold.n = im.cur_mold;
      v_planned := date_bin('15 minutes', hist_end - interval '30 minutes', cfg.p_start);
      PERFORM pg_temp.task(im.id, im.cur_mold, v_adjuster, m.cav * 500, 5, 0, v_planned,
                           v_planned - interval '3 hours', hist_end, NULL, NULL, NULL);
      PERFORM pg_temp.st(im.id, 'Manual', hist_end, NULL);

    ELSE -- run: текущее задание в работе
      SELECT * INTO m FROM _mold WHERE _mold.n = im.cur_mold;
      v_adj := CASE WHEN im.n = 1 THEN v_adjuster ELSE adj[1 + floor(random() * (array_length(adj, 1) - 1))::int] END;
      s_beg := hist_end;
      s_end := s_beg + pg_temp.rmin(60, 120);
      v_planned := date_bin('15 minutes', s_beg - pg_temp.rmin(0, 60), cfg.p_start);
      v_task := pg_temp.task(im.id, im.cur_mold, v_adj,
                             GREATEST(floor(60 * 3600 / (m.ct + 1) * m.cav / 100.0) * 100, 100)::int, 2, 0,
                             v_planned, v_planned - pg_temp.rmin(60, 600), s_beg, s_end, NULL, NULL);
      PERFORM pg_temp.st(im.id, 'Manual', s_beg, s_end);
      t := s_end; v_dt_done := false;
      LOOP
        -- ТПА-2: неразмеченный простой ~1,5 ч назад — наладчику есть что разметить на мобильном
        IF im.n = 2 AND NOT v_dt_done AND t > cfg.t0 - interval '100 minutes' AND t < cfg.t0 - interval '30 minutes' THEN
          x_end := t + interval '20 minutes';
          PERFORM pg_temp.st(im.id, 'Idle', t, x_end);
          PERFORM pg_temp.ev(im.id, v_task, v_adj, NULL, t, x_end);
          v_dt_done := true;
          t := x_end;
        END IF;
        b_end := LEAST(t + pg_temp.rmin(40, 240), cfg.t0,
                       CASE WHEN im.n = 2 AND NOT v_dt_done AND t < cfg.t0 - interval '100 minutes'
                            THEN cfg.t0 - interval '90 minutes' ELSE 'infinity'::timestamptz END);
        n := pg_temp.cyc(im.id, v_task, m.id, m.cav, m.ct, m.inj, 1, t, b_end, cno, 1000000);
        cno := cno + n;
        IF b_end >= cfg.t0 THEN
          PERFORM pg_temp.st(im.id, 'Auto', t, NULL);
          EXIT;
        END IF;
        c_end := t + n * (m.ct + 1) * interval '1 second';
        PERFORM pg_temp.st(im.id, 'Auto', t, c_end);
        x_end := c_end + pg_temp.rmin(2, 10);
        IF x_end >= cfg.t0 THEN
          PERFORM pg_temp.st(im.id, 'Idle', c_end, NULL);
          EXIT;
        END IF;
        PERFORM pg_temp.st(im.id, 'Idle', c_end, x_end);
        t := x_end;
      END LOOP;
    END IF;

    RAISE NOTICE '% — готово', im.name;
  END LOOP;
END $$;

-- Очередь «на сейчас»: выданные демо-adjuster и задания других наладчиков
DO $$
DECLARE cfg record; v_adjuster text; q record; v_planned timestamptz; m record;
BEGIN
  SELECT * INTO cfg FROM _cfg;
  SELECT "Id" INTO v_adjuster FROM "Users" WHERE "UserName" = 'adjuster';
  FOR q IN SELECT * FROM _queue ORDER BY imm, ago DESC LOOP
    SELECT * INTO m FROM _mold WHERE n = q.mold;
    v_planned := date_bin('15 minutes', cfg.t0 - q.ago * interval '1 minute', cfg.p_start);
    PERFORM pg_temp.task((SELECT id FROM _imm WHERE n = q.imm), q.mold,
                         CASE WHEN q.who = 'demo' THEN v_adjuster ELSE q.who END,
                         m.cav * q.shots, q.status, 0, v_planned,
                         CASE WHEN q.status = 1 THEN v_planned - interval '2 hours' END, NULL, NULL, NULL, NULL);
  END LOOP;
END $$;

-- Выпуск, брак и материал — из фактических циклов
UPDATE "ShiftTasks" t
SET "ActualQuantity"            = q.qty,
    "DefectQuantity"            = CASE WHEN t."Status" IN (3, 4) THEN (q.qty * random() * 0.02)::int ELSE 0 END,
    "ActualMaterialWeightGrams" = round(q.qty * m."PartWeightGrams" + q.n * m."RunnerWeightGrams", 2)
FROM (SELECT "TaskId", count(*) AS n, COALESCE(sum("Cavities") FILTER (WHERE "IsSuccessful"), 0) AS qty
      FROM "ImmCycles" WHERE "TaskId" IS NOT NULL GROUP BY "TaskId") q,
     "Molds" m
WHERE q."TaskId" = t."Id" AND m."Id" = t."MoldId";

-- ============================================================
-- Заказы: по изделию — две «волны» (первые 15 суток и остальное), ~¾ заданий с заказом
-- ============================================================
CREATE TEMP TABLE _tb AS
SELECT t."Id" AS task_id, t."ProductId" AS product_id,
       CASE WHEN t."PlannedDate" < (SELECT (p_start AT TIME ZONE 'Europe/Moscow')::date + 15 FROM _cfg) THEN 1 ELSE 2 END AS bucket
FROM "ShiftTasks" t
JOIN "Products" p ON p."Id" = t."ProductId"
WHERE p."IsActive" AND abs(hashtext(t."Id"::text)) % 4 <> 0;

CREATE TEMP TABLE _ord AS
SELECT gen_random_uuid() AS id, product_id, bucket, row_number() OVER (ORDER BY bucket, product_id) AS rn
FROM (SELECT DISTINCT product_id, bucket FROM _tb) d;

INSERT INTO "Orders" ("Id","Number","OrderDate","DueDate","Quantity","Status","Note","CreatedAt","ProductId")
SELECT o.id, 'З-' || lpad(o.rn::text, 3, '0'),
       min(t."CreatedAt") - interval '2 days',
       LEAST((max(t."PlannedDate") + 5)::timestamp AT TIME ZONE 'Europe/Moscow', (SELECT t0 FROM _cfg)),
       (ceil(sum(t."PlanQuantity") / 1000.0) * 1000)::int,
       CASE WHEN bool_and(t."Status" IN (3, 4)) THEN 1 ELSE 0 END,
       NULL, min(t."CreatedAt") - interval '2 days', o.product_id
FROM _ord o
JOIN _tb b ON b.product_id = o.product_id AND b.bucket = o.bucket
JOIN "ShiftTasks" t ON t."Id" = b.task_id
GROUP BY o.id, o.rn, o.product_id;

UPDATE "ShiftTasks" t SET "OrderId" = o.id
FROM _tb b JOIN _ord o ON o.product_id = b.product_id AND o.bucket = b.bucket
WHERE t."Id" = b.task_id;

INSERT INTO "Orders" ("Id","Number","OrderDate","DueDate","Quantity","Status","Note","CreatedAt","ProductId")
SELECT gen_random_uuid(), v.num, c.t0 - v.ago * interval '1 day', c.t0 - v.due * interval '1 day', v.qty, v.status,
       v.note, c.t0 - v.ago * interval '1 day', (SELECT "Id" FROM "Products" WHERE "FullArticle" = v.art)
FROM _cfg c,
     (VALUES ('З-900', 20, 5, 5000,  2, 'Отменён заказчиком',                    'КП38.К'),
             ('З-901',  1, 0, 12000, 0, 'Новый заказ — задания ещё не созданы',  'ВТ20.Н')) v(num, ago, due, qty, status, note, art);

-- ============================================================
-- Телеметрия: ppc точек на каждый датчик на каждый цикл, по фазам цикла
-- (prio — какие фазы берутся при ppc < 6; frac — доля длительности цикла, NULL — конец литья)
-- ============================================================
DO $$ BEGIN RAISE NOTICE 'Телеметрия...'; END $$;

CREATE TEMP TABLE _phase (k int, prio int, frac numeric, press numeric, screw numeric, mold_dt numeric, clamp numeric);
INSERT INTO _phase VALUES
  (1, 1, 0.00,    5, 80, 0.0,    0),   -- смыкание
  (2, 4, 0.04,  950, 78, 0.6, 1450),   -- начало литья
  (3, 2, NULL,  620, 12, 1.6, 1500),   -- конец литья
  (4, 5, 0.60,   40, 64, 1.1, 1500),   -- середина охлаждения
  (5, 6, 0.92,    5, 80, 0.4,  350),   -- начало раскрытия
  (6, 3, 1.00,    5, 80, 0.0,    0);   -- раскрытие

INSERT INTO "Telemetry" ("ImmId","Timestamp","ParameterName","ValueNumeric")
SELECT c."ImmId",
       CASE WHEN ph.frac IS NULL THEN c."InjectionStartTime" + c."InjectionDurationMs" * interval '1 millisecond'
            ELSE c."StartTime" + ph.frac * c."DurationSeconds" * interval '1 second' END,
       s.name,
       round((CASE s.name
         WHEN 'MeltTemp'          THEN 222 + (hashtext(c."ImmId"::text) & 7) + random() * 1.5
         WHEN 'InjectionPressure' THEN ph.press * (0.95 + random() * 0.1)
         WHEN 'ScrewPosition'     THEN ph.screw + random() * 2
         WHEN 'MoldTemp'          THEN 45 + ph.mold_dt + random() * 0.5
         ELSE                          ph.clamp * (0.98 + random() * 0.04) END)::numeric, 1)
FROM "ImmCycles" c
CROSS JOIN (SELECT * FROM _phase WHERE prio <= (SELECT ppc FROM _cfg)) ph
CROSS JOIN (VALUES ('MeltTemp'), ('InjectionPressure'), ('ScrewPosition'), ('MoldTemp'), ('ClampForce')) s(name)
WHERE c."EndTime" IS NOT NULL;

-- Пульс приложения = t0: при старте API закроет открытые статусы записью Offline сразу после t0
INSERT INTO "AppHeartbeat" ("Id","LastHeartbeatAt") SELECT 1, t0 FROM _cfg
ON CONFLICT ("Id") DO UPDATE SET "LastHeartbeatAt" = EXCLUDED."LastHeartbeatAt";

ANALYZE;

SELECT i."Name",
       (SELECT count(*) FROM "ShiftTasks" t WHERE t."ImmId" = i."Id")       AS tasks,
       (SELECT count(*) FROM "ImmCycles" c WHERE c."ImmId" = i."Id")        AS cycles,
       (SELECT count(*) FROM "ImmStatusHistory" h WHERE h."ImmId" = i."Id") AS statuses,
       (SELECT count(*) FROM "Events" e WHERE e."ImmId" = i."Id")           AS downtimes
FROM "Imms" i ORDER BY i."Name";
