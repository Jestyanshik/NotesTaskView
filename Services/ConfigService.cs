using System.IO;
using System.Text.Json;

namespace NotesTaskView.Services;

public sealed class ConfigService
{
    private const string ConfigFileName = "appsettings.json";

    public AppConfig Load()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
        if (!File.Exists(configPath))
        {
            return new AppConfig();
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json);
            if (config is null || string.IsNullOrWhiteSpace(config.NotesFolderPath))
            {
                return new AppConfig();
            }

            config.NotesFolderPath = Environment.ExpandEnvironmentVariables(config.NotesFolderPath.Trim());
            return config;
        }
        catch
        {
            return new AppConfig();
        }
    }
}
