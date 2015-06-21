using System;
using System.Collections.Generic;

namespace PackOPdater
{
	public static class Extensions
	{
		public static int IndexOf<T>(this IEnumerable<T> enumerable, Func<T, bool> predicate)
		{
			var enumerator = enumerable.GetEnumerator();
			for (int i = 0;; i++) {
				if (!enumerator.MoveNext()) return -1;
				if (predicate(enumerator.Current)) return i;
			}
		}
		public static int IndexOf<T>(this IEnumerable<T> enumerable, T element)
		{
			return IndexOf(enumerable, e => EqualityComparer<T>.Default.Equals(e, element));
		}
	}
}

