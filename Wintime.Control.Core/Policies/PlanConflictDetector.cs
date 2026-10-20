namespace Wintime.Control.Core.Policies;

/// <summary>Ресурс, за который спорят два задания.</summary>
public enum ConflictResource { Imm, Mold }

/// <summary>
/// Вид конфликта: <c>Plan</c> — пересекаются только плановые отрезки (ошибка планировщика);
/// <c>Deviation</c> — в паре есть факт или прогноз (выполнение ушло от плана).
/// </summary>
public enum ConflictKind { Plan, Deviation }

/// <summary>Задание для детектора: ресурсы и отрезки занятости (<see cref="TaskOccupancy"/>).</summary>
public sealed record ConflictTaskInput(Guid TaskId, Guid ImmId, Guid MoldId, IReadOnlyList<OccupancySegment> Segments);

/// <summary>Пара заданий в конфликте по одному ресурсу; <c>OverlapSeconds</c> — суммарная длина пересечения.</summary>
public sealed record PlanConflict(Guid TaskId, Guid OtherTaskId, ConflictResource Resource, ConflictKind Kind,
                                  DateTime OverlapStart, DateTime OverlapEnd, long OverlapSeconds);

/// <summary>
/// Конфликты плана (PZP-07b, ADR-0022): пары заданий на одном ТПА или с одной ПФ, у которых суммарное пересечение
/// отрезков строго больше порога. Чистая функция, не хранится, ничего не блокирует. Сравнение — внутри групп
/// по ТПА и по ПФ (группы малы: задания одного станка/формы).
/// </summary>
public static class PlanConflictDetector
{
    public static IReadOnlyList<PlanConflict> Detect(IReadOnlyCollection<ConflictTaskInput> tasks, TimeSpan threshold)
    {
        var ordered = tasks
            .Where(t => t.Segments.Count > 0)
            .OrderBy(t => t.Segments.Min(s => s.Start))
            .ThenBy(t => t.TaskId)
            .ToList();

        var result = new List<PlanConflict>();
        foreach (var group in ordered.GroupBy(t => t.ImmId))
            Compare(group.ToList(), ConflictResource.Imm, threshold, result);
        foreach (var group in ordered.GroupBy(t => t.MoldId))
            Compare(group.ToList(), ConflictResource.Mold, threshold, result);

        return result
            .OrderBy(c => c.Kind)
            .ThenBy(c => c.OverlapStart)
            .ThenBy(c => c.TaskId)
            .ThenBy(c => c.Resource)
            .ToList();
    }

    private static void Compare(List<ConflictTaskInput> group, ConflictResource resource, TimeSpan threshold,
                                List<PlanConflict> result)
    {
        for (var i = 0; i < group.Count; i++)
        for (var j = i + 1; j < group.Count; j++)
        {
            var a = group[i];
            var b = group[j];
            var total = TimeSpan.Zero;
            DateTime? first = null, last = null;

            foreach (var sa in a.Segments)
            foreach (var sb in b.Segments)
            {
                var start = sa.Start > sb.Start ? sa.Start : sb.Start;
                var end = sa.End < sb.End ? sa.End : sb.End;
                if (end <= start)
                    continue;
                total += end - start;
                if (first is null || start < first) first = start;
                if (last is null || end > last) last = end;
            }

            if (total <= threshold || first is null || last is null)
                continue;

            var kind = PlanOnly(a) && PlanOnly(b) ? ConflictKind.Plan : ConflictKind.Deviation;
            result.Add(new PlanConflict(a.TaskId, b.TaskId, resource, kind, first.Value, last.Value,
                                        (long)Math.Ceiling(total.TotalSeconds)));
        }
    }

    private static bool PlanOnly(ConflictTaskInput task) => task.Segments.All(s => s.Kind == OccupancyKind.Plan);
}
