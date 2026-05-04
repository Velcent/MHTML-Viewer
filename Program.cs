using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;

internal sealed class Node {
	public string name { get; set; } = string.Empty;
	public string path { get; set; } = string.Empty;
	public List<Node>? children { get; set; }
}

[JsonSerializable(typeof(List<Node>))]
[JsonSerializable(typeof(string))]
internal sealed partial class AppJsonContext : JsonSerializerContext {
}

internal static class Program {
	[STAThread]
	private static void Main() {
		using var app = new WebViewHost();
		app.Create();
		SynchronizationContext.SetSynchronizationContext(new WindowSynchronizationContext(app.Handle));
		_ = app.InitializeAsync().ContinueWith(t => {
			if (t.Exception != null) {
				string error = t.Exception.GetBaseException().ToString();
				string tempPath = Path.Combine(Path.GetTempPath(), "MHTMLViewer");
				File.WriteAllText(Path.Combine(tempPath, "startup-error.txt"), error);
				NativeMethods.ShowMessage(app.Handle, error, "Startup Error");
			}
		}, TaskScheduler.FromCurrentSynchronizationContext());
		NativeMethods.RunMessageLoop();
	}
}

internal sealed class WebViewHost : IDisposable {
	const int InitialWidth = 1900;
	const int InitialHeight = 1000;
	const int CollapsedSidebarWidth = 0;
	const int MinSidebarWidth = 220;
	const int MaxSidebarWidth = 720;

	const string AppTitle = "MHTML Viewer by Velcent";
	const string AppVersion = "1.0.0";
	const string AppDescription = "A simple MHTML viewer using WebView2.";
	const string SidebarRes = "Sidebar.html";
	const string ToggleSidebarRes = "ToggleSidebar.js";

	readonly string baseRoot = Directory.GetCurrentDirectory();
	readonly ConcurrentDictionary<string, string> contentLocationMap = new(StringComparer.OrdinalIgnoreCase);
	CoreWebView2Controller? navController;
	CoreWebView2Controller? viewerController;
	CoreWebView2? navWeb;
	CoreWebView2? viewerWeb;
	IntPtr handle;
	int sidebarWidth = 340;
	bool sidebarCollapsed;

	// IMPORTANT: prevent GC
	WndProcDelegate? wndProcDelegate;

	public IntPtr Handle => handle;

