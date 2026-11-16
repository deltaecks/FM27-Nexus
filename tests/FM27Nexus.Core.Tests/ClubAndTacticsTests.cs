using FM27Nexus.Core.Club;
using FM27Nexus.Core.Memory.OpenEditor;
using FM27Nexus.Core.Tactics;

namespace FM27Nexus.Core.Tests;

public class ClubAndTacticsTests
{
    [Theory]
    [InlineData(@"C:\saves\Lewis Carter - Cardiff.fm", "Cardiff")]
    [InlineData(@"C:\saves\Manager - Real Madrid.fm", "Real Madrid")]
    [InlineData(@"C:\saves\NoDash.fm", null)]
    public void TryParseClubFromSavePath(string path, string? expected)
    {
        Assert.Equal(expected, ClubIdentity.TryParseClubFromSavePath(path));
    }

    [Fact]
    public void ResolvePreferredClub_UsesSettingsOverride()
    {
        var club = ClubIdentity.ResolvePreferredClub(
            @"C:\saves\X - Cardiff.fm",
            ["Cardiff", "Swansea"],
            "Swansea");
        Assert.Equal("Swansea", club);
    }

    [Fact]
    public void ResolvePreferredClub_MatchesSaveNameAgainstTable()
    {
        var club = ClubIdentity.ResolvePreferredClub(
            @"C:\saves\Lewis - Cardiff City.fm",
            ["Cardiff City", "Swansea"],
            null);
        Assert.Equal("Cardiff City", club);
    }

    [Fact]
    public void TacticalFactors_ResolveMappedAndUnmapped()
    {
        var path = Path.Combine(Path.GetTempPath(), "fm27nexus-tactics-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """
        {
          "game": "Football Manager 2027",
          "version": "test",
          "module": "game_plugin.dll",
          "moduleVersions": ["27.0.1+2494687"],
          "collections": {
            "Player": {
              "layout": "rttiInstances",
              "classes": [ { "class": "db::ACTUAL_PLAYER", "vtableOffset": "0x288" } ]
            }
          },
          "structs": {
            "Player": {
              "Id": { "offset": "0xC", "type": "i32" },
              "TacticalUnderstanding.BuildUp": { "offset": "0x10", "type": "u8", "scale": 1 },
              "PassingStyle": { "offset": "0x11", "type": "u8", "scale": 1 }
            }
          }
        }
        """);

        try
        {
            var def = OpenEditorDefinition.Load(path);
            var mapped = TacticalUnderstandingFactors.Resolve(def);
            Assert.Equal(10, mapped.Count);
            Assert.Contains(mapped, m => m.Factor.Id == "BuildUp" && m.IsMapped && m.Entity == "Player");
            Assert.Contains(mapped, m => m.Factor.Id == "PassingStyle" && m.IsMapped);
            Assert.Contains(mapped, m => m.Factor.Id == "PressingTrap" && !m.IsMapped);
            Assert.Equal(2, mapped.Count(m => m.IsMapped));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
