using Newtonsoft.Json;

namespace RemovalsUpdater.Services;

public class SettingsService
{
    [JsonIgnore]
    private readonly string _settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RemovalsUpdater", "settings.json");
    public string DatabasePath { get; set; }
    public string GamePath { get; set; }
    
    public bool EnableMods { get; set; }
    
    [JsonIgnore]
    private static SettingsService? instance;
    
    [JsonIgnore]
    private static object _lock = new();

    [JsonIgnore]
    public static SettingsService Instance
    {
        get
        {
            lock (_lock)
            {
                if (instance != null)
                    return instance;
                instance = new SettingsService();
                instance.LoadSettings();
            }
            return instance;
        }
    }
    

    
    private SettingsService()
    {
        DatabasePath = "";
        GamePath = "";
        EnableMods = false;
    }
    
    private void LoadSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath) ?? throw new InvalidOperationException());
        if (!File.Exists(_settingsFilePath))
            return;
        var json = File.ReadAllText(_settingsFilePath);
        var savedSettings = JsonConvert.DeserializeObject<SettingsService>(json);

        if (savedSettings == null)
        {
            Console.WriteLine("Failed to load settings using default values.");
        }
            
        DatabasePath = savedSettings.DatabasePath;
        GamePath = savedSettings.GamePath;
        EnableMods = savedSettings.EnableMods;
    }

    public void SaveSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath) ?? throw new InvalidOperationException());
        var json = JsonConvert.SerializeObject(this);
        File.WriteAllText(_settingsFilePath, json);
    }
    
    public List<KeyValuePair<string, string>> GetSettings()
    {
        return new List<KeyValuePair<string, string>>()
        {
            new("DatabasePath", DatabasePath),
            new("GamePath", GamePath),
            new("EnableMods", EnableMods.ToString()),
        };
    }

    public void SetSetting(string key, string value)
    {
        switch (key)
        {
            case "DatabasePath":
                DatabasePath = value;
                break;
            case "GamePath":
                GamePath = value;
                break;
            case "EnableMods":
                var parsedBool = bool.TryParse(value, out var enableMods);
                if (!parsedBool)
                {
                    Console.WriteLine("Failed to parse EnableMods value.");
                    break;
                }
                EnableMods = enableMods;
                break;
            default:
                Console.WriteLine($"Unknown setting '{key}'.");
                break;
        }
        SaveSettings();
    }
}