using System.Text;
using DynamicData;
using LightningDB;
using MessagePack;
using RemovalsUpdater.Models.RemovalsUpdater;

namespace RemovalsUpdater.Services;

public class DatabaseService
{
    private static DatabaseService? _instance;
    private LightningDatabase _sectorDb;
    private LightningEnvironment _env;
    private static readonly long Kb = 1024;
    private static readonly long Gb = Kb * Kb * Kb;
    private static readonly int MaxReaders = 512;
    private static readonly long MapSize = Gb * 100;
    private static bool _isInitailized = false;
    
    public static DatabaseService Instance
    {
        get { return _instance ??= new DatabaseService(); }
    }
    
    public void Initialize(string envPath)
    {
        if (_isInitailized)
            return;
        try
        {
            _env = new LightningEnvironment(envPath)
            {
                MaxReaders = MaxReaders,
                MapSize = MapSize,
                MaxDatabases = 1,
            };
            _env.Open();
        
            var tx = _env.BeginTransaction();
            _sectorDb = tx.OpenDatabase("Sectors", new DatabaseConfiguration() { Flags = DatabaseOpenFlags.Create });
            var code = tx.Commit();
            Console.WriteLine(code.ToString());
            _isInitailized = true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to initialize database! {e}");
        }
    }

    public void WriteEntry(byte[] key, byte[] value)
    {
        Console.WriteLine($"DB is initialized: {_isInitailized}");
        if (!_isInitailized)
            return;
        using var tx = _env.BeginTransaction();
        var code = tx.Put(_sectorDb, key, value);
        Console.WriteLine(code.ToString());
        tx.Commit();
    }

    public byte[]? GetEntry(byte[] key)
    {
        if (!_isInitailized)
            return null;
        using var tx = _env.BeginTransaction();
        return tx.Get(_sectorDb, key).value.CopyToNewArray();
    }

    public (long, long) GetStats()
    {
        if (!_isInitailized)
            return (0, 0);
        using var tx = _env.BeginTransaction();
        var size = _sectorDb.DatabaseStats.PageSize;
        var entires = _sectorDb.DatabaseStats.Entries;
        
        return (size, entires);
    }

    public List<KeyValuePair<string, byte[]>>? DumpDB()
    {
        if (!_isInitailized)
            return null;

        using var tx = _env.BeginTransaction();
        using var cursor = tx.CreateCursor(_sectorDb);
        var i = 0;
        
        var output = new List<KeyValuePair<string, byte[]>>();
        
        while (MoveNext(cursor, out var key, out var value))
        {
            output.Add(new KeyValuePair<string, byte[]>(Encoding.UTF8.GetString(key.AsSpan()), value.CopyToNewArray()));
            i++;
        }
        return output;
    }

    private bool MoveNext(LightningCursor cur, out MDBValue key, out MDBValue value)
    {
        var status = cur.Next();
        var result = cur.GetCurrent();
        key = result.key;
        value = result.value;
        return result.resultCode == MDBResultCode.Success;
    }
}