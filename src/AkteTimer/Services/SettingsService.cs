namespace AkteTimer.Services;

public sealed class SettingsService
{
    public const string HotkeySetting = "hotkey";
    public const string LastMatterSetting = "last_matter";

    private readonly DatabaseService _database;

    public SettingsService(DatabaseService database)
    {
        _database = database;
    }

    public void EnsureDefaults()
    {
        if (_database.GetSetting(HotkeySetting) == null)
        {
            _database.SetSetting(HotkeySetting, "Ctrl+Alt+T");
        }
    }

    public string Hotkey => _database.GetSetting(HotkeySetting) ?? "Ctrl+Alt+T";

    public string? LastMatter => _database.GetSetting(LastMatterSetting);

    public void SetLastMatter(string fileRef)
    {
        _database.SetSetting(LastMatterSetting, fileRef);
    }
}
