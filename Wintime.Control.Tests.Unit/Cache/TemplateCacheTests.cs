using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Cache;

namespace Wintime.Control.Tests.Unit.Cache;

public class TemplateCacheTests
{
    // =========================================================================
    // GetById
    // =========================================================================

    /// <summary>
    /// Если шаблон ни разу не добавлялся в кэш, <c>GetById</c> должен вернуть
    /// <c>null</c>, а не выбросить исключение.
    /// </summary>
    [Fact]
    public void GetById_UnknownTemplate_ReturnsNull()
    {
        var cache = new TemplateCache();

        cache.GetById(Guid.NewGuid()).Should().BeNull();
    }

    /// <summary>
    /// После <c>Upsert</c> метод <c>GetById</c> должен возвращать ненулевой результат
    /// с тем же <c>Id</c>, что был у переданного шаблона.
    /// </summary>
    [Fact]
    public void GetById_AfterUpsert_ReturnsEntry()
    {
        var cache = new TemplateCache();
        var template = MakeTemplate();

        cache.Upsert(template);

        cache.GetById(template.Id).Should().NotBeNull();
    }

    // =========================================================================
    // Upsert — базовое поведение
    // =========================================================================

    /// <summary>
    /// После <c>Upsert</c> кэшированный объект должен содержать корректные
    /// <c>Id</c>, <c>Name</c> и <c>UpdatedAt</c>, взятые из переданной сущности.
    /// </summary>
    [Fact]
    public void Upsert_StoresIdNameAndUpdatedAt()
    {
        var cache = new TemplateCache();
        var updatedAt = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var template = MakeTemplate(name: "Mold-Type-A", updatedAt: updatedAt);

        cache.Upsert(template);

        var cached = cache.GetById(template.Id)!;
        cached.Id.Should().Be(template.Id);
        cached.Name.Should().Be("Mold-Type-A");
        cached.UpdatedAt.Should().Be(updatedAt);
    }

    /// <summary>
    /// Повторный вызов <c>Upsert</c> с тем же <c>Id</c> должен перезаписать
    /// предыдущую запись. Это позволяет обновлять шаблон без перезапуска приложения.
    /// </summary>
    [Fact]
    public void Upsert_SameIdTwice_ReplacesExistingEntry()
    {
        var cache = new TemplateCache();
        var id = Guid.NewGuid();
        cache.Upsert(MakeTemplate(id, name: "Old Name"));

        cache.Upsert(MakeTemplate(id, name: "New Name"));

        cache.GetById(id)!.Name.Should().Be("New Name");
    }

    // =========================================================================
    // Remove
    // =========================================================================

    /// <summary>
    /// После <c>Remove</c> метод <c>GetById</c> должен вернуть <c>null</c> —
    /// шаблон больше не присутствует в кэше.
    /// </summary>
    [Fact]
    public void Remove_KnownTemplate_EntryBecomesNull()
    {
        var cache = new TemplateCache();
        var template = MakeTemplate();
        cache.Upsert(template);

        cache.Remove(template.Id);

        cache.GetById(template.Id).Should().BeNull();
    }

    /// <summary>
    /// Вызов <c>Remove</c> для несуществующего идентификатора не должен
    /// приводить к исключению — операция должна завершиться без ошибок.
    /// </summary>
    [Fact]
    public void Remove_UnknownTemplate_DoesNotThrow()
    {
        var cache = new TemplateCache();

        var act = () => cache.Remove(Guid.NewGuid());

        act.Should().NotThrow();
    }

    // =========================================================================
    // GetAll
    // =========================================================================

    /// <summary>
    /// Когда кэш пуст, <c>GetAll</c> должен возвращать пустой список,
    /// а не <c>null</c>.
    /// </summary>
    [Fact]
    public void GetAll_EmptyCache_ReturnsEmptyList()
    {
        new TemplateCache().GetAll().Should().BeEmpty();
    }

    /// <summary>
    /// <c>GetAll</c> должен возвращать все добавленные шаблоны;
    /// количество элементов должно совпадать с числом вызовов <c>Upsert</c>.
    /// </summary>
    [Fact]
    public void GetAll_MultipleTemplates_ReturnsAll()
    {
        var cache = new TemplateCache();
        cache.Upsert(MakeTemplate());
        cache.Upsert(MakeTemplate());
        cache.Upsert(MakeTemplate());

        cache.GetAll().Should().HaveCount(3);
    }

    /// <summary>
    /// После <c>Remove</c> метод <c>GetAll</c> не должен содержать удалённый
    /// шаблон — список сокращается на один элемент.
    /// </summary>
    [Fact]
    public void GetAll_AfterRemove_DoesNotContainRemovedTemplate()
    {
        var cache = new TemplateCache();
        var t1 = MakeTemplate();
        var t2 = MakeTemplate();
        cache.Upsert(t1);
        cache.Upsert(t2);

        cache.Remove(t1.Id);

        cache.GetAll().Should().HaveCount(1)
            .And.NotContain(t => t.Id == t1.Id);
    }

