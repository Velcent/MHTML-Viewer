using System.Text.Json;
class STATE {
    public string? lastFile { get; set; }
    public int sidebarWidth { get; set; } = 340;
    public bool collapsed { get; set; }
}

static class State {
    static string StatePath => Path.Combine(Path.GetTempPath(), "MHTMLViewer", "state.json");
    public static STATE Current { get; set; } = Load();
    public static STATE Load() {
        if (!File.Exists(StatePath))
            return new STATE();
        try {
            return JsonSerializer.Deserialize(File.ReadAllText(StatePath), AppJsonContext.Default.STATE) ?? new STATE();
        }
        catch {
            return new STATE();
        }
    }
    public static void Save(STATE state) {
        var stateJson = JsonSerializer.Serialize(state, AppJsonContext.Default.STATE);
        File.WriteAllText(StatePath, stateJson);
    }
}

