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
				File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "startup-error.txt"), error);
				NativeMethods.ShowMessage(app.Handle, error, "Startup Error");
			}
		}, TaskScheduler.FromCurrentSynchronizationContext());
		NativeMethods.RunMessageLoop();
	}
}

internal sealed class WebViewHost : IDisposable {
	const int InitialWidth = 1900;
	const int InitialHeight = 1000;
	const int SidebarWidth = 340;

	readonly string baseRoot = Directory.GetCurrentDirectory();
	readonly ConcurrentDictionary<string, string> contentLocationMap = new(StringComparer.OrdinalIgnoreCase);
	CoreWebView2Controller? navController;
	CoreWebView2Controller? viewerController;
	CoreWebView2? navWeb;
	CoreWebView2? viewerWeb;
	IntPtr handle;

	public IntPtr Handle => handle;

	public void Create() {
		handle = NativeMethods.CreateMainWindow("MHTML Viewer by Velcent", InitialWidth, InitialHeight, WndProc);
	}

	public async Task InitializeAsync() {
		string tempPath = Path.Combine(Path.GetTempPath(), "MHTMLViewer");
		var env = await CoreWebView2Environment.CreateAsync(null, tempPath);
		navController = await env.CreateCoreWebView2ControllerAsync(handle);
		viewerController = await env.CreateCoreWebView2ControllerAsync(handle);
		navWeb = navController.CoreWebView2;
		viewerWeb = viewerController.CoreWebView2;
		ResizeWebView();

		navWeb.Settings.AreDevToolsEnabled = true;
		viewerWeb.Settings.AreDevToolsEnabled = false;
		navWeb.SetVirtualHostNameToFolderMapping("app.local", tempPath, CoreWebView2HostResourceAccessKind.Allow);
		navWeb.WebMessageReceived += WebMessageReceived;
		viewerWeb.NavigationStarting += ViewerNavigationStarting;
		viewerWeb.NavigationCompleted += ViewerNavigationCompleted;

		string first = FindFirstFile(baseRoot);
		if (!string.IsNullOrEmpty(first)) {
			NativeMethods.SetWindowText(handle, Path.GetFileNameWithoutExtension(first));
		}

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
	}

	IntPtr WndProc(IntPtr hwnd, uint msg, UIntPtr wParam, IntPtr lParam) {
		switch (msg) {
			case NativeMethods.WmSize:
				ResizeWebView();
				return IntPtr.Zero;
			case NativeMethods.WmAppDispatch:
				WindowSynchronizationContext.DispatchQueuedCallbacks();
				return IntPtr.Zero;
			case NativeMethods.WmDestroy:
				NativeMethods.PostQuitMessage(0);
				return IntPtr.Zero;
			default:
				return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
		}
	}

	void ResizeWebView() {
		if (navController == null || viewerController == null || handle == IntPtr.Zero) return;
		NativeMethods.GetClientRect(handle, out var rect);
		int width = Math.Max(0, rect.Right - rect.Left);
		int height = Math.Max(0, rect.Bottom - rect.Top);
		int sidebarWidth = Math.Min(SidebarWidth, width);
		navController.Bounds = new System.Drawing.Rectangle(0, 0, sidebarWidth, height);
		viewerController.Bounds = new System.Drawing.Rectangle(sidebarWidth, 0, Math.Max(0, width - sidebarWidth), height);
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
			}
		} catch (Exception ex) {
			await ShowErrorAsync(ex.Message);
		}
	}

	async void ViewerNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e) {
		if (!e.Uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;
		if (!TryResolveMhtml(e.Uri, out string file, out string fragment)) return;
		e.Cancel = true;
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
		NativeMethods.PostMessage(hwnd, NativeMethods.WmAppDispatch, UIntPtr.Zero, IntPtr.Zero);
	}

	public static void DispatchQueuedCallbacks() {
		while (Queue.TryDequeue(out var work)) {
			work.Callback(work.State);
		}
	}
}

internal static partial class NativeMethods {
	public const uint WmDestroy = 0x0002;
	public const uint WmSize = 0x0005;
	public const uint WmAppDispatch = 0x8001;

