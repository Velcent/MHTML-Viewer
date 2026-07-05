/// <summary>
/// Local replacement for an online asset referenced by an MHTML part.
/// </summary>
internal sealed record OfflineAsset(string FilePath, string ContentType);

/// <summary>
/// Serialized startup cache containing the first file, sidebar tree, and URL-to-file index.
/// </summary>
internal sealed record ViewerCacheData(
	string FirstFile,
	List<Node> Tree,
	Dictionary<string, string> ContentLocations
);

/// <summary>
/// Parsed document payload served through the internal WebView2 resource handler.
/// </summary>
internal sealed record LoadedDocument(
	string Html,
	byte[] HtmlBytes,
	Dictionary<string, ResourceEntry> ResourcesByUrl,
	Dictionary<string, ResourceEntry> ResourcesByCid
);

/// <summary>
/// Resource response data; content can come from memory or from an offline asset file.
/// </summary>
internal sealed record ResourceEntry(string ContentType, byte[]? Bytes, string? FilePath);

/// <summary>
/// Browser history entry for either a document+fragment or a local media page.
/// </summary>
internal sealed record NavigationEntry(string FilePath, string Fragment, string MediaUrl) {
	/// <summary>Creates history for a document navigation.</summary>
	public static NavigationEntry Document(string filePath, string fragment) {
		return new NavigationEntry(filePath, fragment, string.Empty);
	}

	/// <summary>Creates history for an image/video opened through the local media host.</summary>
	public static NavigationEntry Media(string url) {
		return new NavigationEntry(string.Empty, string.Empty, url);
	}
}
