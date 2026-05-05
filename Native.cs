using System.Runtime.InteropServices;

internal static partial class Native {
	public const int WM_PAINT = 0x000F;
	public const int DWMWA_NCRENDERING_POLICY = 2;
	public const int DWMNCRP_DISABLED = 1;
	public const int WM_APP = 0x8000;
	public const int WM_CUSTOM = WM_APP + 1; // = 0x8001
	public const int WM_ERASEBKGND = 0x0014;
	public const int WM_NCPAINT = 0x0085;
	public const int WM_ACTIVATE = 0x0006;
	public const int WM_SETFOCUS = 0x0007;
	public const int WM_KILLFOCUS = 0x0008;
	public const int HTCLIENT = 1;
	public const uint SWP_NOSIZE = 0x0001;
	public const uint SWP_NOMOVE = 0x0002;
	public const uint SWP_FRAMECHANGED = 0x0020;
	public const uint SWP_NOZORDER = 0x0004;
	public const uint SWP_NOACTIVATE = 0x0010;
	public const int SM_CXSCREEN = 0;
	public const int SM_CYSCREEN = 1;
	public const int GWL_STYLE = -16;
	public const int WM_NCCALCSIZE = 0x0083;
	public const int WM_SIZE = 0x0005;
	public const int WM_DESTROY = 0x0002;
	public const int WS_CAPTION = 0x00C00000;
	public const int WS_THICKFRAME = 0x00040000;
	public const int WS_MAXIMIZEBOX = 0x00010000;
	public const int WS_MINIMIZEBOX = 0x00020000;
	public const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
	public const int WS_POPUP = unchecked((int)0x80000000);
	public const int WS_VISIBLE = 0x10000000;
	public const int WM_LBUTTONDOWN = 0x0201;
	public const int WM_LBUTTONUP   = 0x0202;
	public const int WM_MOUSEMOVE   = 0x0200;
	public const int WM_RBUTTONDOWN = 0x0204;
	public const int WM_RBUTTONUP   = 0x0205;
	public const int WM_NCLBUTTONDOWN = 0x00A1;
	public const int WM_NCLBUTTONDBLCLK = 0x00A3;
	public const int WM_MOUSEWHEEL = 0x020A;
	public const int HTCAPTION = 0x2;
	public const int SW_MAXIMIZE = 3;
	public const int SW_RESTORE = 9;
	public const int SW_MINIMIZE = 6;
	public const int WM_NCHITTEST = 0x0084;
	public const int HTLEFT = 10;
	public const int HTRIGHT = 11;
	public const int HTTOP = 12;
	public const int HTTOPLEFT = 13;
	public const int HTTOPRIGHT = 14;
	public const int HTBOTTOM = 15;
	public const int HTBOTTOMLEFT = 16;
	public const int HTBOTTOMRIGHT = 17;
	public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

	public static void ShowMessage(IntPtr owner, string text, string caption) {
		MessageBox(owner, text, caption, 0x00000010);
	}
	
	public static void RunMessageLoop() {
		while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0) {
			TranslateMessage(ref msg);
			DispatchMessage(ref msg);
		}
	}
	public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	public static extern int GetSystemMetrics(int nIndex);

    // ===== PAINT FUNCTIONS =====
    [DllImport("user32.dll")]
    public static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    public static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT lpPaint);

    // ===== GDI FUNCTIONS =====
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateSolidBrush(int color);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    public static extern bool FillRect(IntPtr hdc, ref RECT lprc, IntPtr hbr);

	[DllImport("user32.dll")]
	public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

	[DllImport("dwmapi.dll")]
	public static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

	[DllImport("user32.dll")]
	public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

	[DllImport("user32.dll")]
	public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

	[DllImport("user32.dll")]
	public static extern bool IsZoomed(IntPtr hWnd);

	[DllImport("user32.dll")]
	public static extern bool ReleaseCapture();

	[DllImport("user32.dll")]
	public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

	[DllImport("user32.dll")]
	public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

	[DllImport("shell32.dll")]
	public static extern uint ExtractIconEx(
        string lpszFile, int nIconIndex,
        out IntPtr phiconLarge, out IntPtr phiconSmall, 
        uint nIcons);

	[DllImport("kernel32.dll")]
	public static extern IntPtr GetModuleHandle(string? lpModuleName);

	[DllImport("user32.dll")]
	public static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

	[DllImport("user32.dll")]
	public static extern IntPtr CreateWindowEx(
		int exStyle, string className, string windowName, int style,
		int x, int y, int width, int height,
		IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

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
	public static extern int GetClientRect(IntPtr hWnd, out RECT lpRect);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern int PostMessage(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy,
        uint uFlags);

	[DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
	static extern int MessageBox(IntPtr hWnd, string lpText, string lpCaption, uint uType);

	[DllImport("user32.dll")]
	public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

	public struct WNDCLASS {
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

	public struct MSG {
		public IntPtr hwnd;
		public uint message;
		public IntPtr wParam;
		public IntPtr lParam;
		public uint time;
		public int pt_x;
		public int pt_y;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct NCCALCSIZE_PARAMS {
		public RECT rgrc0;
		public RECT rgrc1;
		public RECT rgrc2;
		public IntPtr lppos;
	}

    // ===== WM PAINT STRUCT =====
    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

	[StructLayout(LayoutKind.Sequential)]
	public struct RECT {
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}
}
