using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;
using System.Text.RegularExpressions;

class Node {
	public string name { get; set; } = string.Empty;
	public string path { get; set; } = string.Empty;
	public object children { get; set; } = new();
}

public class MainForm : Form {
	WebView2 navWeb;
	WebView2 viewerWeb;
	string BaseRoot;

	public MainForm() {
		Text = "MHTML Viewer by Velcent";
		Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
    	ShowIcon=true;
		Width = 1400;
		Height = 900;
		// WindowState = FormWindowState.Maximized;
		BaseRoot = Directory.GetCurrentDirectory();

        SplitContainer split = new() {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 35,
            IsSplitterFixed = false
        };

        navWeb = new() {
			Dock = DockStyle.Fill
		};

		viewerWeb = new() {
			Dock = DockStyle.Fill
		};

		split.Panel1.Controls.Add(navWeb);
		split.Panel2.Controls.Add(viewerWeb);

		Controls.Add(split);

		Load += async (s, e) => {
			
			string tempPath = Path.Combine(Path.GetTempPath(), "MHTMLViewer");
			// Directory.CreateDirectory(tempPath);

			var env = await Microsoft.Web.WebView2.Core
				.CoreWebView2Environment
				.CreateAsync(null, tempPath);

			await navWeb.EnsureCoreWebView2Async(env);
			await viewerWeb.EnsureCoreWebView2Async(env);

			navWeb.CoreWebView2.Settings.AreDevToolsEnabled = false;
			viewerWeb.CoreWebView2.Settings.AreDevToolsEnabled = false;

			// auto open first file
			var first = FindFirstFile(BaseRoot);
			if (first != null) {
				Text = Path.GetFileNameWithoutExtension(first);
				viewerWeb.Source = new Uri(first);
			}

			var tree = BuildTree(BaseRoot);
			string json = JsonSerializer.Serialize(tree);

			navWeb.CoreWebView2.NavigationCompleted += async (_, __) => {
				await navWeb.CoreWebView2.ExecuteScriptAsync($"initTree({json})");
			};

			string uiName = "ui.html";
			string tempUI = Path.Combine(tempPath, uiName);
			string localUI = Path.Combine(BaseRoot, uiName);

			// tulis embedded resource ke temp
			File.WriteAllText(tempUI, LoadEmbedded(uiName));

			// map folder sebagai host internal
			navWeb.CoreWebView2.SetVirtualHostNameToFolderMapping(
					"app.local",
					tempPath,
					Microsoft.Web.WebView2.Core
						.CoreWebView2HostResourceAccessKind
						.Allow
				);

			if (File.Exists(localUI)) navWeb.Source = new Uri(localUI);
			else navWeb.Source = new Uri("https://app.local/" + uiName);

			// message from js
			navWeb.CoreWebView2.WebMessageReceived += (ws, we) => {
				try {
					var msg = JsonDocument.Parse(we.WebMessageAsJson);
					string type = msg.RootElement.GetProperty("type").GetString()!;

					if (type == "open") {
						string fullPath = msg.RootElement.GetProperty("path").GetString()!;
						if (File.Exists(fullPath)) {
							viewerWeb.Source = new Uri(fullPath);
						} else {
							MessageBox.Show("File not found:\n" + fullPath);
						}
					}
				} catch (Exception ex) {
					MessageBox.Show(ex.Message, "Open Error");
				}
			};

			// hyperlink in mhtml can navigate
			viewerWeb.CoreWebView2.NavigationStarting += (x, ev) => {
				if (ev.Uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)){
					ev.Cancel = true;
					viewerWeb.Source = new Uri(ev.Uri);
				}
			};

		};
	}

	//==================
	// BUILD TREE
	//==================
	object BuildTree(string root) {
		var items = new List<Node>();
		// ----- folders -----
		foreach (var dir in Directory.GetDirectories(root).Where(ContainsMhtml)) {
			var children = BuildTree(dir);
			string twinFile = Directory.GetFiles(root, "*.mhtml").FirstOrDefault(f =>
					Path.GetFileNameWithoutExtension(f)
					.Equals(Path.GetFileName(dir), StringComparison.OrdinalIgnoreCase)
				)!;

			string target = twinFile ?? FindFirstFile(dir);
			items.Add(
				new Node {
					name = Path.GetFileName(dir),
					path = target,
					children = children
				}
			);
		}

		// ----- files -----
		foreach (var f in Directory.GetFiles(root, "*.mhtml")) {
			items.Add(
				new Node {
					name = Path.GetFileNameWithoutExtension(f),
					path = f
				}
			);
		}

		// -----------------------
		// remove duplicate twin:
		// prefer folder over file
		// -----------------------
		var dedup = items
			.GroupBy(x => x.name, StringComparer.OrdinalIgnoreCase)
			.Select(g => {
				// ambil folder kalau ada
				var folderNode = g.FirstOrDefault(n => n.children != null);
				if (folderNode != null) return folderNode;
				return g.First();
			}).ToList();

		// -----------------------
		// mixed natural sorting
		// -----------------------
		dedup = dedup
			.OrderBy(n => ExtractNumber(n.name))
			.ThenBy(n => n.name, new NaturalComparer())
			.ToList();

		return dedup;
	}

	string LoadEmbedded(string resourceName) {
		var asm = typeof(MainForm).Assembly;
		// biasanya Namespace.FileName
		string resName = asm
			.GetManifestResourceNames()
			.First(x=>x.EndsWith(resourceName));

		using var stream = asm.GetManifestResourceStream(resName);
		using var reader = new StreamReader(stream!);

		return reader.ReadToEnd();
	}

	bool ContainsMhtml(string folder) {
		return Directory
			.GetFiles(folder, "*.mhtml", SearchOption.AllDirectories)
			.Any();
	}

	class NaturalComparer : IComparer<string> {
		public int Compare(string? a, string? b) {
			var aa = Regex.Split(a!, @"(\d+)");
			var bb = Regex.Split(b!, @"(\d+)");
			int len =  Math.Max(aa.Length, bb.Length);

			for (int i = 0; i < len; i++) {
				if (i >= aa.Length) return -1;
				if (i >= bb.Length) return 1;
				if (int.TryParse(aa[i], out int na) && int.TryParse(bb[i], out int nb)) {
					if (na != nb) return na.CompareTo(nb);
				} else {
					int cmp = string.Compare(aa[i], bb[i], true);
					if (cmp != 0) return cmp;
				}
			}
			return 0;
		}
	}

	string FindFirstFile(string root) {
		var files = Directory.GetFiles(root, "*.mhtml")
			.OrderBy(f => ExtractNumber(Path.GetFileName(f)))
			.ThenBy(f => f);

		if (files.Any()) return files.First();

		foreach (var dir in Directory.GetDirectories(root).OrderBy(d => ExtractNumber(Path.GetFileName(d)))) {
			var found = FindFirstFile(dir);
			if (found != null) return found;
		}
		return string.Empty;
	}

	int ExtractNumber(string name) {
		var parts = name.Split('.');
		if (int.TryParse(parts[0], out int n)) return n;
		return int.MaxValue;
	}
}