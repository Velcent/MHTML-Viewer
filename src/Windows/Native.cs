using System.Runtime.InteropServices;

/// <summary>
/// Thin P/Invoke surface used by the custom Win32 window host.
/// </summary>
internal static partial class Native {
	// Window messages handled by WebView.WndProc.
	public const int WM_APP = 0x8000;
	public const int WM_CUSTOM = WM_APP + 1;
	public const int WM_ACTIVATE = 0x0006;
	public const int WM_NCCALCSIZE = 0x0083;
	public const int WM_SIZE = 0x0005;
	public const int WM_DESTROY = 0x0002;
	public const int WM_NCHITTEST = 0x0084;
	public const int WM_NCLBUTTONDOWN = 0x00A1;

	// Hit-test return values used to keep resize behavior with a borderless title bar.
	public const int HTCLIENT = 1;
	public const int HTCAPTION = 2;
	public const int HTLEFT = 10;
	public const int HTRIGHT = 11;
	public const int HTTOP = 12;
	public const int HTTOPLEFT = 13;
	public const int HTTOPRIGHT = 14;
	public const int HTBOTTOM = 15;
	public const int HTBOTTOMLEFT = 16;
	public const int HTBOTTOMRIGHT = 17;

	public const int SM_CXSCREEN = 0;
	public const int SM_CYSCREEN = 1;
	public const int GWL_STYLE = -16;

	// Window style flags configured after CreateWindowEx.
	public const int WS_CAPTION = 0x00C00000;
	public const int WS_THICKFRAME = 0x00040000;
	public const int WS_MAXIMIZEBOX = 0x00010000;
	public const int WS_MINIMIZEBOX = 0x00020000;
	public const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
	public const int WS_POPUP = unchecked((int)0x80000000);
	public const int WS_VISIBLE = 0x10000000;

	public const int SW_MAXIMIZE = 3;
	public const int SW_RESTORE = 9;
	public const int SW_MINIMIZE = 6;

	public const uint SWP_NOSIZE = 0x0001;
	public const uint SWP_NOMOVE = 0x0002;
	public const uint SWP_NOZORDER = 0x0004;
	public const uint SWP_FRAMECHANGED = 0x0020;

	/// <summary>Shows a modal error dialog owned by the main window.</summary>
	public static void ShowMessage(IntPtr owner, string text, string caption) {
		MessageBox(owner, text, caption, 0x00000010);
	}

	/// <summary>Runs the native UI loop until WM_QUIT is posted.</summary>
	public static void RunMessageLoop() {
		while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0) {
			TranslateMessage(ref msg);
			DispatchMessage(ref msg);
		}
	}

	public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	// User32/GDI imports are kept in one file so the rest of the app stays managed and readable.
	[DllImport("user32.dll")]
	public static extern int GetSystemMetrics(int nIndex);

	[DllImport("gdi32.dll")]
	public static extern IntPtr CreateSolidBrush(int color);

	[DllImport("gdi32.dll")]
	public static extern bool DeleteObject(IntPtr hObject);

	[DllImport("user32.dll")]
	public static extern bool DestroyIcon(IntPtr hIcon);

	[DllImport("user32.dll")]
	public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

	[DllImport("user32.dll")]
	public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

	[DllImport("user32.dll")]
	public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

	[DllImport("user32.dll")]
	public static extern bool IsZoomed(IntPtr hWnd);

	[DllImport("user32.dll")]
	public static extern bool ReleaseCapture();

	[DllImport("user32.dll")]
	public static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

	[DllImport("user32.dll")]
	public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	public static extern uint ExtractIconEx(
		string file,
		int iconIndex,
		out IntPtr largeIcon,
		out IntPtr smallIcon,
		uint iconCount
	);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	public static extern IntPtr GetModuleHandle(string? moduleName);

	[DllImport("user32.dll", EntryPoint = "RegisterClassW", CharSet = CharSet.Unicode)]
	public static extern ushort RegisterClass(ref WNDCLASS wndClass);

	[DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
	public static extern IntPtr CreateWindowEx(
		int exStyle,
		string className,
		string windowName,
		int style,
		int x,
		int y,
		int width,
		int height,
		IntPtr parent,
		IntPtr menu,
		IntPtr instance,
		IntPtr param
	);

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

	[DllImport("user32.dll", SetLastError = true)]
	public static extern int GetClientRect(IntPtr hWnd, out RECT rect);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern int PostMessage(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern bool SetWindowPos(
		IntPtr hWnd,
		IntPtr hWndInsertAfter,
		int x,
		int y,
		int cx,
		int cy,
		uint flags
	);

	[DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
	static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

	[DllImport("user32.dll")]
	public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

	/// <summary>Native window class registration data passed to RegisterClassW.</summary>
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct WNDCLASS {
		public int style;
		public IntPtr lpfnWndProc;
		public int cbClsExtra;
		public int cbWndExtra;
		public IntPtr hInstance;
		public IntPtr hIcon;
		public IntPtr hCursor;
		public IntPtr hbrBackground;
		public string? lpszMenuName;
		public string lpszClassName;
	}

	/// <summary>Message data consumed by the Win32 message loop.</summary>
	public struct MSG {
		public IntPtr hwnd;
		public uint message;
		public IntPtr wParam;
		public IntPtr lParam;
		public uint time;
		public int pt_x;
		public int pt_y;
	}

	/// <summary>Non-client resize data used when removing the default Windows title bar.</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct NCCALCSIZE_PARAMS {
		public RECT rgrc0;
		public RECT rgrc1;
		public RECT rgrc2;
		public IntPtr lppos;
	}

	/// <summary>Win32 rectangle with inclusive left/top and exclusive right/bottom bounds.</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct RECT {
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}
}
