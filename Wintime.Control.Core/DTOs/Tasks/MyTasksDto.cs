namespace Wintime.Control.Core.DTOs.Tasks;

/// <summary>
/// Задания наладчика по разделам планшета (PZP-13):
/// <see cref="Active"/> — в наладке и в работе; <see cref="Queue"/> — выданные, по возрастанию
/// планового начала; <see cref="Archive"/> — завершённые/закрытые, новые сверху, с пагинацией.
/// </summary>
public class MyTasksDto
{
    public List<TaskDto> Active { get; set; } = new();
    public List<TaskDto> Queue { get; set; } = new();
    public PagedTasksDto Archive { get; set; } = new();
}

/// <summary>Страница архивных заданий.</summary>
public class PagedTasksDto
{
    public List<TaskDto> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
