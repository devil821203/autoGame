using Cultivation.Domain;
using Cultivation.Simulation;

namespace Cultivation.Persistence;

public sealed class SqliteWorldStore:IWorldStore
{
    private const string ApplicationId="1129663572"; // CULT. Engineering format ID.
    private readonly Action<string>? checkpoint;
    public SqliteWorldStore(){}
    internal SqliteWorldStore(Action<string> checkpoint)=>this.checkpoint=checkpoint;
    public static string ProviderVersion=>NativeSqlite.Version;
    public void Save(string path,WorldState snapshot)
    {
        WorldIntegrity.Validate(snapshot);
        var content=snapshot.Time?.Content??throw new InvalidOperationException("SQLite新存檔需要R4內容版本；請建立R4新情境，舊檔不轉換");
        string payload=SaveMigration.Write(snapshot),full=Path.GetFullPath(path),temporary=full+"."+Guid.NewGuid().ToString("N")+".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        try
        {
            // Reserve our own filename exclusively; the destination is never opened for writing.
            using(new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){}
            using(var db=new NativeSqlite(temporary,false))
            {
                db.Exec("PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL; BEGIN IMMEDIATE;");
                db.Exec("PRAGMA application_id="+ApplicationId+"; PRAGMA user_version=1; CREATE TABLE snapshot (id INTEGER PRIMARY KEY CHECK(id=1), content_version TEXT NOT NULL, content_hash TEXT NOT NULL, payload TEXT NOT NULL, payload_hash TEXT NOT NULL, provider_version TEXT NOT NULL);");
                using(var insert=db.Prepare("INSERT INTO snapshot VALUES(1,?1,?2,?3,?4,?5);"))
                {
                    insert.Bind(1,content.ContentVersion);insert.Bind(2,ContentProfile.Fingerprint(content));insert.Bind(3,payload);insert.Bind(4,ContentProfile.Hash(payload));insert.Bind(5,ProviderVersion);
                    if(insert.Row())throw new InvalidDataException("Unexpected SQLite insert result");
                }
                checkpoint?.Invoke("BeforeCommit");db.Exec("COMMIT;");checkpoint?.Invoke("AfterCommit");
            }
            using(var file=new FileStream(temporary,FileMode.Open,FileAccess.Write,FileShare.None))file.Flush(true);
            File.Move(temporary,full,overwrite:false);
        }
        finally
        {
            // Only remove this operation's private temporary files, never the user's destination.
            foreach(string suffix in new[]{"","-journal"})if(File.Exists(temporary+suffix))File.Delete(temporary+suffix);
        }
    }
    public WorldState Load(string path)
    {
        using var db=new NativeSqlite(Path.GetFullPath(path),true);
        db.Exec("PRAGMA query_only=ON; BEGIN;");
        if(db.Scalar("PRAGMA application_id;")!=ApplicationId||db.Scalar("PRAGMA user_version;")!="1")throw new InvalidDataException("Unsupported SQLite save schema/application");
        if(db.Scalar("PRAGMA quick_check;")!="ok")throw new InvalidDataException("SQLite save failed integrity check");
        using var row=db.Prepare("SELECT content_version,content_hash,payload,payload_hash,provider_version FROM snapshot WHERE id=1;");
        if(!row.Row())throw new InvalidDataException("SQLite snapshot missing");
        string version=row.Text(0),contentHash=row.Text(1),payload=row.Text(2),payloadHash=row.Text(3);_ = row.Text(4);
        if(row.Row()||db.Scalar("SELECT count(*) FROM snapshot;")!="1"||ContentProfile.Hash(payload)!=payloadHash)throw new InvalidDataException("SQLite snapshot duplicate or hash mismatch");
        var world=SaveMigration.Read(payload);WorldIntegrity.Validate(world);
        if(world.Time?.Content is not {} content||content.ContentVersion!=version||ContentProfile.Fingerprint(content)!=contentHash)throw new InvalidDataException("SQLite content metadata mismatch");
        db.Exec("COMMIT;");return world;
    }
}
