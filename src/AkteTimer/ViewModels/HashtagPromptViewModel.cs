using System.Collections.ObjectModel;

namespace AkteTimer.ViewModels;

public sealed class HashtagPromptViewModel
{
    public HashtagPromptViewModel(IEnumerable<string> tags, string defaultHashtag)
    {
        Tags = new ObservableCollection<string>(tags);
        DefaultHashtag = defaultHashtag;
    }

    public ObservableCollection<string> Tags { get; }

    public string DefaultHashtag { get; }

    public string DefaultHashtagText => $"Vorschlag: {DefaultHashtag}";
}
