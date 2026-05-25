-- ============================================================
-- Wintime Control — Demo Seed Data
-- Period    : April 2026 (both day and night shifts)
-- Shop      : 17 IMMs modelled after real Feb-2026 report
-- OEE       : ~60% (day) / ~50% (night)
-- Users     : admin, manager, adj1-adj6  — password: Demo@123
-- Idempotent: run multiple times safely (ON CONFLICT + DELETE by period)
-- ============================================================

-- ============================================================
-- BLOCK 1 — Static reference data
-- ============================================================
DO $$
DECLARE
  pw  TEXT := 'AQAAAAEAAYagAAAAEI80oht8Xp0POmstjkwfegtuSbDirfOYzpBrtROf7w8kGYzBTu7MQxOl2fRSXRA8Dg==';

  t1  UUID := 'a1000000-0000-0000-0000-000000000001';  -- Малые ТПА
  t2  UUID := 'a1000000-0000-0000-0000-000000000002';  -- Средние ТПА
  t3  UUID := 'a1000000-0000-0000-0000-000000000003';  -- Крупные ТПА

  -- IMMs 1-17
  b01 UUID := 'b1000000-0000-0000-0000-000000000001';  -- Alfa 100
  b02 UUID := 'b1000000-0000-0000-0000-000000000002';  -- Babyplast 1
  b03 UUID := 'b1000000-0000-0000-0000-000000000003';  -- H-drive
  b04 UUID := 'b1000000-0000-0000-0000-000000000004';  -- Sallsen 1
  b05 UUID := 'b1000000-0000-0000-0000-000000000005';  -- Sallsen 2
  b06 UUID := 'b1000000-0000-0000-0000-000000000006';  -- Siger 160v1
  b07 UUID := 'b1000000-0000-0000-0000-000000000007';  -- Siger 160v2
  b08 UUID := 'b1000000-0000-0000-0000-000000000008';  -- Siger 160v3
  b09 UUID := 'b1000000-0000-0000-0000-000000000009';  -- Siger 160v4
  b10 UUID := 'b1000000-0000-0000-0000-000000000010';  -- Siger 160v5
  b11 UUID := 'b1000000-0000-0000-0000-000000000011';  -- Siger 200v1
  b12 UUID := 'b1000000-0000-0000-0000-000000000012';  -- Siger 200v2 (offline)
  b13 UUID := 'b1000000-0000-0000-0000-000000000013';  -- Siger 200v3
  b14 UUID := 'b1000000-0000-0000-0000-000000000014';  -- Siger 200s1
  b15 UUID := 'b1000000-0000-0000-0000-000000000015';  -- Siger 320
  b16 UUID := 'b1000000-0000-0000-0000-000000000016';  -- Siger 320S
  b17 UUID := 'b1000000-0000-0000-0000-000000000017';  -- Siger 320s1 (offline)

  -- Molds 1-21
  c01 UUID := 'c1000000-0000-0000-0000-000000000001';  -- ID34448 Шайба 24x8
  c02 UUID := 'c1000000-0000-0000-0000-000000000002';  -- ID23717 КлипДак (48)
  c03 UUID := 'c1000000-0000-0000-0000-000000000003';  -- ID45556 Боковой V1
  c04 UUID := 'c1000000-0000-0000-0000-000000000004';  -- ID32250 Загл. 20x30 прям.
  c05 UUID := 'c1000000-0000-0000-0000-000000000005';  -- ID45741 Загл. 20x30 тех.
  c06 UUID := 'c1000000-0000-0000-0000-000000000006';  -- ID34131 Подпятник треуг.
  c07 UUID := 'c1000000-0000-0000-0000-000000000007';  -- ID31108 Проставка d20x10
  c08 UUID := 'c1000000-0000-0000-0000-000000000008';  -- ID32244 Загл. 20x30 опор.
  c09 UUID := 'c1000000-0000-0000-0000-000000000009';  -- ID35217 8100.102.120
  c10 UUID := 'c1000000-0000-0000-0000-000000000010';  -- ID46937 Загл. 20x30x5 дек.
  c11 UUID := 'c1000000-0000-0000-0000-000000000011';  -- ID33484 Колесо С428
  c12 UUID := 'c1000000-0000-0000-0000-000000000012';  -- ID33485 Диск С-348
  c13 UUID := 'c1000000-0000-0000-0000-000000000013';  -- ID30003 Опора H40
  c14 UUID := 'c1000000-0000-0000-0000-000000000014';  -- ID41017 Подпятник d8 D18
  c15 UUID := 'c1000000-0000-0000-0000-000000000015';  -- ID49910 Кронштейн
  c16 UUID := 'c1000000-0000-0000-0000-000000000016';  -- ID45757 Центральный V1
  c17 UUID := 'c1000000-0000-0000-0000-000000000017';  -- ID50690 Загл. 20x20x2.5
  c18 UUID := 'c1000000-0000-0000-0000-000000000018';  -- ID46946 Зацеп скользящий
  c19 UUID := 'c1000000-0000-0000-0000-000000000019';  -- ID48614 Петля-загл. 30x20
  c20 UUID := 'c1000000-0000-0000-0000-000000000020';  -- ID51909 Опора колёсная
  c21 UUID := 'c1000000-0000-0000-0000-000000000021';  -- ID35222 Шкант D10 H=33

  -- Downtime reasons 1-8
  d01 UUID := 'd1000000-0000-0000-0000-000000000001';
  d02 UUID := 'd1000000-0000-0000-0000-000000000002';
  d03 UUID := 'd1000000-0000-0000-0000-000000000003';
  d04 UUID := 'd1000000-0000-0000-0000-000000000004';
  d05 UUID := 'd1000000-0000-0000-0000-000000000005';
  d06 UUID := 'd1000000-0000-0000-0000-000000000006';
  d07 UUID := 'd1000000-0000-0000-0000-000000000007';
  d08 UUID := 'd1000000-0000-0000-0000-000000000008';

  -- Users
  u1 TEXT := 'u0000000-0000-0000-0000-000000000001';  -- admin
  u2 TEXT := 'u0000000-0000-0000-0000-000000000002';  -- manager
  u3 TEXT := 'u0000000-0000-0000-0000-000000000003';  -- adj1
  u4 TEXT := 'u0000000-0000-0000-0000-000000000004';  -- adj2
  u5 TEXT := 'u0000000-0000-0000-0000-000000000005';  -- adj3
  u6 TEXT := 'u0000000-0000-0000-0000-000000000006';  -- adj4
  u7 TEXT := 'u0000000-0000-0000-0000-000000000007';  -- adj5
  u8 TEXT := 'u0000000-0000-0000-0000-000000000008';  -- adj6

