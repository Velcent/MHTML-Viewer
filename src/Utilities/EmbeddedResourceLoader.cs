using System.Reflection;

internal static class EmbeddedResourceLoader {
	static readonly Assembly Assembly = typeof(EmbeddedResourceLoader).Assembly;

	public static byte[] LoadBytes(string resourceName) {
		using Stream stream = Open(resourceName);
		using var buffer = new MemoryStream();
		stream.CopyTo(buffer);
		return buffer.ToArray();
	}

	public static string LoadText(string resourceName) {
		using Stream stream = Open(resourceName);
		using var reader = new StreamReader(stream);
		return reader.ReadToEnd();
	}

	static Stream Open(string resourceName) {
		string fullName = Assembly
			.GetManifestResourceNames()
			.First(name => name.EndsWith(resourceName, StringComparison.Ordinal));

		return Assembly.GetManifestResourceStream(fullName)
			?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
	}
}
