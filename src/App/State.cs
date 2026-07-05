using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class AppState {
	[JsonPropertyName("lastFile")]
	public string? LastFile { get; set; }

	[JsonPropertyName("sidebarWidth")]
	public int SidebarWidth { get; set; } = 340;

	[JsonPropertyName("collapsed")]
	public bool Collapsed { get; set; }
}

internal static class State {
	public static AppState Current { get; set; } = Load();

	/// <summary>
	/// Loads UI state from temp storage. Corrupt state is ignored so the viewer can still start.
	/// </summary>
	public static AppState Load() {
		if (!File.Exists(AppPaths.StateFile)) return new AppState();

		try {
			string json = File.ReadAllText(AppPaths.StateFile);
			return JsonSerializer.Deserialize(json, AppJsonContext.Default.AppState) ?? new AppState();
		} catch {
			return new AppState();
		}
	}

	public static void Save(AppState state) {
		Directory.CreateDirectory(AppPaths.TempDirectory);
		string stateJson = JsonSerializer.Serialize(state, AppJsonContext.Default.AppState);
		File.WriteAllText(AppPaths.StateFile, stateJson);
	}
}

