using System.IO;
using System.Text;
using System.Text.Json;

namespace PersonalLearningAgent;

internal sealed record MemoryHighlight(int Start, int Length, string Color);

internal sealed record MemoryEntry(
    string Id,
    DateTimeOffset CreatedAt,
    string Text,
    IReadOnlyList<MemoryHighlight>? Highlights = null,
    string? Category = null,
    bool IsPinned = false);

internal sealed record MemoryMergeResult(int AddedCount, int SkippedCount);

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

    public void EnsureDataFile()
    {
        var directory = Path.GetDirectoryName(DataFilePath)
            ?? throw new InvalidOperationException("Không xác định được thư mục lưu dữ liệu.");
        Directory.CreateDirectory(directory);
        if (!File.Exists(DataFilePath))
        {
            using var _ = File.Create(DataFilePath);
        }
    }

    public MemoryEntry Append(string text, string? category = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Nội dung ghi nhớ không được để trống.", nameof(text));
        }

        var directory = Path.GetDirectoryName(DataFilePath)
            ?? throw new InvalidOperationException("Không xác định được thư mục lưu dữ liệu.");
        Directory.CreateDirectory(directory);

        var entry = new MemoryEntry(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            text.Trim(),
            Category: NormalizeCategory(category));
        AppendEntry(entry);
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

            entries.Add(entry with
            {
                Category = NormalizeCategory(entry.Category),
                Highlights = NormalizeHighlights(entry.Text, entry.Highlights ?? [])
            });
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
            WriteEntries(
                temporaryPath,
                entries.Where(entry => !string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase)));
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

    public MemoryEntry UpdateHighlights(string id, IReadOnlyList<MemoryHighlight> highlights)
    {
        var entries = ReadAll().ToList();
        var index = entries.FindIndex(
            entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            throw new InvalidOperationException("Không tìm thấy ghi nhớ cần cập nhật.");
        }

        var entry = entries[index];
        var updatedEntry = entry with { Highlights = NormalizeHighlights(entry.Text, highlights) };
        entries[index] = updatedEntry;
        ReplaceEntries(entries);
        return updatedEntry;
    }

    public MemoryEntry Update(MemoryEntry updatedEntry)
    {
        var entries = ReadAll().ToList();
        var index = entries.FindIndex(
            entry => string.Equals(entry.Id, updatedEntry.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            throw new InvalidOperationException("Không tìm thấy ghi nhớ cần cập nhật.");
        }

        updatedEntry = updatedEntry with
        {
            Text = updatedEntry.Text.Trim(),
            Category = NormalizeCategory(updatedEntry.Category),
            Highlights = NormalizeHighlights(updatedEntry.Text.Trim(), updatedEntry.Highlights ?? [])
        };
        if (string.IsNullOrWhiteSpace(updatedEntry.Text))
        {
            throw new ArgumentException("Nội dung ghi nhớ không được để trống.", nameof(updatedEntry));
        }

        entries[index] = updatedEntry;
        ReplaceEntries(entries);
        return updatedEntry;
    }

    public MemoryEntry UpdatePinned(string id, bool isPinned)
    {
        var entries = ReadAll().ToList();
        var index = entries.FindIndex(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            throw new InvalidOperationException("Không tìm thấy ghi nhớ cần cập nhật.");
        }

        var updatedEntry = entries[index] with { IsPinned = isPinned };
        entries[index] = updatedEntry;
        ReplaceEntries(entries);
        return updatedEntry;
    }

    public void Restore(MemoryEntry entry)
    {
        var entries = ReadAll();
        if (entries.Any(existing => string.Equals(existing.Id, entry.Id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Ghi nhớ này đã tồn tại; không thể khôi phục bản trùng.");
        }

        AppendEntry(entry);
    }

    public void ExportBackup(string destinationPath)
    {
        var entries = ReadAll();
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("Không xác định được thư mục lưu bản sao lưu.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"backup.{Guid.NewGuid():N}.tmp");
        try
        {
            WriteEntries(temporaryPath, entries);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public MemoryMergeResult MergeBackup(string backupPath)
    {
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("Không tìm thấy tệp sao lưu.", backupPath);
        }

        var backup = ReadEntriesFromFile(backupPath);
        var entries = ReadAll().ToList();
        var knownIds = entries.Select(entry => entry.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = backup.Where(entry => knownIds.Add(entry.Id)).ToArray();
        var merged = entries.Concat(added).ToArray();
        ReplaceEntries(merged);
        return new MemoryMergeResult(added.Length, backup.Count - added.Length);
    }

    internal static IReadOnlyList<MemoryHighlight> NormalizeHighlights(
        string text,
        IEnumerable<MemoryHighlight> highlights)
    {
        var normalized = new List<MemoryHighlight>();
        foreach (var highlight in highlights)
        {
            var start = Math.Clamp(highlight.Start, 0, text.Length);
            var end = Math.Clamp((long)highlight.Start + highlight.Length, 0, text.Length);
            if (end <= start || string.IsNullOrWhiteSpace(highlight.Color) || !IsHexColor(highlight.Color))
            {
                continue;
            }

            var color = highlight.Color.ToUpperInvariant();
            var replacement = new List<MemoryHighlight>();
            foreach (var existing in normalized)
            {
                var existingEnd = existing.Start + existing.Length;
                if (existingEnd <= start || existing.Start >= end)
                {
                    replacement.Add(existing);
                    continue;
                }

                if (existing.Start < start)
                {
                    replacement.Add(existing with { Length = start - existing.Start });
                }

                if (existingEnd > end)
                {
                    replacement.Add(existing with
                    {
                        Start = (int)end,
                        Length = existingEnd - (int)end
                    });
                }
            }

            replacement.Add(new MemoryHighlight(start, (int)end - start, color));
            normalized = replacement;
        }

        return normalized
            .OrderBy(highlight => highlight.Start)
            .Aggregate(
                new List<MemoryHighlight>(),
                (merged, current) =>
                {
                    if (merged.Count > 0)
                    {
                        var previous = merged[^1];
                        if (previous.Start + previous.Length == current.Start
                            && string.Equals(previous.Color, current.Color, StringComparison.OrdinalIgnoreCase))
                        {
                            merged[^1] = previous with { Length = previous.Length + current.Length };
                            return merged;
                        }
                    }

                    merged.Add(current);
                    return merged;
                });
    }

    private static bool IsHexColor(string color) =>
        color.Length == 7
        && color[0] == '#'
        && color.AsSpan(1).ToString().All(Uri.IsHexDigit);

    private static void WriteEntries(string path, IEnumerable<MemoryEntry> entries)
    {
        var jsonLines = entries.Select(entry => JsonSerializer.Serialize(entry, JsonOptions));
        File.WriteAllLines(path, jsonLines, new UTF8Encoding(false));
    }

    private static IReadOnlyList<MemoryEntry> ReadEntriesFromFile(string path)
    {
        var entries = new List<MemoryEntry>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
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
                throw new JsonException($"JSONL sao lưu không hợp lệ ở dòng {lineNumber}: {exception.Message}", exception);
            }

            if (entry is null || string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.Text))
            {
                throw new JsonException($"JSONL sao lưu không hợp lệ ở dòng {lineNumber}: thiếu ID hoặc nội dung.");
            }

            if (!ids.Add(entry.Id))
            {
                throw new JsonException($"JSONL sao lưu có ID bị lặp ở dòng {lineNumber}.");
            }

            entries.Add(entry with
            {
                Category = NormalizeCategory(entry.Category),
                Highlights = NormalizeHighlights(entry.Text, entry.Highlights ?? [])
            });
        }

        return entries;
    }

    private static string? NormalizeCategory(string? category)
    {
        var normalized = category?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private void AppendEntry(MemoryEntry entry)
    {
        var directory = Path.GetDirectoryName(DataFilePath)
            ?? throw new InvalidOperationException("Không xác định được thư mục lưu dữ liệu.");
        Directory.CreateDirectory(directory);
        if (File.Exists(DataFilePath) && new FileInfo(DataFilePath).Length > 0)
        {
            bool needsLineBreak;
            using (var stream = File.OpenRead(DataFilePath))
            {
                stream.Seek(-1, SeekOrigin.End);
                var lastByte = stream.ReadByte();
                needsLineBreak = lastByte is not ('\n' or '\r');
            }

            if (needsLineBreak)
            {
                File.AppendAllText(DataFilePath, Environment.NewLine, new UTF8Encoding(false));
            }
        }

        File.AppendAllText(
            DataFilePath,
            JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine,
            new UTF8Encoding(false));
    }

    private void ReplaceEntries(IEnumerable<MemoryEntry> entries)
    {
        var directory = Path.GetDirectoryName(DataFilePath)
            ?? throw new InvalidOperationException("Không xác định được thư mục lưu dữ liệu.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"memories.{Guid.NewGuid():N}.tmp");
        try
        {
            WriteEntries(temporaryPath, entries);
            File.Move(temporaryPath, DataFilePath, overwrite: true);
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
