namespace Wintime.Control.Core.DTOs.Tasks;

/// <summary>
/// Задания наладчика, разложенные по разделам для планшетного интерфейса.
/// </summary>
/// <remarks>
/// Принадлежность к смене определяется по <see cref="TaskDto.IssuedAt"/> относительно
/// границы текущей смены (см. фронтенд <c>computeShiftBoundary</c>):
/// <list type="bullet">
///   <item><see cref="CurrentShift"/> — выдано в текущей (или ближайшей) смене, любой статус.</item>
///   <item><see cref="Unfinished"/> — выдано в прошедших сменах, но ещё не завершено.</item>
///   <item><see cref="Archive"/> — завершённые/закрытые задания прошедших смен (с пагинацией).</item>
/// </list>
/// </remarks>
public class MyTasksDto
{
    public List<TaskDto> CurrentShift { get; set; } = new();
    public List<TaskDto> Unfinished { get; set; } = new();
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