BEGIN

  -- ---- Templates ----
  INSERT INTO "Templates" ("Id","Name","Manufacturer","Model","Version","Author","JsonConfig","IsActive","CreatedAt","UpdatedAt")
  VALUES
    (t1,'Малые ТПА',  'Siger',  '160', '1.0','Демо','{}',true,'2025-01-01 00:00:00+00','2025-01-01 00:00:00+00'),
    (t2,'Средние ТПА','Siger',  '200', '1.0','Демо','{}',true,'2025-01-01 00:00:00+00','2025-01-01 00:00:00+00'),
    (t3,'Крупные ТПА','Siger',  '320', '1.0','Демо','{}',true,'2025-01-01 00:00:00+00','2025-01-01 00:00:00+00')
  ON CONFLICT ("Id") DO NOTHING;

  -- ---- IMMs ----
  INSERT INTO "Imms" ("Id","Name","InventoryNumber","TemplateId","IsActive","CreatedAt")
  VALUES
    (b01,'Alfa 100',    'INV-001',t1,true, '2025-01-01 00:00:00+00'),
    (b02,'Babyplast 1', 'INV-002',t1,true, '2025-01-01 00:00:00+00'),
    (b03,'H-drive',     'INV-003',t2,true, '2025-01-01 00:00:00+00'),
    (b04,'Sallsen 1',   'INV-004',t2,true, '2025-01-01 00:00:00+00'),
    (b05,'Sallsen 2',   'INV-005',t2,true, '2025-01-01 00:00:00+00'),
    (b06,'Siger 160v1', 'INV-006',t1,true, '2025-01-01 00:00:00+00'),
    (b07,'Siger 160v2', 'INV-007',t1,true, '2025-01-01 00:00:00+00'),
    (b08,'Siger 160v3', 'INV-008',t1,true, '2025-01-01 00:00:00+00'),
    (b09,'Siger 160v4', 'INV-009',t1,true, '2025-01-01 00:00:00+00'),
    (b10,'Siger 160v5', 'INV-010',t1,true, '2025-01-01 00:00:00+00'),
    (b11,'Siger 200v1', 'INV-011',t2,true, '2025-01-01 00:00:00+00'),
    (b12,'Siger 200v2', 'INV-012',t2,false,'2025-01-01 00:00:00+00'),
    (b13,'Siger 200v3', 'INV-013',t2,true, '2025-01-01 00:00:00+00'),
    (b14,'Siger 200s1', 'INV-014',t2,true, '2025-01-01 00:00:00+00'),
    (b15,'Siger 320',   'INV-015',t3,true, '2025-01-01 00:00:00+00'),
    (b16,'Siger 320S',  'INV-016',t3,true, '2025-01-01 00:00:00+00'),
    (b17,'Siger 320s1', 'INV-017',t3,false,'2025-01-01 00:00:00+00')
  ON CONFLICT ("Id") DO NOTHING;

  -- ---- Molds ----
  --                              Id   FormId           Name                            Cav  PartWt  RunnWt MaxRes To1      To2     StoreLoc DrawP PhotoP Act  CreatedAt
  INSERT INTO "Molds" ("Id","FormId","Name","Cavities","PartWeightGrams","RunnerWeightGrams","MaxResourceCycles","To1Cycles","To2Cycles","IsActive","CreatedAt")
  VALUES
    (c01,'ID34448','Шайба 24×8',              24, 0.88,  0.0,  1000000,800000,950000,true,'2025-01-01 00:00:00+00'),
    (c02,'ID23717','КлипДак (48)',             48, 2.6,   0.0,   500000,400000,480000,true,'2025-01-01 00:00:00+00'),
    (c03,'ID45556','Боковой V1',               16, 6.37, 26.0,   300000,250000,280000,true,'2025-01-01 00:00:00+00'),
    (c04,'ID32250','Заглушка 20×30 прям.',     24, 3.5,   0.0,   500000,400000,480000,true,'2025-01-01 00:00:00+00'),
    (c05,'ID45741','Заглушка 20×30 тех.',      24, 2.15,  0.0,   500000,400000,480000,true,'2025-01-01 00:00:00+00'),
    (c06,'ID34131','Подпятник треуг. 25×3',     4, 1.84,  0.0,   800000,700000,780000,true,'2025-01-01 00:00:00+00'),
    (c07,'ID31108','Проставка d20×10',         32, 1.95,  0.0,   800000,700000,780000,true,'2025-01-01 00:00:00+00'),
    (c08,'ID32244','Заглушка 20×30 опорная',   24, 8.7,   0.0,   400000,350000,390000,true,'2025-01-01 00:00:00+00'),
    (c09,'ID35217','8100.102.120',              4,64.0,  12.8,   100000, 80000, 95000,true,'2025-01-01 00:00:00+00'),
    (c10,'ID46937','Заглушка 20×30×5 дек.',     4, 2.82,  0.0,   500000,400000,480000,true,'2025-01-01 00:00:00+00'),
    (c11,'ID33484','Колесо С428',               8,18.5,   0.0,   200000,150000,190000,true,'2025-01-01 00:00:00+00'),
    (c12,'ID33485','Диск С-348',                8, 7.0,   0.0,   300000,250000,290000,true,'2025-01-01 00:00:00+00'),
    (c13,'ID30003','Опора H40 под шуруп',       8,18.0,  12.5,   200000,150000,190000,true,'2025-01-01 00:00:00+00'),
    (c14,'ID41017','Подпятник d8 D18',         24, 2.02,  0.0,   600000,500000,580000,true,'2025-01-01 00:00:00+00'),
    (c15,'ID49910','Кронштейн боковины',         4, 3.98,  0.0,   400000,350000,390000,true,'2025-01-01 00:00:00+00'),
    (c16,'ID45757','Центральный V1',            16, 8.14, 24.0,   300000,250000,290000,true,'2025-01-01 00:00:00+00'),
    (c17,'ID50690','Заглушка 20×20×2.5 тех.',   8, 1.49,  0.0,   600000,500000,580000,true,'2025-01-01 00:00:00+00'),
    (c18,'ID46946','Зацеп скользящий',          2, 0.5,   0.0,   800000,700000,780000,true,'2025-01-01 00:00:00+00'),
    (c19,'ID48614','Петля-заглушка 30×20',      4,18.4,   0.0,   300000,250000,290000,true,'2025-01-01 00:00:00+00'),
    (c20,'ID51909','Опора колёсная',            1, 3.0,   0.0,   400000,350000,390000,true,'2025-01-01 00:00:00+00'),
    (c21,'ID35222','Шкант D10 H=33',           24, 2.98,  0.0,   300000,250000,290000,true,'2025-01-01 00:00:00+00')
  ON CONFLICT ("FormId") DO NOTHING;

  -- ---- Downtime reasons ----
  INSERT INTO "DowntimeReasons" ("Id","Name","Type","IsActive","CreatedAt")
  VALUES
    (d01,'Неисправность оборудования','Emergency',true,'2025-01-01 00:00:00+00'),
    (d02,'Неисправность пресс-формы', 'Emergency',true,'2025-01-01 00:00:00+00'),
    (d03,'Ожидание сырья',            'Planned',  true,'2025-01-01 00:00:00+00'),
    (d04,'Отсутствие оператора',      'Planned',  true,'2025-01-01 00:00:00+00'),
    (d05,'ТО-1 (плановое)',           'Planned',  true,'2025-01-01 00:00:00+00'),
    (d06,'ТО-2 (плановое)',           'Planned',  true,'2025-01-01 00:00:00+00'),
    (d07,'Переналадка',               'Planned',  true,'2025-01-01 00:00:00+00'),
    (d08,'Пуско-наладка',             'Planned',  true,'2025-01-01 00:00:00+00')
  ON CONFLICT ("Id") DO NOTHING;

  -- ---- Shifts (only if table is empty) ----
  IF NOT EXISTS (SELECT 1 FROM "Shifts" LIMIT 1) THEN
    INSERT INTO "Shifts" ("Id","StartMinutes","DurationMinutes","BreakStartMinutes","BreakDurationMinutes","CreatedAt")
    VALUES
      (gen_random_uuid(), 360, 720, 720,  30, NOW()),   -- День: 06:00-18:00, перерыв в 12:00
      (gen_random_uuid(),1080, 720,   0,  30, NOW());   -- Ночь: 18:00-06:00, перерыв в 00:00
  END IF;

  -- ---- Users ----
  -- Users table IS the ASP.NET Identity table (renamed from AspNetUsers)
  INSERT INTO "Users" (
    "Id","FullName","Role","EmployeeId","IsActive",
    "UserName","NormalizedUserName","Email","NormalizedEmail",
    "EmailConfirmed","PasswordHash","SecurityStamp","ConcurrencyStamp",
    "PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount"
  ) VALUES
    (u1,'Администратор',     0,'EMP-001',true,'admin@demo.local',   'ADMIN@DEMO.LOCAL',   'admin@demo.local',   'ADMIN@DEMO.LOCAL',   true,pw,gen_random_uuid()::text,gen_random_uuid()::text,false,false,true,0),
    (u2,'Иван Петров',       1,'EMP-002',true,'manager@demo.local', 'MANAGER@DEMO.LOCAL', 'manager@demo.local', 'MANAGER@DEMO.LOCAL', true,pw,gen_random_uuid()::text,gen_random_uuid()::text,false,false,true,0),
    (u3,'Алексей Смирнов',   2,'EMP-003',true,'adj1@demo.local',    'ADJ1@DEMO.LOCAL',    'adj1@demo.local',    'ADJ1@DEMO.LOCAL',    true,pw,gen_random_uuid()::text,gen_random_uuid()::text,false,false,true,0),
    (u4,'Дмитрий Козлов',    2,'EMP-004',true,'adj2@demo.local',    'ADJ2@DEMO.LOCAL',    'adj2@demo.local',    'ADJ2@DEMO.LOCAL',    true,pw,gen_random_uuid()::text,gen_random_uuid()::text,false,false,true,0),
    (u5,'Сергей Новиков',    2,'EMP-005',true,'adj3@demo.local',    'ADJ3@DEMO.LOCAL',    'adj3@demo.local',    'ADJ3@DEMO.LOCAL',    true,pw,gen_random_uuid()::text,gen_random_uuid()::text,false,false,true,0),
    (u6,'Андрей Морозов',    2,'EMP-006',true,'adj4@demo.local',    'ADJ4@DEMO.LOCAL',    'adj4@demo.local',    'ADJ4@DEMO.LOCAL',    true,pw,gen_random_uuid()::text,gen_random_uuid()::text,false,false,true,0),
    (u7,'Михаил Волков',     2,'EMP-007',true,'adj5@demo.local',    'ADJ5@DEMO.LOCAL',    'adj5@demo.local',    'ADJ5@DEMO.LOCAL',    true,pw,gen_random_uuid()::text,gen_random_uuid()::text,false,false,true,0),
    (u8,'Николай Соколов',   2,'EMP-008',true,'adj6@demo.local',    'ADJ6@DEMO.LOCAL',    'adj6@demo.local',    'ADJ6@DEMO.LOCAL',    true,pw,gen_random_uuid()::text,gen_random_uuid()::text,false,false,true,0)
  ON CONFLICT ("NormalizedUserName") DO NOTHING;