	const int CsHRedraw = 0x0002;
	const int CsVRedraw = 0x0001;
	const int CwUseDefault = unchecked((int)0x80000000);
	const int SwShow = 5;
	const uint WsOverlappedWindow = 0x00CF0000;
	const uint WsVisible = 0x10000000;
	static readonly IntPtr IdiApplication = new(32512);

	static NativeMethods() {
		wndProc = DispatchWindowMessage;
	}

	static readonly WndProc wndProc;
	static WndProc? currentWndProc;

	public static IntPtr CreateMainWindow(string title, int width, int height, WndProc callback) {
		currentWndProc = callback;
		IntPtr instance = GetModuleHandle(null);
		string className = "MHTMLViewerNativeWindow";
		var wc = new WndClassEx {
			cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
			style = CsHRedraw | CsVRedraw,
			lpfnWndProc = wndProc,
			hInstance = instance,
			hIcon = LoadIcon(IntPtr.Zero, IdiApplication),
			hCursor = LoadCursor(IntPtr.Zero, new IntPtr(32512)),
			hbrBackground = IntPtr.Zero,
			lpszClassName = className
		};

		ushort atom = RegisterClassEx(ref wc);
		if (atom == 0) {
			int error = Marshal.GetLastWin32Error();
			if (error != 1410) Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
		}

		IntPtr hwnd = CreateWindowEx(
			0,
			className,
			title,
			WsOverlappedWindow | WsVisible,
			CwUseDefault,
			CwUseDefault,
			width,
			height,
			IntPtr.Zero,
			IntPtr.Zero,
			instance,
			IntPtr.Zero);
		if (hwnd == IntPtr.Zero) Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
		ShowWindow(hwnd, SwShow);
		UpdateWindow(hwnd);
		return hwnd;
	}

	public static void RunMessageLoop() {
		while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0) {
			TranslateMessage(ref msg);
			DispatchMessage(ref msg);
		}
	}

	static IntPtr DispatchWindowMessage(IntPtr hwnd, uint msg, UIntPtr wParam, IntPtr lParam) {
		return currentWndProc?.Invoke(hwnd, msg, wParam, lParam) ?? DefWindowProc(hwnd, msg, wParam, lParam);
	}

	public delegate IntPtr WndProc(IntPtr hwnd, uint msg, UIntPtr wParam, IntPtr lParam);

	[StructLayout(LayoutKind.Sequential)]
	public struct Rect {
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct Msg {
		public IntPtr hwnd;
		public uint message;
		public UIntPtr wParam;
		public IntPtr lParam;
		public uint time;
		public int ptX;
		public int ptY;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	struct WndClassEx {
		public uint cbSize;
		public int style;
		public WndProc lpfnWndProc;
		public int cbClsExtra;
		public int cbWndExtra;
		public IntPtr hInstance;
		public IntPtr hIcon;
		public IntPtr hCursor;
		public IntPtr hbrBackground;
		public string? lpszMenuName;
		public string lpszClassName;
		public IntPtr hIconSm;
	}

	[DllImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, CharSet = CharSet.Unicode)]
	static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

	[DllImport("user32.dll")]
	static extern IntPtr DispatchMessage(ref Msg lpMsg);

	[DllImport("user32.dll")]
	public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

	[DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true, CharSet = CharSet.Unicode)]
	static extern IntPtr GetModuleHandle(string? lpModuleName);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern int GetClientRect(IntPtr hWnd, out Rect lpRect);

	[DllImport("user32.dll")]
	static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

	[DllImport("user32.dll")]
	static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

	[DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
	static extern int MessageBox(IntPtr hWnd, string lpText, string lpCaption, uint uType);

	[DllImport("user32.dll")]
	public static extern void PostQuitMessage(int nExitCode);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern int PostMessage(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true, CharSet = CharSet.Unicode)]
	static extern ushort RegisterClassEx(ref WndClassEx lpWndClass);

	[DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode)]
	public static extern int SetWindowText(IntPtr hWnd, string lpString);

	[DllImport("user32.dll")]
	static extern int ShowWindow(IntPtr hWnd, int nCmdShow);

	[DllImport("user32.dll")]
	static extern int TranslateMessage(ref Msg lpMsg);

	[DllImport("user32.dll")]
	static extern int UpdateWindow(IntPtr hWnd);

	public static void ShowMessage(IntPtr owner, string text, string caption) {
		MessageBox(owner, text, caption, 0x00000010);
	}
}
