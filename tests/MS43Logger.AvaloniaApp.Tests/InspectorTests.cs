using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MS43Logger.AvaloniaApp.Controls;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

public class InspectorTests
{
    [AvaloniaFact]
    public void ProjectionKeepsCentreFixed()
    {
        var size = new Size(300, 250);
        foreach (var (yaw, pitch) in new[] { (-0.72, 0.6), (0.4, 1.2), (2.0, 0.3) })
        {
            var (point, _) = SurfaceMath.Project(0.5, 0.5, 0, yaw, pitch, size);
            Assert.Equal(150, point.X, 6);
            Assert.Equal(250 * 0.58, point.Y, 6);
        }
    }
}
