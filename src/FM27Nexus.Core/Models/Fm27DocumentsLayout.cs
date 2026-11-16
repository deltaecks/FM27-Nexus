namespace FM27Nexus.Core.Models;

public sealed class Fm27DocumentsLayout
{
    public required string Root { get; init; }
    public string Games => Path.Combine(Root, "games");
    public string EditorData => Path.Combine(Root, "editor data");
    public string Skin => Path.Combine(Root, "skin");
    public string Graphics => Path.Combine(Root, "graphics");
    public string Tactics => Path.Combine(Root, "tactics");
    public bool RootExists => Directory.Exists(Root);
}
