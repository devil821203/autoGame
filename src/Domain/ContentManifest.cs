namespace Cultivation.Domain;

public sealed record ContentIdentity(int SchemaVersion, string ContentVersion);
public sealed record ContentManifest(int SchemaVersion, string ContentVersion, SortedDictionary<string,string> FileHashes);
