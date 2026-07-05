/// <summary>
/// Centralizes filesystem paths that are shared across startup, state, and WebView2 initialization.
/// </summary>
internal static class AppPaths {
	public const string AppFolderName = "MHTMLViewer";

	/// <summary>
	/// Shared temp directory used by WebView2 user data, startup diagnostics, and persisted UI state.
	/// </summary>
	public static string TempDirectory => Path.Combine(Path.GetTempPath(), AppFolderName);

	/// <summary>
	/// JSON file that stores the last UI state between viewer sessions.
	/// </summary>
	public static string StateFile => Path.Combine(TempDirectory, "state.json");
}
