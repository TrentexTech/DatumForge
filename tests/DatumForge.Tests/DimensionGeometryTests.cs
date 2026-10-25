using DatumForge.Core.Editing;
using DatumForge.Core.Models;

namespace DatumForge.Tests;

public sealed class DimensionGeometryTests
{
    [Theory]
    [InlineData(DrawingUnits.Millimeters, "5 mm")]
    [InlineData(DrawingUnits.Centimeters, "5 cm")]
    [InlineData(DrawingUnits.Meters, "5 m")]
    [InlineData(DrawingUnits.Inches, "5 in")]
    [InlineData(DrawingUnits.Feet, "5 ft")]
    public void AlignedDimensionMeasuresDistanceAndFormatsDrawingUnits(DrawingUnits units, string expectedLabel)
    {
        Assert.True(DimensionGeometry.TryCreate(new CadPoint(0, 0), new CadPoint(3, 4), out var dimension));

        Assert.Equal(5, dimension.Length, 8);
        Assert.Equal(new CadPoint(1.5, 2), dimension.Midpoint);
        Assert.Equal(expectedLabel, DimensionGeometry.Label(dimension, units));
    }

    [Fact]
    public void DimensionRequiresTwoDistinctPoints()
    {
        Assert.False(DimensionGeometry.TryCreate(new CadPoint(2, 3), new CadPoint(2, 3), out _));
        Assert.False(DimensionGeometry.TryCreate([new CadPoint(2, 3)], out _));
    }
}
