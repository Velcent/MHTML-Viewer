using System.Collections.Concurrent;

/// <summary>
/// Application entry point. On Windows it starts the custom Win32/WebView2 shell;
/// on other targets it only reports that the UI backend is not available.
/// </summary>
internal static class Program {
	[STAThread]
	private static void Main() {
#if WINDOWS
		// WebView2 and many Win32 APIs require the main thread to remain in an STA message loop.
		using var app = new WebView();
		app.Create();
		SynchronizationContext.SetSynchronizationContext(new SyncContext(app.Handle));
		_ = app.InitializeAsync().ContinueWith(t => {
			if (t.Exception != null) {
				string error = t.Exception.GetBaseException().ToString();
				Directory.CreateDirectory(AppPaths.TempDirectory);
				File.WriteAllText(Path.Combine(AppPaths.TempDirectory, "startup-error.txt"), error);
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
/// <summary>
/// Minimal SynchronizationContext that marshals async continuations back through the Win32 message queue.
/// </summary>
internal sealed class SyncContext : SynchronizationContext {
	static readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> Queue = new();
	readonly IntPtr hwnd;

	public SyncContext(IntPtr hwnd) {
		this.hwnd = hwnd;
	}

	public override void Post(SendOrPostCallback d, object? state) {
		// WM_CUSTOM wakes the native message loop so queued managed callbacks run on the UI thread.
		Queue.Enqueue((d, state));
		Native.PostMessage(hwnd, Native.WM_CUSTOM, UIntPtr.Zero, IntPtr.Zero);
	}

	/// <summary>
	/// Executes all callbacks previously queued by async continuations.
	/// </summary>
	public static void DispatchQueuedCallbacks() {
		while (Queue.TryDequeue(out var work)) {
			work.Callback(work.State);
		}
	}
}
#endif
