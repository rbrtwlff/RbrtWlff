using System.Globalization;
using System.Windows;

namespace AkteTimer.Services;

public sealed class SettingsService
{
    public const string HotkeySetting = "hotkey";
    public const string LastMatterSetting = "last_matter";
    public const string LastHashtagSetting = "last_hashtag";
    public const string GlobalTargetRateSetting = "global_target_rate_eur_per_hour";
    public const string HashtagStopPromptSetting = "hashtag_stop_prompt";
    public const string UseLastHashtagDefaultSetting = "start_default_hashtag_last_day";
    public const string TableVersionSetting = "table_version";
    public const string PopupHotkeyHelpSetting = "popup_hotkey_help";
    public const string DataDirectorySetting = DataDirectoryService.DataDirectorySetting;
    public const string ReportsWindowLeftSetting = "reports_window_left";
    public const string ReportsWindowTopSetting = "reports_window_top";
    public const string ReportsWindowWidthSetting = "reports_window_width";
    public const string ReportsWindowHeightSetting = "reports_window_height";
    public const string ReportsWindowStateSetting = "reports_window_state";

    private const string DefaultHotkey = "Ctrl+Alt+T";
    private const string DefaultHashtag = "#Sonstiges";
    private const string DefaultTableVersion = "V1";

    private readonly DatabaseService _database;

    public SettingsService(DatabaseService database)
    {
        _database = database;
    }

    public void EnsureDefaults()
    {
        if (_database.GetSetting(HotkeySetting) == null)
        {
            _database.SetSetting(HotkeySetting, DefaultHotkey);
        }

        if (_database.GetSetting(LastHashtagSetting) == null)
        {
            _database.SetSetting(LastHashtagSetting, DefaultHashtag);
        }

        if (_database.GetSetting(GlobalTargetRateSetting) == null)
        {
            _database.SetSetting(GlobalTargetRateSetting, "0");
        }

        if (_database.GetSetting(HashtagStopPromptSetting) == null)
        {
            _database.SetSetting(HashtagStopPromptSetting, "1");
        }

        if (_database.GetSetting(UseLastHashtagDefaultSetting) == null)
        {
            _database.SetSetting(UseLastHashtagDefaultSetting, "1");
        }

        if (_database.GetSetting(TableVersionSetting) == null)
        {
            _database.SetSetting(TableVersionSetting, DefaultTableVersion);
        }

        if (_database.GetSetting(PopupHotkeyHelpSetting) == null)
        {
            _database.SetSetting(PopupHotkeyHelpSetting, "0");
        }
    }

    public string Hotkey => _database.GetSetting(HotkeySetting) ?? DefaultHotkey;

    public void SetHotkey(string hotkey)
    {
        _database.SetSetting(HotkeySetting, hotkey);
    }

    public string? LastMatter => _database.GetSetting(LastMatterSetting);

    public void SetLastMatter(string fileRef)
    {
        _database.SetSetting(LastMatterSetting, fileRef);
    }

    public string LastHashtag => _database.GetSetting(LastHashtagSetting) ?? DefaultHashtag;

    public void SetLastHashtag(string hashtag)
    {
        _database.SetSetting(LastHashtagSetting, hashtag);
    }

    public decimal GlobalTargetRateEurPerHour => GetDecimal(GlobalTargetRateSetting, 0m);

    public void SetGlobalTargetRateEurPerHour(decimal value)
    {
        _database.SetSetting(GlobalTargetRateSetting, value.ToString(CultureInfo.InvariantCulture));
    }

    public bool IsHashtagStopPromptEnabled => GetBool(HashtagStopPromptSetting, true);

    public void SetHashtagStopPromptEnabled(bool value)
    {
        _database.SetSetting(HashtagStopPromptSetting, value ? "1" : "0");
    }

    public bool UseLastHashtagAsDefault => GetBool(UseLastHashtagDefaultSetting, true);

    public void SetUseLastHashtagAsDefault(bool value)
    {
        _database.SetSetting(UseLastHashtagDefaultSetting, value ? "1" : "0");
    }

    public string TableVersion => _database.GetSetting(TableVersionSetting) ?? DefaultTableVersion;

    public void SetTableVersion(string value)
    {
        _database.SetSetting(TableVersionSetting, value);
    }

    public bool IsPopupHotkeyHelpVisible => GetBool(PopupHotkeyHelpSetting, false);

    public void SetPopupHotkeyHelpVisible(bool value)
    {
        _database.SetSetting(PopupHotkeyHelpSetting, value ? "1" : "0");
    }

    public string? DataDirectory => _database.GetSetting(DataDirectorySetting);

    public void SetDataDirectory(string directory)
    {
        _database.SetSetting(DataDirectorySetting, directory);
    }

    public ReportsWindowPlacement? GetReportsWindowPlacement()
    {
        var left = GetDoubleSetting(ReportsWindowLeftSetting);
        var top = GetDoubleSetting(ReportsWindowTopSetting);
        var width = GetDoubleSetting(ReportsWindowWidthSetting);
        var height = GetDoubleSetting(ReportsWindowHeightSetting);
        var stateRaw = _database.GetSetting(ReportsWindowStateSetting);

        if (left == null || top == null || width == null || height == null || string.IsNullOrWhiteSpace(stateRaw))
        {
            return null;
        }

        if (!Enum.TryParse(stateRaw, out WindowState state))
        {
            state = WindowState.Normal;
        }

        return new ReportsWindowPlacement(left.Value, top.Value, width.Value, height.Value, state);
    }

    public void SetReportsWindowPlacement(ReportsWindowPlacement placement)
    {
        _database.SetSetting(ReportsWindowLeftSetting, placement.Left.ToString(CultureInfo.InvariantCulture));
        _database.SetSetting(ReportsWindowTopSetting, placement.Top.ToString(CultureInfo.InvariantCulture));
        _database.SetSetting(ReportsWindowWidthSetting, placement.Width.ToString(CultureInfo.InvariantCulture));
        _database.SetSetting(ReportsWindowHeightSetting, placement.Height.ToString(CultureInfo.InvariantCulture));
        _database.SetSetting(ReportsWindowStateSetting, placement.State.ToString());
    }

    public string GetStartHashtag()
    {
        return UseLastHashtagAsDefault ? LastHashtag : DefaultHashtag;
    }

    private decimal GetDecimal(string key, decimal fallback)
    {
        var raw = _database.GetSetting(key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private bool GetBool(string key, bool fallback)
    {
        var raw = _database.GetSetting(key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (bool.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        return raw == "1";
    }

    private double? GetDoubleSetting(string key)
    {
        var raw = _database.GetSetting(key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return double.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}

public sealed record ReportsWindowPlacement(double Left, double Top, double Width, double Height, WindowState State);
