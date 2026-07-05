using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Persisted UI preferences. JSON names are kept stable for compatibility with existing state files.
/// </summary>
internal sealed class AppState {
	/// <summary>Last document selected in the sidebar.</summary>
	[JsonPropertyName("lastFile")]
	public string? LastFile { get; set; }

	/// <summary>Sidebar width in device-independent pixels.</summary>
	[JsonPropertyName("sidebarWidth")]
	public int SidebarWidth { get; set; } = 340;

	/// <summary>Whether the navigation sidebar is currently hidden.</summary>
	[JsonPropertyName("collapsed")]
	public bool Collapsed { get; set; }
}

/// <summary>
/// Loads and saves the current UI state from the shared application temp folder.
/// </summary>
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

	/// <summary>
	/// Persists state atomically enough for this app's single-process usage.
	/// </summary>
	public static void Save(AppState state) {
		Directory.CreateDirectory(AppPaths.TempDirectory);
		string stateJson = JsonSerializer.Serialize(state, AppJsonContext.Default.AppState);
		File.WriteAllText(AppPaths.StateFile, stateJson);
	}
}

