using System;
using System.Collections.Generic;
using System.Reflection;

namespace SlimDX.Compat;

/// <summary>
/// Reproduces pre-.NET-4.5 List.ForEach's live index walk, not snapshot semantics.
/// Removing the current item advances past the item shifted into its place;
/// appends are visited during the same walk. These are intentional legacy effects.
/// The modern List enumerator cannot be used because it rejects mutation.
/// </summary>
public static class ListCompat
{
    public static IEnumerable<T> LegacyItems<T>(IList<T> list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return Walk(list);
    }

    private static IEnumerable<T> Walk<T>(IList<T> list)
    {
        if (list is List<T> concrete)
        {
            for (int index=0; index<concrete.Count; index++)
                yield return ListStorage<T>.ReadItems(concrete)[index];
            yield break;
        }
        for (int index=0; index<list.Count; index++) yield return list[index];
    }

    private static class ListStorage<T>
    {
        private static readonly FieldInfo ItemsField = FindItemsField();

        private static FieldInfo FindItemsField()
        {
            var field = typeof(List<T>).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic);
            return field?.FieldType == typeof(T[]) ? field : null;
        }

        public static T[] ReadItems(List<T> list)
        {
            if (ItemsField == null)
                throw new PlatformNotSupportedException("Legacy list iteration requires List<T>._items with type T[].");
            // Re-read each turn: Clear may expose nulls, while appends may replace the array.
            return (T[])ItemsField.GetValue(list);
        }
    }

    /// <summary>Explicit snapshot for callers that really require frozen membership.</summary>
    public static T[] Snapshot<T>(List<T> list) => list.ToArray();
    public static T[] Snapshot<T>(IList<T> list) => System.Linq.Enumerable.ToArray(list);

    public static void ForEach<T>(List<T> list, Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(action);
        for (int index=0; index<list.Count; index++) action(ListStorage<T>.ReadItems(list)[index]);
    }
}
