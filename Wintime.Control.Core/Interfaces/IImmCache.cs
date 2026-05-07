using Wintime.Control.Core.Cache;

namespace Wintime.Control.Core.Interfaces;

/// <summary>
/// Потокобезопасный in-memory кеш ТПА.
/// Хранит последние значения датчиков и время последнего сообщения для каждого ТПА.
/// Используется для COV-фильтрации и определения статуса online/offline.
/// </summary>
public interface IImmCache
{
    /// <summary>
    /// Возвращает запись кеша для ТПА или null, если ТПА не зарегистрирован в кеше.
    /// </summary>
    ImmCacheEntry? GetEntry(Guid immId);

    /// <summary>
    /// Добавляет новый ТПА в кеш с пустыми значениями датчиков.
    /// Если ТПА уже есть в кеше — не изменяет существующую запись.
    /// </summary>
    void AddImm(Guid immId, int timeoutSeconds);

    /// <summary>
    /// Удаляет ТПА из кеша (при удалении или деактивации оборудования).
    /// </summary>
    void RemoveImm(Guid immId);

    /// <summary>
    /// Обновляет запись кеша: время последнего сообщения и полный словарь значений датчиков.
    /// Если ТПА отсутствует в кеше — создаёт запись с указанным <paramref name="timeoutSeconds"/>.
    /// </summary>
    void UpdateEntry(Guid immId, DateTime messageAt, int timeoutSeconds, IReadOnlyDictionary<string, string> sensorValues);

    /// <summary>
    /// Возвращает снимок всех записей кеша на момент вызова.
    /// </summary>
    IReadOnlyList<ImmCacheEntry> GetAll();
}
