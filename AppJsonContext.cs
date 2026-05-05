
using System.Text.Json.Serialization;

[JsonSerializable(typeof(STATE))]
[JsonSerializable(typeof(List<Node>))]
[JsonSerializable(typeof(string))]
internal sealed partial class AppJsonContext : JsonSerializerContext {
}
