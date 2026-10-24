namespace DatumForge.Tests;

public class FormatDefaultsTests
{
    [Fact]
    public void NewDocumentsUseCurrentFormatVersion()
    {
        Assert.Equal(DatumForge.Core.Models.DrawingDocument.CurrentFormatVersion, new DatumForge.Core.Models.DrawingDocument().FormatVersion);
        Assert.Equal(DatumForge.Core.Models.ProjectDocument.CurrentFormatVersion, new DatumForge.Core.Models.ProjectDocument().FormatVersion);
    }
}
