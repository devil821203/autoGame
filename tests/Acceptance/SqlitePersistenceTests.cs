using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;
using System.Text.Json.Nodes;

internal static class SqlitePersistenceTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private sealed class Files:IDisposable
    {
        internal readonly string DirectoryPath=Path.Combine(Path.GetTempPath(),"cultivation-r4-"+Guid.NewGuid().ToString("N"));
        internal Files()=>Directory.CreateDirectory(DirectoryPath);
        internal string PathFor(string name)=>Path.Combine(DirectoryPath,name);
        public void Dispose(){foreach(string path in Directory.GetFiles(DirectoryPath))File.Delete(path);Directory.Delete(DirectoryPath);}
    }
    private static WorldState Fresh()
    {
        var w=TestScenarioCatalog.Create("methods");DiscreteTime.EnableFresh(w);ActionLifecycle.EnableFresh(w);EventDecisions.EnableFresh(w);ContentProfile.EnableFresh(w);return w;
    }
    private static ActionIntent Train()=>new("r4-batch","player","player","method:QingyunFist",6,0,DecisionSource.Player,ActionKind.TrainMortalAttribute);
    private static void Reject(Action action,string message){try{action();throw new Exception(message);}catch(InvalidDataException){}}
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("R4 native SQLite provider writes a real Unicode database without packages",()=>{
            using var files=new Files();string path=files.PathFor("存檔's.sqlite");var w=Fresh();int index=w.Characters.FindIndex(a=>a.Id=="player");w.Characters[index]=w.Characters[index] with {Name="行者'); DROP TABLE snapshot;--"};
            var store=new SqliteWorldStore();store.Save(path,w);
            Check(System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path),0,16)=="SQLite format 3\0","Not SQLite");
            Check(WorldCodec.Digest(w)==WorldCodec.Digest(store.Load(path)),"Snapshot mismatch");
            Console.WriteLine("R4_SQLITE_PROVIDER "+SqliteWorldStore.ProviderVersion);
        }),
        ("R4 live batch pending decisions RNG and event versions resume identically",()=>{
            using var files=new Files();var e=new SimulationEngine(Fresh());e.Submit(Train());e.AdvanceTo(600);
            e.ScheduleWorldSignal(1020,"retained","exact seconds");e.ScheduleWorldSignal(1100,"cancelled","must not run");e.CancelWorldSignal("cancelled");
            var before=e.Snapshot();string path=files.PathFor("live.sqlite");new SqliteWorldStore().Save(path,before);var copy=new SqliteWorldStore().Load(path);
            Check(WorldCodec.Digest(before)==WorldCodec.Digest(copy),"Queue/AI/RNG/checkpoint lost");
            var restored=new SimulationEngine(copy);e.AdvanceTo(3600,true);restored.AdvanceTo(3600,true);
            Check(WorldCodec.Digest(e.Snapshot())==WorldCodec.Digest(restored.Snapshot()),"Continued results differ");
            Check(restored.Snapshot().Time!.ActionRuntime!.Actions["r4-batch"].Phase==ActionPhase.Completed,"Batch not completed");
        }),
        ("R4 paid service escrow survives SQLite and settles once",()=>{
            using var files=new Files();var w=Fresh();var doctor=w.Characters.Single(a=>a.TestProfession==TestProfession.Physician);
            w.Characters=w.Characters.Select(a=>a.Id=="player"?a with {Health=50,DemoLocationId=doctor.DemoLocationId,HexPosition=doctor.HexPosition,RegionId=doctor.RegionId}:a).ToList();
            w.Ledger.Add(new(w.NextSequence++,0,"r4-fixture","$genesis","player","herb",30,"Genesis"));
            var intent=new ActionIntent("r4-service","player",doctor.Id,"medical",1,0,DecisionSource.Player,ActionKind.RequestLifeService,LimitPrice:PaidLifeServices.Quote(w,"medical",ServiceMaterialSource.Client),ServiceMaterials:ServiceMaterialSource.Client);
            var e=new SimulationEngine(w);e.Submit(intent);e.AdvanceTo(0);var before=e.Snapshot();
            Check(before.Time!.ActionRuntime!.Reservations.Values.Any(r=>r.Remaining>0),"No real escrow");
            string path=files.PathFor("escrow.sqlite");new SqliteWorldStore().Save(path,before);var restored=new SimulationEngine(new SqliteWorldStore().Load(path));
            e.AdvanceTo(1200);restored.AdvanceTo(1200);Check(WorldCodec.Digest(e.Snapshot())==WorldCodec.Digest(restored.Snapshot()),"Service differs");
            restored.AdvanceTo(1800);Check(restored.Snapshot().PaidServiceReceipts.Count==1&&restored.Snapshot().Time!.ActionRuntime!.Reservations.Values.All(r=>r.Remaining==0),"Repeated payment or leaked escrow");
        }),
        ("R4 transaction and publication failures preserve previous save and leave no partial file",()=>{
            using var files=new Files();string old=files.PathFor("existing.sqlite");var w=Fresh();new SqliteWorldStore().Save(old,w);byte[] original=File.ReadAllBytes(old);
            foreach(string stage in new[]{"BeforeCommit","AfterCommit"}){
                string target=files.PathFor(stage+".sqlite");
                try{new SqliteWorldStore(at=>{if(at==stage)throw new IOException("Injected disk/write interruption");}).Save(target,w);throw new Exception("Fault not injected");}catch(IOException){}
                Check(!File.Exists(target)&&!Directory.GetFiles(files.DirectoryPath,"*.tmp*").Any()&&File.ReadAllBytes(old).SequenceEqual(original),"Partial publication or previous save changed");
            }
            try{new SqliteWorldStore().Save(old,w);throw new Exception("Overwrote existing");}catch(IOException){}
            Check(File.ReadAllBytes(old).SequenceEqual(original)&&Directory.GetFiles(files.DirectoryPath).Length==1,"Overwrite changed file or leaked temporary");
        }),
        ("R4 schema content and payload corruption are rejected with read-only load",()=>{
            using var files=new Files();var store=new SqliteWorldStore();
            foreach(string mutation in new[]{"PRAGMA user_version=99;","UPDATE snapshot SET content_version='unknown';","UPDATE snapshot SET payload='{}';","UPDATE snapshot SET content_hash='bad';"}){
                string path=files.PathFor(Guid.NewGuid()+".sqlite");store.Save(path,Fresh());using(var db=new NativeSqlite(path,false))db.Exec(mutation);
                byte[] before=File.ReadAllBytes(path);Reject(()=>store.Load(path),"Corrupt save accepted");Check(File.ReadAllBytes(path).SequenceEqual(before),"Load rewrote corrupt file");
            }
            string missing=files.PathFor("missing.sqlite");Reject(()=>store.Load(missing),"Missing save opened");Check(!File.Exists(missing),"Read created missing database");
            string broken=files.PathFor("broken.sqlite");File.WriteAllText(broken,"not a database");Reject(()=>store.Load(broken),"Non-SQLite accepted");
        }),
        ("R4 unknown content in internally consistent database is rejected",()=>{
            using var files=new Files();string path=files.PathFor("future.sqlite");new SqliteWorldStore().Save(path,Fresh());using(var db=new NativeSqlite(path,false)){
                var node=JsonNode.Parse(db.Scalar("SELECT payload FROM snapshot;"))!;node["Time"]!["Content"]!["ContentVersion"]="future";string payload=node.ToJsonString();
                using var statement=db.Prepare("UPDATE snapshot SET payload=?1,payload_hash=?2,content_version='future';");statement.Bind(1,payload);statement.Bind(2,ContentProfile.Hash(payload));statement.Row();
            }
            Reject(()=>new SqliteWorldStore().Load(path),"Unknown content accepted");
        }),
        ("R4 JSON compatibility and wire guard retain config snapshot instead of live values",()=>{
            using var files=new Files();var w=Fresh();w.Rules=w.Rules with {HerbPrice=9};string path=files.PathFor("r4.json");new JsonWorldStore().Save(path,w);
            var root=JsonNode.Parse(File.ReadAllText(path))!;Check(root["SchemaVersion"]!.GetValue<int>()==7,"Missing format guard");
            var copy=WorldStores.ForPath(path).Load(path);Check(copy.Rules.HerbPrice==9&&copy.Time!.Content!.FileHashes.Count>1,"Snapshot replaced from config");
            root["SchemaVersion"]=6;File.WriteAllText(path,root.ToJsonString());Reject(()=>new JsonWorldStore().Load(path),"Old format silently dropped content");
            var legacy=TestScenarioCatalog.Create("methods");string old=files.PathFor("old.json");new JsonWorldStore().Save(old,legacy);Check(WorldCodec.Digest(legacy)==WorldCodec.Digest(WorldStores.ForPath(old).Load(old)),"Legacy JSON changed");
        }),
        ("R4 new game retains content mode without copying live runtime state",()=>{
            var e=new SimulationEngine(Fresh());e.Submit(Train());e.AdvanceTo(600);var w=NewGameFactory.CreateLike(e.Snapshot(),"重建角色");
            Check(w.Time!.Content?.ContentVersion==ContentProfile.SupportedVersion&&w.Clock.Tick==0&&w.Time.ActionRuntime!.Actions.Count==0,"Fresh R4 mode or reset wrong");WorldIntegrity.Validate(w);
        })
    };
}
