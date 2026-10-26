using System.IO;
using System.Text;
using System.Text.Json;

namespace PersonalLearningAgent;

internal sealed record MemoryEntry(string Id, DateTimeOffset CreatedAt, string Text);

internal sealed class MemoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MemoryStore()
    {
        var projectRoot = FindProjectRoot();
        DataFilePath = projectRoot is null
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalLearningAgent",
                "data",
                "memories.jsonl")
            : Path.Combine(projectRoot, "data", "memories.jsonl");
    }

    public string DataFilePath { get; }

    public MemoryEntry Append(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Nội dung ghi nhớ không được để trống.", nameof(text));
        }

        var directory = Path.GetDirectoryName(DataFilePath)
            ?? throw new InvalidOperationException("Không xác định được thư mục lưu dữ liệu.");
        Directory.CreateDirectory(directory);

        var entry = new MemoryEntry(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, text.Trim());
        var jsonLine = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
        File.AppendAllText(DataFilePath, jsonLine, new UTF8Encoding(false));
        return entry;
    }

    public IReadOnlyList<MemoryEntry> ReadAll()
    {
        if (!File.Exists(DataFilePath))
        {
            return [];
        }

        var entries = new List<MemoryEntry>();
        var lineNumber = 0;
        foreach (var line in File.ReadLines(DataFilePath))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            MemoryEntry? entry;
            try
            {
                entry = JsonSerializer.Deserialize<MemoryEntry>(line, JsonOptions);
            }
            catch (JsonException exception)
            {
                throw new JsonException($"JSONL không hợp lệ ở dòng {lineNumber}: {exception.Message}", exception);
            }

            if (entry is null || string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.Text))
            {
                throw new JsonException($"JSONL không hợp lệ ở dòng {lineNumber}: thiếu ID hoặc nội dung.");
            }

            entries.Add(entry);
        }

        return entries;
    }

    public bool Delete(string id)
    {
        var entries = ReadAll();
        if (!entries.Any(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(DataFilePath)
            ?? throw new InvalidOperationException("Không xác định được thư mục lưu dữ liệu.");
        var temporaryPath = Path.Combine(directory, $"memories.{Guid.NewGuid():N}.tmp");

        try
        {
            var remainingEntries = entries
                .Where(entry => !string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase))
                .Select(entry => JsonSerializer.Serialize(entry, JsonOptions));
            File.WriteAllLines(temporaryPath, remainingEntries, new UTF8Encoding(false));
            File.Move(temporaryPath, DataFilePath, overwrite: true);
            return true;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string? FindProjectRoot()
    {
        foreach (var startPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(startPath);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "PersonalLearningAgent.csproj")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }
}
