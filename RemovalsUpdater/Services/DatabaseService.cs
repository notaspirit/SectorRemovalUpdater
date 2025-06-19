using System.Text;
using DynamicData;
using LightningDB;
using MessagePack;
using RemovalsUpdater.Models.RemovalsUpdater;

namespace RemovalsUpdater.Services;

public class DatabaseService
{
    private static DatabaseService? _instance;
    private LightningDatabase[] _databases = new LightningDatabase[2];
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
                MaxDatabases = 2,
            };
            _env.Open();
        
            var tx = _env.BeginTransaction();
            foreach (var dbname in Enum.GetValues(typeof(Enums.DatabaseNames)))
            {
                _databases[(int)dbname] = tx.OpenDatabase(dbname.ToString(), new DatabaseConfiguration() { Flags = DatabaseOpenFlags.Create });
            }
            tx.Commit();
            _isInitailized = true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to initialize database! {e}");
        }
    }

    public void WriteEntry(byte[] key, byte[] value, Enums.DatabaseNames dbn)
    {
        if (!_isInitailized)
            return;
        using var tx = _env.BeginTransaction();
        var db = _databases[(int)dbn];
        var code = tx.Put(db, key, value);
        Console.WriteLine(code.ToString());
        tx.Commit();
    }

    public byte[]? GetEntry(byte[] key, Enums.DatabaseNames dbn)
    {
        if (!_isInitailized)
            return null;
        using var tx = _env.BeginTransaction();
        var db = _databases[(int)dbn];
        return tx.Get(db, key).value.CopyToNewArray();
    }

    public (long, long) GetStats(Enums.DatabaseNames dbn)
    {
        if (!_isInitailized)
            return (0, 0);
        var db = _databases[(int)dbn];
        using var tx = _env.BeginTransaction();
        var size = db.DatabaseStats.PageSize;
        var entires = db.DatabaseStats.Entries;
        
        return (size, entires);
    }

    public List<KeyValuePair<string, byte[]>>? DumpDB(Enums.DatabaseNames dbn)
    {
        if (!_isInitailized)
            return null;
        var db = _databases[(int)dbn];
        using var tx = _env.BeginTransaction();
        using var cursor = tx.CreateCursor(db);
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