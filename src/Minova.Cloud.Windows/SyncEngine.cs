using System.Security.Cryptography;
namespace Minova.Cloud;

public sealed class SyncEngine : IDisposable {
    public const string Marker=".minova-sync-root";
    readonly string root,account,ledgerFile;readonly CloudApi api;readonly LocalStore store;
    readonly SemaphoreSlim gate=new(1,1);readonly CancellationTokenSource lifetime=new();
    readonly SemaphoreSlim wake=new(0,1);FileSystemWatcher? watcher;
    readonly CloudFilesProvider provider;SyncLedger ledger;Task? loop;bool paused;
    public string Root=>root;
    public event Action<SyncStatus>? Status;
    public event Action<string>? Activity;
    public bool Paused=>paused;
    public bool IsBusy=>gate.CurrentCount==0||wake.CurrentCount>0||provider.IsTransferring;
    public SyncEngine(string root,CloudApi api,LocalStore store,bool test=false) {
        this.root=Path.GetFullPath(root);this.api=api;this.store=store;
        account=store.AccountPath(api.Session!.User.Id);ledgerFile=Path.Combine(account,"sync.json");
        ledger=store.Read<SyncLedger>(ledgerFile)??new();ledger.Items=new(ledger.Items,StringComparer.OrdinalIgnoreCase);ledger.Pins=new(ledger.Pins,StringComparer.OrdinalIgnoreCase);ledger.DefaultFolders=new(ledger.DefaultFolders,StringComparer.OrdinalIgnoreCase);
        provider=new(root,api,api.Session.User.Id,test);provider.Activity+=message=>Activity?.Invoke(message);
    }
    public async Task Connect(bool firstConnection) {
        if(firstConnection && !File.Exists(ledgerFile)) {
            if(Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())throw new IOException("Choose a new, empty folder for Minova Cloud.");
            Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,Marker),ledger.RootKey);File.SetAttributes(Path.Combine(root,Marker),FileAttributes.Hidden|FileAttributes.System);Save();
        }
        VerifyRoot();await provider.Connect();watcher=new(root){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.FileName|NotifyFilters.DirectoryName|NotifyFilters.Size|NotifyFilters.LastWrite|NotifyFilters.Attributes};
        watcher.Changed+=(_,_)=>Wake();watcher.Created+=(_,_)=>Wake();watcher.Deleted+=(_,_)=>Wake();watcher.Renamed+=(_,_)=>Wake();watcher.Error+=(_,_)=>Wake();watcher.EnableRaisingEvents=true;
    }
    void Wake(){try{if(wake.CurrentCount==0)wake.Release();}catch(SemaphoreFullException){} }
    public void Start(){loop=Task.Run(async()=>{while(!lifetime.IsCancellationRequested){if(!paused)try{await SyncOnce(lifetime.Token);}catch(OperationCanceledException) when(lifetime.IsCancellationRequested){break;}catch(ApiException error) when(error.Status==401){paused=true;provider.ProviderStatus(false,true);Status?.Invoke(new("Sign in again","Your Minova connection expired or was disconnected. Your local files are kept.",Error:true));}catch(Exception error){provider.ProviderStatus(false,true);Status?.Invoke(new("Sync paused",error is HttpRequestException?"Waiting for a connection to Minova Cloud. Retrying automatically.":error.Message,Error:true));}try{if(await wake.WaitAsync(TimeSpan.FromSeconds(15),lifetime.Token))await Task.Delay(1000,lifetime.Token);}catch(OperationCanceledException){break;}}});}
    public void TogglePause(){paused=!paused;provider.ProviderStatus(false,paused);Status?.Invoke(new(paused?"Sync paused":"Resuming sync",paused?"Your changes will wait until you resume.":"Checking your library…"));}
    void Save()=>store.Write(ledgerFile,ledger);
    void VerifyRoot() {
        if(!Directory.Exists(root)||!File.Exists(Path.Combine(root,Marker))||File.ReadAllText(Path.Combine(root,Marker))!=ledger.RootKey)throw new IOException("The sync folder is missing or was replaced. Reconnect its drive; no cloud files have been deleted.");
        var attributes=File.GetAttributes(root);if((attributes&FileAttributes.ReparsePoint)!=0 && (CloudFilesNative.State(root)&2)==0)throw new IOException("The sync folder cannot be a link.");
    }
    public static string SafePath(string root,string relative) {
        if(string.IsNullOrWhiteSpace(relative)||relative.Length>2000||relative.StartsWith('/')||relative.Contains('\\')||Path.IsPathRooted(relative))throw new IOException("Invalid cloud file path.");
        foreach(var part in relative.Split('/'))if(part.Length==0||part is "." or ".."||part.EndsWith('.')||part.EndsWith(' ')||part.IndexOfAny(Path.GetInvalidFileNameChars())>=0||part.Equals(Marker,StringComparison.OrdinalIgnoreCase)||System.Text.RegularExpressions.Regex.IsMatch(part,@"^(con|prn|aux|nul|com[0-9]|lpt[0-9])(?:\.|$)",System.Text.RegularExpressions.RegexOptions.IgnoreCase))throw new IOException("Unsupported cloud file name.");
        var target=Path.GetFullPath(Path.Combine(root,relative.Replace('/','\\')));
        if(!target.StartsWith(Path.GetFullPath(root).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("Cloud file escaped the sync folder.");
        for(var current=Path.GetDirectoryName(target);current!=null && !current.Equals(root,StringComparison.OrdinalIgnoreCase);current=Path.GetDirectoryName(current))
            if(Directory.Exists(current) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0 && !CloudFilesProvider.IsPlaceholder(current))throw new IOException("Linked folders are not synced.");
        return target;
    }
    static bool Exists(string path)=>File.Exists(path)||Directory.Exists(path);
    public static LocalStamp Stamp(string path){var value=new FileInfo(path);return new(value.Length,value.LastWriteTimeUtc.Ticks);}
    static bool Changed(string path,TrackedItem tracked) {
        if(tracked.Remote.IsDirectory)return false;
        if(CloudFilesProvider.OnlineOnly(path))return false;
        return Stamp(path)!=tracked.Local || (CloudFilesProvider.IsPlaceholder(path)&&!CloudFilesProvider.Clean(path));
    }
    Dictionary<string,bool> LocalFiles() {
        var result=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
        void Scan(string directory) {
            foreach(var path in Directory.EnumerateFileSystemEntries(directory)) {
                var relative=Path.GetRelativePath(root,path).Replace('\\','/');if(relative==Marker)continue;
                _=SafePath(root,relative);var attributes=File.GetAttributes(path);
                if((attributes&FileAttributes.ReparsePoint)!=0 && !CloudFilesProvider.IsPlaceholder(path))throw new IOException("Remove the linked item from the sync folder before continuing: "+relative);
                bool folder=(attributes&FileAttributes.Directory)!=0;result.Add(relative,folder);if(result.Count>20000)throw new IOException("This version supports up to 20,000 local items.");if(folder)Scan(path);
            }
        }
        Scan(root);return result;
    }
    void Track(CloudItem item) {var path=SafePath(root,item.Path);ledger.Items[item.Path]=new(item,item.IsDirectory?null:Stamp(path));Save();}
    string ConflictPath(string relative) {
        var directory=Path.GetDirectoryName(relative.Replace('/','\\'));var name=Path.GetFileNameWithoutExtension(relative);var extension=Path.GetExtension(relative);
        name=name[..Math.Min(name.Length,110)];var device=string.Concat(Environment.MachineName.Where(c=>char.IsLetterOrDigit(c)||c=='-'));
        var suffix=$" (conflict {device[..Math.Min(device.Length,20)]} {DateTime.Now:yyyyMMdd-HHmmss})";
        var candidate=(string.IsNullOrEmpty(directory)?"":directory.Replace('\\','/')+"/")+name+suffix+extension;
        for(int number=2;Exists(SafePath(root,candidate));number++)candidate=(string.IsNullOrEmpty(directory)?"":directory.Replace('\\','/')+"/")+name+suffix+"-"+number+extension;
        return candidate;
    }
    void PreserveConflict(string relative) {
        // An atomic rename preserves writes through any already-open handles too.
        var target=ConflictPath(relative);File.Move(SafePath(root,relative),SafePath(root,target));Activity?.Invoke("Kept both versions · "+target);
    }
    void LocalParents(string relative) {var parent=Path.GetDirectoryName(SafePath(root,relative));if(parent!=null)Directory.CreateDirectory(parent);}
    async Task EnsureRemoteParents(string relative,Dictionary<string,CloudItem> remote,CancellationToken ct) {
        var parts=relative.Split('/');var parent="";
        for(int i=0;i<parts.Length-1;i++){parent=parent.Length==0?parts[i]:parent+"/"+parts[i];if(remote.ContainsKey(parent))continue;try{var value=await api.Folder(parent,ct);remote[parent]=value.GetProperty("item").Deserialize<CloudItem>(LocalStore.Json)!;}catch(ApiException error) when(error.Status==409){remote[parent]=await api.Item(parent,ct);}Track(remote[parent]);}
    }
    void FinishMove(Dictionary<string,CloudItem> remote) {
        if(ledger.Move==null)return;var move=ledger.Move;
        if(remote.ContainsKey(move.Source)&&remote.ContainsKey(move.Destination))throw new IOException("A moved file changed on another device. Keep both locations until the next check: "+move.Source);
        if(!remote.ContainsKey(move.Source)&&remote.ContainsKey(move.Destination)) {
            foreach(var pair in ledger.Items.Where(x=>x.Key.Equals(move.Source,StringComparison.OrdinalIgnoreCase)||x.Key.StartsWith(move.Source+"/",StringComparison.OrdinalIgnoreCase)).ToArray()) {
                var destination=move.Destination+pair.Key[move.Source.Length..];var changed=pair.Value.Remote with {Path=destination,Name=Path.GetFileName(destination)};
                if(remote.TryGetValue(destination,out var actual) && (actual.Revision==changed.Revision || (pair.Key.Equals(move.Source,StringComparison.OrdinalIgnoreCase) && pair.Value.Remote.Revision==move.Revision && actual.Size==changed.Size && actual.Modified==changed.Modified)))changed=actual;
                ledger.Items.Remove(pair.Key);ledger.Items[destination]=pair.Value with {Remote=changed};
                if(Exists(SafePath(root,destination)))provider.Reidentify(changed);
            }
            Activity?.Invoke("Moved in Cloud · "+move.Destination);
        }
        ledger.Move=null;Save();
    }
    async Task InitializeDefaultFolders(Dictionary<string,CloudItem> remote,Dictionary<string,bool> local,CancellationToken ct) {
        foreach(var name in new[]{"Files","Music","Videos","Pictures"}) {
            if(ledger.DefaultFolders.Contains(name))continue;
            // Respect an existing library and any already-tracked deletions or moves.
            if(remote.TryGetValue(name,out var existing) || ledger.Items.ContainsKey(name) || (local.TryGetValue(name,out bool directory) && !directory)) {
                if((existing!=null && !existing.IsDirectory) || (local.TryGetValue(name,out var folder) && !folder))Activity?.Invoke("Default folder skipped · a file already uses the name "+name);
                ledger.DefaultFolders.Add(name);Save();continue;
            }
            try {
                var response=await api.Folder(name,ct);remote[name]=response.GetProperty("item").Deserialize<CloudItem>(LocalStore.Json)!;
            }catch(ApiException error) when(error.Status==409) {
                var item=await api.Item(name,ct);remote[item.Path]=item;
                if(!item.IsDirectory)Activity?.Invoke("Default folder skipped · a file already uses the name "+name);
            }
            ledger.DefaultFolders.Add(name);Save();
        }
    }
    public async Task SyncOnce(CancellationToken ct=default) {
        await gate.WaitAsync(ct);
        try {
            VerifyRoot();Status?.Invoke(new("Syncing","Checking files across your devices…",ledger.Items.Count));provider.ProviderStatus(true);
            var snapshot=await api.Snapshot(ct);var remote=snapshot.Items.ToDictionary(x=>x.Path,StringComparer.OrdinalIgnoreCase);
            foreach(var item in remote.Values)_=SafePath(root,item.Path);
            var initialLocal=LocalFiles(); // Complete and validate the local scan before considering any deletions.
            await InitializeDefaultFolders(remote,initialLocal,ct);
            FinishMove(remote);
            foreach(var pair in initialLocal.OrderBy(x=>x.Key.Count(c=>c=='/')).ToArray()) {
                if(remote.ContainsKey(pair.Key))continue;var identity=CloudFilesProvider.ReadIdentity(SafePath(root,pair.Key));
                if(identity==null || !ledger.Items.ContainsKey(identity.Path) || Exists(SafePath(root,identity.Path)) || !remote.TryGetValue(identity.Path,out var original))continue;
                await EnsureRemoteParents(pair.Key,remote,ct);ledger.Move=new(identity.Path,pair.Key,original.Revision);Save();
                await api.Request<JsonElement>("/api/move",HttpMethod.Post,new {path=identity.Path,destination=pair.Key,revision=original.Revision},ct);
                remote=(await api.Snapshot(ct)).Items.ToDictionary(x=>x.Path,StringComparer.OrdinalIgnoreCase);FinishMove(remote);
            }
            foreach(var pair in ledger.Items.ToArray().Where(x=>!remote.ContainsKey(x.Key)).OrderByDescending(x=>x.Key.Count(c=>c=='/')).ThenBy(x=>x.Value.Remote.IsDirectory)) {
                var path=SafePath(root,pair.Key);
                if(Exists(path)) {
                    if(pair.Value.Remote.IsDirectory){if(!Directory.EnumerateFileSystemEntries(path).Any())Directory.Delete(path);}
                    else if(!Changed(path,pair.Value)) {
                        if(CloudFilesProvider.OnlineOnly(path))File.Delete(path);
                        else {var recovery=Path.Combine(account,"Recovered files",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]);Directory.CreateDirectory(recovery);File.Move(path,Path.Combine(recovery,Path.GetFileName(path)));}
                        Activity?.Invoke("Removed from Cloud · "+pair.Key);
                    }
                }
                ledger.Items.Remove(pair.Key);Save();
            }
            foreach(var item in remote.Values.OrderBy(x=>x.Path.Count(c=>c=='/')).ThenByDescending(x=>x.IsDirectory).ToArray()) {
                ct.ThrowIfCancellationRequested();var path=SafePath(root,item.Path);ledger.Items.TryGetValue(item.Path,out var baseline);
                if(Exists(path) && Directory.Exists(path)!=item.IsDirectory)throw new IOException("A file and folder have the same name on different devices: "+item.Path);
                if(item.IsDirectory) {
                    if(Directory.Exists(path)){Track(item);continue;}
                    if(baseline!=null)continue;
                    LocalParents(item.Path);provider.Create(item);Track(item);continue;
                }
                if(!File.Exists(path)) {
                    if(baseline!=null && baseline.Remote.Revision==item.Revision)continue;
                    LocalParents(item.Path);provider.Create(item);Track(item);Activity?.Invoke("Available in Explorer · "+item.Name);continue;
                }
                if(baseline==null) {
                    // A first connection never silently overwrites an existing file.
                    if(CloudFilesProvider.OnlineOnly(path))throw new IOException("An untracked cloud placeholder needs attention: "+item.Path);
                    var stamp=Stamp(path);using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
                    var hash=Convert.ToHexStringLower(await SHA256.HashDataAsync(input,ct));input.Close();
                    var check=await api.Request<JsonElement>("/api/checksum?path="+CloudApi.Encode(item.Path),HttpMethod.Get,ct:ct);
                    if(hash!=check.GetProperty("sha256").GetString()){PreserveConflict(item.Path);provider.Create(item);}else provider.Update(item,false,stamp);
                    Track(item);continue;
                }
                if(baseline.Remote.Revision!=item.Revision) {
                    if(Changed(path,baseline)){PreserveConflict(item.Path);provider.Create(item);}
                    else {bool pinned=CloudFilesProvider.Pinned(path);provider.Update(item,true);if(pinned)await Task.Run(()=>CloudFilesProvider.KeepOffline(path),ct);}
                    Track(item);Activity?.Invoke("Updated from Cloud · "+item.Name);
                }
            }
            var local=LocalFiles();
            foreach(var pair in local.Where(x=>x.Value&&!remote.ContainsKey(x.Key)).OrderBy(x=>x.Key.Count(c=>c=='/')).ToArray()) {
                await EnsureRemoteParents(pair.Key,remote,ct);var value=await api.Folder(pair.Key,ct);var item=value.GetProperty("item").Deserialize<CloudItem>(LocalStore.Json)!;remote[item.Path]=item;Track(item);
            }
            foreach(var pair in local.Where(x=>!x.Value)) {
                ct.ThrowIfCancellationRequested();var path=SafePath(root,pair.Key);ledger.Items.TryGetValue(pair.Key,out var baseline);
                if(baseline!=null && !Changed(path,baseline))continue;
                if(CloudFilesProvider.OnlineOnly(path))continue;
                await EnsureRemoteParents(pair.Key,remote,ct);var directory=Path.Combine(account,"Transfers");Directory.CreateDirectory(directory);var temporary=Path.Combine(directory,Guid.NewGuid()+".upload");
                try {
                    LocalStamp stamp;using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                        stamp=Stamp(path);await using var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,1024*1024,true);await input.CopyToAsync(output,ct);await output.FlushAsync(ct);
                    }
                    Activity?.Invoke("Uploading · "+pair.Key);
                    CloudItem item;
                    try{item=await api.Upload(pair.Key,temporary,baseline?.Remote.Revision,ct);}
                    catch(ApiException error) when(error.Status==409 && File.Exists(path)) {
                        // Preserve local edits; the next complete snapshot reconciles the remote version.
                        PreserveConflict(pair.Key);throw new IOException("Both versions were kept. Sync will reconcile "+pair.Key+" on the next check.");
                    }
                    remote[item.Path]=item;
                    if(File.Exists(path)&&Stamp(path)==stamp){provider.Update(item,false,stamp);Track(item);}
                    else {ledger.Items[item.Path]=new(item,stamp);Save();}
                    Activity?.Invoke("Synced · "+pair.Key);
                }finally{File.Delete(temporary);}
            }
            // Remove only previously tracked, unchanged remote versions. Directories must be empty.
            foreach(var pair in ledger.Items.ToArray().Where(x=>!Exists(SafePath(root,x.Key))&&remote.ContainsKey(x.Key)).OrderByDescending(x=>x.Key.Count(c=>c=='/')).ThenBy(x=>x.Value.Remote.IsDirectory)) {
                var item=remote[pair.Key];if(item.Revision!=pair.Value.Remote.Revision)continue;
                if(item.IsDirectory)item=await api.Item(item.Path,ct); // Children just removed changed its directory timestamp.
                try{await api.Trash(item,item.IsDirectory,ct);ledger.Items.Remove(pair.Key);Save();Activity?.Invoke("Moved to Cloud trash · "+pair.Key);}
                catch(ApiException error) when(error.Status==409){Activity?.Invoke("Kept a changed Cloud item · "+pair.Key);}
            }
            foreach(var tracked in ledger.Items.Values.Where(x=>!x.Remote.IsDirectory).ToArray()) {
                var path=SafePath(root,tracked.Remote.Path);if(!File.Exists(path)||!CloudFilesProvider.IsPlaceholder(path))continue;
                var attributes=(uint)File.GetAttributes(path);var pin=attributes&0x180000;
                ledger.Pins.TryGetValue(tracked.Remote.Path,out var previous);
                if(pin==0x80000 && CloudFilesProvider.OnlineOnly(path))await Task.Run(()=>CloudFilesProvider.KeepOffline(path),ct);
                else if(pin==0x100000 && previous!=pin && !CloudFilesProvider.OnlineOnly(path) && CloudFilesProvider.Clean(path))await Task.Run(()=>CloudFilesProvider.FreeSpace(path),ct);
                ledger.Pins[tracked.Remote.Path]=pin;
            }
            foreach(var key in ledger.Pins.Keys.Where(x=>!ledger.Items.ContainsKey(x)).ToArray())ledger.Pins.Remove(key);Save();
            provider.ProviderStatus(false);Status?.Invoke(new("Up to date","Your files are synced across Minova Cloud.",ledger.Items.Values.Count(x=>!x.Remote.IsDirectory),DateTimeOffset.Now));
        }finally{gate.Release();}
    }
    public Task KeepOffline(string relative)=>Task.Run(()=>CloudFilesProvider.KeepOffline(SafePath(root,relative)));
    public Task FreeSpace(string relative)=>Task.Run(()=>CloudFilesProvider.FreeSpace(SafePath(root,relative)));
    public void UnregisterForTest()=>provider.UnregisterForTest();
    public void Dispose(){lifetime.Cancel();watcher?.Dispose();provider.Dispose();}
}
