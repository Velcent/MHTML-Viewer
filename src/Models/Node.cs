using System.Text.Json.Serialization;

/// <summary>
/// Navigation tree item sent to the sidebar WebView.
/// JSON property names intentionally stay camelCase because the sidebar script consumes them directly.
/// </summary>
internal sealed class Node {
	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("path")]
	public string Path { get; set; } = string.Empty;

	[JsonPropertyName("keepNumbering")]
	public bool KeepNumbering { get; set; }

	[JsonPropertyName("children")]
	public List<Node>? Children { get; set; }
}
