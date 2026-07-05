using System.Text.RegularExpressions;

/// <summary>
/// Sorts strings in human order, so "2" comes before "10".
/// </summary>
internal sealed class NaturalComparer : IComparer<string> {
	public int Compare(string? a, string? b) {
		string[] left = Regex.Split(a ?? "", @"(\d+)");
		string[] right = Regex.Split(b ?? "", @"(\d+)");
		int length = Math.Max(left.Length, right.Length);

		for (int i = 0; i < length; i++) {
			if (i >= left.Length) return -1;
			if (i >= right.Length) return 1;

			if (int.TryParse(left[i], out int leftNumber) &&
				int.TryParse(right[i], out int rightNumber)) {
				if (leftNumber != rightNumber) return leftNumber.CompareTo(rightNumber);
				continue;
			}

			int textCompare = string.Compare(left[i], right[i], StringComparison.OrdinalIgnoreCase);
			if (textCompare != 0) return textCompare;
		}

		return 0;
	}
}
