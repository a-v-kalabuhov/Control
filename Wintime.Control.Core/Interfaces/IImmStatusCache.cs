using Wintime.Control.Core.Cache;

namespace Wintime.Control.Core.Interfaces;

/// <summary>
/// Контракт in-memory кеша текущих статусов ТПА.
/// </summary>
/// <remarks>
/// Статус отражает текущий режим работы ТПА и принимает одно из следующих значений:
/// <list type="table">
///   <listheader><term>Значение</term><description>Описание</description></listheader>
///   <item><term><c>Auto</c></term><description>Автоматический режим — машина работает по программе.</description></item>
///   <item><term><c>Manual</c></term><description>Ручной режим — оператор управляет машиной вручную.</description></item>
///   <item><term><c>Alarm</c></term><description>Аварийный режим — зафиксирована ошибка или тревога.</description></item>
///   <item><term><c>Idle</c></term><description>Простой — машина включена, но не производит полезную работу.</description></item>
///   <item><term><c>Offline</c></term><description>Нет связи — телеметрия не поступала дольше таймаута устройства.</description></item>
/// </list>
/// Статус определяется из поля <c>Mode</c> входящего MQTT-сообщения телеметрии.
/// </remarks>
public interface IImmStatusCache
{
    /// <summary>
    /// Возвращает текущий статус ТПА.
    /// </summary>
    /// <param name="immId">Идентификатор ТПА.</param>
    /// <returns>Строка статуса или <see langword="null"/>, если ТПА не найден в кеше.</returns>
    string? GetStatus(Guid immId);

    /// <summary>
    /// Возвращает полную запись статуса ТПА, включая метку времени последней смены.
    /// </summary>
    /// <param name="immId">Идентификатор ТПА.</param>
    /// <returns><see cref="ImmStatusEntry"/> или <see langword="null"/>, если ТПА не найден в кеше.</returns>
    ImmStatusEntry? GetEntry(Guid immId);

    /// <summary>
    /// Устанавливает или перезаписывает статус ТПА в кеше.
    /// </summary>
    /// <param name="immId">Идентификатор ТПА.</param>
    /// <param name="status">Новый статус (например, <c>"Online"</c>, <c>"Offline"</c>).</param>
    /// <param name="sinceUtc">Момент перехода в данный статус (UTC).</param>
    void SetStatus(Guid immId, string status, DateTime sinceUtc);

    /// <summary>
    /// Возвращает снимок текущих статусов всех ТПА.
    /// </summary>
    /// <returns>Неизменяемый список записей <see cref="ImmStatusEntry"/>.</returns>
    IReadOnlyList<ImmStatusEntry> GetAll();
}
