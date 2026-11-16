using FM27Nexus.Core.Paths;

namespace FM27Nexus.Core.Tests;

public class DocumentsPathResolverTests
{
    [Fact]
    public void GetDocumentsFolder_ReturnsExistingPath()
    {
        var resolver = new DocumentsPathResolver();
        var docs = resolver.GetDocumentsFolder();
        Assert.False(string.IsNullOrWhiteSpace(docs));
        Assert.True(Directory.Exists(docs));
    }

    [Fact]
    public void Resolve_UsesManualOverride()
    {
        var temp = Path.Combine(Path.GetTempPath(), "FM27Docs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var layout = new DocumentsPathResolver().Resolve(temp);
            Assert.Equal(Path.GetFullPath(temp), layout.Root);
            Assert.Equal(Path.Combine(layout.Root, "games"), layout.Games);
            Assert.Equal(Path.Combine(layout.Root, "editor data"), layout.EditorData);
            Assert.Equal(Path.Combine(layout.Root, "skin"), layout.Skin);
            Assert.Equal(Path.Combine(layout.Root, "graphics"), layout.Graphics);
            Assert.Equal(Path.Combine(layout.Root, "tactics"), layout.Tactics);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
