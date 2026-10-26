using System.IO;
using System.Text.Json;

namespace PersonalLearningAgent;

internal sealed record AppSettings(string BackgroundColor)
{
    public static AppSettings Default { get; } = new("#F4F6FB");
}

internal sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettingsStore()
    {
        DataFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PersonalLearningAgent",
            "settings.json");
    }

    public string DataFilePath { get; }

    public AppSettings Load()
    {
        if (!File.Exists(DataFilePath))
        {
            return AppSettings.Default;
        }

        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(DataFilePath));
        if (settings is null || !SettingsWindow.IsValidColor(settings.BackgroundColor))
        {
            throw new JsonException("Màu nền trong tệp cài đặt không hợp lệ.");
        }

        return settings;
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(DataFilePath)
            ?? throw new InvalidOperationException("Không xác định được thư mục lưu cài đặt.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(directory, $"settings.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
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
}
