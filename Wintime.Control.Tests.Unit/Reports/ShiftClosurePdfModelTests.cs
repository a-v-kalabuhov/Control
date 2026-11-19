using System.Text;
using FluentAssertions;
using Wintime.Control.Core.DTOs.ShiftClosures;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Exceptions;
using Wintime.Control.Infrastructure.Reports;
using Xunit;

namespace Wintime.Control.Tests.Unit.Reports;

public class ShiftClosurePdfModelTests
{
    private static readonly DateTime StartUtc = new(2026, 11, 18, 5, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EndUtc = new(2026, 11, 18, 17, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ClosedAtUtc = new(2026, 11, 18, 15, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Blank_CopiesCurrentPlanAutomaticAndSharesButClearsAllSixManualFields()
    {
        var dto = Draft();
        var generatedAtUtc = new DateTime(2026, 11, 18, 12, 34, 56, DateTimeKind.Utc);

        var model = ShiftClosurePdfModelFactory.CreateBlank(dto, generatedAtUtc);

        model.IsBlank.Should().BeTrue();
        model.Mode.Should().Be("Бланк — смена не закрыта");
        model.GeneratedAtLocal.Should().Be(Local(generatedAtUtc));
        model.StartAtLocal.Should().Be(Local(StartUtc));
        model.EndAtLocal.Should().Be(Local(EndUtc));
        model.FactEndAtLocal.Should().Be(Local(StartUtc.AddHours(4)));
        model.ShiftLeadName.Should().Be("Старший Смены");
        model.ShiftLeadSignature.Should().BeEmpty();
        model.ClosedBySignature.Should().BeEmpty();

        var entry = model.Entries.Should().ContainSingle().Subject;
        entry.ImmName.Should().Be("ТПА-SNAPSHOT");
        entry.ProductName.Should().Be("Изделие-SNAPSHOT");
        entry.RemainingAtStart.Should().Be(11);
        entry.PlasticIngredients.Should().Be("Пластик-SNAPSHOT (100%)");
        entry.ColorantIngredients.Should().Be("Краситель-SNAPSHOT (1,5%)");
        entry.NormPartWeightGrams.Should().Be(4);
        entry.PlannedFullCycleSeconds.Should().Be(50);
        entry.AutomaticAverageFullCycleSeconds.Should().Be(42.5);
        entry.TaskPackagingUnit.Should().Be(20);
        entry.DisplayPackagingUnit.Should().Be(20);
        entry.Cavities.Should().Be(2);
        entry.ShiftPlanQuantity.Should().Be(10);
        entry.PlannedMaterialWeightGrams.Should().Be(60);
        entry.PlannedMaterialWeightKg.Should().Be(0.06m);
        entry.AutomaticOutputQuantity.Should().Be(6);
        entry.ActualPartWeightGrams.Should().BeNull();
        entry.ManualFullCycleSeconds.Should().BeNull();
        entry.ActualPackagingUnit.Should().BeNull();
        entry.PackagingPlaces.Should().BeNull();
        entry.ManualGoodQuantity.Should().BeNull();
        entry.ManualDefectQuantity.Should().BeNull();
        entry.WarehouseAccepted.Should().BeEmpty();
        entry.Shares.Should().HaveCount(3);
        entry.Shares.Select(s => s.Quantity).Should().Equal(2, 1, 3);
        entry.Shares[0].IntervalStartLocal.Should().Be(Local(StartUtc));
        entry.Shares[0].IntervalEndLocal.Should().Be(Local(StartUtc.AddHours(1)));
    }

    [Fact]
    public void Closed_UsesFrozenSharesForOperatorRowsAndContainsExactEarlyCloseMessage()
    {
        var dto = Closed();

        var model = ShiftClosurePdfModelFactory.CreateClosed(dto);

        model.IsBlank.Should().BeFalse();
        model.Mode.Should().Be("Закрытый рапорт");
        model.ShiftLeadName.Should().Be("Старший Смены");
        model.ClosedByName.Should().Be("Текущий Закрывший");
        model.ClosedAtLocal.Should().Be(Local(ClosedAtUtc));
        model.EarlyCloseMessage.Should().Be(
            $"Смена закрыта досрочно: фактическое время закрытия {Local(ClosedAtUtc):dd.MM.yyyy HH:mm}, " +
            $"плановое окончание {Local(EndUtc):dd.MM.yyyy HH:mm}");
        model.GeneratedAtLocal.Should().BeNull();
        model.ShiftLeadSignature.Should().BeEmpty();
        model.ClosedBySignature.Should().BeEmpty();

        var entry = model.Entries.Should().ContainSingle().Subject;
        entry.ActualPartWeightGrams.Should().Be(4.2m);
        entry.ManualFullCycleSeconds.Should().Be(55);
        entry.ActualPackagingUnit.Should().Be(24);
        entry.DisplayPackagingUnit.Should().Be(24);
        entry.PackagingPlaces.Should().Be(1.25m);
        entry.ManualGoodQuantity.Should().Be(8);
        entry.ManualDefectQuantity.Should().Be(2);
        entry.WarehouseAccepted.Should().BeEmpty();
        entry.Operators.Should().HaveCount(2);
        entry.Operators[0].EmployeeId.Should().Be("SNAP-1");
        entry.Operators[0].FullName.Should().Be("Литейщик-SNAPSHOT-1");
        entry.Operators[0].Quantity.Should().Be(5);
        entry.Operators[0].Signature.Should().BeEmpty();
        entry.Operators[1].EmployeeId.Should().Be("SNAP-2");
        entry.Operators[1].FullName.Should().Be("Литейщик-SNAPSHOT-2");
        entry.Operators[1].Quantity.Should().Be(1);
        entry.Operators[1].Signature.Should().BeEmpty();
        entry.Operators.Should().NotContain(o => o.FullName == "НЕЗАМОРОЖЕННЫЙ ИТОГ" || o.Quantity == 999);
    }

    [Fact]
    public void ClosedModel_DoesNotChangeWhenCurrentTaskOrUsersChange()
    {
        var dto = Closed();
        var currentTask = new ShiftTask
        {
            PartWeightGrams = dto.Entries[0].NormPartWeightGrams,
            Imm = new Imm { Name = dto.Entries[0].ImmName },
            Product = new Product { Name = dto.Entries[0].ProductName }
        };
        var currentOperator = new User
        {
            FullName = dto.Entries[0].OutputShares[0].OperatorFullName,
            EmployeeId = dto.Entries[0].OutputShares[0].OperatorEmployeeId
        };
        var model = ShiftClosurePdfModelFactory.CreateClosed(dto);

        currentTask.PartWeightGrams = 99;
        currentTask.Imm.Name = "ТЕКУЩИЙ ТПА";
        currentTask.Product!.Name = "ТЕКУЩЕЕ ИЗДЕЛИЕ";
        currentOperator.FullName = "ТЕКУЩИЙ ЛИТЕЙЩИК";
        currentOperator.EmployeeId = "CURRENT-1";

        var entry = model.Entries.Single();
        entry.ImmName.Should().Be("ТПА-SNAPSHOT");
        entry.ProductName.Should().Be("Изделие-SNAPSHOT");
        entry.NormPartWeightGrams.Should().Be(4);
        entry.Operators[0].FullName.Should().Be("Литейщик-SNAPSHOT-1");
        entry.Operators[0].EmployeeId.Should().Be("SNAP-1");
    }

    [Fact]
    public void RenderClosed_RejectsDraftAndBothModesReturnPdfBytes()
    {
        var service = new ShiftClosurePdfService();

        var reject = () => service.RenderClosed(Draft());
        reject.Should().Throw<DomainException>();

        AssertPdf(service.RenderBlank(Draft(), ClosedAtUtc));
        AssertPdf(service.RenderClosed(Closed()));
    }

    private static ShiftClosureDto Draft() => Header(ShiftClosureState.Draft) with
    {
        ClosedByName = null,
        ClosedAt = null,
        ClosedEarly = false
    };

    private static ShiftClosureDto Closed() => Header(ShiftClosureState.Closed);

    private static ShiftClosureDto Header(ShiftClosureState state) => new()
    {
        Id = state == ShiftClosureState.Closed ? Guid.NewGuid() : null,
        ShiftPlanItemId = Guid.NewGuid(),
        State = state,
        ShiftDate = new DateOnly(2026, 11, 18),
        StartAtUtc = StartUtc,
        EndAtUtc = EndUtc,
        FactEndUtc = state == ShiftClosureState.Closed ? ClosedAtUtc : StartUtc.AddHours(4),
        TimeZoneId = "Europe/Moscow",
        ShiftLeadId = "lead-1",
        ShiftLeadName = "Старший Смены",
        CreatedById = "lead-1",
        CreatedAt = StartUtc,
        ClosedById = state == ShiftClosureState.Closed ? "closer-1" : null,
        ClosedByName = state == ShiftClosureState.Closed ? "Текущий Закрывший" : null,
        ClosedAt = state == ShiftClosureState.Closed ? ClosedAtUtc : null,
        ClosedEarly = state == ShiftClosureState.Closed,
        Entries =
        {
            new ShiftClosureEntryDto
            {
                TaskId = Guid.NewGuid(),
                ImmId = Guid.NewGuid(),
                ImmName = "ТПА-SNAPSHOT",
                ProductName = "Изделие-SNAPSHOT",
                RemainingAtStart = 11,
                PlasticIngredients = "Пластик-SNAPSHOT (100%)",
                ColorantIngredients = "Краситель-SNAPSHOT (1,5%)",
                NormPartWeightGrams = 4,
                ActualPartWeightGrams = 4.2m,
                PlannedFullCycleSeconds = 50,
                AutomaticAverageFullCycleSeconds = 42.5,
                ManualFullCycleSeconds = 55,
                TaskPackagingUnit = 20,
                ActualPackagingUnit = 24,
                PackagingPlaces = 1.25m,
                Cavities = 2,
                ShiftPlanQuantity = 10,
                PlannedMaterialWeightGrams = 60,
                AutomaticOutputQuantity = 6,
                ManualGoodQuantity = 8,
                ManualDefectQuantity = 2,
                HasOperators = true,
                OutputShares =
                {
                    Share("op-1", "Литейщик-SNAPSHOT-1", "SNAP-1", StartUtc, StartUtc.AddHours(1), 2),
                    Share("op-2", "Литейщик-SNAPSHOT-2", "SNAP-2", StartUtc.AddHours(1), StartUtc.AddHours(2), 1),
                    Share("op-1", "Литейщик-SNAPSHOT-1", "SNAP-1", StartUtc.AddHours(2), StartUtc.AddHours(3), 3)
                },
                OperatorTotals =
                {
                    new ShiftClosureOperatorTotalDto
                    {
                        OperatorId = "wrong", OperatorFullName = "НЕЗАМОРОЖЕННЫЙ ИТОГ",
                        OperatorEmployeeId = "WRONG", Quantity = 999
                    }
                }
            }
        }
    };

    private static ShiftClosureOutputShareDto Share(
        string operatorId, string fullName, string employeeId,
        DateTime startUtc, DateTime endUtc, int quantity) => new()
    {
        IntervalStartUtc = startUtc,
        IntervalEndUtc = endUtc,
        OperatorId = operatorId,
        OperatorFullName = fullName,
        OperatorEmployeeId = employeeId,
        SlotAtIntervalStart = 1,
        Quantity = quantity
    };

    private static DateTime Local(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"));

    private static void AssertPdf(byte[] bytes)
    {
        bytes.Should().NotBeEmpty();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }
}
