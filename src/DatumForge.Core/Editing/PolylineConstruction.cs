using DatumForge.Core.Models;

namespace DatumForge.Core.Editing;

public sealed class PolylineConstruction
{
    private readonly List<CadPoint> _vertices = [];

    public IReadOnlyList<CadPoint> Vertices => _vertices;
    public bool IsActive => _vertices.Count > 0;
    public bool CanComplete => _vertices.Count > 1;

    public bool TryAddVertex(CadPoint point)
    {
        if (_vertices.Count > 0 && _vertices[^1] == point) return false;
        _vertices.Add(point);
        return true;
    }

    public bool TryComplete(out List<CadPoint> vertices)
    {
        if (!CanComplete)
        {
            vertices = [];
            return false;
        }

        vertices = [.. _vertices];
        _vertices.Clear();
        return true;
    }

    public void Reset() => _vertices.Clear();
}
