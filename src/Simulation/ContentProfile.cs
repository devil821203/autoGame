using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class ContentProfile
{
    // Format/compatibility identifiers, not gameplay parameters.
    public const int SchemaVersion=1;
    public const string SupportedVersion="r8-1";
    private static bool Supported(string version)=>version is "r4-1" or "r5-1" or "r6a-1" or "r6b-1" or SupportedVersion;
    public static void EnableFresh(WorldState w,GameConfiguration? configuration=null)
    {
        if(w.Time?.EventDecisions is null||w.Time.Content is not null||w.Clock.Tick!=0||w.Time.DiscreteEvents!.WorldTime!=0||w.Pending.Count!=0||w.Time.Charges.Count!=0)
            throw new InvalidOperationException("R4 content identity requires a fresh R3 world; old files are not converted");
        w.Time.Content=(configuration??GameConfiguration.Find()).CaptureContent();Validate(w);
    }
    internal static ContentManifest Capture(GameConfiguration config)
    {
        var identity=config.Read<ContentIdentity>("content/manifest.json");
        if(identity.SchemaVersion!=SchemaVersion||!Supported(identity.ContentVersion))throw new InvalidDataException("config/content/manifest.json: unsupported SchemaVersion/ContentVersion");
        var hashes=new SortedDictionary<string,string>(StringComparer.Ordinal);
        foreach(var path in Directory.EnumerateFiles(config.DirectoryPath,"*.json",SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            byte[] bytes=File.ReadAllBytes(path);
            try{using var reader=new StreamReader(new MemoryStream(bytes),Encoding.UTF8,detectEncodingFromByteOrderMarks:true);using var json=JsonDocument.Parse(reader.ReadToEnd());}
            catch(JsonException error){throw new InvalidDataException($"Invalid config JSON: {Path.GetRelativePath(config.DirectoryPath,path)}",error);}
            hashes.Add(Path.GetRelativePath(config.DirectoryPath,path).Replace('\\','/'),Convert.ToHexString(SHA256.HashData(bytes)));
        }
        return new(identity.SchemaVersion,identity.ContentVersion,hashes);
    }
    public static string Fingerprint(ContentManifest content)=>Hash(JsonSerializer.Serialize(content with {FileHashes=new(content.FileHashes,StringComparer.Ordinal)}));
    public static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static void Validate(WorldState w)
    {
        if(w.Time?.Content is not {} content)return;
        if(w.Time.EventDecisions is null||content.SchemaVersion!=SchemaVersion||!Supported(content.ContentVersion)||content.FileHashes is null||!content.FileHashes.ContainsKey("content/manifest.json")||
            content.FileHashes.Any(p=>string.IsNullOrWhiteSpace(p.Key)||p.Key.Contains('\\')||p.Key.StartsWith('/')||p.Key.Split('/').Any(s=>s is "" or "." or "..")||!p.Key.EndsWith(".json",StringComparison.Ordinal)||p.Value is null||p.Value.Length!=64||p.Value.Any(c=>!char.IsAsciiHexDigit(c))))
            throw new InvalidDataException("Invalid or unsupported R4 content identity/hash manifest");
    }
}
