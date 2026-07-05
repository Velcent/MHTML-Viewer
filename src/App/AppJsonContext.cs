using System.Text.Json.Serialization;

[JsonSerializable(typeof(AppState))]
[JsonSerializable(typeof(List<Node>))]
[JsonSerializable(typeof(string))]
internal sealed partial class AppJsonContext : JsonSerializerContext {
}
