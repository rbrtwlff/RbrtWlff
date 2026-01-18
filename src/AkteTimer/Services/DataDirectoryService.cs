using System;
using System.Globalization;
using System.IO;

namespace AkteTimer.Services;

public sealed class DataDirectoryService
{
    public const string DataDirectorySetting = "DataDirectory";
    private const string PersistedDirectoryFileName = "data-directory.txt";

    private readonly string _defaultDirectory;
    private readonly string _persistedDirectoryPath;

    public DataDirectoryService()
    {
        _defaultDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AkteTimer");
        _persistedDirectoryPath = Path.Combine(_defaultDirectory, PersistedDirectoryFileName);
        CurrentDirectory = _defaultDirectory;
    }

    public string DefaultDirectory => _defaultDirectory;

    public string CurrentDirectory { get; private set; }

    public string DatabasePath => GetDatabasePath(CurrentDirectory);

    public string LogsDirectory => Path.Combine(CurrentDirectory, "logs");

    public string BackupsDirectory => Path.Combine(CurrentDirectory, "backups");

    public string? LoadPersistedDirectory()
    {
        if (!File.Exists(_persistedDirectoryPath))
        {
            return null;
        }

        var content = File.ReadAllText(_persistedDirectoryPath).Trim();
        return string.IsNullOrWhiteSpace(content) ? null : content;
    }

    public void PersistDirectory(string directory)
    {
        Directory.CreateDirectory(_defaultDirectory);
        File.WriteAllText(_persistedDirectoryPath, directory.Trim(), System.Text.Encoding.UTF8);
    }

    public bool TryEnsureWritable(string directory, out string? errorMessage)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var testFile = Path.Combine(directory, $"write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testFile, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            File.Delete(testFile);
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public void SetCurrentDirectory(string directory)
    {
        CurrentDirectory = directory;
        EnsureSubdirectories(directory);
    }

    public string GetDatabasePath(string directory)
    {
        return Path.Combine(directory, "aktetimer.db");
    }

    public static bool AreSameDirectory(string left, string right)
    {
        var normalizedLeft = NormalizePath(left);
        var normalizedRight = NormalizePath(right);
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        var normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalized;
    }

    private static void EnsureSubdirectories(string directory)
    {
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "logs"));
        Directory.CreateDirectory(Path.Combine(directory, "backups"));
    }
}
