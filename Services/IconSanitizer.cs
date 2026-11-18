using System;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;

namespace PickleballClub.Services;

public static class IconSanitizer
{
    private static readonly Regex SafeIconNameRegex = new(
        @"^[a-z0-9]+:[a-z0-9-]+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExtractIconifyAttrRegex = new(
        @"icon\s*=\s*['""]([a-z0-9]+:[a-z0-9-]+)['""]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string ExtractSafeIconName(string? rawIcon, string fallback = "lucide:cup-soda")
    {
        if (string.IsNullOrWhiteSpace(rawIcon))
        {
            return fallback;
        }

        string trimmed = rawIcon.Trim();
        if (SafeIconNameRegex.IsMatch(trimmed))
        {
            return trimmed.ToLowerInvariant();
        }

        // Only extract icon attribute if the string is strictly an <iconify-icon ...> tag without event handlers or scripts
        if (trimmed.StartsWith("<iconify-icon", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.Contains("on", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.Contains("<script", StringComparison.OrdinalIgnoreCase))
        {
            var match = ExtractIconifyAttrRegex.Match(trimmed);
            if (match.Success)
            {
                return match.Groups[1].Value.ToLowerInvariant();
            }
        }

        // Slight broader check for standard <iconify-icon icon='mdi:cup-water' width='24'></iconify-icon> (note "icon" contains "on"!)
        if (trimmed.StartsWith("<iconify-icon", StringComparison.OrdinalIgnoreCase) &&
            !Regex.IsMatch(trimmed, @"\bon[a-z]+\s*=", RegexOptions.IgnoreCase) &&
            !trimmed.Contains("<script", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.Contains("javascript:", StringComparison.OrdinalIgnoreCase))
        {
            var match = ExtractIconifyAttrRegex.Match(trimmed);
            if (match.Success)
            {
                return match.Groups[1].Value.ToLowerInvariant();
            }
        }

        return fallback;
    }

    public static string FormatSafeIconHtmlString(string? rawIcon, int width = 22, string fallback = "lucide:cup-soda")
    {
        string safeName = WebUtility.HtmlEncode(ExtractSafeIconName(rawIcon, fallback));
        return $"<iconify-icon icon='{safeName}' width='{width}'></iconify-icon>";
    }

    public static IHtmlContent RenderSafeIcon(string? rawIcon, int width = 24, string fallback = "mdi:cup-water")
    {
        return new HtmlString(FormatSafeIconHtmlString(rawIcon, width, fallback));
    }
}
