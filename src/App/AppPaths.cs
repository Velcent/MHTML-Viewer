internal static class AppPaths {
	public const string AppFolderName = "MHTMLViewer";

	/// <summary>
	/// Shared temp directory used by WebView2 user data, startup diagnostics, and persisted UI state.
	/// </summary>
	public static string TempDirectory => Path.Combine(Path.GetTempPath(), AppFolderName);

	public static string StateFile => Path.Combine(TempDirectory, "state.json");
}