    // =========================================================================
    // Parse — device_timeout_seconds
    // =========================================================================

    /// <summary>
    /// Значение <c>device_timeout_seconds</c> из JSON-конфига должно попасть
    /// в поле <c>DeviceTimeoutSeconds</c> кэшированного шаблона.
    /// </summary>
    [Fact]
    public void Upsert_JsonWithTimeout_ParsesDeviceTimeoutSeconds()
    {
        var cache = new TemplateCache();
        var template = MakeTemplate(json: """{"device_timeout_seconds": 120, "sensors": []}""");

        cache.Upsert(template);

        cache.GetById(template.Id)!.DeviceTimeoutSeconds.Should().Be(120);
    }

    /// <summary>
    /// Если поле <c>device_timeout_seconds</c> отсутствует в JSON, должно
    /// использоваться значение по умолчанию — 30 секунд.
    /// </summary>
    [Fact]
    public void Upsert_JsonWithoutTimeout_DefaultsTo30Seconds()
    {
        var cache = new TemplateCache();
        var template = MakeTemplate(json: """{"sensors": []}""");

        cache.Upsert(template);

        cache.GetById(template.Id)!.DeviceTimeoutSeconds.Should().Be(30);
    }

    /// <summary>
    /// Если <c>JsonConfig</c> пустой или равен <c>"{}"</c>, список сенсоров должен
    /// быть пуст, а таймаут — принять значение по умолчанию 30 секунд.
    /// </summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("")]
    [InlineData("   ")]
    public void Upsert_EmptyOrBlankJson_ReturnsDefaultsAndEmptySensors(string json)
    {
        var cache = new TemplateCache();
        var template = MakeTemplate(json: json);

        cache.Upsert(template);

        var cached = cache.GetById(template.Id)!;
        cached.DeviceTimeoutSeconds.Should().Be(30);
        cached.Sensors.Should().BeEmpty();
    }

    // =========================================================================
    // Parse — сенсоры: структура
    // =========================================================================

    /// <summary>
    /// Поля <c>name</c> и <c>field</c> из JSON-конфига должны попасть соответственно
    /// в <c>SensorTemplate.Name</c> и <c>SensorTemplate.ParameterName</c>.
    /// </summary>
    [Fact]
    public void Upsert_SensorWithNameAndField_ParsesNameAndParameterName()
    {
        var cache = new TemplateCache();
        var template = MakeTemplate(json: """
            {
                "sensors": [
                    { "name": "Температура", "field": "temp", "type": "float" }
                ]
            }
            """);

        cache.Upsert(template);

        var sensor = cache.GetById(template.Id)!.Sensors.Single();
        sensor.Name.Should().Be("Температура");
        sensor.ParameterName.Should().Be("temp");
    }

    /// <summary>
    /// Поле <c>type</c> из JSON-конфига должно попасть в <c>SensorTemplate.ParameterType</c>.
    /// </summary>
    [Theory]
    [InlineData("float")]
    [InlineData("int")]
    [InlineData("boolean")]
    [InlineData("string")]
    [InlineData("cycleCounter")]
    public void Upsert_SensorWithType_ParsesParameterType(string type)
    {
        var cache = new TemplateCache();
        var template = MakeTemplate(json: $$"""
            {
                "sensors": [
                    { "name": "s", "field": "s", "type": "{{type}}" }
                ]
            }
            """);

        cache.Upsert(template);

        cache.GetById(template.Id)!.Sensors.Single().ParameterType.Should().Be(type);
    }

    /// <summary>
    /// Если поле <c>type</c> отсутствует в описании сенсора, должно
    /// использоваться значение по умолчанию <c>"float"</c>.
    /// </summary>
    [Fact]
    public void Upsert_SensorWithoutType_DefaultsToFloat()
    {
        var cache = new TemplateCache();
        var template = MakeTemplate(json: """
            {
                "sensors": [
                    { "name": "s", "field": "s" }
                ]
            }
            """);

        cache.Upsert(template);

        cache.GetById(template.Id)!.Sensors.Single().ParameterType.Should().Be("float");
    }

    // =========================================================================
    // Parse — сенсоры: threshold
    // =========================================================================

    /// <summary>
    /// Значение поля <c>threshold</c> из JSON-конфига должно точно попасть
    /// в <c>SensorTemplate.Threshold</c>.
    /// </summary>
    [Fact]
    public void Upsert_SensorWithThreshold_ParsesThreshold()
    {
        var cache = new TemplateCache();
        var template = MakeTemplate(json: """
            {
                "sensors": [
                    { "name": "s", "field": "s", "type": "float", "threshold": 0.5 }
                ]
            }
            """);

        cache.Upsert(template);

        cache.GetById(template.Id)!.Sensors.Single().Threshold.Should().Be(0.5m);
    }

