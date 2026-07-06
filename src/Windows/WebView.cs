using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;
using System.Net;

/// <summary>
/// Main desktop shell that hosts three WebView2 surfaces: title bar, sidebar, and document viewer.
/// </summary>
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
	const string EpicSwitchRes = "EpicSwitch.js";
	const string EpicCodeRes = "EpicCode.js";
	const string EpicBlueprintRes = "EpicBlueprint.js";
	const string EpicComparisonSliderRes = "EpicComparisonSlider.js";
	const string EpicSliderSequenceRes = "EpicSliderSequence.js";
	const string ToggleSidebarRes = "ToggleSideBar.js";
	const string DocumentResourceHost = "mhtml.local";
	const string LocalMediaHost = "media.local";
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
	string firstFilePath = string.Empty;
	readonly Dictionary<string, string> titleCache = new(StringComparer.OrdinalIgnoreCase);
	readonly List<NavigationEntry> navigationHistory = new();
	int navigationIndex = -1;
	IntPtr handle;
	IntPtr backgroundBrush;
	IntPtr largeIcon;
	IntPtr smallIcon;
	bool canPersistWindowState;

	// Keep the delegate rooted for the lifetime of the window; Win32 calls this pointer directly.
	Native.WndProcDelegate? wndProcDelegate;

	public IntPtr Handle => handle;

	/// <summary>
	/// Registers and creates the borderless Win32 host window used by all WebView2 controllers.
	/// </summary>
	public void Create() {
		wndProcDelegate = WndProc;
		Native.ExtractIconEx(Environment.ProcessPath!, 0, out largeIcon, out smallIcon, 1);
		backgroundBrush = Native.CreateSolidBrush(0x101010);
		var wc = new Native.WNDCLASS {
			lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProcDelegate),
			lpszClassName = "MHTMLViewerWindow",
			hInstance = Native.GetModuleHandle(null),
			hIcon = largeIcon,
			hbrBackground = backgroundBrush
		};
		Native.RegisterClass(ref wc);
		int screenWidth = Native.GetSystemMetrics(Native.SM_CXSCREEN);
		int screenHeight = Native.GetSystemMetrics(Native.SM_CYSCREEN);
		// Use a centered desktop window when the screen is large enough; otherwise fill the display.
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
				Native.GetSystemMetrics(Native.SM_CXSCREEN),
				Native.GetSystemMetrics(Native.SM_CYSCREEN),
				IntPtr.Zero,
				IntPtr.Zero,
				wc.hInstance,
				IntPtr.Zero
			);
		}
		int style = Native.GetWindowLong(handle, Native.GWL_STYLE);
		style &= ~Native.WS_CAPTION;
		style |= Native.WS_THICKFRAME | Native.WS_MAXIMIZEBOX | Native.WS_MINIMIZEBOX;
		Native.SetWindowLong(handle, Native.GWL_STYLE, style);
		Native.SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOMOVE | Native.SWP_FRAMECHANGED);
		if (State.Current.Maximized) {
			Native.ShowWindow(handle, Native.SW_MAXIMIZE);
		}
		canPersistWindowState = true;
	}

	/// <summary>
	/// Handles native window messages that cannot be delegated to WebView2.
	/// </summary>
	IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) {
		switch (msg) {
			case Native.WM_ACTIVATE:
				Native.InvalidateRect(hWnd, IntPtr.Zero, true);
				return IntPtr.Zero;
			case Native.WM_NCHITTEST:
				const int resizeBorder = ResizeBorder + 2;

				Native.GetWindowRect(hWnd, out var r);

				int x = (short)(lParam.ToInt32() & 0xFFFF);
				int y = (short)((lParam.ToInt32() >> 16) & 0xFFFF);

				// Borderless windows must return resize hit-test values manually.
				bool onLeft = x >= r.Left && x < r.Left + resizeBorder;
				bool onRight = x <= r.Right && x > r.Right - resizeBorder;
				bool onTop = y >= r.Top && y < r.Top + resizeBorder;
				bool onBottom = y <= r.Bottom && y > r.Bottom - resizeBorder;

				if (onTop && onLeft) return Native.HTTOPLEFT;
				if (onTop && onRight) return Native.HTTOPRIGHT;
				if (onBottom && onLeft) return Native.HTBOTTOMLEFT;
				if (onBottom && onRight) return Native.HTBOTTOMRIGHT;

				if (onLeft) return Native.HTLEFT;
				if (onRight) return Native.HTRIGHT;
				if (onTop) return Native.HTTOP;
				if (onBottom) return Native.HTBOTTOM;

				return Native.HTCLIENT;
			case Native.WM_NCCALCSIZE:
				if (wParam != IntPtr.Zero) {
					// Shrink the non-client area so Windows keeps resizing semantics without drawing a title bar.
					var p = Marshal.PtrToStructure<Native.NCCALCSIZE_PARAMS>(lParam);
					p.rgrc0.Top += 1;
					Marshal.StructureToPtr(p, lParam, false);
					return IntPtr.Zero;
				}
				return Native.DefWindowProc(hWnd, msg, wParam, lParam);
			case Native.WM_SIZE:
				PersistMaximizedState(wParam);
				ResizeWebView();
				return IntPtr.Zero;
			case Native.WM_DESTROY:
				PersistMaximizedState();
				Native.PostQuitMessage(0);
				return IntPtr.Zero;
			case Native.WM_CUSTOM:
				// Posted by SyncContext to run async continuations on the UI thread.
				SyncContext.DispatchQueuedCallbacks();
				return IntPtr.Zero;
			default:
				return Native.DefWindowProc(hWnd, msg, wParam, lParam);
		}
	}

	/// <summary>
	/// Recomputes the three WebView2 controller bounds after resize, maximize, or sidebar changes.
	/// </summary>
	void ResizeWebView() {
		if (navController == null || viewerController == null || titleController == null || handle == IntPtr.Zero) return;
		Native.GetClientRect(handle, out var rect);
		int width = Math.Max(0, rect.Right - rect.Left);
		int height = Math.Max(0, rect.Bottom - rect.Top);
		int contentHeight = height - TitleBarHeight;
		int sidebarW = State.Current.Collapsed ? 0 : State.Current.SidebarWidth;
		bool isMax = Native.IsZoomed(handle);
		int border = isMax ? ResizeBorder + 5 : ResizeBorder;
		// Title bar spans the top; sidebar and viewer share the remaining content height.
		titleController.Bounds = new Rectangle(
			border,
			isMax ? border : 0,
			width - (border * 2),
			TitleBarHeight
		);
		navController.Bounds = new Rectangle(
			border,
			isMax ? TitleBarHeight + border : TitleBarHeight,
			sidebarW, contentHeight - border
		);
		viewerController.Bounds = new Rectangle(
			sidebarW + border,
			isMax ? TitleBarHeight + border : TitleBarHeight,
			width - sidebarW - (border * 2), contentHeight - border
		);
	}

	/// <summary>
	/// Creates WebView2 controllers, loads embedded UI, builds indexes/cache, and initializes the sidebar.
	/// </summary>
	public async Task InitializeAsync() {
		workspaceRoot = ResolveWorkspaceRoot();
		baseRoot = Path.Combine(workspaceRoot, "mhtml");

		string tempPath = AppPaths.TempDirectory;
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

		// The three WebViews have different UX roles, so settings and zoom are tuned independently.
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
		titleWeb.NavigationCompleted += async (_, _) => await UpdateMaximizeState();
		navWeb.WebMessageReceived += NavWebMessageReceived;
		viewerWeb.NavigationStarting += ViewerNavigationStarting;
		viewerWeb.FrameNavigationStarting += ViewerFrameNavigationStarting;
		viewerWeb.NavigationCompleted += ViewerNavigationCompleted;
		viewerWeb.NewWindowRequested += ViewerNewWindowRequested;
		viewerWeb.WebMessageReceived += ViewerWebMessageReceived;
		viewerWeb.WebResourceRequested += ViewerWebResourceRequested;

		// The title bar is available first so startup progress can be shown while indexes load.
		titleWeb.NavigateToString(EmbeddedResourceLoader.LoadText(TitleBarRes));
		await SetIcon($"data:{GetMime(IconRes)};base64,{Convert.ToBase64String(EmbeddedResourceLoader.LoadBytes(IconRes))}");
		_ = GetTitleLoop();

		await ShowTitleLoading(30, "Loading Assets...");
		offlineAssets = OfflineAssetIndex.Load(
			Path.Combine(workspaceRoot, "assets", "mhtml-uuid.tsv"),
			workspaceRoot
		);

		await ShowTitleLoading(60, "Loading Cache...");
		string cachePath = Path.Combine(workspaceRoot, "assets", ViewerCacheStore.FileName);
		if (!ViewerCacheStore.TryLoad(cachePath, out ViewerCacheData viewerCache)) {
			// Cache miss: rebuild both the URL lookup and sidebar tree from the mhtml directory.
			await ShowTitleLoading(50, "Building Link Index...");
			foreach (var kv in ContentLocationIndexBuilder.Build(baseRoot, workspaceRoot)) {
				contentLocationMap.TryAdd(kv.Key, kv.Value);
			}

			await ShowTitleLoading(80, "Building File Tree...");
			List<Node> builtTree = DocumentTreeBuilder.Build(baseRoot, workspaceRoot);
			string builtFirst = DocumentTreeBuilder.FindFirstTreePath(builtTree);
			viewerCache = new ViewerCacheData(
				builtFirst,
				builtTree,
				contentLocationMap.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
			);
			ViewerCacheStore.Save(cachePath, viewerCache);
		}

		ApplyViewerCache(viewerCache);
		string first = firstFilePath;
		if (string.IsNullOrEmpty(first)) {
			Native.ShowMessage(handle, "No MHTML files found in the mhtml folder.", "Error");
			Native.PostQuitMessage(0);
			return;
		}

		List<Node> tree = viewerCache.Tree;
		NormalizeTreePaths(tree);
		viewerTree = tree;
		titleCache.Clear();
		NormalizePersistedState();
		string treeJson = JsonSerializer.Serialize(tree, AppJsonContext.Default.ListNode);
		string firstJson = JsonSerializer.Serialize(first, AppJsonContext.Default.String);
		string stateJson = JsonSerializer.Serialize(State.Current, AppJsonContext.Default.AppState);

		navWeb.NavigationCompleted += async (_, _) => {
			// Sidebar JS owns tree rendering; C# only sends the prepared data once the page is ready.
			await navWeb.ExecuteScriptAsync($@"
				initTree({treeJson}, {firstJson}, {stateJson});
			");
			await ShowTitleLoading(90, "Almost Ready...");
		};
		navWeb.NavigateToString(EmbeddedResourceLoader.LoadText(SidebarRes));
	}

	static string ResolveWorkspaceRoot() {
		string appBase = AppContext.BaseDirectory;
		string current = Directory.GetCurrentDirectory();
		var candidates = new List<string>();
		AddCandidateAndParents(candidates, appBase);
		AddCandidateAndParents(candidates, current);

		foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase)) {
			if (LooksLikeWorkspaceRoot(candidate)) return Path.GetFullPath(candidate);
		}

		return Path.GetFullPath(current);
	}

	static void AddCandidateAndParents(List<string> candidates, string? start) {
		if (string.IsNullOrWhiteSpace(start)) return;

		var dir = new DirectoryInfo(Path.GetFullPath(start));
		while (dir != null) {
			candidates.Add(dir.FullName);
			dir = dir.Parent;
		}
	}

	static bool LooksLikeWorkspaceRoot(string path) {
		return Directory.Exists(Path.Combine(path, "mhtml"))
			|| File.Exists(Path.Combine(path, "assets", "mhtml-uuid.tsv"));
	}

	/// <summary>
	/// Returns a content type for resources injected into WebView2.
	/// </summary>
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
		// media.local exposes files from the workspace while TryResolveLocalMediaPath enforces root bounds.
		web.SetVirtualHostNameToFolderMapping(
			LocalMediaHost,
			workspaceRoot,
			CoreWebView2HostResourceAccessKind.Allow
		);
	}

	/// <summary>
	/// Polls the active document for its visible section title and mirrors it into the custom title bar.
	/// </summary>
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
			// Documents may not have the title helper injected yet; the next loop tick will retry.
		}
	}

	/// <summary>
	/// Builds breadcrumb-style title HTML from the sidebar tree, falling back to the file name.
	/// </summary>
	string BuildTitle(string file) {
		if (!string.IsNullOrWhiteSpace(file) &&
			titleCache.TryGetValue(file, out string? cachedTitle)) {
			return cachedTitle;
		}

		string title;
		if (!string.IsNullOrWhiteSpace(file) && FindTitleNodeChain(viewerTree, file, out var chain)) {
			title = string.Join(" \u2b9e ", chain
				.Where(node => !string.IsNullOrWhiteSpace(node.Name))
				.Select(node => BuildTitleLink(CleanTreeTitle(node.Name), node.Path, file)));
		} else {
			string firstKnownFile = !string.IsNullOrWhiteSpace(firstFilePath)
				? firstFilePath
				: DocumentTreeBuilder.FindFirstFile(baseRoot, workspaceRoot);
			string fallback = !string.IsNullOrWhiteSpace(file)
				? Path.GetFileNameWithoutExtension(file)
				: Path.GetFileNameWithoutExtension(firstKnownFile);
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
			: path;
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

	/// <summary>
	/// Depth-first search that keeps the longest matching tree path for breadcrumb titles.
	/// </summary>
	static void FindNodeChain(Node node, string file, List<Node> current, ref List<Node>? best) {
		current.Add(node);
		if (!string.IsNullOrWhiteSpace(node.Path) &&
			node.Path.Equals(file, StringComparison.OrdinalIgnoreCase) &&
			(best == null || current.Count > best.Count)) {
			best = current.ToList();
		}

		if (node.Children != null) {
			foreach (var child in node.Children) {
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
		await viewerWeb!.ExecuteScriptAsync(EmbeddedResourceLoader.LoadText(GetTitleRes));
	}
	async Task InjectToggleButton() {
		await viewerWeb!.ExecuteScriptAsync(EmbeddedResourceLoader.LoadText(ToggleSidebarRes));
		await UpdateToggleSidebar();
	}
	async Task InjectEpicSwitch() {
		await viewerWeb!.ExecuteScriptAsync(EmbeddedResourceLoader.LoadText(EpicSwitchRes));
	}
	async Task InjectEpicCode() {
		await viewerWeb!.ExecuteScriptAsync(EmbeddedResourceLoader.LoadText(EpicCodeRes));
	}
	async Task InjectEpicBlueprint() {
		await viewerWeb!.ExecuteScriptAsync(EmbeddedResourceLoader.LoadText(EpicBlueprintRes));
	}
	async Task InjectEpicComparisonSlider() {
		await viewerWeb!.ExecuteScriptAsync(EmbeddedResourceLoader.LoadText(EpicComparisonSliderRes));
	}
	async Task InjectEpicSliderSequence() {
		await viewerWeb!.ExecuteScriptAsync(EmbeddedResourceLoader.LoadText(EpicSliderSequenceRes));
	}

	/// <summary>
	/// Toggles sidebar state in both persisted state and live WebView layout.
	/// </summary>
	async Task ToggleSidebar() {
		State.Current.Collapsed = !State.Current.Collapsed;
		State.Save(State.Current);
		ResizeWebView();
		await UpdateToggleSidebar();
	}
	async Task UpdateToggleSidebar() {
		await viewerWeb!.ExecuteScriptAsync($@"
			document.querySelector('.sidebarHandle a').innerHTML = '" + (State.Current.Collapsed ? '⮞' : '⮜') + @"';
		");
		await navWeb!.ExecuteScriptAsync($"setCollapsed({State.Current.Collapsed.ToString().ToLower()})");
	}
	async Task UpdateMaximizeState() {
		bool isMax = Native.IsZoomed(handle);
		PersistMaximizedState(isMax);
		await titleWeb!.ExecuteScriptAsync($"setMaximized({isMax.ToString().ToLower()});");
	}

	/// <summary>
	/// Saves window maximized state without treating temporary minimize events as restored windows.
	/// </summary>
	void PersistMaximizedState(IntPtr sizeMessageWParam) {
		if (sizeMessageWParam.ToInt32() == Native.SIZE_MINIMIZED) return;
		PersistMaximizedState();
	}

	void PersistMaximizedState() {
		if (!canPersistWindowState || handle == IntPtr.Zero) return;
		if (Native.IsIconic(handle)) return;
		PersistMaximizedState(Native.IsZoomed(handle));
	}

	void PersistMaximizedState(bool isMaximized) {
		if (!canPersistWindowState || State.Current.Maximized == isMaximized) return;
		State.Current.Maximized = isMaximized;
		State.Save(State.Current);
	}
	async void ViewerWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e) {
		// Document scripts only send title updates; navigation commands are handled by the title/sidebar views.
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

	/// <summary>
	/// Handles commands from the custom title bar: links, drag, minimize, maximize, and close.
	/// </summary>
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

	/// <summary>
	/// Handles sidebar commands for document opening, history navigation, and resize persistence.
	/// </summary>
	async void NavWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e) {
		try {
			using var msg = JsonDocument.Parse(e.WebMessageAsJson);
			string type = msg.RootElement.GetProperty("type").GetString() ?? "";
			switch (type) {
				case "setLastFile":
					string lastPath = msg.RootElement.GetProperty("path").GetString() ?? "";
					if (TryResolveDocumentPath(lastPath, out string lastFile, out _)) {
						State.Current.LastFile = lastFile;
					}
					break;
				case "open":
					string path = msg.RootElement.GetProperty("path").GetString() ?? "";
					if (TryResolveDocumentPath(path, out string file, out string fragment))
						await OpenDocument(file, fragment);
					else
						await ShowError("File not found:\\n" + path);
					break;
				case "back":
					await NavigateHistory(-1);
					break;
				case "forward":
					await NavigateHistory(1);
					break;
				case "resizeSidebar":
					int requestedWidth = msg.RootElement.GetProperty("width").GetInt32();
					State.Current.SidebarWidth = Math.Clamp(requestedWidth, MinSidebarWidth, MaxSidebarWidth);
					State.Current.Collapsed = false;
					State.Save(State.Current);
					ResizeWebView();
					break;
			}
		} catch (Exception ex) {
			await ShowError(ex.Message);
		}
	}

	/// <summary>
	/// Intercepts viewer navigations so archived links resolve to local files instead of the network.
	/// </summary>
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

	/// <summary>
	/// Handles media navigations started from frames inside the archived document.
	/// </summary>
	async void ViewerFrameNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e) {
		if (IsDocumentResourceUrl(e.Uri)) return;
		if (!IsLocalMediaUrl(e.Uri)) return;
		e.Cancel = true;
		await OpenLocalMedia(e.Uri);
	}

	/// <summary>
	/// Redirects new-window requests for local media back into the main viewer.
	/// </summary>
	async void ViewerNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e) {
		if (IsDocumentResourceUrl(e.Uri)) return;
		if (!IsLocalMediaUrl(e.Uri)) return;
		e.Handled = true;
		await OpenLocalMedia(e.Uri);
	}

	/// <summary>
	/// Injects viewer helpers after navigation and applies pending fragment scrolling.
	/// </summary>
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
			// These scripts progressively enhance static documentation pages after WebView navigation.
			await InjectGetTitle();
			await InjectToggleButton();
			await InjectEpicSwitch();
			await InjectEpicCode();
			await InjectEpicBlueprint();
			await InjectEpicComparisonSlider();
			await InjectEpicSliderSequence();
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
			// Navigation completion is best-effort; failed script injection should not close the viewer.
		}
	}

	/// <summary>
	/// Resolves a WebView2 navigation event into local document/media behavior.
	/// </summary>
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

	/// <summary>
	/// Resolves a title-bar link click into the same navigation pipeline used by the viewer.
	/// </summary>
	async Task OpenLink(string url) {
		if (TryResolveDocumentPath(url, out string localFile, out string localFragment)) {
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

	/// <summary>
	/// Scrolls inside the current document when only the fragment changed.
	/// </summary>
	async Task<bool> TryNavigateCurrentDocumentFragment(string file, string fragment, bool addHistory = true) {
		file = ToWorkspacePath(file);
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

	/// <summary>
	/// Opens local media through the mapped media host after validating the path.
	/// </summary>
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
		// Do not allow crafted media.local URLs to escape the workspace.
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
		fragment = uri.Fragment.Length > 0 ? uri.Fragment[1..] : string.Empty;
		return TryResolveDocumentFullPath(fullPath, out file);
	}
	bool TryResolveDocumentPath(string pathOrUrl, out string file, out string fragment) {
		file = string.Empty;
		fragment = string.Empty;
		if (string.IsNullOrWhiteSpace(pathOrUrl)) return false;

		if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uri) && uri.IsFile) {
			fragment = uri.Fragment.Length > 0 ? uri.Fragment[1..] : string.Empty;
			return TryResolveDocumentFullPath(Path.GetFullPath(uri.LocalPath), out file);
		}
		if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out _) && !Path.IsPathRooted(pathOrUrl)) return false;

		string path = pathOrUrl;
		int hashIndex = path.IndexOf('#');
		if (hashIndex >= 0) {
			fragment = path[(hashIndex + 1)..];
			path = path[..hashIndex];
		}

		if (!TryResolveWorkspacePath(Uri.UnescapeDataString(path), out string fullPath)) return false;
		return TryResolveDocumentFullPath(fullPath, out file);
	}
	bool TryResolveDocumentFullPath(string fullPath, out string file) {
		file = string.Empty;
		if (!IsDocumentFile(fullPath)) return false;
		// File navigations are limited to workspace documents.
		if (!IsPathInsideRoot(fullPath, workspaceRoot)) return false;
		if (!File.Exists(fullPath)) return false;

		file = ToWorkspacePath(fullPath);
		return true;
	}
	string ToWorkspacePath(string path) {
		if (string.IsNullOrWhiteSpace(path)) return string.Empty;
		if (!TryResolveWorkspacePath(path, out string fullPath)) return NormalizeRelativePath(path);

		return Path.GetRelativePath(workspaceRoot, fullPath)
			.Replace(Path.DirectorySeparatorChar, '/')
			.Replace(Path.AltDirectorySeparatorChar, '/');
	}
	bool TryResolveWorkspacePath(string workspacePath, out string fullPath) {
		fullPath = string.Empty;
		if (string.IsNullOrWhiteSpace(workspacePath)) return false;

		try {
			string normalized = workspacePath.Replace('/', Path.DirectorySeparatorChar);
			string candidate = Path.IsPathRooted(normalized)
				? Path.GetFullPath(normalized)
				: Path.GetFullPath(Path.Combine(workspaceRoot, normalized));

			if (!IsPathInsideRoot(candidate, workspaceRoot)) return false;

			fullPath = candidate;
			return true;
		} catch {
			return false;
		}
	}
	static string NormalizeRelativePath(string path) {
		return path.TrimStart('/', '\\')
			.Replace('\\', '/');
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
		if (!TryResolveDocumentPath(file, out string relativeFile, out string resolvedFragment)) {
			await ShowError("File not found:\\n" + file);
			return;
		}

		file = relativeFile;
		if (string.IsNullOrEmpty(fragment)) fragment = resolvedFragment;

		if (IsHtmlFile(file)) {
			await OpenHtml(file, fragment, addHistory);
			return;
		}

		await OpenMhtml(file, fragment, addHistory);
	}
	Task OpenHtml(string file, string fragment, bool addHistory = true, bool navigate = true) {
		if (!TryResolveWorkspacePath(file, out string fullPath)) return Task.CompletedTask;
		file = ToWorkspacePath(fullPath);
		currentFilePath = file;
		pendingFragment = fragment;
		currentDocument = null;
		State.Current.LastFile = file;
		State.Save(State.Current);
		if (addHistory) AddHistory(NavigationEntry.Document(file, fragment));
		if (navigate) {
			pendingLocalHtmlNavigationPath = file;
			viewerWeb!.Navigate(new Uri(fullPath).AbsoluteUri);
		}
		return Task.CompletedTask;
	}
	async Task OpenMhtml(string file, string fragment, bool addHistory = true) {
		file = ToWorkspacePath(file);
		currentFilePath = file;
		pendingFragment = fragment;
		currentDocument = documentCache.GetOrAdd(file, LoadDocument);
		State.Current.LastFile = file;
		State.Save(State.Current);
		if (addHistory) AddHistory(NavigationEntry.Document(file, fragment));
		viewerWeb!.Navigate(BuildDocumentRootUrl());
	}
	string BuildDocumentRootUrl() {
		// Bump the URL on each MHTML navigation so WebView2 does not reuse a stale document response.
		int version = ++documentNavigationVersion;
		return $"https://{DocumentResourceHost}/document/{version}/index.html";
	}
	void AddHistory(NavigationEntry entry) {
		// Avoid duplicate adjacent entries and drop forward history after branching.
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

	/// <summary>
	/// Sends errors to the sidebar when available, otherwise falls back to a native dialog.
	/// </summary>
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
			contentLocationMap[kv.Key] = ToWorkspacePath(kv.Value);
		}

		firstFilePath = ToWorkspacePath(cache.FirstFile);
	}
	void NormalizePersistedState() {
		string? lastFile = State.Current.LastFile;
		if (string.IsNullOrWhiteSpace(lastFile)) return;

		if (TryResolveDocumentPath(lastFile, out string relativeFile, out _)) {
			if (!relativeFile.Equals(lastFile, StringComparison.OrdinalIgnoreCase)) {
				State.Current.LastFile = relativeFile;
				State.Save(State.Current);
			}
			return;
		}

		State.Current.LastFile = null;
		State.Save(State.Current);
	}
	void NormalizeTreePaths(List<Node> nodes) {
		foreach (Node node in nodes) {
			if (!string.IsNullOrWhiteSpace(node.Path)) {
				node.Path = ToWorkspacePath(node.Path);
			}

			if (node.Children != null) {
				NormalizeTreePaths(node.Children);
			}
		}
	}

	/// <summary>
	/// Maps an original captured URL to the local MHTML file and fragment.
	/// </summary>
	bool TryResolveMhtml(string url, out string file, out string fragment) {
		file = string.Empty;
		fragment = string.Empty;
		// Preserve the fragment so the local page can scroll to the same section.
		int hashIndex = url.IndexOf('#');
		if (hashIndex >= 0) fragment = url[(hashIndex + 1)..];

		string baseUrl = ContentLocationIndexBuilder.NormalizeUrl(url);

		if (contentLocationMap.TryGetValue(baseUrl, out string? mappedFile) &&
			TryResolveDocumentPath(mappedFile, out file, out _)) {
			return true;
		}
		else return false;
	}

	/// <summary>
	/// Serves the current MHTML root document and embedded resources to WebView2.
	/// </summary>
	void ViewerWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e) {
		if (viewerWeb == null || currentDocument == null) return;
		if (!TryResolveDocumentResource(e.Request.Uri, out var resource)) return;

		string contentType = resource.ContentType;
		string headers = $"Content-Type: {contentType}\r\nAccess-Control-Allow-Origin: *";
		Stream stream;
		if (resource.FilePath != null) {
			if (!TryResolveWorkspacePath(resource.FilePath, out string filePath) || !File.Exists(filePath)) return;
			stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
		} else {
			stream = new MemoryStream(resource.Bytes!, writable: false);
		}

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
			// The synthetic root URL always returns the parsed HTML for the current MHTML file.
			resource = new ResourceEntry("text/html; charset=utf-8", document.HtmlBytes, null);
			return true;
		}

		string cidPrefix = $"https://{DocumentResourceHost}/cid/";
		if (requestUrl.StartsWith(cidPrefix, StringComparison.OrdinalIgnoreCase)) {
			// Rewritten cid: references use this branch.
			string cid = Uri.UnescapeDataString(requestUrl[cidPrefix.Length..]);
			return document.ResourcesByCid.TryGetValue(cid, out resource!);
		}

		return document.ResourcesByUrl.TryGetValue(requestUrl, out resource!);
	}
	LoadedDocument LoadDocument(string file) {
		if (!TryResolveWorkspacePath(file, out string fullPath)) {
			throw new InvalidOperationException($"Document path is outside the workspace: {file}");
		}

		return MhtmlDocumentLoader.Load(fullPath, offlineAssets, DocumentResourceHost);
	}

	/// <summary>
	/// Releases WebView2 controllers and unmanaged Win32 resources owned by this shell.
	/// </summary>
	public void Dispose() {
		viewerController?.Close();
		navController?.Close();
		titleController?.Close();
		if (backgroundBrush != IntPtr.Zero) {
			Native.DeleteObject(backgroundBrush);
			backgroundBrush = IntPtr.Zero;
		}
		if (largeIcon != IntPtr.Zero) {
			Native.DestroyIcon(largeIcon);
			largeIcon = IntPtr.Zero;
		}
		if (smallIcon != IntPtr.Zero) {
			Native.DestroyIcon(smallIcon);
			smallIcon = IntPtr.Zero;
		}
	}
}
