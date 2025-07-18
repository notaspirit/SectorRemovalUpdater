using Newtonsoft.Json;

namespace SectorRemovalUpdater.Services;

public class SettingsService
{
    [JsonIgnore]
    private readonly string _settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SectorRemovalUpdater", "settings.json");
    public string DatabasePath { get; set; }
    public string GamePath { get; set; }
    
    public bool EnableMods { get; set; }
    public int MaxSectorDepth { get; set; }
    public double MinimumActorHashMatchRate { get; set; }
    public bool VerboseLogging { get; set; }
    
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
        MaxSectorDepth = 2;
        MinimumActorHashMatchRate = 0.9;
        VerboseLogging = false;
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
        MaxSectorDepth = savedSettings.MaxSectorDepth;
        MinimumActorHashMatchRate = savedSettings.MinimumActorHashMatchRate;
        VerboseLogging = savedSettings.VerboseLogging;
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
            new("MaxSectorDepth", MaxSectorDepth.ToString()),
            new("MinimumActorHashMatchRate", MinimumActorHashMatchRate.ToString()),
            new("VerboseLogging", VerboseLogging.ToString())
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
            case "MaxSectorDepth":
                var parsedInt = int.TryParse(value, out var maxSectorDepth);
                if (!parsedInt)
                {
                    Console.WriteLine("Failed to parse MaxSectorDepth value.");
                    break;
                }

                MaxSectorDepth = maxSectorDepth;
                break;
            case "MinimumActorHashMatchRate":
                var parsedDouble = double.TryParse(value, out var minActorHashMatchRate);
                if (!parsedDouble)
                {
                    Console.WriteLine("Failed to parse MinimumActorHashMatchRate value.");
                    break;
                }
                MinimumActorHashMatchRate = minActorHashMatchRate;
                break;
            case "VerboseLogging":
                var parsedVerboseLogging = bool.TryParse(value, out var verboseLogging);
                if (!parsedVerboseLogging)
                {
                    Console.WriteLine("Failed to parse VerboseLogging value.");
                    break;
                }
                VerboseLogging = verboseLogging;
                break;
            default:
                Console.WriteLine($"Unknown setting '{key}'.");
                break;
        }
        SaveSettings();
    }
}