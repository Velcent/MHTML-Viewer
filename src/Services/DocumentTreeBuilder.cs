using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;

internal static class DocumentTreeBuilder {
	/// <summary>
	/// Builds the sidebar tree from local MHTML/HTML files and folds platform/API variants into one entry.
	/// </summary>
	public static List<Node> Build(string root) {
		List<string> allFiles = Directory
			.EnumerateFiles(root, "*.mhtml", SearchOption.AllDirectories)
			.Concat(Directory.EnumerateFiles(root, "*.html", SearchOption.AllDirectories))
			.GroupBy(GetSwitchVariantGroupKey, StringComparer.OrdinalIgnoreCase)
			.Select(group => group
				.OrderBy(GetSwitchVariantPreference)
				.ThenBy(file => file, StringComparer.OrdinalIgnoreCase)
				.First())
			.ToList();

		Dictionary<string, List<string>> filesByDir = allFiles
			.GroupBy(file => Path.GetDirectoryName(file)!)
			.ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

		return BuildNode(root, filesByDir);
	}

	public static string FindFirstTreePath(List<Node> nodes) {
		foreach (Node node in nodes) {
			if (!string.IsNullOrWhiteSpace(node.Path)) return node.Path;
			if (node.Children != null) {
				string childPath = FindFirstTreePath(node.Children);
				if (!string.IsNullOrWhiteSpace(childPath)) return childPath;
			}
		}

		return string.Empty;
	}

	public static string FindFirstFile(string root) {
		IOrderedEnumerable<string> files = Directory.GetFiles(root, "*.mhtml", SearchOption.AllDirectories)
			.GroupBy(GetSwitchVariantGroupKey, StringComparer.OrdinalIgnoreCase)
			.Select(group => group
				.OrderBy(GetSwitchVariantPreference)
				.ThenBy(file => file, StringComparer.OrdinalIgnoreCase)
				.First())
			.OrderBy(file => ExtractNumber(Path.GetFileName(file)))
			.ThenBy(file => file);

		return files.FirstOrDefault() ?? string.Empty;
	}

	static List<Node> BuildNode(string currentDir, Dictionary<string, List<string>> filesByDir) {
		var items = new ConcurrentBag<Node>();

		if (filesByDir.TryGetValue(currentDir, out List<string>? files)) {
			Parallel.ForEach(files, file => {
				items.Add(new Node {
					Name = DecodeTreeName(GetSwitchVariantBaseName(Path.GetFileNameWithoutExtension(file))),
					Path = file,
					KeepNumbering = IsInsideApiReferencePath(file)
				});
			});
		}

		Parallel.ForEach(Directory.EnumerateDirectories(currentDir), dir => {
			if (!HasContent(dir, filesByDir)) return;

			List<Node> children = BuildNode(dir, filesByDir);
			string? twinFile = filesByDir.TryGetValue(currentDir, out List<string>? currentFiles)
				? currentFiles.FirstOrDefault(file =>
					GetSwitchVariantBaseName(Path.GetFileNameWithoutExtension(file))
						.Equals(Path.GetFileName(dir), StringComparison.OrdinalIgnoreCase))
				: null;

			items.Add(new Node {
				Name = DecodeTreeName(Path.GetFileName(dir)),
				Path = twinFile ?? FindFirstFromCache(dir, filesByDir),
				KeepNumbering = IsInsideApiReferencePath(dir),
				Children = children
			});
		});

		return SortTreeItems(currentDir, items);
	}