END $$;

-- ============================================================
-- BLOCK 2 — Clean April 2026 data (for idempotent re-runs)
-- ============================================================
DELETE FROM "ImmStatusHistory" WHERE "ChangedAt" >= '2026-04-01' AND "ChangedAt" < '2026-05-01';
DELETE FROM "ImmCycles"        WHERE "StartTime"  >= '2026-04-01' AND "StartTime"  < '2026-05-01';
DELETE FROM "Telemetry"        WHERE "Timestamp"  >= '2026-04-01' AND "Timestamp"  < '2026-05-01';
DELETE FROM "Events"           WHERE "StartTime"  >= '2026-04-01' AND "StartTime"  < '2026-05-01';
DELETE FROM "Tasks"            WHERE "PlannedDate" >= '2026-04-01' AND "PlannedDate" < '2026-05-01';

-- ============================================================
-- BLOCK 3 — Dynamic data generation  (April 2026)
-- ============================================================
DO $$
DECLARE
  -- IMM UUIDs (re-declared — each DO block has its own scope)
  imm_ids UUID[] := ARRAY[
    'b1000000-0000-0000-0000-000000000001'::UUID,   -- 1  Alfa 100
    'b1000000-0000-0000-0000-000000000002'::UUID,   -- 2  Babyplast 1
    'b1000000-0000-0000-0000-000000000003'::UUID,   -- 3  H-drive
    'b1000000-0000-0000-0000-000000000004'::UUID,   -- 4  Sallsen 1
    'b1000000-0000-0000-0000-000000000005'::UUID,   -- 5  Sallsen 2
    'b1000000-0000-0000-0000-000000000006'::UUID,   -- 6  Siger 160v1
    'b1000000-0000-0000-0000-000000000007'::UUID,   -- 7  Siger 160v2
    'b1000000-0000-0000-0000-000000000008'::UUID,   -- 8  Siger 160v3
    'b1000000-0000-0000-0000-000000000009'::UUID,   -- 9  Siger 160v4
    'b1000000-0000-0000-0000-000000000010'::UUID,   -- 10 Siger 160v5
    'b1000000-0000-0000-0000-000000000011'::UUID,   -- 11 Siger 200v1
    'b1000000-0000-0000-0000-000000000012'::UUID,   -- 12 Siger 200v2 (offline)
    'b1000000-0000-0000-0000-000000000013'::UUID,   -- 13 Siger 200v3
    'b1000000-0000-0000-0000-000000000014'::UUID,   -- 14 Siger 200s1
    'b1000000-0000-0000-0000-000000000015'::UUID,   -- 15 Siger 320
    'b1000000-0000-0000-0000-000000000016'::UUID,   -- 16 Siger 320S
    'b1000000-0000-0000-0000-000000000017'::UUID    -- 17 Siger 320s1 (offline)
  ];

  -- true = machine can work this type of shift
  imm_day   BOOLEAN[] := ARRAY[true,true,true,true,true,true,true,true,true,true,true,false,true,true,true,true,false];
  imm_night BOOLEAN[] := ARRAY[true,false,true,true,true,true,false,true,true,false,true,false,true,true,false,false,false];

  -- Primary mold per machine (index into mold UUIDs)
  mold_prim UUID[] := ARRAY[
    'c1000000-0000-0000-0000-000000000001'::UUID,   -- 1  Alfa 100     → Шайба 24x8
    'c1000000-0000-0000-0000-000000000018'::UUID,   -- 2  Babyplast 1  → Зацеп скользящий
    'c1000000-0000-0000-0000-000000000016'::UUID,   -- 3  H-drive      → Центральный V1
    'c1000000-0000-0000-0000-000000000017'::UUID,   -- 4  Sallsen 1    → Загл. 20x20x2.5
    'c1000000-0000-0000-0000-000000000015'::UUID,   -- 5  Sallsen 2    → Кронштейн
    'c1000000-0000-0000-0000-000000000006'::UUID,   -- 6  Siger 160v1  → Подпятник треуг.
    'c1000000-0000-0000-0000-000000000011'::UUID,   -- 7  Siger 160v2  → Колесо С428
    'c1000000-0000-0000-0000-000000000010'::UUID,   -- 8  Siger 160v3  → Загл. 20x30x5 дек.
    'c1000000-0000-0000-0000-000000000003'::UUID,   -- 9  Siger 160v4  → Боковой V1
    'c1000000-0000-0000-0000-000000000012'::UUID,   -- 10 Siger 160v5  → Диск С-348
    'c1000000-0000-0000-0000-000000000009'::UUID,   -- 11 Siger 200v1  → 8100.102.120
    'c1000000-0000-0000-0000-000000000001'::UUID,   -- 12 (offline placeholder)
    'c1000000-0000-0000-0000-000000000013'::UUID,   -- 13 Siger 200v3  → Опора H40
    'c1000000-0000-0000-0000-000000000002'::UUID,   -- 14 Siger 200s1  → КлипДак (48)
    'c1000000-0000-0000-0000-000000000004'::UUID,   -- 15 Siger 320    → Загл. 20x30 прям.
    'c1000000-0000-0000-0000-000000000008'::UUID,   -- 16 Siger 320S   → Загл. 20x30 опор.
    'c1000000-0000-0000-0000-000000000001'::UUID    -- 17 (offline placeholder)
  ];

  -- Alternate mold (NULL = no rotation)
  mold_alt UUID[] := ARRAY[
    NULL::UUID,                                     -- 1  no alt
    NULL::UUID,                                     -- 2  no alt
    NULL::UUID,                                     -- 3  no alt
    NULL::UUID,                                     -- 4  no alt
    'c1000000-0000-0000-0000-000000000020'::UUID,   -- 5  Sallsen 2   → Опора колёсная
    'c1000000-0000-0000-0000-000000000007'::UUID,   -- 6  Siger 160v1 → Проставка d20x10
    NULL::UUID,                                     -- 7  no alt
    NULL::UUID,                                     -- 8  no alt
    NULL::UUID,                                     -- 9  no alt
    NULL::UUID,                                     -- 10 no alt
    'c1000000-0000-0000-0000-000000000021'::UUID,   -- 11 Siger 200v1 → Шкант D10
    NULL::UUID,                                     -- 12 (offline)
    'c1000000-0000-0000-0000-000000000019'::UUID,   -- 13 Siger 200v3 → Петля-загл. 30x20
    'c1000000-0000-0000-0000-000000000005'::UUID,   -- 14 Siger 200s1 → Загл. 20x30 тех.
    NULL::UUID,                                     -- 15 no alt
    NULL::UUID,                                     -- 16 no alt
    NULL::UUID                                      -- 17 (offline)
  ];

  -- Primary cycle time (sec) for each machine / mold
  cycle_prim INTEGER[] := ARRAY[55,25,51,65,65,65,130,55,34,62,160,60,78,55,60,100,60];
  -- Alternate cycle time (0 = no alt)
  cycle_alt  INTEGER[] := ARRAY[ 0, 0, 0, 0,60,55,  0, 0, 0, 0,120, 0,140,44, 0,  0, 0];

  -- Cavities: primary / alternate
  cav_prim INTEGER[] := ARRAY[24, 2,16, 8, 4, 4, 8, 4,16, 8, 4, 4, 8,48,24,24, 4];
  cav_alt  INTEGER[] := ARRAY[ 0, 0, 0, 0, 1,32, 0, 0, 0, 0,24, 0, 4,24, 0,  0, 0];

  -- Part weight (g) for material calculation
  part_wt_g FLOAT[] := ARRAY[0.88,0.5,8.14,1.49,3.98,1.84,18.5,2.82,6.37,7.0,64.0,0.0,18.0,2.6,3.5,8.7,0.0];
  -- Runner weight (g) per cycle (not per part)
  run_wt_g  FLOAT[] := ARRAY[0.0, 0.0,24.0,0.0,0.0,0.0,0.0,0.0,26.0,0.0,12.8,0.0,12.5,0.0,0.0,0.0,0.0];

  -- Adjuster index (1-6) responsible for each machine
  adj_idx INTEGER[] := ARRAY[1,1,1,2,2,2,3,3,3,4,4,4,5,5,5,6,6];

  adj_ids TEXT[] := ARRAY[
    'u0000000-0000-0000-0000-000000000003',   -- adj1: Alfa100, Baby, H-drive
    'u0000000-0000-0000-0000-000000000004',   -- adj2: Sallsen1, Sallsen2, Siger160v1
    'u0000000-0000-0000-0000-000000000005',   -- adj3: Siger160v2, v3, v4
    'u0000000-0000-0000-0000-000000000006',   -- adj4: Siger160v5, Siger200v1, v2
    'u0000000-0000-0000-0000-000000000007',   -- adj5: Siger200v3, 200s1, Siger320
    'u0000000-0000-0000-0000-000000000008'    -- adj6: Siger320S, Siger320s1
  ];

  -- Downtime reasons for random selection
  reason_ids   UUID[] := ARRAY[
    'd1000000-0000-0000-0000-000000000001'::UUID,
    'd1000000-0000-0000-0000-000000000002'::UUID,
    'd1000000-0000-0000-0000-000000000003'::UUID,
    'd1000000-0000-0000-0000-000000000004'::UUID,
    'd1000000-0000-0000-0000-000000000005'::UUID,
    'd1000000-0000-0000-0000-000000000006'::UUID,
    'd1000000-0000-0000-0000-000000000007'::UUID,
    'd1000000-0000-0000-0000-000000000008'::UUID
  ];
  reason_names TEXT[] := ARRAY[
    'Неисправность оборудования','Неисправность пресс-формы',
    'Ожидание сырья','Отсутствие оператора',
    'ТО-1 (плановое)','ТО-2 (плановое)',
    'Переналадка','Пуско-наладка'
  ];
  -- Starting cycle counts per machine (accumulated before April 2026)
  base_cycles INTEGER[] := ARRAY[
    142000,  -- 1  Alfa 100
     45000,  -- 2  Babyplast 1
     38000,  -- 3  H-drive
     62000,  -- 4  Sallsen 1
     55000,  -- 5  Sallsen 2
     78000,  -- 6  Siger 160v1
     24000,  -- 7  Siger 160v2
     48000,  -- 8  Siger 160v3
     82000,  -- 9  Siger 160v4  (Боковой V1 had 40K in Feb alone)
     35000,  -- 10 Siger 160v5
     18000,  -- 11 Siger 200v1
         0,  -- 12 offline
     29000,  -- 13 Siger 200v3
     94000,  -- 14 Siger 200s1  (КлипДак 23K in Feb)
     41000,  -- 15 Siger 320
     38000,  -- 16 Siger 320S
         0   -- 17 offline
  ];

  -- Loop variables
  d          INTEGER;
  shift_n    INTEGER;
  i          INTEGER;
  ri         INTEGER;  -- reason index

  v_imm_id   UUID;
  v_mold_id  UUID;
  v_task_id  UUID;
  v_adj      TEXT;
  v_csec     INTEGER;  -- cycle seconds (effective)
  v_cav      INTEGER;  -- cavities
  v_pwt      FLOAT;
  v_rwt      FLOAT;
  v_ecsec    INTEGER;  -- effective cycle sec for generate_series (min 25)

  shift_start TIMESTAMPTZ;
  shift_end   TIMESTAMPTZ;
  sa          TIMESTAMPTZ;  -- segment A (start)
  sb          TIMESTAMPTZ;  -- segment B (end)

  -- Time budget (seconds in shift = 43200)
  auto_s   INTEGER;
  man_s    INTEGER;
  down_s   INTEGER;
  idle_s   INTEGER;
  auto1_s  INTEGER;
  auto2_s  INTEGER;
  down1_s  INTEGER;

  plan_qty   INTEGER;
  actual_qty INTEGER;
  mat_wt     FLOAT;
  cyc1       INTEGER;  -- cycles in auto block 1
  cyc2       INTEGER;  -- cycles in auto block 2
  cyc_base   INTEGER;

  -- Temp table: running cycle totals per machine

