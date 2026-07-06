using System.Text.Json.Serialization;

/// <summary>
/// Navigation tree item sent to the sidebar WebView.
/// JSON property names intentionally stay camelCase because the sidebar script consumes them directly.
/// </summary>
internal sealed class Node {
	/// <summary>Display text shown in the sidebar tree.</summary>
	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	/// <summary>Relative file path opened when this item is selected.</summary>
	[JsonPropertyName("path")]
	public string Path { get; set; } = string.Empty;

	/// <summary>True when numbering should remain visible for API reference entries.</summary>
	[JsonPropertyName("keepNumbering")]
	public bool KeepNumbering { get; set; }

	/// <summary>Nested sidebar items for directory-like nodes.</summary>
	[JsonPropertyName("children")]
	public List<Node>? Children { get; set; }
}
