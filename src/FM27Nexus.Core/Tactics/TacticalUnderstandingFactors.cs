using FM27Nexus.Core.Memory.OpenEditor;

namespace FM27Nexus.Core.Tactics;

/// <summary>
/// Name-only catalog of FM27 Tactical Understanding factors.
/// Offsets are never stored here — resolution requires a user-imported OpenEditor definition.
/// </summary>
public static class TacticalUnderstandingFactors
{
    public static IReadOnlyList<TacticalFactorDef> All { get; } =
    [
        new("InPossessionShape", "In Possession Shape", TacticalFactorGroup.Area,
            ["TacticalUnderstanding.InPossessionShape", "Understanding.InPossessionShape", "InPossessionShape"]),
        new("PassingStyle", "Passing Style", TacticalFactorGroup.Area,
            ["TacticalUnderstanding.PassingStyle", "Understanding.PassingStyle", "PassingStyle"]),
        new("BuildUp", "Build Up", TacticalFactorGroup.Area,
            ["TacticalUnderstanding.BuildUp", "Understanding.BuildUp", "BuildUp"]),
        new("ChanceCreation", "Chance Creation", TacticalFactorGroup.Area,
            ["TacticalUnderstanding.ChanceCreation", "Understanding.ChanceCreation", "ChanceCreation"]),
        new("OutOfPossessionShape", "Out Of Possession Shape", TacticalFactorGroup.Area,
            ["TacticalUnderstanding.OutOfPossessionShape", "Understanding.OutOfPossessionShape", "OutOfPossessionShape"]),
        new("LineOfEngagement", "Line of Engagement", TacticalFactorGroup.Area,
            ["TacticalUnderstanding.LineOfEngagement", "Understanding.LineOfEngagement", "LineOfEngagement"]),
        new("PressingIntensity", "Pressing Intensity", TacticalFactorGroup.Area,
            ["TacticalUnderstanding.PressingIntensity", "Understanding.PressingIntensity", "PressingIntensity"]),
        new("PressingTrap", "Pressing Trap", TacticalFactorGroup.Area,
            ["TacticalUnderstanding.PressingTrap", "Understanding.PressingTrap", "PressingTrap"]),
        new("PlayerTacticalAbility", "Player Tactical Ability", TacticalFactorGroup.Comprehension,
            ["TacticalUnderstanding.PlayerTacticalAbility", "PlayerTacticalAbility", "TacticalAbility"]),
        new("TacticalComplexity", "Tactical Complexity", TacticalFactorGroup.Comprehension,
            ["TacticalUnderstanding.TacticalComplexity", "TacticalComplexity", "Complexity"])
    ];

    public static IReadOnlyList<TacticalFactorMapping> Resolve(OpenEditorDefinition definition)
    {
        var structs = definition.Structs;
        var entityOrder = new[] { "Player", "Team", "Club", "Staff" };
        var list = new List<TacticalFactorMapping>();
        foreach (var factor in All)
        {
            string? entity = null;
            string? fieldKey = null;
            OpenEditorField? field = null;
            foreach (var entityName in entityOrder)
            {
                if (!structs.TryGetValue(entityName, out var fields))
                    continue;
                foreach (var alias in factor.Aliases)
                {
                    if (!fields.TryGetValue(alias, out var f) || f.Offset is null)
                        continue;
                    entity = entityName;
                    fieldKey = alias;
                    field = f;
                    break;
                }

                if (field is not null)
                    break;
            }

            list.Add(new TacticalFactorMapping(
                factor,
                entity,
                fieldKey,
                field,
                IsMapped: field is not null));
        }

        return list;
    }

    /// <summary>Display-scale max for a mapped factor (shown value before scale multiply on write).</summary>
    public static long DisplayMax(OpenEditorField field)
    {
        var scale = field.Scale <= 0 ? 1 : field.Scale;
        return field.Type.ToLowerInvariant() switch
        {
            "u8" or "i8" or "bool" => 20, // FM attrs often 1–20 display with scale 5 → stored 100
            "u16" or "i16" => 100,
            "u32" or "i32" or "i64" => 100,
            "f32" or "f64" => 100,
            _ => 100
        } * (scale > 1 && field.Type.ToLowerInvariant() is "u8" or "i8" ? 1 : 1);
    }

    /// <summary>
    /// Preferred display value to write when maximizing: for scaled u8 attrs use 20 (stored 20*scale),
    /// otherwise 100 for percentage-like gauges.
    /// </summary>
    public static long MaximizeDisplayValue(OpenEditorField field)
    {
        var t = field.Type.ToLowerInvariant();
        if (t is "u8" or "i8")
            return field.Scale > 1 ? 20 : 100;
        if (t is "bool")
            return 1;
        return 100;
    }
}

public enum TacticalFactorGroup
{
    Area,
    Comprehension
}

public sealed record TacticalFactorDef(
    string Id,
    string DisplayName,
    TacticalFactorGroup Group,
    IReadOnlyList<string> Aliases);

public sealed record TacticalFactorMapping(
    TacticalFactorDef Factor,
    string? Entity,
    string? FieldKey,
    OpenEditorField? Field,
    bool IsMapped);
