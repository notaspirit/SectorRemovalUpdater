using System;
using System.IO;
using WolvenKit;
using WolvenKit.Common.Services;
using WolvenKit.Core.Interfaces;
using WolvenKit.Core.Services;
using WolvenKit.RED4.CR2W;
using WolvenKit.RED4.CR2W.Archive;

namespace RemovalsUpdater.Services;

public class WolvenKitWrapper
{
    private static WolvenKitWrapper? instance;

    private readonly ILoggerService _loggerService;
    private readonly IProgressService<double> _progressService;
    
    public HashService HashService;
    public HookService HookService;
    public Red4ParserService Red4ParserService;
    public ArchiveManager ArchiveManager;
    public GeometryCacheService GeometryCacheService;
    
    public string GameExePath { get; set; }
    
    private WolvenKitWrapper(string gameExePath, bool enableMods = false)
    {
        if (string.IsNullOrEmpty(gameExePath)) throw new ArgumentNullException(nameof(gameExePath), "Game executable path cannot be null or empty.");
        if (!File.Exists(gameExePath)) throw new FileNotFoundException("Game executable path cannot be found.");
        
        GameExePath = gameExePath;
        
        _loggerService = new SerilogWrapper();
        _progressService = new ProgressService<double>();
        HashService = new HashService();
        HookService = new HookService();
        Red4ParserService = new Red4ParserService(HashService, _loggerService, HookService);
        ArchiveManager = new ArchiveManager(HashService, Red4ParserService, _loggerService, _progressService);
        
        ArchiveManager.Initialize(new FileInfo(gameExePath), enableMods);
        
        GeometryCacheService = new GeometryCacheService(ArchiveManager, Red4ParserService);
    }

    /// <summary>
    /// Initializes the service (should only be called once when interactive mode is being opened), does not return an instance
    /// </summary>
    /// <param name="gameExePath"></param>
    /// <param name="enableMods"></param>
    /// <returns></returns>
    public static bool Initialize(string gameExePath, bool enableMods = false)
    {
        if (instance != null) return true;
        try
        {
            instance = new WolvenKitWrapper(gameExePath, enableMods);
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine("Error initializing WolvenKitWrapper: " + e.Message);
            return false;
        }
    }
    /// <summary>
    /// Get the current Instance
    /// </summary>
    /// <exception cref="ArgumentException">if wrapper is not initialized</exception>
    public static WolvenKitWrapper? Instance
    {
        get
        {
            if (instance != null)
                return instance;
            throw new ArgumentException("Instance is not initialized");
        }
    }
}