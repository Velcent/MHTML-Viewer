using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;

internal sealed class WebView : IDisposable {
	const int InitialWidth = 1900;
	const int InitialHeight = 1000;
	const int MinSidebarWidth = 220;
	const int MaxSidebarWidth = 720;
	const int TitleBarHeight = 35;
	const int ResizeBorder = 2;

	const string AppTitle = "MHTML Viewer";
	const string AppVersion = "1.0.0";
	const string AppDescription = "A simple MHTML viewer using WebView2.";
	const string SidebarRes = "SideBar.html";
	const string ToggleSidebarRes = "ToggleSideBar.js";
	const string TitleBarRes = "TitleBar.html";
	const string IconRes = "app.ico";
	bool isTitleUpdated = false;

	readonly string baseRoot = Directory.GetCurrentDirectory();
	readonly ConcurrentDictionary<string, string> contentLocationMap = new(StringComparer.OrdinalIgnoreCase);
	CoreWebView2Controller? navController;
	CoreWebView2Controller? viewerController;
	CoreWebView2Controller? titleController;
	CoreWebView2? navWeb;
	CoreWebView2? viewerWeb;
	CoreWebView2? titleWeb;
	IntPtr handle;
	int sidebarWidth = 340;
	bool sidebarCollapsed;

	// IMPORTANT: prevent GC
	Native.WndProcDelegate? wndProcDelegate;

	public IntPtr Handle => handle;

