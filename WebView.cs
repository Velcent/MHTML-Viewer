using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;
using System.Net;

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
	const string IconRes = "app.ico";
	const string TitleBarRes = "TitleBar.html";
	const string SidebarRes = "SideBar.html";
	const string GetTitleRes = "GetTitle.js";
	const string ToggleSidebarRes = "ToggleSideBar.js";
	const string DocumentResourceHost = "mhtml.local";
	const string LocalMediaHost = "media.local";
	const string ViewerCacheFileName = "viewer-cache.bin";
	const string ViewerCacheMagic = "MHTMLViewerCache";
	const int ViewerCacheVersion = 2;
	bool isTitleUpdated = false;
	bool isTitleInit = false;
	bool isLoading = false;

	string workspaceRoot = string.Empty;
	string baseRoot = string.Empty;
	Dictionary<string, OfflineAsset> offlineAssets = new(StringComparer.Ordinal);
	readonly ConcurrentDictionary<string, string> contentLocationMap = new(StringComparer.OrdinalIgnoreCase);
	readonly ConcurrentDictionary<string, LoadedDocument> documentCache = new(StringComparer.OrdinalIgnoreCase);
	CoreWebView2Controller? navController;
	CoreWebView2Controller? viewerController;
	CoreWebView2Controller? titleController;
	CoreWebView2? navWeb;
	CoreWebView2? viewerWeb;
	CoreWebView2? titleWeb;
	LoadedDocument? currentDocument;
	string currentFilePath = string.Empty;
	string pendingFragment = string.Empty;
	string pendingLocalHtmlNavigationPath = string.Empty;
	int documentNavigationVersion = 0;
	List<Node> viewerTree = new();
	readonly Dictionary<string, string> titleCache = new(StringComparer.OrdinalIgnoreCase);
	readonly List<NavigationEntry> navigationHistory = new();
	int navigationIndex = -1;
	IntPtr handle;

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
		int screenWidth = Native.GetSystemMetrics(Native.SM_CXSCREEN);
		int screenHeight = Native.GetSystemMetrics(Native.SM_CYSCREEN);
		if (InitialWidth < screenWidth) {
			int x = (screenWidth - InitialWidth) / 2;
			int y = (screenHeight - InitialHeight) / 2;
			handle = Native.CreateWindowEx(
				0,
				wc.lpszClassName,
				AppTitle,
				Native.WS_OVERLAPPEDWINDOW | Native.WS_VISIBLE,
				x, y, InitialWidth, InitialHeight,
				IntPtr.Zero,
				IntPtr.Zero,
				wc.hInstance,
				IntPtr.Zero
			);
		} else {
			handle = Native.CreateWindowEx(
				0,
				wc.lpszClassName,
				AppTitle,
				Native.WS_POPUP | Native.WS_VISIBLE,
				0, 0,
				Native.GetSystemMetrics(0), // SM_CXSCREEN
				Native.GetSystemMetrics(1), // SM_CYSCREEN
				IntPtr.Zero,
				IntPtr.Zero,
				wc.hInstance,
				IntPtr.Zero
			);
		}
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
		int sidebarW = State.Current.collapsed ? 0 : State.Current.sidebarWidth;
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
		workspaceRoot = Directory.GetCurrentDirectory();
		baseRoot = Path.Combine(workspaceRoot, "mhtml");

		string tempPath = Path.Combine(Path.GetTempPath(), "MHTMLViewer");
		var options = new CoreWebView2EnvironmentOptions(
			"--allow-file-access-from-files --disable-web-security"
		);
		var env = await CoreWebView2Environment.CreateAsync(null, tempPath, options);
		
		navController = await env.CreateCoreWebView2ControllerAsync(handle);
		viewerController = await env.CreateCoreWebView2ControllerAsync(handle);
		titleController = await env.CreateCoreWebView2ControllerAsync(handle);

		navWeb = navController.CoreWebView2;
		viewerWeb = viewerController.CoreWebView2;
		titleWeb = titleController.CoreWebView2;
		ConfigureLocalMediaHost(viewerWeb);
		viewerWeb.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);

		ResizeWebView();

		navWeb.Settings.AreDevToolsEnabled = false;
		viewerWeb.Settings.AreDevToolsEnabled = false;
		titleWeb.Settings.AreDevToolsEnabled = false;
		navWeb.Settings.AreDefaultContextMenusEnabled = false;
		viewerWeb.Settings.AreDefaultContextMenusEnabled = true;
		titleWeb.Settings.AreDefaultContextMenusEnabled = false;
		navController.ZoomFactor = 0.8;
		viewerController.ZoomFactor = 0.9;
		titleController.ZoomFactor = 1.0;

		titleWeb.WebMessageReceived += TitleWebMessageReceived;
		navWeb.WebMessageReceived += NavWebMessageReceived;
		viewerWeb.NavigationStarting += ViewerNavigationStarting;
		viewerWeb.FrameNavigationStarting += ViewerFrameNavigationStarting;
		viewerWeb.NavigationCompleted += ViewerNavigationCompleted;
		viewerWeb.NewWindowRequested += ViewerNewWindowRequested;
		viewerWeb.WebMessageReceived += ViewerWebMessageReceived;
		viewerWeb.WebResourceRequested += ViewerWebResourceRequested;

		titleWeb.NavigateToString(LoadEmbedded(TitleBarRes));
		await SetIcon($"data:{GetMime(IconRes)};base64,{Convert.ToBase64String(LoadEmbeddedBytes(IconRes))}");
		_ = GetTitleLoop();

		await ShowTitleLoading(30, "Loading Assets...");
		offlineAssets = LoadOfflineAssetIndex(
			Path.Combine(workspaceRoot, "assets", "mhtml-uuid.tsv"),
			workspaceRoot
		);

		await ShowTitleLoading(60, "Loading Cache...");
		string cachePath = Path.Combine(workspaceRoot, "assets", ViewerCacheFileName);
		if (!TryLoadViewerCache(cachePath, out ViewerCacheData viewerCache)) {
			await ShowTitleLoading(50, "Building Link Index...");
			BuildLinkIndex();

			await ShowTitleLoading(80, "Building File Tree...");
			string builtFirst = FindFirstFile(baseRoot);
			viewerCache = new ViewerCacheData(
				builtFirst,
				BuildTree(baseRoot),
				contentLocationMap.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
			);
			SaveViewerCache(cachePath, viewerCache);
		}

		ApplyViewerCache(viewerCache);
		string first = viewerCache.FirstFile;
		if (string.IsNullOrEmpty(first)) {
			Native.ShowMessage(handle, "No MHTML files found in the mhtml folder.", "Error");
			Native.PostQuitMessage(0);
			return;
		}

		List<Node> tree = viewerCache.Tree;
		viewerTree = tree;
		titleCache.Clear();
		string treeJson = JsonSerializer.Serialize(tree, AppJsonContext.Default.ListNode);
		string firstJson = JsonSerializer.Serialize(first, AppJsonContext.Default.String);
		string stateJson = JsonSerializer.Serialize(State.Current, AppJsonContext.Default.STATE);

		navWeb.NavigationCompleted += async (_, _) => {
			await navWeb.ExecuteScriptAsync($@"
				initTree({treeJson}, {firstJson}, {stateJson});
			");
			await ShowTitleLoading(90, "Almost Ready...");
		};
		navWeb.NavigateToString(LoadEmbedded(SidebarRes));
	}
	string GetMime(string fileName) {
		string ext = Path.GetExtension(fileName).ToLowerInvariant();
		return ext switch {
			".mp4" => "video/mp4",
			".png" => "image/png",
			".jpg" or ".jpeg" => "image/jpeg",
			".svg" => "image/svg+xml",
			".ico" => "image/x-icon",
			".webp" => "image/webp",
			_ => "application/octet-stream"
		};
	}
	void ConfigureLocalMediaHost(CoreWebView2 web) {
		web.SetVirtualHostNameToFolderMapping(
			LocalMediaHost,
			workspaceRoot,
			CoreWebView2HostResourceAccessKind.Allow
		);
	}
	async Task GetTitleLoop() {
		while (true) {
			if (isTitleInit) await UpdateJsTitle();
			await Task.Delay(200);
			if (!isTitleInit) continue;
			if (isLoading) isTitleUpdated = true;
			if (!isTitleUpdated) {
				await SetTitle(BuildTitle(currentFilePath), false);
			}
			isTitleUpdated = false;
		}
	}
	async Task SetTitle(string title, bool isAnimate = true) {
		string titleJson = JsonSerializer.Serialize(title, AppJsonContext.Default.String);
		await titleWeb!.ExecuteScriptAsync($"setTitle({titleJson}, {isAnimate.ToString().ToLowerInvariant()})");
	}
	async Task UpdateJsTitle(bool animate = true) {
		try {
			string animateJson = animate.ToString().ToLowerInvariant();
			await viewerWeb!.ExecuteScriptAsync($"if (typeof getTitle === 'function') getTitle({animateJson});");
		} catch {
		}
	}
	string BuildTitle(string file) {
		if (!string.IsNullOrWhiteSpace(file) &&
			titleCache.TryGetValue(file, out string? cachedTitle)) {
			return cachedTitle;
		}

		string title;
		if (!string.IsNullOrWhiteSpace(file) && FindTitleNodeChain(viewerTree, file, out var chain)) {
			title = string.Join(" \u2b9e ", chain
				.Where(node => !string.IsNullOrWhiteSpace(node.name))
				.Select(node => BuildTitleLink(CleanTreeTitle(node.name), node.path, file)));
		} else {
			string fallback = !string.IsNullOrWhiteSpace(file)
				? Path.GetFileNameWithoutExtension(file)
				: Path.GetFileNameWithoutExtension(FindFirstFile(baseRoot));
			title = WebUtility.HtmlEncode(CleanTreeTitle(fallback));
		}

		if (!string.IsNullOrWhiteSpace(file)) titleCache[file] = title;
		return title;
	}
	static string BuildTitleLink(string title, string path, string currentFile) {
		string encodedTitle = WebUtility.HtmlEncode(title);
		if (string.IsNullOrWhiteSpace(path)) return encodedTitle;

		string href = path.Equals(currentFile, StringComparison.OrdinalIgnoreCase)
			? "#"
			: new Uri(path).AbsoluteUri;
		return $"<span data-link=\"true\" href=\"{WebUtility.HtmlEncode(href)}\">{encodedTitle}</span>";
	}
	static string CleanTreeTitle(string title) {
		return Regex.Replace(title, @"^\d+(\.\d+)*\.?\s*", "");
	}
	static bool FindTitleNodeChain(List<Node> nodes, string file, out List<Node> chain) {
		List<Node>? best = null;
		var current = new List<Node>();
		foreach (var node in nodes) {
			FindNodeChain(node, file, current, ref best);
		}

		chain = best ?? new List<Node>();
		return best != null;
	}
	static void FindNodeChain(Node node, string file, List<Node> current, ref List<Node>? best) {
		current.Add(node);
		if (!string.IsNullOrWhiteSpace(node.path) &&
			node.path.Equals(file, StringComparison.OrdinalIgnoreCase) &&
			(best == null || current.Count > best.Count)) {
			best = current.ToList();
		}

		if (node.children != null) {
			foreach (var child in node.children) {
				FindNodeChain(child, file, current, ref best);
			}
		}

		current.RemoveAt(current.Count - 1);
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
	async Task InjectGetTitle() {
		await viewerWeb!.ExecuteScriptAsync(LoadEmbedded(GetTitleRes));
	}
	async Task InjectToggleButton() {
		await viewerWeb!.ExecuteScriptAsync(LoadEmbedded(ToggleSidebarRes));
		await UpdateToggleSidebar();
	}
	async Task ToggleSidebar() {
		State.Current.collapsed = !State.Current.collapsed;
		State.Save(State.Current);
		ResizeWebView();
		await UpdateToggleSidebar();
	}
	async Task UpdateToggleSidebar() {
		await viewerWeb!.ExecuteScriptAsync($@"
			document.querySelector('.sidebarHandle a').innerHTML = '" + (State.Current.collapsed ? '⮞' : '⮜') + @"';
		");
		await navWeb!.ExecuteScriptAsync($"setCollapsed({State.Current.collapsed.ToString().ToLower()})");
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
				var anim = json.RootElement.GetProperty("anim").GetBoolean();
				isTitleUpdated = true;
				await SetTitle(data!, anim);
				break;
			case "UpdateTitle":
				isTitleUpdated = true;
				break;
		}
	}
	async void TitleWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e) {
		using var msg = JsonDocument.Parse(e.WebMessageAsJson);
		string type = msg.RootElement.GetProperty("type").GetString() ?? "";
		switch (type) {
			case "OpenLink":
				string url = msg.RootElement.GetProperty("url").GetString() ?? "";
				if (url == "#") await viewerWeb!.ExecuteScriptAsync("window.scrollTo(0, 0)");
				else await OpenLink(url);
				break;
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
			switch (type) {
				case "setLastFile":
					State.Current.lastFile = msg.RootElement.GetProperty("path").GetString() ?? "";
					break;
				case "open":
					string fullPath = msg.RootElement.GetProperty("path").GetString() ?? "";
					if (File.Exists(fullPath))
						await OpenDocument(fullPath, "");
					else
						await ShowError("File not found:\\n" + fullPath);
					break;
				case "back":
					await NavigateHistory(-1);
					break;
				case "forward":
					await NavigateHistory(1);
					break;
				case "resizeSidebar":
					int requestedWidth = msg.RootElement.GetProperty("width").GetInt32();
					State.Current.sidebarWidth = Math.Clamp(requestedWidth, MinSidebarWidth, MaxSidebarWidth);
					State.Current.collapsed = false;
					State.Save(State.Current);
					ResizeWebView();
					break;
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
		if (IsDocumentResourceUrl(e.Uri)) return;
		if (IsLocalMediaUrl(e.Uri)) return;
		if (e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;
		await OpenLink(e);
	}
	async void ViewerFrameNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e) {
		if (IsDocumentResourceUrl(e.Uri)) return;
		if (!IsLocalMediaUrl(e.Uri)) return;
		e.Cancel = true;
		await OpenLocalMedia(e.Uri);
	}
	async void ViewerNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e) {
		if (IsDocumentResourceUrl(e.Uri)) return;
		if (!IsLocalMediaUrl(e.Uri)) return;
		e.Handled = true;
		await OpenLocalMedia(e.Uri);
	}
	async void ViewerNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e) {
		try {
			if (viewerWeb?.Source != null && IsLocalMediaUrl(viewerWeb.Source)) {
				string title = Path.GetFileName(new Uri(viewerWeb.Source).LocalPath);
				await SetTitle(title, false);
				await HideTitleLoading();
				isLoading = false;
				return;
			}
			if (string.IsNullOrEmpty(currentFilePath)) return;
			string pathJson = JsonSerializer.Serialize(currentFilePath, AppJsonContext.Default.String);
			await navWeb!.ExecuteScriptAsync($"setActiveByPath({pathJson}); hideLoading();");
			await InjectGetTitle();
			await InjectToggleButton();
			if (!string.IsNullOrWhiteSpace(pendingFragment)) {
				string fragment = pendingFragment;
				pendingFragment = string.Empty;
				await NavigateToFragment(fragment);
			}
			await HideTitleLoading();
			isLoading = false;
			if (!isTitleInit) {
				isTitleInit = true;
				await UpdateJsTitle(false);
			}
		} catch {
		}
	}
	async Task OpenLink(CoreWebView2NavigationStartingEventArgs e) {
		if (TryResolveLocalDocument(e.Uri, out string localFile, out string localFragment)) {
			if (IsHtmlFile(localFile) &&
				localFragment.Length == 0 &&
				localFile.Equals(pendingLocalHtmlNavigationPath, StringComparison.OrdinalIgnoreCase)) {
				pendingLocalHtmlNavigationPath = string.Empty;
				return;
			}

			if (await TryNavigateCurrentDocumentFragment(localFile, localFragment)) {
				e.Cancel = true;
				return;
			}

			if (IsHtmlFile(localFile)) {
				await OpenHtml(localFile, localFragment, navigate: false);
			} else {
				e.Cancel = true;
				await OpenMhtml(localFile, localFragment);
			}
			return;
		}
		if (!e.Uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)) {
			isLoading = true;
			await navWeb!.ExecuteScriptAsync("showLoading()");
			return;
		}
		e.Cancel = true;
		if (!TryResolveMhtml(e.Uri, out string file, out string fragment)) return;
		if (await TryNavigateCurrentDocumentFragment(file, fragment)) return;
		await OpenDocument(file, fragment);
	}
	async Task OpenLink(string url) {
		if (TryResolveLocalDocument(url, out string localFile, out string localFragment)) {
			if (await TryNavigateCurrentDocumentFragment(localFile, localFragment)) return;
			await OpenDocument(localFile, localFragment);
			return;
		}
		if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) {
			isLoading = true;
			await navWeb!.ExecuteScriptAsync("showLoading()");
			return;
		}
		if (!TryResolveMhtml(url, out string file, out string fragment)) return;
		if (await TryNavigateCurrentDocumentFragment(file, fragment)) return;
		await OpenDocument(file, fragment);
	}
	async Task<bool> TryNavigateCurrentDocumentFragment(string file, string fragment, bool addHistory = true) {
		if (!file.Equals(currentFilePath, StringComparison.OrdinalIgnoreCase)) return false;
		if (viewerWeb == null) return false;

		if (addHistory) AddHistory(NavigationEntry.Document(file, fragment));
		await NavigateToFragment(fragment);
		return true;
	}
	async Task NavigateToFragment(string fragment) {
		string fragmentJson = JsonSerializer.Serialize(fragment, AppJsonContext.Default.String);
		await viewerWeb!.ExecuteScriptAsync($@"
			(() => {{
				const raw = {fragmentJson};
				if (!raw) {{
					history.replaceState(null, '', location.pathname + location.search);
					window.scrollTo(0, 0);
					return;
				}}
				const decoded = decodeURIComponent(raw);
				const target = document.getElementById(decoded)
					|| document.getElementById(raw)
					|| document.querySelector(`[name=""${{CSS.escape(decoded)}}""]`)
					|| document.querySelector(`[name=""${{CSS.escape(raw)}}""]`);
				if (target) target.scrollIntoView();
				location.hash = raw;
			}})();
		");
	}
	bool IsLocalMediaUrl(string url) {
		return Uri.TryCreate(url, UriKind.Absolute, out var uri)
			&& uri.Host.Equals(LocalMediaHost, StringComparison.OrdinalIgnoreCase);
	}
	bool IsDocumentResourceUrl(string url) {
		if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
		if (!uri.Host.Equals(DocumentResourceHost, StringComparison.OrdinalIgnoreCase)) return false;
		return uri.AbsolutePath.StartsWith("/cid/", StringComparison.OrdinalIgnoreCase)
			|| uri.AbsolutePath.StartsWith("/document/", StringComparison.OrdinalIgnoreCase);
	}
	async Task OpenLocalMedia(string url, bool addHistory = true) {
		if (!TryResolveLocalMediaPath(url, out string file)) {
			await ShowError("Media file not found:\\n" + url);
			return;
		}
		if (addHistory) AddHistory(NavigationEntry.Media(url));
		viewerWeb!.Navigate(url);
	}
	bool TryResolveLocalMediaPath(string url, out string file) {
		file = string.Empty;
		if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
		if (!uri.Host.Equals(LocalMediaHost, StringComparison.OrdinalIgnoreCase)) return false;

		string relativePath = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'))
			.Replace('/', Path.DirectorySeparatorChar);
		string fullPath = Path.GetFullPath(Path.Combine(workspaceRoot, relativePath));
		string rootPath = Path.GetFullPath(workspaceRoot);
		if (!rootPath.EndsWith(Path.DirectorySeparatorChar)) rootPath += Path.DirectorySeparatorChar;
		if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)) return false;
		if (!File.Exists(fullPath)) return false;

		file = fullPath;
		return true;
	}
	bool TryResolveLocalDocument(string url, out string file, out string fragment) {
		file = string.Empty;
		fragment = string.Empty;
		if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
		if (!uri.IsFile) return false;

		string fullPath = Path.GetFullPath(uri.LocalPath);
		if (!IsDocumentFile(fullPath)) return false;
		if (!IsPathInsideRoot(fullPath, workspaceRoot)) return false;
		if (!File.Exists(fullPath)) return false;

		file = fullPath;
		fragment = uri.Fragment.Length > 0 ? uri.Fragment[1..] : string.Empty;
		return true;
	}
	static bool IsHtmlFile(string file) {
		string ext = Path.GetExtension(file);
		return ext.Equals(".html", StringComparison.OrdinalIgnoreCase)
			|| ext.Equals(".htm", StringComparison.OrdinalIgnoreCase);
	}
	static bool IsDocumentFile(string file) {
		string ext = Path.GetExtension(file);
		return IsHtmlFile(file)
			|| ext.Equals(".mhtml", StringComparison.OrdinalIgnoreCase)
			|| ext.Equals(".mht", StringComparison.OrdinalIgnoreCase);
	}
	static bool IsPathInsideRoot(string path, string root) {
		string fullPath = Path.GetFullPath(path);
		string rootPath = Path.GetFullPath(root);
		if (!rootPath.EndsWith(Path.DirectorySeparatorChar)) rootPath += Path.DirectorySeparatorChar;
		return fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase);
	}
	async Task OpenDocument(string file, string fragment, bool addHistory = true) {
		if (IsHtmlFile(file)) {
			await OpenHtml(file, fragment, addHistory);
			return;
		}

		await OpenMhtml(file, fragment, addHistory);
	}
	Task OpenHtml(string file, string fragment, bool addHistory = true, bool navigate = true) {
		currentFilePath = file;
		pendingFragment = fragment;
		currentDocument = null;
		State.Current.lastFile = file;
		State.Save(State.Current);
		if (addHistory) AddHistory(NavigationEntry.Document(file, fragment));
		if (navigate) {
			pendingLocalHtmlNavigationPath = file;
			viewerWeb!.Navigate(new Uri(file).AbsoluteUri);
		}
		return Task.CompletedTask;
	}
	async Task OpenMhtml(string file, string fragment, bool addHistory = true) {
		currentFilePath = file;
		pendingFragment = fragment;
		currentDocument = documentCache.GetOrAdd(file, LoadDocument);
		State.Current.lastFile = file;
		State.Save(State.Current);
		if (addHistory) AddHistory(NavigationEntry.Document(file, fragment));
		viewerWeb!.Navigate(BuildDocumentRootUrl());
	}
	string BuildDocumentRootUrl() {
		int version = ++documentNavigationVersion;
		return $"https://{DocumentResourceHost}/document/{version}/index.html";
	}
	void AddHistory(NavigationEntry entry) {
		if (navigationIndex >= 0 &&
			navigationIndex < navigationHistory.Count &&
			navigationHistory[navigationIndex].Equals(entry)) {
			return;
		}

		if (navigationIndex < navigationHistory.Count - 1) {
			navigationHistory.RemoveRange(navigationIndex + 1, navigationHistory.Count - navigationIndex - 1);
		}

		navigationHistory.Add(entry);
		navigationIndex = navigationHistory.Count - 1;
	}
	async Task NavigateHistory(int delta) {
		int nextIndex = navigationIndex + delta;
		if (nextIndex < 0 || nextIndex >= navigationHistory.Count) return;

		navigationIndex = nextIndex;
		NavigationEntry entry = navigationHistory[navigationIndex];
		if (!string.IsNullOrEmpty(entry.MediaUrl)) {
			await OpenLocalMedia(entry.MediaUrl, false);
			return;
		}

		if (await TryNavigateCurrentDocumentFragment(entry.FilePath, entry.Fragment, false)) return;
		await OpenDocument(entry.FilePath, entry.Fragment, false);
	}
	async Task ShowError(string message) {
		string json = JsonSerializer.Serialize(message, AppJsonContext.Default.String);
		if (navWeb != null) {
			await navWeb.ExecuteScriptAsync($"showError({json});");
		} else {
			Native.ShowMessage(handle, message, "Open Error");
		}
	}
	void ApplyViewerCache(ViewerCacheData cache) {
		contentLocationMap.Clear();
		foreach (var kv in cache.ContentLocations) {
			contentLocationMap[kv.Key] = kv.Value;
		}

		FirstFile = cache.FirstFile;
		isFirstFileInit = true;
	}
	bool TryLoadViewerCache(string path, out ViewerCacheData cache) {
		cache = default!;
		if (!File.Exists(path)) return false;

		try {
			using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
			using var reader = new BinaryReader(fs, Encoding.UTF8);

			if (!reader.ReadString().Equals(ViewerCacheMagic, StringComparison.Ordinal)) return false;
			if (reader.ReadInt32() != ViewerCacheVersion) return false;

			string firstFile = reader.ReadString();
			var locations = ReadStringDictionary(reader, StringComparer.OrdinalIgnoreCase);
			var tree = ReadNodeList(reader);

			cache = new ViewerCacheData(firstFile, tree, locations);
			return true;
		} catch {
			return false;
		}
	}
	void SaveViewerCache(string path, ViewerCacheData cache) {
		try {
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan);
			using var writer = new BinaryWriter(fs, Encoding.UTF8);

			writer.Write(ViewerCacheMagic);
			writer.Write(ViewerCacheVersion);
			writer.Write(cache.FirstFile);
			WriteStringDictionary(writer, cache.ContentLocations);
			WriteNodeList(writer, cache.Tree);
		} catch {
		}
	}
	static void WriteStringDictionary(BinaryWriter writer, Dictionary<string, string> items) {
		writer.Write(items.Count);
		foreach (var kv in items) {
			writer.Write(kv.Key);
			writer.Write(kv.Value);
		}
	}
	static Dictionary<string, string> ReadStringDictionary(BinaryReader reader, StringComparer comparer) {
		int count = reader.ReadInt32();
		var items = new Dictionary<string, string>(count, comparer);
		for (int i = 0; i < count; i++) {
			items[reader.ReadString()] = reader.ReadString();
		}
		return items;
	}
	static void WriteNodeList(BinaryWriter writer, List<Node> nodes) {
		writer.Write(nodes.Count);
		foreach (var node in nodes) {
			WriteNode(writer, node);
		}
	}
	static List<Node> ReadNodeList(BinaryReader reader) {
		int count = reader.ReadInt32();
		var nodes = new List<Node>(count);
		for (int i = 0; i < count; i++) {
			nodes.Add(ReadNode(reader));
		}
		return nodes;
	}
	static void WriteNode(BinaryWriter writer, Node node) {
		writer.Write(node.name);
		writer.Write(node.path);
		writer.Write(node.children != null);
		if (node.children != null) {
			WriteNodeList(writer, node.children);
		}
	}
	static Node ReadNode(BinaryReader reader) {
		var node = new Node {
			name = reader.ReadString(),
			path = reader.ReadString()
		};

		if (reader.ReadBoolean()) {
			node.children = ReadNodeList(reader);
		}

		return node;
	}
	void BuildLinkIndex() {
		Parallel.ForEach(
			Directory.EnumerateFiles(baseRoot, "*.mhtml", SearchOption.AllDirectories),
			file => {
				Span<byte> buffer = stackalloc byte[512];
				using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 512, FileOptions.SequentialScan);

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
		// 1. Scan semua file
		var allFiles = Directory
			.EnumerateFiles(root, "*.mhtml", SearchOption.AllDirectories)
			.Concat(Directory.EnumerateFiles(root, "*.html", SearchOption.AllDirectories))
			.ToList();

		// 3. Group ke folder (RAM only)
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
					name = DecodeTreeName(Path.GetFileNameWithoutExtension(file)),
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
				name = DecodeTreeName(Path.GetFileName(dir)),
				path = target,
				children = children
			});
		});

		// sorting tetap di akhir (single-thread)
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
	string DecodeTreeName(string name) {
		string normalized = Regex.Replace(
			name,
			@"&(#\d+|#x[0-9a-fA-F]+|[A-Za-z][A-Za-z0-9]+)_",
			"&$1;"
		);
		return WebUtility.HtmlDecode(normalized);
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
	bool isFirstFileInit = false;
	string FirstFile = string.Empty;
	string FindFirstFile(string root) {
		if (isFirstFileInit) return FirstFile;
		var files = Directory.GetFiles(root, "*.mhtml", SearchOption.AllDirectories)
			.OrderBy(f => ExtractNumber(Path.GetFileName(f)))
			.ThenBy(f => f);
		if (files.Any()) FirstFile = files.First();
		isFirstFileInit = true;
		return FirstFile;
	}
	int ExtractNumber(string name) {
		string[] parts = name.Split('.');
		return int.TryParse(parts[0], out int n) ? n : int.MaxValue;
	}
	void ViewerWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e) {
		if (viewerWeb == null || currentDocument == null) return;
		if (!TryResolveDocumentResource(e.Request.Uri, out var resource)) return;

		string contentType = resource.ContentType;
		string headers = $"Content-Type: {contentType}\r\nAccess-Control-Allow-Origin: *";
		Stream stream = resource.FilePath != null
			? new FileStream(resource.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan)
			: new MemoryStream(resource.Bytes!, writable: false);

		e.Response = viewerWeb.Environment.CreateWebResourceResponse(stream, 200, "OK", headers);
	}
	bool TryResolveDocumentResource(string requestUrl, out ResourceEntry resource) {
		resource = default!;
		var document = currentDocument;
		if (document == null) return false;

		int fragmentIndex = requestUrl.IndexOf('#');
		if (fragmentIndex >= 0) requestUrl = requestUrl[..fragmentIndex];

		if (Uri.TryCreate(requestUrl, UriKind.Absolute, out var uri) &&
			uri.Host.Equals(DocumentResourceHost, StringComparison.OrdinalIgnoreCase) &&
			uri.AbsolutePath.StartsWith("/document/", StringComparison.OrdinalIgnoreCase)) {
			resource = new ResourceEntry("text/html; charset=utf-8", document.HtmlBytes, null);
			return true;
		}

		string cidPrefix = $"https://{DocumentResourceHost}/cid/";
		if (requestUrl.StartsWith(cidPrefix, StringComparison.OrdinalIgnoreCase)) {
			string cid = Uri.UnescapeDataString(requestUrl[cidPrefix.Length..]);
			return document.ResourcesByCid.TryGetValue(cid, out resource!);
		}

		return document.ResourcesByUrl.TryGetValue(requestUrl, out resource!);
	}
	LoadedDocument LoadDocument(string file) {
		string content = File.ReadAllText(file);
		string? boundary = TryExtractBoundary(content);
		if (string.IsNullOrWhiteSpace(boundary)) {
			throw new InvalidOperationException("Invalid MHTML: multipart boundary not found.");
		}

		string marker = "--" + boundary;
		string[] segments = content.Split(marker, StringSplitOptions.None);
		if (segments.Length < 2) {
			throw new InvalidOperationException("Invalid MHTML: no multipart sections found.");
		}

		var resourcesByUrl = new Dictionary<string, ResourceEntry>(StringComparer.Ordinal);
		var resourcesByCid = new Dictionary<string, ResourceEntry>(StringComparer.OrdinalIgnoreCase);
		string? rootHtml = null;

		for (int i = 1; i < segments.Length; i++) {
			string segment = segments[i];
			if (segment.StartsWith("--", StringComparison.Ordinal)) break;

			segment = segment.TrimStart('\r', '\n');
			if (string.IsNullOrWhiteSpace(segment)) continue;

			MhtmlPart part = ParsePart(segment);
			if (rootHtml == null && part.ContentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase)) {
				rootHtml = RewriteCidUrls(DecodePartText(part.Body, part.TransferEncoding, part.ContentType));
			}

			ResourceEntry? resource = CreateResourceEntry(part, offlineAssets);
			if (resource == null) continue;

			if (!string.IsNullOrWhiteSpace(part.ContentLocation)) {
				string location = part.ContentLocation.Trim();
				if (!location.StartsWith("cid:", StringComparison.OrdinalIgnoreCase) && !resourcesByUrl.ContainsKey(location)) {
					resourcesByUrl[location] = resource;
				}

				string cidFromLocation = NormalizeCidToken(location);
				if (!string.IsNullOrEmpty(cidFromLocation) && !resourcesByCid.ContainsKey(cidFromLocation)) {
					resourcesByCid[cidFromLocation] = resource;
				}
			}

			if (!string.IsNullOrWhiteSpace(part.ContentId)) {
				string cid = NormalizeCidToken(part.ContentId);
				if (!string.IsNullOrEmpty(cid) && !resourcesByCid.ContainsKey(cid)) {
					resourcesByCid[cid] = resource;
				}
			}
		}

		if (string.IsNullOrWhiteSpace(rootHtml)) {
			throw new InvalidOperationException("Invalid MHTML: root HTML part not found.");
		}

		return new LoadedDocument(rootHtml, Encoding.UTF8.GetBytes(rootHtml), resourcesByUrl, resourcesByCid);
	}
	static string RewriteCidUrls(string html) {
		return Regex.Replace(
			html,
			@"cid:([^\s""'<>())]+)",
			match => BuildLocalCidUrl(match.Groups[1].Value),
			RegexOptions.IgnoreCase
		);
	}
	static string BuildLocalCidUrl(string cid) {
		return $"https://{DocumentResourceHost}/cid/{Uri.EscapeDataString(NormalizeCidToken(cid))}";
	}
	static string NormalizeCidToken(string cid) {
		if (string.IsNullOrWhiteSpace(cid)) return string.Empty;
		cid = cid.Trim();
		if (cid.StartsWith("cid:", StringComparison.OrdinalIgnoreCase)) cid = cid[4..];
		if (cid.StartsWith("<", StringComparison.Ordinal) && cid.EndsWith(">", StringComparison.Ordinal) && cid.Length > 2) {
			cid = cid[1..^1];
		}
		return cid;
	}
	static string? TryExtractBoundary(string content) {
		var match = Regex.Match(content, "boundary=\"([^\"]+)\"", RegexOptions.IgnoreCase);
		return match.Success ? match.Groups[1].Value : null;
	}
	static MhtmlPart ParsePart(string segment) {
		int separatorLength = 4;
		int separator = segment.IndexOf("\r\n\r\n", StringComparison.Ordinal);
		if (separator < 0) {
			separator = segment.IndexOf("\n\n", StringComparison.Ordinal);
			separatorLength = 2;
		}

		string headerText;
		string body;
		if (separator < 0) {
			headerText = segment.Trim();
			body = string.Empty;
		} else {
			headerText = segment[..separator].TrimEnd();
			body = segment[(separator + separatorLength)..];
		}

		var headers = ParseHeaders(headerText);
		headers.TryGetValue("Content-Type", out string? contentType);
		headers.TryGetValue("Content-Transfer-Encoding", out string? transferEncoding);
		headers.TryGetValue("Content-Location", out string? contentLocation);
		headers.TryGetValue("Content-ID", out string? contentId);

		return new MhtmlPart(
			contentType ?? "application/octet-stream",
			transferEncoding ?? "8bit",
			contentLocation ?? string.Empty,
			contentId ?? string.Empty,
			body
		);
	}
	static Dictionary<string, string> ParseHeaders(string headerText) {
		var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string currentName = string.Empty;

		foreach (string rawLine in headerText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')) {
			if (string.IsNullOrWhiteSpace(rawLine)) continue;

			if ((rawLine[0] == ' ' || rawLine[0] == '\t') && currentName.Length > 0) {
				headers[currentName] += " " + rawLine.Trim();
				continue;
			}

			int colonIndex = rawLine.IndexOf(':');
			if (colonIndex <= 0) continue;

			currentName = rawLine[..colonIndex].Trim();
			headers[currentName] = rawLine[(colonIndex + 1)..].Trim();
		}

		return headers;
	}
	static ResourceEntry? CreateResourceEntry(MhtmlPart part, Dictionary<string, OfflineAsset> offlineAssets) {
		if (HasBody(part.Body)) {
			return new ResourceEntry(part.ContentType, DecodePartBytes(part.Body, part.TransferEncoding, part.ContentType), null);
		}

		if (!string.IsNullOrWhiteSpace(part.ContentLocation) &&
			offlineAssets.TryGetValue(part.ContentLocation.Trim(), out var asset)) {
			return new ResourceEntry(asset.ContentType, null, asset.FilePath);
		}

		return null;
	}
	static bool HasBody(string body) {
		return !string.IsNullOrWhiteSpace(body);
	}
	static byte[] DecodePartBytes(string body, string transferEncoding, string contentType) {
		string normalizedEncoding = transferEncoding.Trim().ToLowerInvariant();
		return normalizedEncoding switch {
			"base64" => DecodeBase64Bytes(body),
			"quoted-printable" => DecodeQuotedPrintableToBytes(body),
			_ => Encoding.UTF8.GetBytes(body)
		};
	}
	static string DecodePartText(string body, string transferEncoding, string contentType) {
		return Encoding.UTF8.GetString(DecodePartBytes(body, transferEncoding, contentType));
	}
	static byte[] DecodeQuotedPrintableToBytes(string input) {
		using var ms = new MemoryStream(input.Length);

		for (int i = 0; i < input.Length; i++) {
			char ch = input[i];
			if (ch != '=') {
				ms.WriteByte((byte)ch);
				continue;
			}

			if (i + 1 < input.Length && input[i + 1] == '\r') {
				i++;
				if (i + 1 < input.Length && input[i + 1] == '\n') i++;
				continue;
			}
			if (i + 1 < input.Length && input[i + 1] == '\n') {
				i++;
				continue;
			}
			if (i + 2 < input.Length &&
				IsHex(input[i + 1]) &&
				IsHex(input[i + 2])) {
				byte value = (byte)((HexToInt(input[i + 1]) << 4) | HexToInt(input[i + 2]));
				ms.WriteByte(value);
				i += 2;
				continue;
			}

			ms.WriteByte((byte)'=');
		}

		return ms.ToArray();
	}
	static bool IsHex(char c) {
		return (c >= '0' && c <= '9')
			|| (c >= 'a' && c <= 'f')
			|| (c >= 'A' && c <= 'F');
	}
	static int HexToInt(char c) {
		return c switch {
			>= '0' and <= '9' => c - '0',
			>= 'a' and <= 'f' => c - 'a' + 10,
			>= 'A' and <= 'F' => c - 'A' + 10,
			_ => 0
		};
	}
	static byte[] DecodeBase64Bytes(string input) {
		string normalized = new(input.Where(c => !char.IsWhiteSpace(c)).ToArray());
		return Convert.FromBase64String(normalized);
	}
	static Dictionary<string, OfflineAsset> LoadOfflineAssetIndex(string path, string rootDirectory) {
		var map = new Dictionary<string, OfflineAsset>(StringComparer.Ordinal);
		if (!File.Exists(path)) return map;

		foreach (string line in File.ReadLines(path).Skip(1)) {
			if (string.IsNullOrWhiteSpace(line)) continue;

			string[] parts = line.Split('\t');
			if (parts.Length < 6) continue;

			string link = parts[0].Trim();
			string relativePath = parts[1].Trim().Replace('/', Path.DirectorySeparatorChar);
			string contentType = parts[2].Trim();
			string fullPath = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));

			map[link] = new OfflineAsset(fullPath, contentType);
		}

		return map;
	}
	public void Dispose() {
		viewerController?.Close();
		navController?.Close();
		titleController?.Close();
	}
	sealed record OfflineAsset(string FilePath, string ContentType);
	sealed record ViewerCacheData(
		string FirstFile,
		List<Node> Tree,
		Dictionary<string, string> ContentLocations
	);
	sealed record LoadedDocument(
		string Html,
		byte[] HtmlBytes,
		Dictionary<string, ResourceEntry> ResourcesByUrl,
		Dictionary<string, ResourceEntry> ResourcesByCid
	);
	sealed record ResourceEntry(string ContentType, byte[]? Bytes, string? FilePath);
	sealed record MhtmlPart(
		string ContentType,
		string TransferEncoding,
		string ContentLocation,
		string ContentId,
		string Body
	);
	sealed record NavigationEntry(string FilePath, string Fragment, string MediaUrl) {
		public static NavigationEntry Document(string filePath, string fragment) {
			return new NavigationEntry(filePath, fragment, string.Empty);
		}

		public static NavigationEntry Media(string url) {
			return new NavigationEntry(string.Empty, string.Empty, url);
		}
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
