using System.Collections.Concurrent;
using System.Runtime.InteropServices;
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
	const int CollapsedSidebarWidth = 44;
	const int MinSidebarWidth = 220;
	const int MaxSidebarWidth = 720;

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
			"MHTML Viewer by Velcent",
			0x10CF0000,
			100, 100, InitialWidth, InitialHeight,
			IntPtr.Zero,
			IntPtr.Zero,
			wc.hInstance,
			IntPtr.Zero
		);
	}

	public async Task InitializeAsync() {
		string tempPath = Path.Combine(Path.GetTempPath(), "MHTMLViewer");
		var env = await CoreWebView2Environment.CreateAsync(null, tempPath);
		navController = await env.CreateCoreWebView2ControllerAsync(handle);
		viewerController = await env.CreateCoreWebView2ControllerAsync(handle);
		navWeb = navController.CoreWebView2;
		viewerWeb = viewerController.CoreWebView2;
		ResizeWebView();

		navWeb.Settings.AreDevToolsEnabled = false;
		viewerWeb.Settings.AreDevToolsEnabled = false;
		navWeb.SetVirtualHostNameToFolderMapping("app.local", tempPath, CoreWebView2HostResourceAccessKind.Allow);
		navWeb.WebMessageReceived += WebMessageReceived;
		viewerWeb.NavigationStarting += ViewerNavigationStarting;
		viewerWeb.NavigationCompleted += ViewerNavigationCompleted;

		string first = FindFirstFile(baseRoot);
		if (string.IsNullOrEmpty(first)) return;

		BuildLinkIndex();
		List<Node> tree = BuildTree(baseRoot);
		string treeJson = JsonSerializer.Serialize(tree, AppJsonContext.Default.ListNode);
		string firstJson = JsonSerializer.Serialize(first, AppJsonContext.Default.String);

		string uiPath = Path.Combine(tempPath, "ui.html");
		File.WriteAllText(uiPath, LoadUiHtml());

		navWeb.NavigationCompleted += async (_, _) => {
			await navWeb.ExecuteScriptAsync($"initTree({treeJson}, {firstJson});");
		};
		navWeb.Navigate("https://app.local/ui.html");
		
		NativeMethods.SetWindowText(handle, Path.GetFileNameWithoutExtension(first));
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
					await OpenMhtmlAsync(fullPath, "");
				} else {
					await ShowErrorAsync("File not found:\\n" + fullPath);
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
			} else if (type == "collapseSidebar") {
				sidebarCollapsed = msg.RootElement.GetProperty("collapsed").GetBoolean();
				if (msg.RootElement.TryGetProperty("width", out var widthProperty)) {
					sidebarWidth = Math.Clamp(widthProperty.GetInt32(), MinSidebarWidth, MaxSidebarWidth);
				}
				ResizeWebView();
			}
		} catch (Exception ex) {
			await ShowErrorAsync(ex.Message);
		}
	}

	async void ViewerNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e) {
		if (!e.Uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;
		e.Cancel = true;
		if (!TryResolveMhtml(e.Uri, out string file, out string fragment)) return;
		await OpenMhtmlAsync(file, fragment);
	}

	async void ViewerNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e) {
		try {
			if (viewerWeb?.Source == null || !viewerWeb.Source.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return;
			string path = new Uri(viewerWeb.Source).LocalPath;
			string pathJson = JsonSerializer.Serialize(path, AppJsonContext.Default.String);
			await navWeb!.ExecuteScriptAsync($"setActiveByPath({pathJson});");
		} catch {
		}
	}

	async Task OpenMhtmlAsync(string file, string fragment) {
		string url = new Uri(file).AbsoluteUri;
		if (!string.IsNullOrWhiteSpace(fragment)) {
			url += "#" + Uri.EscapeDataString(fragment);
		}
		viewerWeb!.Navigate(url);
	}

	async Task ShowErrorAsync(string message) {
		string json = JsonSerializer.Serialize(message, AppJsonContext.Default.String);
		if (navWeb != null) {
			await navWeb.ExecuteScriptAsync($"showError({json});");
		} else {
			NativeMethods.ShowMessage(handle, message, "Open Error");
		}
	}

	void BuildLinkIndex() {
		Parallel.ForEach(
			Directory.GetFiles(baseRoot, "*.mhtml", SearchOption.AllDirectories),
			new ParallelOptions { MaxDegreeOfParallelism = 8 },
			IndexContentLocations);
	}

	void IndexContentLocations(string file) {
		using var sr = new StreamReader(file);
		sr.ReadLine();
		string? line = sr.ReadLine();
		if (line?.StartsWith("Snapshot-Content-Location:", StringComparison.OrdinalIgnoreCase) == true) {
			string loc = line[26..].Trim();
			contentLocationMap.TryAdd(loc, file);
			string name = Path.GetFileName(loc);
			if (!string.IsNullOrWhiteSpace(name)) {
				contentLocationMap.TryAdd(name, file);
			}
		}
	}

	bool TryResolveMhtml(string url, out string file, out string fragment) {
		file = string.Empty;
		fragment = string.Empty;
		int i = url.IndexOf('#');
		string baseUrl = url;
		if (i >= 0) {
			baseUrl = url[..i];
			fragment = url[(i + 1)..];
		}
		if (contentLocationMap.TryGetValue(baseUrl, out file!)) return true;
		string name = Path.GetFileName(baseUrl);
		return contentLocationMap.TryGetValue(name, out file!);
	}

	List<Node> BuildTree(string root) {
		var items = new List<Node>();
		foreach (string dir in Directory.GetDirectories(root).Where(ContainsMhtml)) {
			List<Node> children = BuildTree(dir);
			string? twinFile = Directory.GetFiles(root, "*.mhtml").FirstOrDefault(f =>
				Path.GetFileNameWithoutExtension(f).Equals(Path.GetFileName(dir), StringComparison.OrdinalIgnoreCase));
			string target = twinFile ?? FindFirstFile(dir);
			items.Add(new Node {
				name = Path.GetFileName(dir),
				path = target,
				children = children
			});
		}

		foreach (string file in Directory.GetFiles(root, "*.mhtml")) {
			IndexContentLocations(file);
			items.Add(new Node {
				name = Path.GetFileNameWithoutExtension(file),
				path = file
			});
		}

		return items
			.GroupBy(x => x.name, StringComparer.OrdinalIgnoreCase)
			.Select(g => g.FirstOrDefault(n => n.children != null) ?? g.First())
			.OrderBy(n => ExtractNumber(n.name))
			.ThenBy(n => n.name, new NaturalComparer())
			.ToList();
	}

	string LoadEmbedded(string resourceName) {
		var asm = typeof(WebViewHost).Assembly;
		string resName = asm.GetManifestResourceNames().First(x => x.EndsWith(resourceName, StringComparison.Ordinal));
		using var stream = asm.GetManifestResourceStream(resName);
		using var reader = new StreamReader(stream!);
		return reader.ReadToEnd();
	}

	string LoadUiHtml() {
		string localPath = Path.Combine(baseRoot, "ui.html");
		return File.Exists(localPath) ? File.ReadAllText(localPath) : LoadEmbedded("ui.html");
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
