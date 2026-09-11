using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TSKHook.UI;

/// <summary>Whole-text translations and explicit numeric templates for game UI.</summary>
public sealed class TranslationCatalog
{
    private static readonly Regex Placeholder = new(@"\{([0-9]+)\}", RegexOptions.CultureInvariant);
    private const string Number = @"[-+]?(?:[0-9０-９]{1,3}(?:,[0-9０-９]{3})+|[0-9０-９]+)(?:\.[0-9０-９]+)?";
    // Skill previews color the numeric value; retain that entire value in its slot.
    private const string TemplateNumber = "(?:" + Number + "|<color=#[0-9A-Fa-f]{6}>" + Number + "</color>)";
    private static readonly Regex TowerReward = new(
        @"\A<i>(?<name>[^<>]+)</i><size=70%>×</size>" + Number + @"\z", RegexOptions.CultureInvariant);
    private static readonly Regex CountedItem = new(
        @"\A(?<name>[^<>×\r\n]+)×" + Number + @"\z", RegexOptions.CultureInvariant);
    private readonly Dictionary<string, string> exact = new(StringComparer.Ordinal);
    private readonly List<(Regex Pattern, string Translation)> templates = new();

    public int Count => exact.Count;
    public IReadOnlyDictionary<string, string> Entries => exact;

    public static TranslationCatalog Load(params string[] paths)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            using var stream = File.OpenRead(path);
            var fileEntries = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                ?? throw new InvalidDataException("The UI translation file must be a JSON object.");
            foreach (var entry in fileEntries) entries[entry.Key] = entry.Value;
        }
        var catalog = new TranslationCatalog();
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Value))
                continue;
            catalog.exact[entry.Key] = entry.Value;
            var tokens = Placeholder.Matches(entry.Key);
            if (tokens.Count == 0)
                continue;
            var sourceIds = tokens.Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            var targetIds = Placeholder.Matches(entry.Value).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            if (!sourceIds.SetEquals(targetIds))
                throw new InvalidDataException($"Numeric placeholders differ in translation: {entry.Key}");

            var pattern = new StringBuilder(@"\A");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var end = 0;
            foreach (Match token in tokens)
            {
                pattern.Append(Regex.Escape(entry.Key.Substring(end, token.Index - end)));
                var group = "n" + token.Groups[1].Value;
                pattern.Append(seen.Add(group) ? $"(?<{group}>{TemplateNumber})" : $@"\k<{group}>");
                end = token.Index + token.Length;
            }
            pattern.Append(Regex.Escape(entry.Key.Substring(end))).Append(@"\z");
            catalog.templates.Add((new Regex(pattern.ToString(), RegexOptions.CultureInvariant), entry.Value));
        }
        return catalog;
    }

    /// <summary>Existing story names override UI entries and only match a complete text.</summary>
    public void AddNames(IEnumerable<KeyValuePair<string, string>> names)
    {
        foreach (var name in names)
            if (!string.IsNullOrWhiteSpace(name.Key) && !string.IsNullOrWhiteSpace(name.Value))
                exact[name.Key] = name.Value;
    }

    /// <summary>Translate complete known lines for a caller that explicitly supports a line list.</summary>
    public bool TryTranslateLines(string source, out string result)
    {
        result = source;
        if (string.IsNullOrEmpty(source)) return false;
        var parts = Regex.Split(source, @"(\r\n|\r|\n)");
        var translated = new StringBuilder(source.Length);
        var hasText = false;
        for (var i = 0; i < parts.Length; i += 2)
        {
            var line = parts[i];
            if (string.IsNullOrWhiteSpace(line))
                translated.Append(line);
            else
            {
                if (!TryTranslate(line, out var value)) return false;
                translated.Append(value);
                hasText = true;
            }
            if (i + 1 < parts.Length) translated.Append(parts[i + 1]);
        }
        if (!hasText) return false;
        result = translated.ToString();
        return true;
    }

    /// <returns>True for a known translation, including an intentionally unchanged term.</returns>
    public bool TryTranslate(string source, out string result)
    {
        result = source;
        if (string.IsNullOrEmpty(source))
            return false;
        if (exact.TryGetValue(source, out var translation))
        {
            result = translation;
            return true;
        }
        foreach (var template in templates)
        {
            var match = template.Pattern.Match(source);
            if (!match.Success)
                continue;
            result = Placeholder.Replace(template.Translation, token => match.Groups["n" + token.Groups[1].Value].Value);
            return true;
        }
        // Reward lists use these two captured complete-name and quantity layouts.
        var reward = TowerReward.Match(source);
        if (!reward.Success) reward = CountedItem.Match(source);
        if (reward.Success)
        {
            var name = reward.Groups["name"];
            if (TryTranslate(name.Value, out var itemName))
            {
                result = source[..name.Index] + itemName + source[(name.Index + name.Length)..];
                return true;
            }
        }
        const string pieceSuffix = "のピース";
        if (source.EndsWith(pieceSuffix, StringComparison.Ordinal)
            && exact.TryGetValue(source[..^pieceSuffix.Length], out var pieceTitle))
        {
            result = pieceTitle + "的碎片";
            return true;
        }
        return false;
    }
}