    /// <summary>
    /// Если поле <c>threshold</c> отсутствует в описании сенсора, должно
    /// использоваться значение по умолчанию <c>0</c> (COV-фильтрация отключена).
    /// </summary>
    [Fact]
    public void Upsert_SensorWithoutThreshold_DefaultsToZero()
    {
        var cache = new TemplateCache();
        var template = MakeTemplate(json: """
            {
                "sensors": [
                    { "name": "s", "field": "s", "type": "float" }
                ]
            }
            """);

        cache.Upsert(template);

        cache.GetById(template.Id)!.Sensors.Single().Threshold.Should().Be(0m);
    }

    // =========================================================================
    // Parse — сенсоры: allowed_values и required
    // =========================================================================

    /// <summary>
    /// Массив <c>allowed_values</c> из JSON-конфига должен попасть в
    /// <c>SensorTemplate.AllowedValues</c> с сохранением всех элементов и порядка.
    /// </summary>
    [Fact]
    public void Upsert_SensorWithAllowedValues_ParsesAllowedValues()
    {
        var cache = new TemplateCache();
        var template = MakeTemplate(json: """
            {
                "sensors": [
                    {
                        "name": "mode", "field": "mode", "type": "string",
                        "allowed_values": ["run", "stop", "idle"]
                    }
                ]
            }
            """);

        cache.Upsert(template);

        cache.GetById(template.Id)!.Sensors.Single().AllowedValues
            .Should().BeEquivalentTo(["run", "stop", "idle"], o => o.WithStrictOrdering());
    }

    /// <summary>
    /// Если поле <c>allowed_values</c> отсутствует, свойство
    /// <c>SensorTemplate.AllowedValues</c> должно быть <c>null</c>.
    /// </summary>
    [Fact]
    public void Upsert_SensorWithoutAllowedValues_AllowedValuesIsNull()
    {
        var cache = new TemplateCache();
        var template = MakeTemplate(json: """
            {
                "sensors": [
                    { "name": "s", "field": "s", "type": "float" }
                ]
            }
            """);

        cache.Upsert(template);

        cache.GetById(template.Id)!.Sensors.Single().AllowedValues.Should().BeNull();
    }

    /// <summary>
    /// Если в описании сенсора задано <c>"required": true</c>, свойство
    /// <c>SensorTemplate.Required</c> должно быть <c>true</c>.
    /// </summary>
    [Fact]
    public void Upsert_SensorWithRequiredTrue_SetsRequiredTrue()
    {
        var cache = new TemplateCache();
        var template = MakeTemplate(json: """
            {
                "sensors": [
                    { "name": "s", "field": "s", "type": "float", "required": true }
                ]
            }
            """);

        cache.Upsert(template);

        cache.GetById(template.Id)!.Sensors.Single().Required.Should().BeTrue();
    }

    /// <summary>
    /// Если поле <c>required</c> отсутствует или равно <c>false</c>, свойство
    /// <c>SensorTemplate.Required</c> должно быть <c>false</c>.
    /// </summary>
    [Theory]
    [InlineData("""{ "name": "s", "field": "s", "type": "float" }""")]
    [InlineData("""{ "name": "s", "field": "s", "type": "float", "required": false }""")]
    public void Upsert_SensorWithoutOrFalseRequired_SetsRequiredFalse(string sensorJson)
    {
        var cache = new TemplateCache();
        var template = MakeTemplate(json: $$"""{"sensors": [{{sensorJson}}]}""");

        cache.Upsert(template);

        cache.GetById(template.Id)!.Sensors.Single().Required.Should().BeFalse();
    }

    // =========================================================================
    // Parse — несколько сенсоров
    // =========================================================================

    /// <summary>
    /// Все сенсоры из массива <c>sensors</c> должны быть разобраны и сохранены;
    /// порядок сенсоров и их <c>ParameterName</c> должны соответствовать JSON.
    /// </summary>
    [Fact]
    public void Upsert_MultipleSensors_ParsesAllInOrder()
    {
        var cache = new TemplateCache();
        var template = MakeTemplate(json: """
            {
                "sensors": [
                    { "name": "Temperature", "field": "temp",   "type": "float" },
                    { "name": "Cycles",      "field": "cycles", "type": "cycleCounter" },
                    { "name": "Status",      "field": "status", "type": "string" }
                ]
            }
            """);

        cache.Upsert(template);

        var sensors = cache.GetById(template.Id)!.Sensors;
        sensors.Should().HaveCount(3);
        sensors.Select(s => s.ParameterName)
            .Should().ContainInOrder("temp", "cycles", "status");
    }

    // =========================================================================
    // Вспомогательные методы
    // =========================================================================

    private static Template MakeTemplate(
        Guid? id = null,
        string name = "Test Template",
        string json = "{}",
        DateTime? updatedAt = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            JsonConfig = json,
            UpdatedAt = updatedAt ?? DateTime.UtcNow
        };
}
