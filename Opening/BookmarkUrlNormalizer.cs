using System;
using System.Globalization;

namespace Stranichnik.Opening;

public static class BookmarkUrlNormalizer
{
    private const string DefaultScheme = "https://";
    private static readonly char[] HostTerminators = ['/', '?', '#'];

    public static BookmarkUrlOpenStatus TryNormalizeForOpening(string addressText, out Uri? normalizedUri)
    {
        ArgumentNullException.ThrowIfNull(addressText);

        var normalizedText = addressText.Trim();
        normalizedUri = null;

        if (normalizedText.Length == 0)
            return BookmarkUrlOpenStatus.InvalidAddress;

        if (TryCreateSupportedAbsoluteUri(normalizedText, out normalizedUri))
            return BookmarkUrlOpenStatus.Success;

        if (HasExplicitUnsupportedScheme(normalizedText))
            return BookmarkUrlOpenStatus.UnsupportedScheme;

        if (!LooksLikeWebAddressWithoutScheme(normalizedText))
            return BookmarkUrlOpenStatus.InvalidAddress;

        return TryCreateSupportedAbsoluteUri(DefaultScheme + normalizedText, out normalizedUri)
            ? BookmarkUrlOpenStatus.Success
            : BookmarkUrlOpenStatus.InvalidAddress;
    }

    private static bool TryCreateSupportedAbsoluteUri(string value, out Uri? uri)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri))
            return false;

        if (uri.Scheme is "http" or "https")
            return true;

        uri = null;
        return false;
    }

    private static bool HasExplicitUnsupportedScheme(string value)
    {
        var colonIndex = value.IndexOf(':', StringComparison.Ordinal);
        if (colonIndex <= 0)
            return false;

        var firstPathSeparatorIndex = value.IndexOfAny(HostTerminators);
        if (firstPathSeparatorIndex >= 0 && firstPathSeparatorIndex < colonIndex)
            return false;

        if (value.Length > colonIndex + 2 &&
            value[colonIndex + 1] == '/' &&
            value[colonIndex + 2] == '/')
        {
            return true;
        }

        var schemeCandidate = value[..colonIndex];
        if (CouldBeHostWithInvalidPort(schemeCandidate))
            return false;

        return IsSchemeCandidate(schemeCandidate) && !LooksLikeHostWithPort(value);
    }

    private static bool LooksLikeWebAddressWithoutScheme(string value)
    {
        if (ContainsInvalidWebAddressCharacter(value))
            return false;

        var host = ExtractHost(value);
        if (host.Length == 0)
            return false;

        if (!TrySplitHostAndPort(host, out var hostWithoutPort))
            return false;

        return IsLocalhost(hostWithoutPort) ||
            IsIpv4Address(hostWithoutPort) ||
            LooksLikeDomain(hostWithoutPort);
    }

    private static string ExtractHost(string value)
    {
        var endIndex = value.IndexOfAny(HostTerminators);
        return endIndex < 0 ? value : value[..endIndex];
    }

    private static bool TrySplitHostAndPort(string host, out string hostWithoutPort)
    {
        hostWithoutPort = host;

        var colonIndex = host.LastIndexOf(':');
        if (colonIndex < 0)
            return true;

        if (colonIndex == 0 || colonIndex == host.Length - 1)
            return false;

        var portText = host[(colonIndex + 1)..];
        if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port))
            return false;

        if (port is < 1 or > 65535)
            return false;

        hostWithoutPort = host[..colonIndex];
        return hostWithoutPort.Length > 0;
    }

    private static bool LooksLikeHostWithPort(string value)
    {
        var host = ExtractHost(value);
        return TrySplitHostAndPort(host, out var hostWithoutPort) &&
            hostWithoutPort.Length < host.Length &&
            (IsLocalhost(hostWithoutPort) || IsIpv4Address(hostWithoutPort) || LooksLikeDomain(hostWithoutPort));
    }

    private static bool CouldBeHostWithInvalidPort(string valueBeforeColon)
    {
        return IsLocalhost(valueBeforeColon) ||
            IsIpv4Address(valueBeforeColon) ||
            LooksLikeDomain(valueBeforeColon);
    }

    private static bool LooksLikeDomain(string host)
    {
        if (!host.Contains('.', StringComparison.Ordinal))
            return false;

        var labels = host.Split('.');
        foreach (var label in labels)
        {
            if (label.Length == 0)
                return false;

            if (label[0] == '-' || label[^1] == '-')
                return false;

            foreach (var character in label)
            {
                if (!IsAsciiLetterOrDigit(character) && character != '-')
                    return false;
            }
        }

        return labels[^1].Length >= 2;
    }

    private static bool IsIpv4Address(string host)
    {
        var parts = host.Split('.');
        if (parts.Length != 4)
            return false;

        foreach (var part in parts)
        {
            if (part.Length == 0 || part.Length > 3)
                return false;

            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                return false;

            if (value is < 0 or > 255)
                return false;
        }

        return true;
    }

    private static bool IsLocalhost(string host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSchemeCandidate(string value)
    {
        if (value.Length == 0 || !IsAsciiLetter(value[0]))
            return false;

        for (var index = 1; index < value.Length; index++)
        {
            var character = value[index];
            if (!IsAsciiLetterOrDigit(character) && character is not ('+' or '-' or '.'))
                return false;
        }

        return true;
    }

    private static bool ContainsInvalidWebAddressCharacter(string value)
    {
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) ||
                char.IsControl(character) ||
                character is '"' or '\'' or '<' or '>' or '\\')
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAsciiLetter(char character)
    {
        return character is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
    }

    private static bool IsAsciiLetterOrDigit(char character)
    {
        return IsAsciiLetter(character) || character is >= '0' and <= '9';
    }
}
