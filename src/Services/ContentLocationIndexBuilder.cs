using System.Collections.Concurrent;
using System.Text;

/// <summary>
/// Builds the reverse lookup used when links inside an archived page point back to captured URLs.
/// </summary>
internal static class ContentLocationIndexBuilder {
	const string SnapshotLocationHeader = "Snapshot-Content-Location:";

	/// <summary>
	/// Builds a lookup from the original captured URL to its local MHTML file.
	/// </summary>
	public static Dictionary<string, string> Build(string root, string workspaceRoot) {
		var map = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (!Directory.Exists(root)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		// Each MHTML file stores the original captured URL near the top, so a small prefix read is enough.
		Parallel.ForEach(
			Directory.EnumerateFiles(root, "*.mhtml", SearchOption.AllDirectories),
			file => TryAddFile(map, file, workspaceRoot)
		);

		return map.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Normalizes captured URLs so equivalent links with query strings or fragments resolve to the same file.
	/// </summary>
	public static string NormalizeUrl(string url) {
		if (string.IsNullOrWhiteSpace(url)) return string.Empty;

		url = url.Trim();
		int cut = url.IndexOfAny(new[] { '?', '#' });
		if (cut >= 0) url = url[..cut];
		if (url.EndsWith("/", StringComparison.Ordinal)) url = url[..^1];

		return url;
	}

	static void TryAddFile(ConcurrentDictionary<string, string> map, string file, string workspaceRoot) {
		// The snapshot header is emitted before the MIME body; reading the first 512 bytes avoids full-file IO.
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
		map.TryAdd(location, ToWorkspacePath(workspaceRoot, file));
	}

	static string ToWorkspacePath(string workspaceRoot, string path) {
		string relative = Path.GetRelativePath(workspaceRoot, path);
		return relative
			.Replace(Path.DirectorySeparatorChar, '/')
			.Replace(Path.AltDirectorySeparatorChar, '/');
	}
}
