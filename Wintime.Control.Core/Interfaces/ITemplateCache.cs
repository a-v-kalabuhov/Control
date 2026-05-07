using Wintime.Control.Core.Cache;
using Wintime.Control.Core.Entities;

namespace Wintime.Control.Core.Interfaces;

/// <summary>
/// Кеш шаблонов.
/// </summary>
public interface ITemplateCache
{
    CachedTemplate? GetById(Guid id);
    /// <summary>
    /// Добавить шаблон в кеш или обновить ранее добавленный.
    /// </summary>
    /// <param name="template"></param>
    void Upsert(Template template);
    /// <summary>
    /// Удалить шаблон из кеша.
    /// </summary>
    /// <param name="id">Id шаблона в БД.</param>
    void Remove(Guid id);
    IReadOnlyList<CachedTemplate> GetAll();
}
