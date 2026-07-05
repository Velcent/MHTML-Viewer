internal static class OfflineAssetIndex {
	/// <summary>
	/// Loads the optional TSV index that maps online asset URLs to local files.
	/// </summary>
	public static Dictionary<string, OfflineAsset> Load(string path, string rootDirectory) {
		var map = new Dictionary<string, OfflineAsset>(StringComparer.Ordinal);
		if (!File.Exists(path)) return map;

		foreach (string line in File.ReadLines(path).Skip(1)) {
			if (string.IsNullOrWhiteSpace(line)) continue;

			string[] parts = line.Split('\t');
			if (parts.Length < 6) continue;

			string link = parts[0].Trim();
			string relativePath = parts[1].Trim().Replace('/', Path.DirectorySeparatorChar);
			string contentType = parts[2].Trim();
			string fullPath = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));

			map[link] = new OfflineAsset(fullPath, contentType);
		}

		return map;
	}
}
