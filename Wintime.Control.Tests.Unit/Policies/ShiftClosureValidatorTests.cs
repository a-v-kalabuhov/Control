using FluentAssertions;
using Wintime.Control.Core.DTOs.ShiftClosures;
using Wintime.Control.Core.Exceptions;
using Wintime.Control.Core.Policies;

namespace Wintime.Control.Tests.Unit.Policies;

public class ShiftClosureValidatorTests
{
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly DateTime Start = Utc("2026-11-18T08:00:00");
    private static readonly DateTime End = Utc("2026-11-18T20:00:00");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Draft_does_not_require_completeness_but_close_does(bool requireCompleteness)
    {
        var entry = Entry(fallback: true, automaticOutput: 5);

        var errors = ShiftClosureValidator.Validate(entry, requireCompleteness);

        if (requireCompleteness)
        {
            errors.Should().BeEquivalentTo(new[]
            {
                Error("manualFullCycleSeconds", "required"),
                Error("manualGoodQuantity", "required"),
                Error("manualDefectQuantity", "required"),
                Error("actualPartWeightGrams", "required"),
                Error("actualPackagingUnit", "required")
            });
        }
        else
        {
            errors.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Supplied_weight_cycle_and_packaging_must_be_positive_at_both_stages(
        bool requireCompleteness)
    {
        var entry = Entry(actualWeight: 0, manualCycle: 0, actualPackaging: 0);

        var errors = ShiftClosureValidator.Validate(entry, requireCompleteness);

        errors.Should().BeEquivalentTo(new[]
        {
            Error("actualPartWeightGrams", "mustBePositive"),
            Error("manualFullCycleSeconds", "mustBePositive"),
            Error("actualPackagingUnit", "mustBePositive")
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Supplied_places_must_be_nonnegative_with_at_most_two_decimals(
        bool requireCompleteness)
    {
        var negative = ShiftClosureValidator.Validate(Entry(places: -0.01m), requireCompleteness);
        var tooPrecise = ShiftClosureValidator.Validate(Entry(places: 1.234m), requireCompleteness);

        negative.Should().ContainSingle().Which.Should().Be(
            Error("packagingPlaces", "mustBeNonNegative"));
        tooPrecise.Should().ContainSingle().Which.Should().Be(
            Error("packagingPlaces", "maxTwoDecimalPlaces"));
    }

    [Theory]
    [InlineData(false, -1, null, "manualGoodQuantity")]
    [InlineData(false, null, -1, "manualDefectQuantity")]
    [InlineData(true, -1, null, "manualGoodQuantity")]
    [InlineData(true, null, -1, "manualDefectQuantity")]
    public void Good_and_defect_quantities_are_independently_nonnegative(
        bool requireCompleteness, int? good, int? defect, string expectedField)
    {
        var errors = ShiftClosureValidator.Validate(
            Entry(good: good, defect: defect), requireCompleteness);

        errors.Should().ContainSingle().Which.Should().Be(
            Error(expectedField, "mustBeNonNegative"));
    }

    [Fact]
    public void Ordinary_mode_does_not_require_fallback_fields_on_close()
    {
        var errors = ShiftClosureValidator.Validate(Entry(fallback: false), requireCompleteness: true);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Row_without_operators_does_not_require_fallback_fields_on_close()
    {
        var entry = Entry(hasOperators: false, fallback: true);

        ShiftClosureValidator.Validate(entry, requireCompleteness: true).Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, null, null)]
    [InlineData(0, 1, null)]
    [InlineData(0, null, 1)]
    public void Automatic_or_manual_output_requires_actual_weight_and_effective_packaging(
        int automaticOutput, int? good, int? defect)
    {
        var entry = Entry(automaticOutput: automaticOutput, good: good, defect: defect);

        var errors = ShiftClosureValidator.Validate(entry, requireCompleteness: true);

        errors.Should().BeEquivalentTo(new[]
        {
            Error("actualPartWeightGrams", "required"),
            Error("actualPackagingUnit", "required")
        });
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, 0)]
    public void Null_or_zero_output_quantities_do_not_require_weight_and_packaging(
        int? good, int? defect)
    {
        var entry = Entry(good: good, defect: defect);

        ShiftClosureValidator.Validate(entry, requireCompleteness: true).Should().BeEmpty();
    }

    [Theory]
    [InlineData(12, null)]
    [InlineData(null, 12)]
    public void Actual_or_task_packaging_satisfies_effective_packaging_requirement(
        int? actualPackaging, int? taskPackaging)
    {
        var entry = Entry(automaticOutput: 1, actualWeight: 10,
            actualPackaging: actualPackaging, taskPackaging: taskPackaging);

        ShiftClosureValidator.Validate(entry, requireCompleteness: true).Should().BeEmpty();
    }

    [Fact]
    public void Defect_may_exceed_good_quantity()
    {
        var entry = Entry(actualWeight: 10, actualPackaging: 12, good: 1, defect: 2);

        ShiftClosureValidator.Validate(entry, requireCompleteness: true).Should().BeEmpty();
    }

    [Fact]
    public void Manual_and_automatic_output_are_not_compared()
    {
        var entry = Entry(automaticOutput: 100, actualWeight: 10,
            actualPackaging: 12, good: 1, defect: 0);

        ShiftClosureValidator.Validate(entry, requireCompleteness: true).Should().BeEmpty();
    }

    [Fact]
    public void Close_before_shift_start_is_rejected()
    {
        var act = () => ShiftClosureValidator.ValidateCloseTiming(
            Start.AddTicks(-1), Start, End, confirmEarlyClose: false);

        act.Should().Throw<ShiftClosureValidationException>()
            .Which.Code.Should().Be("closeBeforeShiftStart");
    }

    [Fact]
    public void Close_exactly_at_shift_start_is_allowed_with_early_close_confirmation()
    {
        var act = () => ShiftClosureValidator.ValidateCloseTiming(
            Start, Start, End, confirmEarlyClose: true);

        act.Should().NotThrow();
    }

    [Fact]
    public void Early_close_without_confirmation_is_rejected()
    {
        var act = () => ShiftClosureValidator.ValidateCloseTiming(
            Start.AddMinutes(1), Start, End, confirmEarlyClose: false);

        act.Should().Throw<ShiftClosureValidationException>()
            .Which.Code.Should().Be("earlyCloseConfirmationRequired");
    }

    [Fact]
    public void Early_close_with_confirmation_and_close_at_end_are_allowed()
    {
        var early = () => ShiftClosureValidator.ValidateCloseTiming(
            Start.AddMinutes(1), Start, End, confirmEarlyClose: true);
        var atEnd = () => ShiftClosureValidator.ValidateCloseTiming(
            End, Start, End, confirmEarlyClose: false);

        early.Should().NotThrow();
        atEnd.Should().NotThrow();
    }

    private static ShiftClosureFieldError Error(string field, string code) =>
        new(TaskId, field, code);

    private static ShiftClosureValidationEntry Entry(
        bool hasOperators = true,
        bool fallback = false,
        int automaticOutput = 0,
        int? taskPackaging = null,
        decimal? actualWeight = null,
        int? manualCycle = null,
        int? actualPackaging = null,
        decimal? places = null,
        int? good = null,
        int? defect = null) => new()
    {
        TaskId = TaskId,
        HasOperators = hasOperators,
        IsManualFallbackRequired = fallback,
        AutomaticOutputQuantity = automaticOutput,
        TaskPackagingUnit = taskPackaging,
        ActualPartWeightGrams = actualWeight,
        ManualFullCycleSeconds = manualCycle,
        ActualPackagingUnit = actualPackaging,
        PackagingPlaces = places,
        ManualGoodQuantity = good,
        ManualDefectQuantity = defect
    };

    private static DateTime Utc(string value) =>
        DateTime.SpecifyKind(DateTime.Parse(value), DateTimeKind.Utc);
}
