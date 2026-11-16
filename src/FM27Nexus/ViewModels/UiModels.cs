using CommunityToolkit.Mvvm.ComponentModel;

namespace FM27Nexus.ViewModels;

public partial class NavPageOption : ObservableObject
{
    public NavPageOption(string id, string title)
    {
        Id = id;
        Title = title;
    }

    public string Id { get; }
    public string Title { get; }
}

public partial class ClubFieldRow : ObservableObject
{
    public ClubFieldRow(string key, long? value)
    {
        Key = key;
        Value = value;
        EditValue = value?.ToString() ?? "";
    }

    public string Key { get; }

    [ObservableProperty] private long? value;
    [ObservableProperty] private string editValue = "";
}

public partial class TacticalFactorRow : ObservableObject
{
    public TacticalFactorRow(string id, string displayName, string group, bool isMapped, string? entity, string? fieldKey)
    {
        Id = id;
        DisplayName = displayName;
        Group = group;
        IsMapped = isMapped;
        Entity = entity;
        FieldKey = fieldKey;
        StatusText = isMapped ? $"Mapped · {entity}.{fieldKey}" : "Unmapped";
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Group { get; }
    public bool IsMapped { get; }
    public string? Entity { get; }
    public string? FieldKey { get; }
    public string StatusText { get; }
}
