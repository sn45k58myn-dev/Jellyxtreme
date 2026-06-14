namespace Jellyxtreme.Services;

public static class XtreamSelectionFilter
{
    private const char SelectionSeparator = ':';
    private const string LegacyProviderId = "legacy";

    public static bool IsCategorySelected(string? providerId, string? categoryId, IReadOnlyCollection<string>? selectedCategoryIds)
    {
        if (string.IsNullOrWhiteSpace(categoryId) || selectedCategoryIds is not { Count: > 0 })
        {
            return false;
        }

        var normalizedProviderId = NormalizeProviderId(providerId);
        var normalizedCategoryId = categoryId.Trim();
        var directSelectionId = BuildCategorySelectionId(normalizedProviderId, normalizedCategoryId);
        var legacySelectionId = BuildCategorySelectionId(LegacyProviderId, normalizedCategoryId);

        return selectedCategoryIds.Contains(directSelectionId, StringComparer.OrdinalIgnoreCase)
            || (IsPrimaryProviderAlias(normalizedProviderId)
                && (selectedCategoryIds.Contains(normalizedCategoryId, StringComparer.OrdinalIgnoreCase)
                    || selectedCategoryIds.Contains(legacySelectionId, StringComparer.OrdinalIgnoreCase)));
    }

    public static bool IsCategorySelected(string? providerId, int streamId, IReadOnlyCollection<string>? selectedCategoryIds)
    {
        if (streamId <= 0)
        {
            return false;
        }

        return IsCategorySelected(providerId, streamId.ToString(System.Globalization.CultureInfo.InvariantCulture), selectedCategoryIds);
    }

    public static bool ShouldCacheSection(bool enabled, IReadOnlyCollection<string>? selectedCategoryIds)
        => enabled && selectedCategoryIds is { Count: > 0 };

    public static string BuildCategorySelectionId(string? providerId, string? categoryId)
    {
        var safeProviderId = NormalizeProviderId(providerId);
        var safeCategoryId = categoryId?.Trim();
        if (string.IsNullOrWhiteSpace(safeCategoryId))
        {
            return string.Empty;
        }

        return $"{safeProviderId}{SelectionSeparator}{safeCategoryId}";
    }

    public static string? ExtractProviderFromSelectionId(string? selectionId)
    {
        if (string.IsNullOrWhiteSpace(selectionId))
        {
            return null;
        }

        var separatorIndex = selectionId.LastIndexOf(SelectionSeparator);
        if (separatorIndex <= 0 || separatorIndex >= selectionId.Length - 1)
        {
            return null;
        }

        var providerId = selectionId[..separatorIndex].Trim();
        var categoryId = selectionId[(separatorIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            return null;
        }

        return providerId;
    }

    public static string? ExtractCategoryFromSelectionId(string? selectionId)
    {
        if (string.IsNullOrWhiteSpace(selectionId))
        {
            return null;
        }

        var separatorIndex = selectionId.LastIndexOf(SelectionSeparator);
        if (separatorIndex <= 0 || separatorIndex >= selectionId.Length - 1)
        {
            return selectionId.Trim();
        }

        var categoryId = selectionId[(separatorIndex + 1)..].Trim();
        return string.IsNullOrWhiteSpace(categoryId) ? null : categoryId;
    }

    public static string NormalizeSelectionId(string? selectionId)
    {
        var providerId = ExtractProviderFromSelectionId(selectionId);
        var categoryId = ExtractCategoryFromSelectionId(selectionId);
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(providerId) ? BuildCategorySelectionId(LegacyProviderId, categoryId) : BuildCategorySelectionId(providerId, categoryId);
    }

    private static string NormalizeProviderId(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return LegacyProviderId;
        }

        return providerId.Trim();
    }

    private static bool IsPrimaryProviderAlias(string normalizedProviderId)
        => string.IsNullOrWhiteSpace(normalizedProviderId)
            || string.Equals(normalizedProviderId, "provider1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedProviderId, LegacyProviderId, StringComparison.OrdinalIgnoreCase);
}