	public void Create() {
		wndProcDelegate = WndProc;
		Native.ExtractIconEx(Environment.ProcessPath!, 0, out var large, out var small, 1);
        var wc = new Native.WNDCLASS {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProcDelegate),
            lpszClassName = "MHTMLViewerWindow",
            hInstance = Native.GetModuleHandle(null),
			hIcon = large,
			// hIconSm = small != IntPtr.Zero ? small : large,
			hbrBackground = Native.CreateSolidBrush(0x101010)
        };
        Native.RegisterClass(ref wc);
		handle = Native.CreateWindowEx(
			0,
			wc.lpszClassName,
			AppTitle,
			Native.WS_OVERLAPPEDWINDOW | Native.WS_VISIBLE,
			100, 100, InitialWidth, InitialHeight,
			IntPtr.Zero,
			IntPtr.Zero,
			wc.hInstance,
			IntPtr.Zero
		);
		int style = Native.GetWindowLong(handle, Native.GWL_STYLE);
		style &= ~Native.WS_CAPTION;      // hapus titlebar
		style |= Native.WS_THICKFRAME;    // pastikan resize aktif
		style |= Native.WS_MAXIMIZEBOX;   // optional tapi bagus
		style |= Native.WS_MINIMIZEBOX;   // optional
		Native.SetWindowLong(handle, Native.GWL_STYLE, style);
		Native.SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOMOVE | Native.SWP_FRAMECHANGED);
	}
	IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) {
		switch (msg) {
			case Native.WM_ACTIVATE:
				Native.InvalidateRect(hWnd, IntPtr.Zero, true); // paksa repaint seluruh window
				return IntPtr.Zero;
			case Native.WM_NCHITTEST:
				const int resizeBorder = ResizeBorder + 2;

				Native.GetWindowRect(hWnd, out var r);

				int x = (short)(lParam.ToInt32() & 0xFFFF);
				int y = (short)((lParam.ToInt32() >> 16) & 0xFFFF);

				bool onLeft   = x >= r.Left && x < r.Left + resizeBorder;
				bool onRight  = x <= r.Right && x > r.Right - resizeBorder;
				bool onTop    = y >= r.Top && y < r.Top + resizeBorder;
				bool onBottom = y <= r.Bottom && y > r.Bottom - resizeBorder;

				// Corner dulu
				if (onTop && onLeft) return Native.HTTOPLEFT;
				if (onTop && onRight) return Native.HTTOPRIGHT;
				if (onBottom && onLeft) return Native.HTBOTTOMLEFT;
				if (onBottom && onRight) return Native.HTBOTTOMRIGHT;

				// Edge
				if (onLeft) return Native.HTLEFT;
				if (onRight) return Native.HTRIGHT;
				if (onTop) return Native.HTTOP;
				if (onBottom) return Native.HTBOTTOM;

				// Client
				return Native.HTCLIENT;
			case Native.WM_NCCALCSIZE:
				if (wParam != IntPtr.Zero) {
					var p = Marshal.PtrToStructure<Native.NCCALCSIZE_PARAMS>(lParam);
					p.rgrc0.Top += 1;
					Marshal.StructureToPtr(p, lParam, false);
					return IntPtr.Zero;
				}
				return Native.DefWindowProc(hWnd, msg, wParam, lParam);
			case Native.WM_SIZE:
				ResizeWebView();
				return IntPtr.Zero;
			case Native.WM_DESTROY:
				Native.PostQuitMessage(0);
				return IntPtr.Zero;
			case Native.WM_CUSTOM:
				SyncContext.DispatchQueuedCallbacks();
				return IntPtr.Zero;
			default:
				return Native.DefWindowProc(hWnd, msg, wParam, lParam);
		}
	}
	void ResizeWebView() {
		if (navController == null || viewerController == null || titleController == null || handle == IntPtr.Zero) return;
		Native.GetClientRect(handle, out var rect);
		int width = Math.Max(0, rect.Right - rect.Left);
		int height = Math.Max(0, rect.Bottom - rect.Top);
		int contentHeight = height - TitleBarHeight;
		int sidebarW = sidebarCollapsed ? 0 : sidebarWidth;
		bool isMax = Native.IsZoomed(handle);
		int border = isMax ? ResizeBorder + 5 : ResizeBorder;
		// TitleBar
		titleController.Bounds = new Rectangle(
			border,
			isMax ? border : 0,
			width - (border*2),
			TitleBarHeight
		);
		// Sidebar
		navController.Bounds = new Rectangle(
			border,
			isMax ? TitleBarHeight + border : TitleBarHeight,
			sidebarW, contentHeight - border
		);
		// Viewer
		viewerController.Bounds = new Rectangle(
			sidebarW + border,
			isMax ? TitleBarHeight + border : TitleBarHeight,
			width - sidebarW - (border*2), contentHeight - border
		);
	}
	public async Task InitializeAsync() {

		string first = FindFirstFile(baseRoot);
		if (string.IsNullOrEmpty(first)) return;

		string tempPath = Path.Combine(Path.GetTempPath(), "MHTMLViewer");
		var env = await CoreWebView2Environment.CreateAsync(null, tempPath);
		navController = await env.CreateCoreWebView2ControllerAsync(handle);
		viewerController = await env.CreateCoreWebView2ControllerAsync(handle);
		titleController = await env.CreateCoreWebView2ControllerAsync(handle);

		navWeb = navController.CoreWebView2;
		viewerWeb = viewerController.CoreWebView2;
		titleWeb = titleController.CoreWebView2;

		ResizeWebView();

		navWeb.Settings.AreDevToolsEnabled = false;
		viewerWeb.Settings.AreDevToolsEnabled = true;
		titleWeb.Settings.AreDevToolsEnabled = false;
		navWeb.Settings.AreDefaultContextMenusEnabled = false;
		viewerWeb.Settings.AreDefaultContextMenusEnabled = true;
		titleWeb.Settings.AreDefaultContextMenusEnabled = false;
		navWeb.SetVirtualHostNameToFolderMapping("app.local", tempPath, CoreWebView2HostResourceAccessKind.Allow);
		titleWeb.SetVirtualHostNameToFolderMapping("app.local", tempPath, CoreWebView2HostResourceAccessKind.Allow);

		string sidebarPath = Path.Combine(tempPath, SidebarRes);
		string titlePath = Path.Combine(tempPath, TitleBarRes);
		string iconPath = Path.Combine(tempPath, IconRes);

		File.WriteAllText(sidebarPath, LoadEmbedded(SidebarRes));
		File.WriteAllText(titlePath, LoadEmbedded(TitleBarRes));
		File.WriteAllBytes(iconPath, LoadEmbeddedBytes(IconRes));

		titleWeb.WebMessageReceived += TitleWebMessageReceived;
		navWeb.WebMessageReceived += NavWebMessageReceived;
		viewerWeb.NavigationStarting += ViewerNavigationStarting;
		viewerWeb.NavigationCompleted += ViewerNavigationCompleted;
		viewerWeb.WebMessageReceived += ViewerWebMessageReceived;

		titleWeb.Navigate("https://app.local/" + TitleBarRes);
		await SetIcon("https://app.local/" + IconRes);

		await SetTitle(Path.GetFileNameWithoutExtension(first));

		await ShowTitleLoading(50, "Building Link Index...");
		BuildLinkIndex();

		await ShowTitleLoading(80, "Building File Tree...");
		List<Node> tree = BuildTree(baseRoot);
		string treeJson = JsonSerializer.Serialize(tree, AppJsonContext.Default.ListNode);
		string firstJson = JsonSerializer.Serialize(first, AppJsonContext.Default.String);

		navWeb.NavigationCompleted += async (_, _) => {
			await navWeb.ExecuteScriptAsync($"initTree({treeJson}, {firstJson})");
			await ShowTitleLoading(90, "Almost Ready...");
		};
		navWeb.Navigate("https://app.local/" + SidebarRes);
		_ = GetTitleLoop();
	}
	CancellationTokenSource GetTitleLoopToken = new();
	async Task GetTitleLoop() {
		while (!GetTitleLoopToken.Token.IsCancellationRequested) {
			await Task.Delay(200);
			if(viewerWeb != null) await viewerWeb.ExecuteScriptAsync($"getTitle()");
			if (!isTitleUpdated) {
				await SetTitle(Path.GetFileNameWithoutExtension(FindFirstFile(baseRoot)));
			}
			isTitleUpdated = false;
		}
	}
	async Task SetTitle(string title) {
		await titleWeb!.ExecuteScriptAsync($"setTitle('{title}')");

	}
	async Task SetIcon(string path) {
		string url = new Uri(path).AbsoluteUri;
		await titleWeb!.ExecuteScriptAsync($"setIcon('{url}')");
	}
	async Task ShowTitleLoading(int percent, string status) {
		await titleWeb!.ExecuteScriptAsync($"showLoading('Loading {percent}% - {status}')");
	}
	async Task HideTitleLoading() {
		await titleWeb!.ExecuteScriptAsync($"hideLoading()");
	}
	async Task InjectToggleButton() {
		await viewerWeb!.ExecuteScriptAsync(LoadEmbedded(ToggleSidebarRes));
		await UpdateToggleSidebar();
	}
	async Task ToggleSidebar() {
		sidebarCollapsed = !sidebarCollapsed;
		await UpdateToggleSidebar();
		ResizeWebView();
	}
	async Task UpdateToggleSidebar() {
		await viewerWeb!.ExecuteScriptAsync(@"document.querySelector('.sidebarHandle a').innerHTML = '" + (sidebarCollapsed ? '⮞' : '⮜') + @"';");
	}
	async Task UpdateMaximizeState() {
		bool isMax = Native.IsZoomed(handle);
		await titleWeb!.ExecuteScriptAsync($"setMaximized({isMax.ToString().ToLower()});");
	}
	async void ViewerWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e) {
		var json = JsonDocument.Parse(e.WebMessageAsJson);
		var type = json.RootElement.GetProperty("type").GetString();
		switch (type) {
			case "SetTitle":
				var data = json.RootElement.GetProperty("data").GetString();
				await SetTitle(data!);
				break;
			case "UpdateTitle":
				isTitleUpdated = true;
				break;
		}
	}
	async void TitleWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e) {
		string msg = e.TryGetWebMessageAsString();
		switch (msg) {
			case "toggleMaximize":
				if (Native.IsZoomed(handle))
					Native.ShowWindow(handle, Native.SW_RESTORE);
				else
					Native.ShowWindow(handle, Native.SW_MAXIMIZE);
				await UpdateMaximizeState();
				break;
			case "drag":
				Native.ReleaseCapture();
				Native.SendMessage(handle, Native.WM_NCLBUTTONDOWN, Native.HTCAPTION, 0);
				break;
			case "close":
				Native.PostQuitMessage(0);
				break;
			case "minimize":
				Native.ShowWindow(handle, Native.SW_MINIMIZE);
				break;
			default:
				break;
		}
	}
	async void NavWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e) {
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
				if (viewerWeb!.CanGoBack) {
					viewerWeb.GoBack();
				}
			} else if (type == "forward") {
				if (viewerWeb!.CanGoForward) {
					viewerWeb.GoForward();
				}
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
			await ToggleSidebar();
			return;
		}
		if (!e.Uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)) {
			await navWeb!.ExecuteScriptAsync("showLoading()");
			return;
		}
		e.Cancel = true;
		if (!TryResolveMhtml(e.Uri, out string file, out string fragment)) return;
		await OpenMhtml(file, fragment);

	}
	async void ViewerNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e) {
		try {
			if (viewerWeb?.Source == null || !viewerWeb.Source.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return;
			string path = new Uri(viewerWeb.Source).LocalPath;
			string pathJson = JsonSerializer.Serialize(path, AppJsonContext.Default.String);
			await navWeb!.ExecuteScriptAsync($"setActiveByPath({pathJson}); hideLoading();");
			await InjectToggleButton();
			await HideTitleLoading();
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
			Native.ShowMessage(handle, message, "Open Error");
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
	byte[] LoadEmbeddedBytes(string resourceName){
		var asm = typeof(WebView).Assembly;
		string resName = asm
			.GetManifestResourceNames()
			.First(x => x.EndsWith(resourceName, StringComparison.Ordinal));
		using var stream = asm.GetManifestResourceStream(resName);
		using var ms = new MemoryStream();
		stream!.CopyTo(ms);
		return ms.ToArray();
	}
	string LoadEmbedded(string resourceName) {
		var asm = typeof(WebView).Assembly;
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
