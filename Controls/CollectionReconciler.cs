using System.Collections.ObjectModel;

namespace GeoSilence.Controls;

// Call on the UI thread. Preserve existing rows and avoid Reset notifications.
internal static class CollectionReconciler
{
    public static void Apply<T>(ObservableCollection<T> target, IReadOnlyList<T> desired)
        where T : class
    {
        var retained = new HashSet<T>(desired, ReferenceEqualityComparer.Instance);
        for (var i = target.Count - 1; i >= 0; i--)
            if (!retained.Contains(target[i])) target.RemoveAt(i);

        for (var i = 0; i < desired.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], desired[i])) continue;
            var existingIndex = target.IndexOf(desired[i]);
            if (existingIndex >= 0) target.Move(existingIndex, i);
            else target.Insert(i, desired[i]);
        }
    }
}
