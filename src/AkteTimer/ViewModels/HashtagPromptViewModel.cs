using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace AkteTimer.ViewModels;

public sealed class HashtagPromptViewModel
{
    public HashtagPromptViewModel(IEnumerable<string> tags, string defaultHashtag)
    {
        Tags = new ObservableCollection<TagChipViewModel>(
            tags.Select((tag, index) => new TagChipViewModel(
                tag,
                GetShortcutText(index),
                string.Equals(tag, defaultHashtag, StringComparison.OrdinalIgnoreCase))));
        DefaultHashtag = defaultHashtag;
    }

    public ObservableCollection<TagChipViewModel> Tags { get; }

    public string DefaultHashtag { get; }

    public string DefaultHashtagText => $"Vorschlag: {DefaultHashtag}";

    private static string GetShortcutText(int index)
    {
        return index switch
        {
            >= 0 and <= 6 => $"Ctrl+{index + 1}",
            7 => "Ctrl+0",
            _ => "Ctrl+?"
        };
    }
}

public sealed class TagChipViewModel
{
    public TagChipViewModel(string tag, string shortcut, bool isActive)
    {
        Tag = tag;
        Shortcut = shortcut;
        IsActive = isActive;
    }

    public string Tag { get; }

    public string Shortcut { get; }

    public bool IsActive { get; }
}
