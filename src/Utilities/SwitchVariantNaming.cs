using System.Text.RegularExpressions;

/// <summary>
/// Parses explicit switch variant suffixes written as "[--Option--]".
/// </summary>
internal static class SwitchVariantNaming {
	public static bool TrySplitName(string name, out string baseName, out List<string> variants) {
		baseName = string.Empty;
		variants = new List<string>();
		string current = name.TrimEnd();
		while (true) {
			Match match = Regex.Match(current, @"^(?<base>.+?)\s*\[(?<variant>[^\]]+)\]\s*$");
			if (!match.Success) break;

			string rawVariant = match.Groups["variant"].Value;
			if (!IsExplicitVariant(rawVariant)) break;

			string variant = CleanText(rawVariant);
			if (string.IsNullOrWhiteSpace(variant)) break;

			variants.Insert(0, variant);
			current = match.Groups["base"].Value.TrimEnd();
		}

		baseName = current.TrimEnd();
		return variants.Count > 0 && !string.IsNullOrWhiteSpace(baseName);
	}

	public static string GetBaseName(string name) {
		return TrySplitName(name, out string baseName, out _) ? baseName : name.TrimEnd();
	}

	public static int GetDefaultRankForName(string name) {
		return TrySplitName(name, out _, out List<string> variants)
			? GetDefaultRank(BuildKey(variants))
			: GetDefaultRank(string.Empty);
	}

	public static string BuildKey(IEnumerable<string> variants) {
		return string.Join("-", variants
			.Select(NormalizeKey)
			.Where(part => !string.IsNullOrWhiteSpace(part)));
	}

	public static string BuildLabel(IEnumerable<string> variants) {
		return string.Join(" / ", variants
			.Select(FormatLabel)
			.Where(part => !string.IsNullOrWhiteSpace(part)));
	}

	public static string CleanText(string value) {
		return value.Trim().Trim('-').Trim();
	}

	public static bool IsExplicitVariant(string value) {
		string trimmed = value.Trim();
		return trimmed.Length > 4
			&& trimmed.StartsWith("--", StringComparison.Ordinal)
			&& trimmed.EndsWith("--", StringComparison.Ordinal);
	}

	public static string NormalizeKey(string value) {
		string cleaned = CleanText(value).ToLowerInvariant();
		return Regex.Replace(cleaned, @"[^a-z0-9+#]+", "-").Trim('-');
	}

	public static int GetDefaultRank(string key) {
		return key.ToLowerInvariant() switch {
			"windows" => 0,
			"c++" => 1,
			_ => 2
		};
	}

	static string FormatLabel(string value) {
		string cleaned = CleanText(value)
			.Replace('_', ' ')
			.Replace('-', ' ');
		cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
		if (string.IsNullOrWhiteSpace(cleaned)) return string.Empty;

		return string.Join(" ", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(FormatLabelWord));
	}

	static string FormatLabelWord(string word) {
		if (string.IsNullOrEmpty(word)) return word;
		if (word.Any(ch => !char.IsLetter(ch))) return word.ToUpperInvariant();
		return char.ToUpperInvariant(word[0]) + word[1..];
	}
}
