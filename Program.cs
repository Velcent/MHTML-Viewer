using System.Collections.Concurrent;

internal static class Program {
	[STAThread]
	private static void Main() {
#if WINDOWS
		using var app = new WebView();
		app.Create();
		SynchronizationContext.SetSynchronizationContext(new SyncContext(app.Handle));
		_ = app.InitializeAsync().ContinueWith(t => {
			if (t.Exception != null) {
				string error = t.Exception.GetBaseException().ToString();
				string tempPath = Path.Combine(Path.GetTempPath(), "MHTMLViewer");
				File.WriteAllText(Path.Combine(tempPath, "startup-error.txt"), error);
				Native.ShowMessage(app.Handle, error, "Startup Error");
			}
		}, TaskScheduler.FromCurrentSynchronizationContext());
		Native.RunMessageLoop();
#else
		Console.Error.WriteLine("MHTMLViewer currently uses the WebView2/Win32 backend, which is only available on Windows.");
		Console.Error.WriteLine("Build succeeded for this platform, but the viewer UI is not implemented here yet.");
#endif
	}
}

#if WINDOWS
internal sealed class SyncContext : SynchronizationContext {
	static readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> Queue = new();
	readonly IntPtr hwnd;

	public SyncContext(IntPtr hwnd) {
		this.hwnd = hwnd;
	}

	public override void Post(SendOrPostCallback d, object? state) {
		Queue.Enqueue((d, state));
		Native.PostMessage(hwnd, 0x8001, UIntPtr.Zero, IntPtr.Zero);
	}

	public static void DispatchQueuedCallbacks() {
		while (Queue.TryDequeue(out var work)) {
			work.Callback(work.State);
		}
	}
}
#endif
