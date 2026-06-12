using System;
using System.Net;
using Stranichnik.Opening;

namespace Stranichnik.Icons;

public static class IconLibraryHostScore
{
    public static int Compute(string? targetUrl, string? usedUrl)
    {
        if (!TryGetComparableHost(targetUrl, out var targetHost) ||
            !TryGetComparableHost(usedUrl, out var usedHost))
        {
            return 0;
        }

        if (IsExactOnlyHost(targetHost) || IsExactOnlyHost(usedHost))
        {
            return string.Equals(targetHost, usedHost, StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0;
        }

        var targetLabels = targetHost.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var usedLabels = usedHost.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var targetIndex = targetLabels.Length - 1;
        var usedIndex = usedLabels.Length - 1;
        var score = 0;

        while (targetIndex >= 0 && usedIndex >= 0)
        {
            if (!string.Equals(targetLabels[targetIndex], usedLabels[usedIndex], StringComparison.OrdinalIgnoreCase))
                break;

            score++;
            targetIndex--;
            usedIndex--;
        }

        return score;
    }

    private static bool TryGetComparableHost(string? url, out string host)
    {
        host = string.Empty;

        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (BookmarkUrlNormalizer.TryNormalizeForOpening(url, out var uri) != BookmarkUrlOpenStatus.Success ||
            uri is null)
        {
            return false;
        }

        host = uri.Host.Trim().TrimEnd('.');
        return host.Length > 0;
    }

    private static bool IsExactOnlyHost(string host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            IPAddress.TryParse(host, out _);
    }
}
