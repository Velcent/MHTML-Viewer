internal sealed class Node {
	public string name { get; set; } = string.Empty;
	public string path { get; set; } = string.Empty;
	public List<Node>? children { get; set; }
}
