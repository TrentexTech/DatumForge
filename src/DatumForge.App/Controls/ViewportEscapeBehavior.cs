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
