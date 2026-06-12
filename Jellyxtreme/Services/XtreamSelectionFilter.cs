namespace Jellyxtreme.Services;

public static class XtreamSelectionFilter
{
    public static bool IsCategorySelected(string? categoryId, IReadOnlyCollection<string>? selectedCategoryIds)
        => !string.IsNullOrWhiteSpace(categoryId)
            && selectedCategoryIds is { Count: > 0 }
            && selectedCategoryIds.Contains(categoryId, StringComparer.OrdinalIgnoreCase);
}
