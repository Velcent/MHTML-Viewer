using System.Collections.Concurrent;
using System.Text;

internal static class ContentLocationIndexBuilder {
	const string SnapshotLocationHeader = "Snapshot-Content-Location:";

	/// <summary>
	/// Builds a lookup from the original captured URL to its local MHTML file.
	/// </summary>
	public static Dictionary<string, string> Build(string root) {
		var map = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (!Directory.Exists(root)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		Parallel.ForEach(
			Directory.EnumerateFiles(root, "*.mhtml", SearchOption.AllDirectories),
			file => TryAddFile(map, file)
		);

		return map.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
	}

	public static string NormalizeUrl(string url) {
		if (string.IsNullOrWhiteSpace(url)) return string.Empty;

		url = url.Trim();
		int cut = url.IndexOfAny(new[] { '?', '#' });
		if (cut >= 0) url = url[..cut];
		if (url.EndsWith("/", StringComparison.Ordinal)) url = url[..^1];

		return url;
	}

	static void TryAddFile(ConcurrentDictionary<string, string> map, string file) {
		Span<byte> buffer = stackalloc byte[512];
		using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 512, FileOptions.SequentialScan);

		int read = fs.Read(buffer);
		if (read <= 0) return;

		string text = Encoding.ASCII.GetString(buffer[..read]);
		int headerIndex = text.IndexOf(SnapshotLocationHeader, StringComparison.OrdinalIgnoreCase);
		if (headerIndex < 0) return;

		int start = headerIndex + SnapshotLocationHeader.Length;
		int end = text.IndexOf('\n', start);
		if (end < 0) end = text.Length;

		string location = NormalizeUrl(text[start..end]);
		map.TryAdd(location, file);
	}
}
