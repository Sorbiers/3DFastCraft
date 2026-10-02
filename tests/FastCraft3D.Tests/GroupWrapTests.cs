using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using FastCraft3D.View;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>The selection tools' groups: side by side with a divider, or one under the other with none.</summary>
public class GroupWrapTests
{
    private static void RunSta(Action body)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    /// <summary>Two groups the size of the side panel's: three ribbon buttons and four.</summary>
    private static (GroupWrap Panel, Border Three, Border Four) Laid(double width)
    {
        var three = new Border { Width = 204, Height = 56 };
        var four = new Border { Width = 272, Height = 56 };
        var panel = new GroupWrap { Children = { three, four } };

        panel.Measure(new Size(width, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
        return (panel, three, four);
    }

    private static Point At(UIElement child, GroupWrap panel) => child.TranslatePoint(new Point(0, 0), panel);

    [Fact]
    public void GroupsThatFitGoSideBySideWithADividerBetween()
    {
        RunSta(() =>
        {
            var (panel, three, four) = Laid(600);

            Assert.Equal(0, At(four, panel).Y);
            var divider = Assert.Single(panel.Dividers);
            Assert.True(divider.X > At(three, panel).X + three.Width && divider.X < At(four, panel).X);
            Assert.Equal(56, panel.DesiredSize.Height);
        });
    }

    [Fact]
    public void AGroupThatDoesNotFitGoesUnderneathWithNoDividerOpeningTheLine()
    {
        RunSta(() =>
        {
            var (panel, _, four) = Laid(279);

            Assert.Equal(new Point(0, 56), At(four, panel));
            Assert.Empty(panel.Dividers);
            Assert.Equal(112, panel.DesiredSize.Height);
        });
    }
}
