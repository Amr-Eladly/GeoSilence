namespace GeoSilence.Controls;

// Positions and velocity are in device-independent units. Keep only recent
// movement so a pause or reversal at release does not reuse an earlier flick.
internal sealed class BottomSheetGesture
{
    private const double VelocityWindowSeconds = 0.12;
    private const double FlickVelocity = 450;
    private const double MinimumFlickDistance = 12;
    private readonly Queue<(double Time, double Position)> _samples = new(32);
    private double _startOffset;

    public void Begin(double offset, double time)
    {
        _startOffset = offset;
        _samples.Clear();
        _samples.Enqueue((time, offset));
    }

    public double Move(double totalY, double currentOffset, bool relativeToMovingView, double time)
    {
        // Android reports displacement in the moving header's local coordinates.
        // Adding its current offset restores a stationary coordinate system.
        var position = (relativeToMovingView ? currentOffset : _startOffset) + totalY;
        _samples.Enqueue((time, position));
        while (_samples.Count > 2 && (_samples.Peek().Time < time - VelocityWindowSeconds || _samples.Count > 32))
            _samples.Dequeue();
        return position;
    }

    public bool ShouldExpand(double offset, double collapsedOffset, double time, bool canceled)
    {
        if (!canceled && _samples.Count >= 2)
        {
            var first = _samples.Peek();
            var last = _samples.Last();
            var elapsed = last.Time - first.Time;
            var distance = last.Position - first.Position;
            if (time - last.Time <= VelocityWindowSeconds && elapsed >= 0.016 &&
                Math.Abs(distance) >= MinimumFlickDistance && Math.Abs(distance / elapsed) >= FlickVelocity)
                return distance < 0;
        }

        return offset < collapsedOffset / 2;
    }
}