	public void Create() {
		wndProcDelegate = WndProc;
		NativeMethods.ExtractIconEx(Environment.ProcessPath!, 0, out var large, out var small, 1);
        var wc = new WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProcDelegate),
            lpszClassName = "MHTMLViewerWindow",
            hInstance = NativeMethods.GetModuleHandle(null),
            hIcon = large,
			// hIconSm = small != IntPtr.Zero ? small : large,
			hbrBackground = 1 + 1
        };

        NativeMethods.RegisterClass(ref wc);
		handle = NativeMethods.CreateWindowEx(
			0,
			wc.lpszClassName,
			AppTitle,
			0x10CF0000,
			100, 100, InitialWidth, InitialHeight,
			IntPtr.Zero,
			IntPtr.Zero,
			wc.hInstance,
			IntPtr.Zero
		);
	}

	public async Task InitializeAsync() {

		string first = FindFirstFile(baseRoot);
		if (string.IsNullOrEmpty(first)) return;

		string tempPath = Path.Combine(Path.GetTempPath(), "MHTMLViewer");
		var env = await CoreWebView2Environment.CreateAsync(null, tempPath);
		UpdateLoadingProgress(30, "Creating WebView environment...");
		navController = await env.CreateCoreWebView2ControllerAsync(handle);
		viewerController = await env.CreateCoreWebView2ControllerAsync(handle);

		navWeb = navController.CoreWebView2;
		viewerWeb = viewerController.CoreWebView2;

		ResizeWebView();

		navWeb.Settings.AreDevToolsEnabled = false;
		viewerWeb.Settings.AreDevToolsEnabled = true;
		navWeb.SetVirtualHostNameToFolderMapping("app.local", tempPath, CoreWebView2HostResourceAccessKind.Allow);

		navWeb.WebMessageReceived += WebMessageReceived;
		viewerWeb.NavigationStarting += ViewerNavigationStarting;
		viewerWeb.NavigationCompleted += ViewerNavigationCompleted;

		UpdateLoadingProgress(60, "Building link index...");
		BuildLinkIndex();

		UpdateLoadingProgress(80, "Building file tree...");
		List<Node> tree = BuildTree(baseRoot);
		string treeJson = JsonSerializer.Serialize(tree, AppJsonContext.Default.ListNode);
		string firstJson = JsonSerializer.Serialize(first, AppJsonContext.Default.String);

		UpdateLoadingProgress(90, "Loading UI...");
		string uiPath = Path.Combine(tempPath, SidebarRes);
		File.WriteAllText(uiPath, LoadHtml(SidebarRes));		

		navWeb.NavigationCompleted += async (_, _) => {
			await navWeb.ExecuteScriptAsync($"initTree({treeJson}, {firstJson});");
		};
		navWeb.Navigate("https://app.local/" + SidebarRes);

		NativeMethods.SetWindowText(handle, Path.GetFileNameWithoutExtension(first));
	}
	void UpdateLoadingProgress(int percent, string status) {
		string bar = new string('█', percent / 5);
		string empty = new string(' ', 20 - percent / 5);
		NativeMethods.SetWindowText(handle, $"Loading [{bar}{empty}] {percent}% - {status}");
	}

	async Task InjectToggleButton() {
		await viewerWeb!.ExecuteScriptAsync(LoadEmbedded(ToggleSidebarRes));
	}

	void ToggleSidebar() {
		sidebarCollapsed = !sidebarCollapsed;
		ResizeWebView();
		viewerWeb!.ExecuteScriptAsync(@"
			document.querySelector('#sidebarHandle a').innerHTML = '" + (sidebarCollapsed ? '⮞' : '⮜') + @"';
		");
	}

	IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) {
		switch (msg) {
			case 0x0005: // WM_SIZE
				ResizeWebView();
				return IntPtr.Zero;
			case 0x0002: // WM_DESTROY
				NativeMethods.PostQuitMessage(0);
				return IntPtr.Zero;
			default:
				return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
		}
	}

	void ResizeWebView() {
		if (navController == null || viewerController == null || handle == IntPtr.Zero) return;
		NativeMethods.GetClientRect(handle, out var rect);
		int width = Math.Max(0, rect.Right - rect.Left);
		int height = Math.Max(0, rect.Bottom - rect.Top);
		int requestedSidebarWidth = sidebarCollapsed ? CollapsedSidebarWidth : sidebarWidth;
		int actualSidebarWidth = Math.Clamp(requestedSidebarWidth, 0, width);
		navController.Bounds = new System.Drawing.Rectangle(0, 0, actualSidebarWidth, height);
		viewerController.Bounds = new System.Drawing.Rectangle(actualSidebarWidth, 0, Math.Max(0, width - actualSidebarWidth), height);
	}

	async void WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e) {
		try {
			using var msg = JsonDocument.Parse(e.WebMessageAsJson);
			string type = msg.RootElement.GetProperty("type").GetString() ?? "";
			if (type == "open") {
				string fullPath = msg.RootElement.GetProperty("path").GetString() ?? "";
				if (File.Exists(fullPath)) {
					await OpenMhtml(fullPath, "");
				} else {
					await ShowError("File not found:\\n" + fullPath);
				}
			} else if (type == "back") {
				if (viewerWeb!.CanGoBack) viewerWeb.GoBack();
			} else if (type == "forward") {
				if (viewerWeb!.CanGoForward) viewerWeb.GoForward();
			} else if (type == "resizeSidebar") {
				int requestedWidth = msg.RootElement.GetProperty("width").GetInt32();
				sidebarWidth = Math.Clamp(requestedWidth, MinSidebarWidth, MaxSidebarWidth);
				sidebarCollapsed = false;
				ResizeWebView();
			}
		} catch (Exception ex) {
			await ShowError(ex.Message);
		}
	}

	async void ViewerNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e) {
		if (e.Uri == "app://toggleSidebar") {
			e.Cancel = true;
			ToggleSidebar();
			return;
		}
		if (!e.Uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)) {
			await navWeb!.ExecuteScriptAsync("showLoading()");
			return;
		}
		e.Cancel = true;
		if (!TryResolveMhtml(e.Uri, out string file, out string fragment)) return;
		await navWeb!.ExecuteScriptAsync("showLoading()");
		await OpenMhtml(file, fragment);
	}

	async void ViewerNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e) {
		try {
			if (viewerWeb?.Source == null || !viewerWeb.Source.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return;
			string path = new Uri(viewerWeb.Source).LocalPath;
			string pathJson = JsonSerializer.Serialize(path, AppJsonContext.Default.String);
			await navWeb!.ExecuteScriptAsync($"setActiveByPath({pathJson}); hideLoading();");
			await InjectToggleButton();
		} catch {
		}
	}

	async Task OpenMhtml(string file, string fragment) {
		string url = new Uri(file).AbsoluteUri;
		if (!string.IsNullOrWhiteSpace(fragment)) {
			url += "#" + Uri.EscapeDataString(fragment);
		}
		viewerWeb!.Navigate(url);
	}

	async Task ShowError(string message) {
		string json = JsonSerializer.Serialize(message, AppJsonContext.Default.String);
		if (navWeb != null) {
			await navWeb.ExecuteScriptAsync($"showError({json});");
		} else {
			NativeMethods.ShowMessage(handle, message, "Open Error");
		}
	}

	void BuildLinkIndex() {
		Parallel.ForEach(
			Directory.EnumerateFiles(baseRoot, "*.mhtml", SearchOption.AllDirectories),
			new ParallelOptions { MaxDegreeOfParallelism = 4 }, // tweak sesuai disk
			file => {
				Span<byte> buffer = stackalloc byte[512];

				using var fs = new FileStream(
					file,
					FileMode.Open,
					FileAccess.Read,
					FileShare.Read,
					512,
					FileOptions.SequentialScan);

				int read = fs.Read(buffer);
				if (read <= 0) return;

				var text = Encoding.ASCII.GetString(buffer.Slice(0, read));

				int idx = text.IndexOf("Snapshot-Content-Location:", StringComparison.OrdinalIgnoreCase);
				if (idx < 0) return;

				int start = idx + 26;
				int end = text.IndexOf('\n', start);
				if (end < 0) end = text.Length;

				string loc = text[start..end].Trim();
				loc = NormalizeUrl(loc);

				contentLocationMap.TryAdd(loc, file);
			});
	}

	bool TryResolveMhtml(string url, out string file, out string fragment) {
		file = string.Empty;
		fragment = string.Empty;
		// pisah fragment (#)
		int hashIndex = url.IndexOf('#');
		if (hashIndex >= 0) fragment = url[(hashIndex + 1)..];

		string baseUrl = NormalizeUrl(url);

		if (contentLocationMap.TryGetValue(baseUrl, out file!)) return true;
		else return false;
	}

	string NormalizeUrl(string url) {
		if (string.IsNullOrWhiteSpace(url)) return string.Empty;
		url = url.Trim();
		// buang fragment & query
		int cut = url.IndexOfAny(new[] { '?', '#' });
		if (cut >= 0) url = url[..cut];
		// buang trailing slash
		if (url.EndsWith("/")) url = url[..^1];
		return url;
	}

	List<Node> BuildTree(string root) {
		// 1. Scan semua file SEKALI
		var allFiles = Directory
			.EnumerateFiles(root, "*.mhtml", SearchOption.AllDirectories)
			.ToList();

		// 3. Group ke folder (RAM only → cepat)
		var filesByDir = allFiles
			.GroupBy(f => Path.GetDirectoryName(f)!)
			.ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

		// 4. Build tree paralel
		return BuildNode(root, filesByDir);
	}
	List<Node> BuildNode(string currentDir, Dictionary<string, List<string>> filesByDir) {
		var items = new ConcurrentBag<Node>();

		// === FILES ===
		if (filesByDir.TryGetValue(currentDir, out var files)) {
			Parallel.ForEach(files, file => {
				items.Add(new Node {
					name = Path.GetFileNameWithoutExtension(file),
					path = file
				});
			});
		}

		// === DIRECTORIES ===
		var dirs = Directory.EnumerateDirectories(currentDir);

		Parallel.ForEach(dirs, dir => {

			// cek cepat tanpa IO
			bool hasContent =
				filesByDir.ContainsKey(dir) ||
				filesByDir.Keys.Any(k =>
					k.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

			if (!hasContent) return;

			var children = BuildNode(dir, filesByDir);

			string? twinFile = filesByDir.TryGetValue(currentDir, out var currentFiles)
				? currentFiles.FirstOrDefault(f =>
					Path.GetFileNameWithoutExtension(f)
						.Equals(Path.GetFileName(dir), StringComparison.OrdinalIgnoreCase))
				: null;

			string target = twinFile ?? FindFirstFromCache(dir, filesByDir);

			items.Add(new Node {
				name = Path.GetFileName(dir),
				path = target,
				children = children
			});
		});

		// sorting tetap di akhir (single-thread → stabil)
		return items
			.GroupBy(x => x.name, StringComparer.OrdinalIgnoreCase)
			.Select(g => g.FirstOrDefault(n => n.children != null) ?? g.First())
			.OrderBy(n => ExtractNumber(n.name))
			.ThenBy(n => n.name, new NaturalComparer())
			.ToList();
	}

	string FindFirstFromCache(string dir, Dictionary<string, List<string>> filesByDir) {
		if (filesByDir.TryGetValue(dir, out var files) && files.Count > 0) {
			return files
				.OrderBy(f => ExtractNumber(Path.GetFileName(f)))
				.ThenBy(f => f)
				.First();
		}

		foreach (var kv in filesByDir) {
			if (kv.Key.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) {
				return kv.Value
					.OrderBy(f => ExtractNumber(Path.GetFileName(f)))
					.ThenBy(f => f)
					.First();
			}
		}

		return string.Empty;
	}

	string LoadEmbedded(string resourceName) {
		var asm = typeof(WebViewHost).Assembly;
		string resName = asm.GetManifestResourceNames().First(x => x.EndsWith(resourceName, StringComparison.Ordinal));
		using var stream = asm.GetManifestResourceStream(resName);
		using var reader = new StreamReader(stream!);
		return reader.ReadToEnd();
	}

	string LoadHtml(string resourceName) {
		string localPath = Path.Combine(baseRoot, resourceName);
		return File.Exists(localPath) ? File.ReadAllText(localPath) : LoadEmbedded(resourceName);
	}

	bool ContainsMhtml(string folder) {
		return Directory.GetFiles(folder, "*.mhtml", SearchOption.AllDirectories).Any();
	}

	string FindFirstFile(string root) {
		var files = Directory.GetFiles(root, "*.mhtml")
			.OrderBy(f => ExtractNumber(Path.GetFileName(f)))
			.ThenBy(f => f);
		if (files.Any()) return files.First();

		foreach (string dir in Directory.GetDirectories(root).OrderBy(d => ExtractNumber(Path.GetFileName(d)))) {
			string found = FindFirstFile(dir);
			if (!string.IsNullOrEmpty(found)) return found;
		}
		return string.Empty;
	}

	int ExtractNumber(string name) {
		string[] parts = name.Split('.');
		return int.TryParse(parts[0], out int n) ? n : int.MaxValue;
	}

	public void Dispose() {
		viewerController?.Close();
		navController?.Close();
	}

	sealed class NaturalComparer : IComparer<string> {
		public int Compare(string? a, string? b) {
			string[] aa = Regex.Split(a ?? "", @"(\d+)");
			string[] bb = Regex.Split(b ?? "", @"(\d+)");
			int len = Math.Max(aa.Length, bb.Length);

			for (int i = 0; i < len; i++) {
				if (i >= aa.Length) return -1;
				if (i >= bb.Length) return 1;
				if (int.TryParse(aa[i], out int na) && int.TryParse(bb[i], out int nb)) {
					if (na != nb) return na.CompareTo(nb);
				} else {
					int cmp = string.Compare(aa[i], bb[i], StringComparison.OrdinalIgnoreCase);
					if (cmp != 0) return cmp;
				}
			}
			return 0;
		}
	}
}

internal sealed class WindowSynchronizationContext : SynchronizationContext {
	static readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> Queue = new();
	readonly IntPtr hwnd;

	public WindowSynchronizationContext(IntPtr hwnd) {
		this.hwnd = hwnd;
	}

	public override void Post(SendOrPostCallback d, object? state) {
		Queue.Enqueue((d, state));
		NativeMethods.PostMessage(hwnd, 0x8001, UIntPtr.Zero, IntPtr.Zero);
	}

	public static void DispatchQueuedCallbacks() {
		while (Queue.TryDequeue(out var work)) {
			work.Callback(work.State);
		}
	}
}

struct WNDCLASS {
	public int style;
	public IntPtr lpfnWndProc;
	public int cbClsExtra;
	public int cbWndExtra;
	public IntPtr hInstance;
	public IntPtr hIcon;
	public IntPtr hIconSm;
	public IntPtr hbrBackground;
	public string lpszMenuName;
	public string lpszClassName;
}

struct MSG {
	public IntPtr hwnd;
	public uint message;
	public IntPtr wParam;
	public IntPtr lParam;
	public uint time;
	public int pt_x;
	public int pt_y;
}

delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

internal static partial class NativeMethods {

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	public static extern uint ExtractIconEx(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

	[DllImport("kernel32.dll")]
	public static extern IntPtr GetModuleHandle(string? lpModuleName);

	[DllImport("user32.dll")]
	public static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

	[DllImport("user32.dll")]
	public static extern IntPtr CreateWindowEx(
		int exStyle,
		string className,
		string windowName,
		int style,
		int x, int y, int width, int height,
		IntPtr parent,
		IntPtr menu,
		IntPtr instance,
		IntPtr param);

	[DllImport("user32.dll")]
	public static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);

	[DllImport("user32.dll")]
	public static extern IntPtr DispatchMessage(ref MSG msg);

	[DllImport("user32.dll")]
	public static extern bool TranslateMessage(ref MSG msg);

	[DllImport("user32.dll")]
	public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	public static extern void PostQuitMessage(int code);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	public static extern bool SetWindowText(IntPtr hWnd, string text);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern int GetClientRect(IntPtr hWnd, out Rect lpRect);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern int PostMessage(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

	public static void RunMessageLoop() {
		while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0) {
			TranslateMessage(ref msg);
			DispatchMessage(ref msg);
		}
	}

	public static void ShowMessage(IntPtr owner, string text, string caption) {
		MessageBox(owner, text, caption, 0x00000010);
	}

	[DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
	static extern int MessageBox(IntPtr hWnd, string lpText, string lpCaption, uint uType);

	[StructLayout(LayoutKind.Sequential)]
	public struct Rect {
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}
}
