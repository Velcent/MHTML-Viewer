using System.Collections.Concurrent;

internal static class Program {
	[STAThread]
	private static void Main() {
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
	}
}
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
