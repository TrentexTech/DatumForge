using DatumForge.App.Controls;

namespace DatumForge.Tests;

public sealed class ViewportEscapeBehaviorTests
{
    [Theory]
    [InlineData(false, false, "Line", ViewportEscapeAction.SelectTool)]
    [InlineData(false, false, "Select", ViewportEscapeAction.None)]
    [InlineData(true, true, "Line", ViewportEscapeAction.CancelInteraction)]
    [InlineData(true, false, "Line", ViewportEscapeAction.SelectTool)]
    [InlineData(true, false, "Select", ViewportEscapeAction.ClearSelection)]
    public void EscapeActionReflectsDrawingAndInteractionState(
        bool hasDrawing,
        bool hasActiveOperation,
        string activeTool,
        ViewportEscapeAction expected)
    {
        Assert.Equal(expected, ViewportEscapeBehavior.Decide(hasDrawing, hasActiveOperation, activeTool));
    }
}
