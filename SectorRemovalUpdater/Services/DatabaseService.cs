using System.Text;
using DynamicData;
using LightningDB;
using MessagePack;
using SectorRemovalUpdater.Models.RemovalsUpdater;

namespace SectorRemovalUpdater.Services;

public class DatabaseService
{
    private static DatabaseService? _instance;
    private LightningDatabase _indexingDatabase;
    private Dictionary<string, LightningDatabase> _databases = new();
    private LightningEnvironment _env;
    private static readonly long Kb = 1024;
    private static readonly long Gb = Kb * Kb * Kb;
    private static readonly int MaxReaders = 512;
    private static readonly long MapSize = Gb * 100;
    private static bool _isInitialized = false;
    private static object _lock = new();
    
    public static DatabaseService Instance
    {
        get { return _instance ??= new DatabaseService(); }
    }
    
    public void Initialize(string envPath)
    {
        if (_isInitialized)
            return;
        try
        {
            _env = new LightningEnvironment(envPath)
            {
                MaxReaders = MaxReaders,
                MapSize = MapSize,
                MaxDatabases = 1000,
            };
            _env.Open();
            
            _databases.Clear();
            
            var tx = _env.BeginTransaction();
            _indexingDatabase = tx.OpenDatabase("IndexingDatabase", new DatabaseConfiguration() { Flags = DatabaseOpenFlags.Create });
            tx.Commit();
            var tx2 = _env.BeginTransaction();
            
            using var indexingCursor = tx2.CreateCursor(_indexingDatabase);
            while (MoveNext(indexingCursor, out var key, out var value))
            {
                var dbname = Encoding.UTF8.GetString(key.AsSpan());
                _databases.TryAdd(dbname, tx2.OpenDatabase(dbname, new DatabaseConfiguration() { Flags = DatabaseOpenFlags.Create }));
            }
            tx2.Commit();
            _isInitialized = true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to initialize database! {e}");
        }
    }

    public void WriteEntry(byte[] key, byte[] value, string dbName)
    {
        if (!_isInitialized)
            return;
        using var tx = _env.BeginTransaction();
        var db = _databases.GetValueOrDefault(dbName);
        if (db == null)
            throw new ArgumentException($"Database {dbName} not found!");
        tx.Put(db, key, value);
        tx.Commit();
    }

    public byte[]? GetEntry(byte[] key, string dbName)
    {
        if (!_isInitialized)
            return null;
        using var tx = _env.BeginTransaction();
        var db = _databases.GetValueOrDefault(dbName);
        if (db == null)
            throw new ArgumentException($"Database {dbName} not found!");
        
        var outValue = tx.Get(db, key).value.CopyToNewArray();
        tx.Commit();
        return outValue;
    }

    public (long, long) GetStats(string dbName)
    {
        if (!_isInitialized)
            return (0, 0);
        var db = _databases.GetValueOrDefault(dbName);
        if (db == null)
            return (0, 0);
        
        using var tx = _env.BeginTransaction();
        var size = db.DatabaseStats.PageSize;
        var entires = db.DatabaseStats.Entries;
        
        return (size, entires);
    }

    public List<KeyValuePair<string, byte[]>>? DumpDB(string dbName)
    {
        if (!_isInitialized)
            return null;
        var db = _databases.GetValueOrDefault(dbName);
        if (db == null)
            return null;
        using var tx = _env.BeginTransaction();
        using var cursor = tx.CreateCursor(db);
        
        var output = new List<KeyValuePair<string, byte[]>>();
        
        while (MoveNext(cursor, out var key, out var value))
        {
            output.Add(new KeyValuePair<string, byte[]>(Encoding.UTF8.GetString(key.AsSpan()), value.CopyToNewArray()));
        }
        return output;
    }

    private bool MoveNext(LightningCursor cur, out MDBValue key, out MDBValue value)
    {
        var status = cur.Next();
        var result = cur.GetCurrent();
        key = result.key;
        value = result.value;
        return status == MDBResultCode.Success;
    }
    
    public void CreateNewDataBase(string dbName)
    {
        lock (_lock)
        {
            if (_databases.ContainsKey(dbName))
                return;
            using var tx = _env.BeginTransaction();
            var db = tx.OpenDatabase(dbName, new DatabaseConfiguration() { Flags = DatabaseOpenFlags.Create });
            tx.Put(_indexingDatabase, Encoding.UTF8.GetBytes(dbName), new byte[0]);
            tx.Commit();
            _databases.Add(dbName, db);
        }
    }

    public void LoadDatabaseFromFile(string filePath)
    {
        if (!_isInitialized)
            throw new ArgumentException("Database not initialized!");
        
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found.");
        
        var fileContent = File.ReadAllBytes(filePath);
        var fileDB = MessagePackSerializer.Deserialize<DatabaseFile>(fileContent);
        if (fileDB == null)
            throw new Exception("Failed to deserialize DatabaseFile");
        
        CreateNewDataBase(fileDB.GameVersion);
        
        var db = _databases.GetValueOrDefault(fileDB.GameVersion);
        if (db == null)
            throw new Exception("Failed to get database");
        
        var tx = _env.BeginTransaction();
        foreach (var sector in fileDB.NodeDataEntries)
        {
            tx.Put(db, Encoding.UTF8.GetBytes(sector.Key), MessagePackSerializer.Serialize(sector.Value));
        }
        tx.Commit();
        Console.WriteLine("Database loaded.");
    }

    public void WriteDataBaseToFile(string dbName, string filePath)
    {
        if (!_isInitialized)
            throw new ArgumentException("Database not initialized!");
        
        var db = _databases.GetValueOrDefault(dbName);
        if (db == null)
            throw new ArgumentException("Database not found!");

        var outContent = new DatabaseFile();
        outContent.GameVersion = dbName;
        outContent.NodeDataEntries = new Dictionary<string, NodeDataEntry[]>();
        
        var tx = _env.BeginTransaction();
        using var cursor = tx.CreateCursor(db);
        while (MoveNext(cursor, out var key, out var value))
        {
            outContent.NodeDataEntries.Add(Encoding.UTF8.GetString(key.AsSpan()), MessagePackSerializer.Deserialize<NodeDataEntry[]>(value.CopyToNewArray()));
        }
        
        tx.Commit();
        
        File.WriteAllBytes(filePath, MessagePackSerializer.Serialize(outContent));
        Console.WriteLine($"Database saved to {filePath}");
    }
    
    public string[] GetDatabaseNames()
    {
        return _databases.Keys.ToArray();
    }
}