-- Проверки инвариантов демо-сида (deploy/demo/test-seed.ps1). Каждая строка — «проверка|нарушений»,
-- всё должно быть 0. Запускать сразу после сида: «будущее» — относительно now() проверки.
SELECT 'future_rows', (
      (SELECT count(*) FROM "ShiftTasks"
        WHERE GREATEST("IssuedAt","SetupStartedAt","MoldVerifiedAt","StartedAt","CompletedAt","ClosedAt",
                       "CreatedAt","UpdatedAt") > now()
           OR ("PlannedDate" + COALESCE("PlannedSetupStartTime", time '00:00')) AT TIME ZONE 'Europe/Moscow' > now())
    + (SELECT count(*) FROM "ImmCycles"        WHERE GREATEST("StartTime","EndTime","CreatedAt") > now())
    + (SELECT count(*) FROM "ImmStatusHistory" WHERE GREATEST("ChangedAt","EndedAt") > now())
    + (SELECT count(*) FROM "Events"           WHERE GREATEST("StartTime","EndTime","CreatedAt") > now())
    + (SELECT count(*) FROM "Telemetry"        WHERE "Timestamp" > now())
    + (SELECT count(*) FROM "Orders"           WHERE GREATEST("OrderDate","DueDate","CreatedAt") > now())
    + (SELECT count(*) FROM "UnplannedRuns"    WHERE GREATEST("StartTime","ClosedAt","CreatedAt") > now()))
UNION ALL SELECT 'type_without_product', count(*) FROM "ProductTypes" pt
  WHERE NOT EXISTS (SELECT 1 FROM "Products" p WHERE p."ProductTypeId" = pt."Id")
UNION ALL SELECT 'active_type_without_active_product', count(*) FROM "ProductTypes" pt
  WHERE pt."IsActive" AND NOT EXISTS (SELECT 1 FROM "Products" p WHERE p."ProductTypeId" = pt."Id" AND p."IsActive")
UNION ALL SELECT 'full_article_clash', count(*) FROM "Products" p JOIN "ProductTypes" pt ON pt."Article" = p."FullArticle"
UNION ALL SELECT 'mold_without_type', count(*) FROM "Molds" WHERE "ProductTypeId" IS NULL
UNION ALL SELECT 'task_product_not_of_mold_type', count(*) FROM "ShiftTasks" t
  JOIN "Molds" m ON m."Id" = t."MoldId" JOIN "Products" p ON p."Id" = t."ProductId"
  WHERE p."ProductTypeId" <> m."ProductTypeId"
UNION ALL SELECT 'task_order_product_mismatch', count(*) FROM "ShiftTasks" t
  JOIN "Orders" o ON o."Id" = t."OrderId" WHERE o."ProductId" <> t."ProductId"
UNION ALL SELECT 'task_imm_incompatible', count(*) FROM "ShiftTasks" t
  WHERE EXISTS (SELECT 1 FROM "MoldCompatibleImms" c WHERE c."MoldId" = t."MoldId")
    AND NOT EXISTS (SELECT 1 FROM "MoldCompatibleImms" c WHERE c."MoldId" = t."MoldId" AND c."ImmId" = t."ImmId")
UNION ALL SELECT 'task_without_cycle_norm', count(*) FROM "ShiftTasks" WHERE COALESCE("PlannedFullCycleSeconds", 0) <= 0
-- ТПА останавливается по числу смыканий ⌈План/Гнёзда⌉, поэтому выпуск может превысить план не больше чем на гнёзда − 1
UNION ALL SELECT 'output_over_plan', count(*) FROM "ShiftTasks" t JOIN "Molds" m ON m."Id" = t."MoldId"
  WHERE t."ActualQuantity" >= t."PlanQuantity" + m."Cavities"
UNION ALL SELECT 'imm_multiple_active_tasks', count(*) FROM
  (SELECT "ImmId" FROM "ShiftTasks" WHERE "Status" IN (2, 5) GROUP BY "ImmId" HAVING count(*) > 1) x
UNION ALL SELECT 'issued_without_adjuster', count(*) FROM "ShiftTasks" WHERE "Status" = 1 AND "PersonnelId" IS NULL
UNION ALL SELECT 'demo_adjuster_issued_not_7', abs(7 - (SELECT count(*) FROM "ShiftTasks" t
  JOIN "Users" u ON u."Id" = t."PersonnelId" WHERE u."UserName" = 'adjuster' AND t."Status" = 1))
UNION ALL SELECT 'imm_open_status_not_1', count(*) FROM "Imms" i
  WHERE (SELECT count(*) FROM "ImmStatusHistory" h WHERE h."ImmId" = i."Id" AND h."EndedAt" IS NULL) <> 1
UNION ALL SELECT 'status_overlap', count(*) FROM
  (SELECT "EndedAt", lead("ChangedAt") OVER (PARTITION BY "ImmId" ORDER BY "ChangedAt") AS nxt
   FROM "ImmStatusHistory") s
  WHERE s.nxt < COALESCE(s."EndedAt", 'infinity')
UNION ALL SELECT 'cycle_outside_task_window', count(*) FROM "ImmCycles" c JOIN "ShiftTasks" t ON t."Id" = c."TaskId"
  WHERE c."StartTime" < t."StartedAt" OR c."EndTime" > COALESCE(t."CompletedAt", now())
UNION ALL SELECT 'completed_order_with_open_task', count(*) FROM "Orders" o
  JOIN "ShiftTasks" t ON t."OrderId" = o."Id" WHERE o."Status" = 1 AND t."Status" NOT IN (3, 4)
UNION ALL SELECT 'active_imm_without_recent_telemetry', count(*) FROM "Imms" i
  WHERE i."IsActive" AND NOT EXISTS
    (SELECT 1 FROM "Telemetry" tl WHERE tl."ImmId" = i."Id" AND tl."Timestamp" > now() - interval '3 days')
UNION ALL SELECT 'no_recent_unclassified_downtime',
  CASE WHEN EXISTS (SELECT 1 FROM "Events" WHERE "ReasonId" IS NULL AND "StartTime" > now() - interval '3 hours')
       THEN 0 ELSE 1 END
UNION ALL SELECT 'too_few_orders', CASE WHEN (SELECT count(*) FROM "Orders") >= 10 THEN 0 ELSE 1 END
UNION ALL SELECT 'heartbeat_not_t0',
  CASE WHEN (SELECT "LastHeartbeatAt" FROM "AppHeartbeat" WHERE "Id" = 1) <= now() THEN 0 ELSE 1 END;
