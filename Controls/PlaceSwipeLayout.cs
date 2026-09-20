using System.Runtime.CompilerServices;

namespace GeoSilence.Controls;

/// <summary>
/// Keeps the card content inside the visible part of a right-side SwipeView.
/// The inset belongs to the view, so it must be cleared when that view closes
/// or is rebound to a different item by CollectionView virtualization.
/// </summary>
internal static class PlaceSwipeLayout
{
    private sealed class SwipeState
    {
        public bool IsDragging { get; set; }
    }

    private static readonly ConditionalWeakTable<SwipeView, SwipeState> States = new();

    public static void Start(SwipeView swipe) => States.GetOrCreateValue(swipe).IsDragging = true;

    public static void Change(SwipeView swipe, double offset, double actionWidth)
    {
        // Android can deliver a final SwipeChanging callback after SwipeEnded.
        // Ignore it so it cannot put the inset back on a closed card.
        if (!States.GetOrCreateValue(swipe).IsDragging) return;
        SetInset(swipe, Math.Clamp(Math.Abs(offset), 0, actionWidth));
    }

    public static void End(SwipeView swipe, bool isOpen, double actionWidth)
    {
        var state = States.GetOrCreateValue(swipe);
        if (!state.IsDragging) return;
        state.IsDragging = false;
        SetInset(swipe, isOpen ? actionWidth : 0);
    }

    public static void Reset(SwipeView swipe, bool close = false)
    {
        States.GetOrCreateValue(swipe).IsDragging = false;
        SetInset(swipe, 0);
        if (close) swipe.Close();
    }

    private static void SetInset(SwipeView swipe, double inset)
    {
        if (swipe.Content is Border { Content: Grid content })
            content.Margin = new Thickness(inset, 0, 0, 0);
    }
}
