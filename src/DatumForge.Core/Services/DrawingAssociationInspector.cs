using DatumForge.Core.Models;

namespace DatumForge.Core.Services;

public sealed record DrawingOpenAssessment(bool RequiresWarning, string? Message)
{
    public static DrawingOpenAssessment Safe { get; } = new(false, null);
}

public static class DrawingAssociationInspector
{
    public static DrawingOpenAssessment Assess(DrawingDocument drawing, Guid? activeProjectId)
    {
        if (drawing.ProjectId is null || drawing.ProjectId == activeProjectId) return DrawingOpenAssessment.Safe;
        return new DrawingOpenAssessment(true,
            "This drawing belongs to another DatumForge project. Opening it by itself may omit project history and can break relative references, layouts, or linked drawings. The file will remain unchanged until you save.");
    }
}
