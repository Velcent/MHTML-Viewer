using System.Text.Json.Serialization;
internal sealed class Node {
	public string name { get; set; } = string.Empty;
	public string path { get; set; } = string.Empty;
	public List<Node>? children { get; set; }
}

[JsonSerializable(typeof(List<Node>))]
[JsonSerializable(typeof(string))]
internal sealed partial class AppJsonContext : JsonSerializerContext {
}
