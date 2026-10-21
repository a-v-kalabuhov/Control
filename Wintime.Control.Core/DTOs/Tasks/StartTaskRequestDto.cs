namespace Wintime.Control.Core.DTOs.Tasks;

/// <summary>Начать наладку (PZP-07b): <c>ConfirmMoldBusy</c> — наладчик подтвердил, что ПФ занята на другом ТПА.</summary>
public class StartTaskRequestDto
{
    public bool ConfirmMoldBusy { get; set; }
}
