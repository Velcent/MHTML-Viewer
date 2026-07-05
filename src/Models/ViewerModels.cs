internal sealed record OfflineAsset(string FilePath, string ContentType);

internal sealed record ViewerCacheData(
	string FirstFile,
	List<Node> Tree,
	Dictionary<string, string> ContentLocations
);

internal sealed record LoadedDocument(
	string Html,
	byte[] HtmlBytes,
	Dictionary<string, ResourceEntry> ResourcesByUrl,
	Dictionary<string, ResourceEntry> ResourcesByCid
);

internal sealed record ResourceEntry(string ContentType, byte[]? Bytes, string? FilePath);

internal sealed record NavigationEntry(string FilePath, string Fragment, string MediaUrl) {
	public static NavigationEntry Document(string filePath, string fragment) {
		return new NavigationEntry(filePath, fragment, string.Empty);
	}

	public static NavigationEntry Media(string url) {
		return new NavigationEntry(string.Empty, string.Empty, url);
	}
}
