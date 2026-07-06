using System.Text;

/// <summary>
/// Handles the binary cache that speeds up startup by skipping repeated filesystem scans.
/// </summary>
internal static class ViewerCacheStore {
	public const string FileName = "viewer-cache.bin";

	const string Magic = "MHTMLViewerCache";
	const int Version = 9;

	/// <summary>
	/// Reads the precomputed tree/link cache. Invalid or stale cache files are treated as cache misses.
	/// </summary>
	public static bool TryLoad(string path, out ViewerCacheData cache) {
		cache = default!;
		if (!File.Exists(path)) return false;

		try {
			using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
			using var reader = new BinaryReader(fs, Encoding.UTF8);

			// Magic and version guard the app from reading stale binary layouts.
			if (!reader.ReadString().Equals(Magic, StringComparison.Ordinal)) return false;
			if (reader.ReadInt32() != Version) return false;

			string firstFile = reader.ReadString();
			Dictionary<string, string> locations = ReadStringDictionary(reader, StringComparer.OrdinalIgnoreCase);
			List<Node> tree = ReadNodeList(reader);

			cache = new ViewerCacheData(firstFile, tree, locations);
			return true;
		} catch {
			return false;
		}
	}

	public static void Save(string path, ViewerCacheData cache) {
		try {
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan);
			using var writer = new BinaryWriter(fs, Encoding.UTF8);

			writer.Write(Magic);
			writer.Write(Version);
			writer.Write(cache.FirstFile);
			WriteStringDictionary(writer, cache.ContentLocations);
			WriteNodeList(writer, cache.Tree);
		} catch {
			// Cache is an optimization only; startup can continue with a rebuild next time.
		}
	}

	static void WriteStringDictionary(BinaryWriter writer, Dictionary<string, string> items) {
		// Count-prefixed collections keep the binary format compact and easy to read back sequentially.
		writer.Write(items.Count);
		foreach (var kv in items) {
			writer.Write(kv.Key);
			writer.Write(kv.Value);
		}
	}

	static Dictionary<string, string> ReadStringDictionary(BinaryReader reader, StringComparer comparer) {
		int count = reader.ReadInt32();
		var items = new Dictionary<string, string>(count, comparer);
		for (int i = 0; i < count; i++) {
			items[reader.ReadString()] = reader.ReadString();
		}

		return items;
	}

	static void WriteNodeList(BinaryWriter writer, List<Node> nodes) {
		writer.Write(nodes.Count);
		foreach (Node node in nodes) {
			WriteNode(writer, node);
		}
	}

	static List<Node> ReadNodeList(BinaryReader reader) {
		int count = reader.ReadInt32();
		var nodes = new List<Node>(count);
		for (int i = 0; i < count; i++) {
			nodes.Add(ReadNode(reader));
		}

		return nodes;
	}

	static void WriteNode(BinaryWriter writer, Node node) {
		// Children are optional so leaf nodes do not pay for an empty list in the cache.
		writer.Write(node.Name);
		writer.Write(node.Path);
		writer.Write(node.KeepNumbering);
		writer.Write(node.Children != null);
		if (node.Children != null) {
			WriteNodeList(writer, node.Children);
		}
	}

	static Node ReadNode(BinaryReader reader) {
		var node = new Node {
			Name = reader.ReadString(),
			Path = reader.ReadString(),
			KeepNumbering = reader.ReadBoolean()
		};

		if (reader.ReadBoolean()) {
			node.Children = ReadNodeList(reader);
		}

		return node;
	}
}
