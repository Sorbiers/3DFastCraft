using FastCraft3D.Geometry.Engraving;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The pattern settings have to survive the tool being closed and opened again. A building has
/// four walls, and having to tick "Raised" four times is how three of them end up raised and one
/// cut.
/// </summary>
public class EngraveMemoryTests
{
    [Fact]
    public void TheSettingsOutliveTheFaceTheyWereSetOn()
    {
        var state = new EngraveState
        {
            Options = EngraveOptions.Default with
            {
                Kind = PatternKind.Brick, Size = 4f, GrooveWidth = 0.4f, Depth = 0.3f, Raised = true
            }
        };

        state.Clear();   // what leaving the tool does

        Assert.True(state.Options.Raised);
        Assert.Equal(4f, state.Options.Size);
        Assert.Equal(0.3f, state.Options.Depth);
    }
}
