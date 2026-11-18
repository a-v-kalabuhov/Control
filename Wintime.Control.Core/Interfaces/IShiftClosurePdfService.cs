using Wintime.Control.Core.DTOs.ShiftClosures;

namespace Wintime.Control.Core.Interfaces;

public interface IShiftClosurePdfService
{
    byte[] RenderBlank(ShiftClosureDto draft, DateTime generatedAtUtc);
    byte[] RenderClosed(ShiftClosureDto closed);
}