	static bool HasContent(string dir, Dictionary<string, List<string>> filesByDir) {
		return filesByDir.ContainsKey(dir) ||
			filesByDir.Keys.Any(key =>
				key.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
	}

	static bool IsApiReferencePath(string path) {
		return GetApiReferenceDepth(path) >= 0;
	}

	static bool IsInsideApiReferencePath(string path) {
		return GetApiReferenceDepth(path) > 0;
	}

	static int GetApiReferenceDepth(string path) {
		string fullPath = Path.GetFullPath(path);
		string[] parts = fullPath.Split(
			new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
			StringSplitOptions.RemoveEmptyEntries
		);

		for (int i = 0; i < parts.Length; i++) {
			string name = NormalizeNumberedFolderName(parts[i]);
			if (name is "Unreal Engine Blueprint API Reference"
				or "Unreal Engine C++ API Reference"
				or "Unreal Engine Python API Documentation") {
				return parts.Length - i - 1;
			}
		}

		return -1;
	}

	static string NormalizeNumberedFolderName(string name) {
		return Regex.Replace(name, @"^\d+\.\s*", "");
	}

	static IOrderedEnumerable<string> SortFilesForTree(string dir, IEnumerable<string> files) {
		return IsApiReferencePath(dir)
			? files.OrderBy(file => Path.GetFileNameWithoutExtension(file), new NaturalComparer())
			: files
				.OrderBy(file => ExtractNumber(Path.GetFileName(file)))
				.ThenBy(file => file, StringComparer.OrdinalIgnoreCase);
	}

	static List<Node> SortTreeItems(string currentDir, IEnumerable<Node> items) {
		IEnumerable<Node> deduped = items
			.GroupBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
			.Select(group => group.FirstOrDefault(node => node.Children != null) ?? group.First());

		return (IsApiReferencePath(currentDir)
				? deduped.OrderBy(node => node.Name, new NaturalComparer())
				: deduped.OrderBy(node => ExtractNumber(node.Name)).ThenBy(node => node.Name, new NaturalComparer()))
			.ToList();
	}

	static string FindFirstFromCache(string dir, Dictionary<string, List<string>> filesByDir) {
		IEnumerable<string> directFiles = filesByDir.TryGetValue(dir, out List<string>? files)
			? files
			: Enumerable.Empty<string>();
		string? directFirst = SortFilesForTree(dir, directFiles).FirstOrDefault();
		if (!string.IsNullOrEmpty(directFirst)) return directFirst;

		IEnumerable<string> nestedFiles = filesByDir
			.Where(kv => kv.Key.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			.SelectMany(kv => kv.Value);
		return SortFilesForTree(dir, nestedFiles).FirstOrDefault() ?? string.Empty;
	}

	static string DecodeTreeName(string name) {
		string normalized = Regex.Replace(
			name,
			@"&(#\d+|#x[0-9a-fA-F]+|[A-Za-z][A-Za-z0-9]+)_",
			"&$1;"
		);
		return WebUtility.HtmlDecode(normalized);
	}

	static string GetSwitchVariantGroupKey(string file) {
		string dir = Path.GetDirectoryName(file) ?? "";
		string ext = Path.GetExtension(file);
		string baseName = GetSwitchVariantBaseName(Path.GetFileNameWithoutExtension(file));
		return $"{dir}|{ext}|{baseName}";
	}

	static string GetSwitchVariantBaseName(string name) {
		string current = name;
		while (true) {
			Match match = Regex.Match(current, @"^(?<base>.+?)\s*\[(?<variant>[^\]]+)\]\s*$");
			if (!match.Success) return current.TrimEnd();

			string variant = NormalizeSwitchVariant(match.Groups["variant"].Value);
			if (!IsKnownSwitchVariant(variant)) return current.TrimEnd();

			current = match.Groups["base"].Value;
		}
	}

	static int GetSwitchVariantPreference(string file) {
		List<string> variants = GetSwitchVariants(Path.GetFileNameWithoutExtension(file));
		bool hasWindows = variants.Contains("windows");
		bool hasBlueprint = variants.Contains("blueprint");

		if (hasWindows && hasBlueprint) return 0;
		if (hasWindows) return 1;
		if (hasBlueprint) return 2;
		if (variants.Count == 0) return 3;
		if (variants.Contains("c++") || variants.Contains("cpp") || variants.Contains("cplusplus")) return 4;
		if (variants.Contains("linux")) return 5;
		if (variants.Contains("macos") || variants.Contains("mac") || variants.Contains("apple")) return 6;
		return 10;
	}

	static List<string> GetSwitchVariants(string name) {
		var variants = new List<string>();
		string current = name;
		while (true) {
			Match match = Regex.Match(current, @"^(?<base>.+?)\s*\[(?<variant>[^\]]+)\]\s*$");
			if (!match.Success) return variants;

			string variant = NormalizeSwitchVariant(match.Groups["variant"].Value);
			if (!IsKnownSwitchVariant(variant)) return variants;

			variants.Add(variant);
			current = match.Groups["base"].Value;
		}
	}

	static string NormalizeSwitchVariant(string value) {
		string normalized = value.Trim().Trim('-').Trim().ToLowerInvariant();
		return normalized switch {
			"mac os" => "macos",
			"mac-os" => "macos",
			"mac_os" => "macos",
			"c plus plus" => "cplusplus",
			"c-plus-plus" => "cplusplus",
			"c_plus_plus" => "cplusplus",
			_ => normalized
		};
	}

	static bool IsKnownSwitchVariant(string variant) {
		return variant is "windows" or "linux" or "macos" or "mac" or "apple" or
			"blueprint" or "c++" or "cpp" or "cplusplus";
	}

	static int ExtractNumber(string name) {
		string[] parts = name.Split('.');
		return int.TryParse(parts[0], out int number) ? number : int.MaxValue;
	}
}
