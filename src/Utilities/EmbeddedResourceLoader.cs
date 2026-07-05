using System.Reflection;

/// <summary>
/// Loads HTML, JavaScript, and icon resources embedded into the application assembly.
/// </summary>
internal static class EmbeddedResourceLoader {
	static readonly Assembly Assembly = typeof(EmbeddedResourceLoader).Assembly;

	/// <summary>Reads an embedded resource as bytes.</summary>
	public static byte[] LoadBytes(string resourceName) {
		using Stream stream = Open(resourceName);
		using var buffer = new MemoryStream();
		stream.CopyTo(buffer);
		return buffer.ToArray();
	}

	/// <summary>Reads an embedded resource as UTF-8/reader-decoded text.</summary>
	public static string LoadText(string resourceName) {
		using Stream stream = Open(resourceName);
		using var reader = new StreamReader(stream);
		return reader.ReadToEnd();
	}

	static Stream Open(string resourceName) {
		// The project embeds files from nested folders; matching by suffix keeps call sites path-independent.
		string fullName = Assembly
			.GetManifestResourceNames()
			.First(name => name.EndsWith(resourceName, StringComparison.Ordinal));

		return Assembly.GetManifestResourceStream(fullName)
			?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
	}
}
