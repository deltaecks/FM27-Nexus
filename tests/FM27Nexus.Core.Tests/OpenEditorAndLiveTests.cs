using FM27Nexus.Core.Memory.Live;
using FM27Nexus.Core.Memory.OpenEditor;

namespace FM27Nexus.Core.Tests;

public class OpenEditorAndLiveTests
{
    [Fact]
    public void Demangle_MsvcNestedClass()
    {
        Assert.Equal("db::ACTUAL_PLAYER", MsvcRttiScanner.Demangle(".?AVACTUAL_PLAYER@db@@"));
    }

    [Fact]
    public void OpenEditorDefinition_LoadsRttiPlayerCollection()
    {
        var path = Path.Combine(Path.GetTempPath(), "fm27nexus-test-def-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """
        {
          "game": "Football Manager 2027",
          "version": "test",
          "module": "game_plugin.dll",
          "moduleVersions": ["27.0.1+2494687"],
          "collections": {
            "Player": {
              "layout": "rttiInstances",
              "maxCount": 100,
              "classes": [
                { "class": "db::ACTUAL_PLAYER", "vtableOffset": "0x288" }
              ]
            }
          },
          "structs": {
            "Player": {
              "Id": { "offset": "0xC", "type": "i32" },
              "CurrentAbility": { "offset": "-0x24", "type": "i16", "scale": 1 },
              "Attr.Pace": { "offset": "-0x103", "type": "u8", "scale": 5 }
            }
          }
        }
        """);

        try
        {
            var def = OpenEditorDefinition.Load(path);
            Assert.Equal("game_plugin.dll", def.Module);
            Assert.Equal("rttiInstances", def.Collections["Player"].Layout);
            Assert.Equal(0x288, def.Collections["Player"].Classes[0].VtableOffset);
            Assert.Equal(-0x24, def.Structs["Player"]["CurrentAbility"].Offset);
            Assert.Equal(5, def.Structs["Player"]["Attr.Pace"].Scale);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("CA", "CurrentAbility")]
    [InlineData("PA", "PotentialAbility")]
    [InlineData("Pace", "Attr.Pace")]
    [InlineData("Attr.Finishing", "Attr.Finishing")]
    public void ResolveFieldAlias_MapsCommonEditorNames(string input, string expected)
    {
        Assert.Equal(expected, LiveGameSession.ResolveFieldAlias(input));
    }
}
