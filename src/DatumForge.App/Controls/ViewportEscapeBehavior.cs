namespace DatumForge.App.Controls;

public enum ViewportEscapeAction
{
    None,
    CancelInteraction,
    SelectTool,
    ClearSelection
}

public static class ViewportEscapeBehavior
{
    public static ViewportEscapeAction Decide(bool hasDrawing, bool hasActiveOperation, string activeTool)
    {
        if (hasActiveOperation) return ViewportEscapeAction.CancelInteraction;
        if (activeTool != "Select") return ViewportEscapeAction.SelectTool;
        return hasDrawing ? ViewportEscapeAction.ClearSelection : ViewportEscapeAction.None;
    }
}

public static class ViewportSelectionBehavior
{
    public const double MinimumDragPixels = 4;

    public static bool IsDrag(double deltaX, double deltaY) =>
        deltaX * deltaX + deltaY * deltaY >= MinimumDragPixels * MinimumDragPixels;

    public static bool IsCrossing(double startX, double currentX) => currentX < startX;
}
