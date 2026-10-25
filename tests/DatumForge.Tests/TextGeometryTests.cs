using DatumForge.Core.Editing;
using DatumForge.Core.Models;

namespace DatumForge.Tests;

public sealed class TextGeometryTests
{
    [Fact]
    public void BoundsFollowTextHeightContentAndRotation()
    {
        var text = new DrawingEntity
        {
            Kind = EntityKind.Text,
            Points = [new(10, 20)],
            Text = "AB",
            TextHeight = 10,
            TextRotationDegrees = 90
        };

        var corners = TextGeometry.Corners(text);

        AssertPoint(new(10, 20), corners[0]);
        AssertPoint(new(10, 32), corners[1]);
        AssertPoint(new(0, 32), corners[2]);
        AssertPoint(new(0, 20), corners[3]);
        Assert.True(TextGeometry.Contains(text, new CadPoint(5, 26), 0));
        Assert.False(TextGeometry.Contains(text, new CadPoint(15, 26), 0));
    }

    [Fact]
    public void LegacyTextReceivesSafeDefaultsWhenNormalized()
    {
        var text = new DrawingEntity
        {
            Kind = EntityKind.Text,
            TextHeight = 0,
            TextRotationDegrees = -30
        };

        TextGeometry.Normalize(text);

        Assert.Equal(TextGeometry.DefaultHeight, text.TextHeight);
        Assert.Equal(330, text.TextRotationDegrees);
    }

    [Fact]
    public void InvalidRotationNormalizesToZero()
    {
        Assert.Equal(0, TextGeometry.NormalizeDegrees(double.NaN));
        Assert.Equal(0, TextGeometry.NormalizeDegrees(double.PositiveInfinity));
    }

    [Theory]
    [InlineData(30, 0, 0, 10, 0, 330)]
    [InlineData(30, 0, 0, 0, 10, 150)]
    public void MirroringReflectsTextDirection(
        double rotation,
        double axisStartX,
        double axisStartY,
        double axisEndX,
        double axisEndY,
        double expected)
    {
        Assert.Equal(expected, TextGeometry.MirroredRotation(
            rotation,
            new CadPoint(axisStartX, axisStartY),
            new CadPoint(axisEndX, axisEndY)), 8);
    }

    private static void AssertPoint(CadPoint expected, CadPoint actual)
    {
        Assert.Equal(expected.X, actual.X, 8);
        Assert.Equal(expected.Y, actual.Y, 8);
    }
}
