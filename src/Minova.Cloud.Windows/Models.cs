namespace Minova.Cloud;

public static class Product {public static string Version=>typeof(Product).Assembly.GetName().Version!.ToString(3);}

public sealed record CloudUser(string Id, string Username, string Name, string Role);
public sealed record Session(string Id, string Token, string Csrf, long Expires, CloudUser User);
public sealed record CloudItem(string Name, string Path, bool IsDirectory, long Size, DateTimeOffset Modified, string Revision);
public sealed record Snapshot(List<CloudItem> Items, bool Complete);
public sealed record LocalStamp(long Size, long WriteTicks);
public sealed record TrackedItem(CloudItem Remote, LocalStamp? Local);
public sealed class SyncLedger {
    public string RootKey {get;set;} = Guid.NewGuid().ToString("N");
    public Dictionary<string, TrackedItem> Items {get;set;} = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, uint> Pins {get;set;} = new(StringComparer.OrdinalIgnoreCase);
    public PendingMove? Move {get;set;}
    public HashSet<string> DefaultFolders {get;set;} = new(StringComparer.OrdinalIgnoreCase);
}
public sealed record PendingMove(string Source,string Destination,string Revision);
public sealed class Preferences {
    public string? Root {get;set;}
    public string? UserId {get;set;}
    public bool Startup {get;set;} = true;
    public bool AutoUpdates {get;set;} = true;
}
public sealed record SyncStatus(string Title, string Detail, int Files = 0, DateTimeOffset? LastSync = null, bool Error = false);
public sealed class ApiException(int status, string message) : IOException(message) { public int Status {get;} = status; }
