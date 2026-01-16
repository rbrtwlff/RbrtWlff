namespace AkteTimer.Models;

public sealed class Matter
{
    public long Id { get; set; }
    public string FileRef { get; set; } = string.Empty;
    public string? Title { get; set; }
    public bool IsArchived { get; set; }
}
