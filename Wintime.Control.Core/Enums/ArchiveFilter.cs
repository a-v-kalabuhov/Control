namespace Wintime.Control.Core.Enums;

/// <summary>Фильтр архивных (IsActive = false) сущностей в отчётах.</summary>
public enum ArchiveFilter
{
    Exclude,  // только действующие (по умолчанию)
    Include,  // действующие и архивные
    Only      // только архивные
}