BEGIN
  CREATE TEMP TABLE IF NOT EXISTS _seed_cycles (
    imm_id  UUID PRIMARY KEY,
    cnt     INTEGER NOT NULL DEFAULT 0
  );

  INSERT INTO _seed_cycles (imm_id, cnt)
  SELECT imm_ids[gs], base_cycles[gs]
  FROM generate_series(1,17) AS gs
  ON CONFLICT (imm_id) DO UPDATE SET cnt = EXCLUDED.cnt;  -- reset on re-run

  -- ── Main loop: 30 days × 2 shifts × 17 machines ──────────────────────────
  FOR d IN 1..30 LOOP
    FOR shift_n IN 1..2 LOOP
      -- Day shift starts at 06:00 UTC; night at 18:00 UTC
      shift_start := TIMESTAMPTZ '2026-04-01 06:00:00+00'
                     + ((d - 1)::TEXT || ' days')::INTERVAL
                     + (((shift_n - 1) * 12)::TEXT || ' hours')::INTERVAL;
      shift_end   := shift_start + INTERVAL '12 hours';

      FOR i IN 1..17 LOOP
        -- Skip permanently offline machines
        IF NOT (CASE shift_n WHEN 1 THEN imm_day[i] ELSE imm_night[i] END) THEN
          CONTINUE;
        END IF;

        -- ~8% chance machine is idle the whole shift (realistic spot downtime)
        IF RANDOM() < 0.08 THEN CONTINUE; END IF;

        v_imm_id := imm_ids[i];
        v_adj    := adj_ids[adj_idx[i]];

        -- Rotate to alternate mold ~15% of the time (changeover)
        IF mold_alt[i] IS NOT NULL AND RANDOM() < 0.15 THEN
          v_mold_id := mold_alt[i];
          v_csec    := cycle_alt[i];
          v_cav     := cav_alt[i];
          v_pwt     := 0.0;  -- simplified for alt mold
          v_rwt     := 0.0;
        ELSE
          v_mold_id := mold_prim[i];
          v_csec    := cycle_prim[i];
          v_cav     := cav_prim[i];
          v_pwt     := part_wt_g[i];
          v_rwt     := run_wt_g[i];
        END IF;

        -- Effective cycle sec for generate_series (floor at 25 s for micro machines)
        v_ecsec := GREATEST(v_csec, 25);

        -- Time budget with ±10% variability
        IF shift_n = 1 THEN  -- Day: OEE ~60%
          auto_s := 25920 + (RANDOM() * 4000 - 2000)::INT;
          man_s  :=  6480 + (RANDOM() * 1200 -  600)::INT;
          down_s :=  6480 + (RANDOM() * 1200 -  600)::INT;
        ELSE                  -- Night: OEE ~50%
          auto_s := 21600 + (RANDOM() * 3600 - 1800)::INT;
          man_s  :=  5400 + (RANDOM() * 1000 -  500)::INT;
          down_s :=  7200 + (RANDOM() * 1200 -  600)::INT;
        END IF;
        idle_s := 43200 - auto_s - man_s - down_s;
        IF idle_s < 300 THEN idle_s := 300; auto_s := 43200 - man_s - down_s - idle_s; END IF;

        -- Split auto into 2 blocks (~55% / 45%)
        auto1_s := (auto_s * (0.50 + RANDOM() * 0.10))::INT;
        auto2_s := auto_s - auto1_s;

        -- Split downtime: one bigger event + one smaller
        down1_s := (down_s * (0.55 + RANDOM() * 0.15))::INT;

        -- Cycle counts
        cyc1 := FLOOR(auto1_s::FLOAT / v_ecsec)::INT;
        cyc2 := FLOOR(auto2_s::FLOAT / v_ecsec)::INT;

        -- Current cycle base for this machine
        SELECT cnt INTO cyc_base FROM _seed_cycles WHERE imm_id = v_imm_id;

        -- Plan / actual quantities
        plan_qty   := (cyc1 + cyc2) * v_cav;
        actual_qty := (plan_qty * (0.88 + RANDOM() * 0.22))::INT;
        mat_wt     := actual_qty * v_pwt + ((cyc1 + cyc2)::FLOAT * v_rwt);

        -- ── ShiftTask ────────────────────────────────────────────────────
        v_task_id := gen_random_uuid();
        INSERT INTO "Tasks" (
          "Id","ImmId","MoldId","PersonnelId",
          "PlanQuantity","ActualQuantity","ActualMaterialWeightGrams",
          "Status","PlannedDate",
          "IssuedAt","SetupStartedAt","MoldVerifiedAt","StartedAt","CompletedAt",
          "CreatedAt"
        ) VALUES (
          v_task_id, v_imm_id, v_mold_id, v_adj,
          plan_qty, actual_qty, mat_wt::NUMERIC,
          3,          -- Completed
          shift_start,
          shift_start,
          shift_start + INTERVAL '5 minutes',
          shift_start + man_s * INTERVAL '1 second',
          shift_start + (man_s + LEAST(idle_s/2, 600)) * INTERVAL '1 second',
          shift_end   - INTERVAL '3 minutes',
          shift_start
        );

        -- ── STATUS HISTORY ───────────────────────────────────────────────
        -- Pattern: idle → manual → auto₁ → downtime → auto₂ → [downtime2] → idle

        -- 1. Idle (startup, ~30% of idle budget)
        sa := shift_start;
        sb := sa + (idle_s * 0.30)::INT * INTERVAL '1 second';
        INSERT INTO "ImmStatusHistory" ("ImmId","Status","ChangedAt","EndedAt") VALUES (v_imm_id,'idle',  sa, sb);
        INSERT INTO "Telemetry"        ("ImmId","Timestamp","ParameterName","ValueText") VALUES (v_imm_id, sa,'status','idle');
        sa := sb;

        -- 2. Manual (setup)
        sb := sa + man_s * INTERVAL '1 second';
        INSERT INTO "ImmStatusHistory" ("ImmId","Status","ChangedAt","EndedAt") VALUES (v_imm_id,'manual',sa, sb);
        INSERT INTO "Telemetry"        ("ImmId","Timestamp","ParameterName","ValueText") VALUES (v_imm_id, sa,'status','manual');
        INSERT INTO "Events" ("Id","ImmId","EventType","StartTime","EndTime","PersonnelId","CreatedAt")
          VALUES (gen_random_uuid(), v_imm_id, 2, sa, sb, v_adj, NOW());
        sa := sb;

        -- 3. Auto block 1
        sb := sa + auto1_s * INTERVAL '1 second';
        INSERT INTO "ImmStatusHistory" ("ImmId","Status","ChangedAt","EndedAt") VALUES (v_imm_id,'auto',  sa, sb);
        INSERT INTO "Telemetry"        ("ImmId","Timestamp","ParameterName","ValueText") VALUES (v_imm_id, sa,'status','auto');

        -- Cycles for auto block 1
        INSERT INTO "ImmCycles" ("Id","ImmId","TaskId","MoldId","StartTime","EndTime","DurationSeconds","IsSuccessful","CreatedAt")
        SELECT gen_random_uuid(), v_imm_id, v_task_id, v_mold_id,
               sa + (gs * v_ecsec)             * INTERVAL '1 second',
               sa + (gs * v_ecsec + v_csec -1) * INTERVAL '1 second',
               v_csec + (FLOOR(RANDOM()*6)-3)::INT,
               (RANDOM() > 0.015),
               NOW()
        FROM generate_series(0, GREATEST(0, cyc1 - 1)) AS gs;

        -- Temperature / pressure telemetry (every 10 min during auto)
        INSERT INTO "Telemetry" ("ImmId","Timestamp","ParameterName","ValueNumeric")
        SELECT v_imm_id, sa + gs * INTERVAL '10 minutes',
               'temperature', ROUND((200 + RANDOM()*70)::NUMERIC, 1)
        FROM generate_series(0, GREATEST(0, FLOOR(auto1_s::FLOAT/600)::INT - 1)) AS gs;

        INSERT INTO "Telemetry" ("ImmId","Timestamp","ParameterName","ValueNumeric")
        SELECT v_imm_id, sa + gs * INTERVAL '10 minutes',
               'pressure', ROUND((120 + RANDOM()*80)::NUMERIC, 1)
        FROM generate_series(0, GREATEST(0, FLOOR(auto1_s::FLOAT/600)::INT - 1)) AS gs;

        -- Cycle counter telemetry (every 10 cycles)
        INSERT INTO "Telemetry" ("ImmId","Timestamp","ParameterName","ValueNumeric")
        SELECT v_imm_id, sa + (gs * 10 * v_ecsec) * INTERVAL '1 second',
               'cycles', cyc_base + gs * 10
        FROM generate_series(1, GREATEST(1, FLOOR(cyc1::FLOAT / 10)::INT)) AS gs;

        sa := sb;

        -- 4. Downtime / alarm
        ri := (FLOOR(RANDOM() * 8) + 1)::INT;

        sb := sa + down1_s * INTERVAL '1 second';
        INSERT INTO "ImmStatusHistory" ("ImmId","Status","ChangedAt","EndedAt") VALUES (v_imm_id,'alarm', sa, sb);
        INSERT INTO "Telemetry"        ("ImmId","Timestamp","ParameterName","ValueText") VALUES (v_imm_id, sa,'status','alarm');
        INSERT INTO "Events" ("Id","ImmId","EventType","ReasonId","ReasonName","StartTime","EndTime","PersonnelId","CreatedAt")
          VALUES (gen_random_uuid(), v_imm_id, 0, reason_ids[ri], reason_names[ri], sa, sb, v_adj, NOW());
        sa := sb;

        -- 5. Auto block 2
        sb := sa + auto2_s * INTERVAL '1 second';
        INSERT INTO "ImmStatusHistory" ("ImmId","Status","ChangedAt","EndedAt") VALUES (v_imm_id,'auto',  sa, sb);
        INSERT INTO "Telemetry"        ("ImmId","Timestamp","ParameterName","ValueText") VALUES (v_imm_id, sa,'status','auto');

        INSERT INTO "ImmCycles" ("Id","ImmId","TaskId","MoldId","StartTime","EndTime","DurationSeconds","IsSuccessful","CreatedAt")
        SELECT gen_random_uuid(), v_imm_id, v_task_id, v_mold_id,
               sa + (gs * v_ecsec)             * INTERVAL '1 second',
               sa + (gs * v_ecsec + v_csec -1) * INTERVAL '1 second',
               v_csec + (FLOOR(RANDOM()*6)-3)::INT,
               (RANDOM() > 0.015),
               NOW()
        FROM generate_series(0, GREATEST(0, cyc2 - 1)) AS gs;

        INSERT INTO "Telemetry" ("ImmId","Timestamp","ParameterName","ValueNumeric")
        SELECT v_imm_id, sa + gs * INTERVAL '10 minutes',
               'temperature', ROUND((200 + RANDOM()*70)::NUMERIC, 1)
        FROM generate_series(0, GREATEST(0, FLOOR(auto2_s::FLOAT/600)::INT - 1)) AS gs;

        INSERT INTO "Telemetry" ("ImmId","Timestamp","ParameterName","ValueNumeric")
        SELECT v_imm_id, sa + gs * INTERVAL '10 minutes',
               'pressure', ROUND((120 + RANDOM()*80)::NUMERIC, 1)
        FROM generate_series(0, GREATEST(0, FLOOR(auto2_s::FLOAT/600)::INT - 1)) AS gs;

        INSERT INTO "Telemetry" ("ImmId","Timestamp","ParameterName","ValueNumeric")
        SELECT v_imm_id, sa + (gs * 10 * v_ecsec) * INTERVAL '1 second',
               'cycles', cyc_base + cyc1 + gs * 10
        FROM generate_series(1, GREATEST(1, FLOOR(cyc2::FLOAT / 10)::INT)) AS gs;

        sa := sb;

        -- 6. Idle (end of shift, remaining time)
        sb := shift_end;
        INSERT INTO "ImmStatusHistory" ("ImmId","Status","ChangedAt","EndedAt") VALUES (v_imm_id,'idle',  sa, sb);
        INSERT INTO "Telemetry"        ("ImmId","Timestamp","ParameterName","ValueText") VALUES (v_imm_id, sa,'status','idle');

        -- Advance cycle base
        UPDATE _seed_cycles SET cnt = cyc_base + cyc1 + cyc2 WHERE imm_id = v_imm_id;

      END LOOP; -- machine
    END LOOP;   -- shift
  END LOOP;     -- day

  DROP TABLE IF EXISTS _seed_cycles;

  RAISE NOTICE 'Seed complete.';
  RAISE NOTICE '  Tasks    : %', (SELECT COUNT(*) FROM "Tasks"            WHERE "PlannedDate" >= '2026-04-01' AND "PlannedDate" < '2026-05-01');
  RAISE NOTICE '  Cycles   : %', (SELECT COUNT(*) FROM "ImmCycles"        WHERE "StartTime"  >= '2026-04-01' AND "StartTime"  < '2026-05-01');
  RAISE NOTICE '  Telemetry: %', (SELECT COUNT(*) FROM "Telemetry"        WHERE "Timestamp"  >= '2026-04-01' AND "Timestamp"  < '2026-05-01');
  RAISE NOTICE '  Events   : %', (SELECT COUNT(*) FROM "Events"           WHERE "StartTime"  >= '2026-04-01' AND "StartTime"  < '2026-05-01');

END $$;
