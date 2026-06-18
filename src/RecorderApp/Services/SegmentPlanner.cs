namespace RecorderApp.Services;

public sealed class SegmentPlanner
{
    public (DateTime Start, DateTime End) GetCurrentWindow(DateTime now, (DateTime Start, DateTime End)? activeWindow)
    {
        if (activeWindow.HasValue && now < activeWindow.Value.End)
        {
            return activeWindow.Value;
        }

        var segmentStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Kind);
        var roundedMinute = ((now.Minute / 10) + 1) * 10;
        DateTime segmentEnd;
        if (roundedMinute >= 60)
        {
            segmentEnd = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Kind).AddHours(1);
        }
        else
        {
            segmentEnd = new DateTime(now.Year, now.Month, now.Day, now.Hour, roundedMinute, 0, now.Kind);
        }

        if (now.Second == 0 && now.Minute % 10 == 0)
        {
            segmentEnd = segmentStart.AddMinutes(10);
        }

        return (segmentStart, segmentEnd);
    }
}
