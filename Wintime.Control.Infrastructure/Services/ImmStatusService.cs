using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wintime.Control.Core.Cache;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Task = System.Threading.Tasks.Task;

namespace Wintime.Control.Infrastructure.Services;

/// <summary>
/// Реализация <see cref="IImmStatusService"/>, синхронизирующая статус ТПА
/// между базой данных и in-memory кешем.
/// </summary>
/// <remarks>
/// При обновлении статуса сначала закрывается открытая запись в <c>ImmStatusHistory</c>
/// (проставляется <c>EndedAt</c>), затем добавляется новая запись, после чего кеш
/// обновляется. Операции чтения обращаются только к кешу и не нагружают БД.
/// </remarks>
public class ImmStatusService : IImmStatusService
{
    private readonly ControlDbContext _dbContext;
    private readonly IImmStatusCache _statusCache;
    private readonly ILogger<ImmStatusService> _logger;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ImmStatusService"/>.
    /// </summary>
    /// <param name="dbContext">Контекст EF Core для записи истории статусов.</param>
    /// <param name="statusCache">In-memory кеш текущих статусов ТПА.</param>
    /// <param name="logger">Логгер сервиса.</param>
    public ImmStatusService(
        ControlDbContext dbContext,
        IImmStatusCache statusCache,
        ILogger<ImmStatusService> logger)
    {
        _dbContext = dbContext;
        _statusCache = statusCache;
        _logger = logger;
    }

    /// <summary>
    /// Переводит ТПА в новый статус, записывая переход в историю и обновляя кеш.
    /// </summary>
    /// <remarks>
    /// Если текущий статус в кеше совпадает с <paramref name="newStatus"/>,
    /// метод завершается без обращения к БД. Иначе открытая запись истории
    /// закрывается (<c>EndedAt = changedAt</c>) и добавляется новая с <c>EndedAt = null</c>.
    /// </remarks>
    /// <param name="immId">Идентификатор ТПА.</param>
    /// <param name="newStatus">Новый статус (например, <c>"Online"</c>, <c>"Offline"</c>).</param>
    /// <param name="changedAt">Момент перехода в UTC.</param>
    public async Task UpdateStatusAsync(Guid immId, string newStatus, DateTime changedAt)
    {
        var current = _statusCache.GetStatus(immId);
        if (current == newStatus)
            return;
        
        var openRecord = await _dbContext.ImmStatusHistory
            .Where(h => h.ImmId == immId && h.EndedAt == null)
            .FirstOrDefaultAsync();

        if (openRecord != null)
            openRecord.EndedAt = changedAt;

        _dbContext.ImmStatusHistory.Add(new ImmStatusHistory
        {
            ImmId = immId,
            Status = newStatus,
            ChangedAt = changedAt,
            EndedAt = null
        });

        await _dbContext.SaveChangesAsync();

        _statusCache.SetStatus(immId, newStatus, changedAt);

        _logger.LogInformation("IMM {ImmId} status changed: {OldStatus} → {NewStatus}", immId, current ?? "unknown", newStatus);
    }

    /// <summary>
    /// Возвращает текущий статус ТПА из кеша.
    /// </summary>
    /// <param name="immId">Идентификатор ТПА.</param>
    /// <returns>Строка статуса или <see langword="null"/>, если ТПА не найден в кеше.</returns>
    public string? GetCurrentStatus(Guid immId)
        => _statusCache.GetStatus(immId);

    /// <summary>
    /// Возвращает снимок текущих статусов всех ТПА из кеша.
    /// </summary>
    /// <returns>Неизменяемый список записей <see cref="ImmStatusEntry"/>.</returns>
    public IReadOnlyList<ImmStatusEntry> GetAllStatuses()
        => _statusCache.GetAll();
}
