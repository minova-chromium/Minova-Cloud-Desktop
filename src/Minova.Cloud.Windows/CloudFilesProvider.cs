using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Windows.Storage;
using Windows.Storage.Provider;
using N=Minova.Cloud.CloudFilesNative;
using FileAttributes=System.IO.FileAttributes;
namespace Minova.Cloud;

public sealed class CloudFilesProvider : IDisposable {
    readonly string root;readonly CloudApi api;readonly N.Callback fetch,cancel;
    readonly ConcurrentDictionary<long,CancellationTokenSource> transfers=new();
    long connection;bool disposed;
    public string RegistrationId {get;}
    public event Action<string>? Activity;
    public CloudFilesProvider(string root,CloudApi api,string userId,bool test=false) {
        this.root=Path.GetFullPath(root);this.api=api;fetch=OnFetch;cancel=OnCancel;
        RegistrationId=$"MinovaCloud{(test?"Test":"") }!{WindowsIdentity.GetCurrent().User!.Value}!{userId}";
    }
    public async Task Connect() {
        var drive=new DriveInfo(Path.GetPathRoot(root)!);if(drive.DriveFormat!="NTFS")throw new IOException("Choose a folder on an NTFS drive for Minova Cloud.");
        var folder=await StorageFolder.GetFolderFromPathAsync(root);
        var info=new StorageProviderSyncRootInfo {
            Id=RegistrationId,Path=folder,DisplayNameResource="Minova Cloud",IconResource=Environment.ProcessPath+",0",
            HydrationPolicy=StorageProviderHydrationPolicy.Full,HydrationPolicyModifier=StorageProviderHydrationPolicyModifier.None,
            PopulationPolicy=StorageProviderPopulationPolicy.AlwaysFull,
            InSyncPolicy=StorageProviderInSyncPolicy.FileLastWriteTime,
            Version="0.1.0",ShowSiblingsAsGroup=false,HardlinkPolicy=StorageProviderHardlinkPolicy.None,
            RecycleBinUri=new Uri(CloudApi.Origin+"/#trash")
        };
        StorageProviderSyncRootManager.Register(info);
        N.Check(N.CfConnectSyncRoot(root,[new(){Type=0,Callback=Marshal.GetFunctionPointerForDelegate(fetch)},new(){Type=2,Callback=Marshal.GetFunctionPointerForDelegate(cancel)},new(){Type=uint.MaxValue}],IntPtr.Zero,4,out connection));
    }
    static byte[] Identity(CloudItem item)=>JsonSerializer.SerializeToUtf8Bytes(item,LocalStore.Json);
    static N.Metadata Metadata(CloudItem item)=>new(){Size=item.IsDirectory?0:item.Size,Basic=new(){Creation=item.Modified.UtcDateTime.ToFileTimeUtc(),Write=item.Modified.UtcDateTime.ToFileTimeUtc(),Attributes=item.IsDirectory?0x10u:0x20u}};
    public void Create(CloudItem item) {
        var identity=Identity(item);var pointer=Marshal.AllocHGlobal(identity.Length);
        try {
            Marshal.Copy(identity,0,pointer,identity.Length);
            var target=SyncEngine.SafePath(root,item.Path);
            var values=new N.Placeholder[]{new(){Name=Path.GetFileName(target),Metadata=Metadata(item),Identity=pointer,IdentityLength=(uint)identity.Length,Flags=item.IsDirectory?3u:2u}};
            N.Check(N.CfCreatePlaceholders(Path.GetDirectoryName(target)!,values,1,0,out _));N.Check(values[0].Result);
        }finally{Marshal.FreeHGlobal(pointer);}
    }
    public void Update(CloudItem item,bool invalidate,LocalStamp? expected=null) {
        var path=SyncEngine.SafePath(root,item.Path);var identity=Identity(item);var metadata=Metadata(item);
        using var file=new N.Handle(path,3);
        if(expected!=null && SyncEngine.Stamp(path)!=expected)throw new IOException("This local file is still changing. Sync will try again.");
        uint state=N.State(path);
        if((state&1)==0)N.Check(N.CfConvertToPlaceholder(file.Value,identity,(uint)identity.Length,1,IntPtr.Zero,IntPtr.Zero));
        N.Check(N.CfUpdatePlaceholder(file.Value,ref metadata,identity,(uint)identity.Length,IntPtr.Zero,0,2u|(invalidate?5u:0u),IntPtr.Zero,IntPtr.Zero));
    }
    public static bool IsPlaceholder(string path)=>(N.State(path)&1)!=0;
    public static bool OnlineOnly(string path)=>(File.GetAttributes(path)&(FileAttributes)0x401000)!=0;
    public static bool Clean(string path)=>(N.State(path)&8)!=0;
    public static bool Pinned(string path) {
        if(!IsPlaceholder(path))return false;using var handle=new N.Handle(path);var buffer=new byte[8192];N.Check(N.CfGetPlaceholderInfo(handle.Value,0,buffer,(uint)buffer.Length,out _));return BitConverter.ToUInt32(buffer)==1;
    }
    public static CloudItem? ReadIdentity(string path) {
        if(!IsPlaceholder(path))return null;using var handle=new N.Handle(path);var buffer=new byte[8192];N.Check(N.CfGetPlaceholderInfo(handle.Value,0,buffer,(uint)buffer.Length,out _));
        int length=BitConverter.ToInt32(buffer,24);if(length<=0||length>4096)return null;
        return JsonSerializer.Deserialize<CloudItem>(buffer.AsSpan(28,length),LocalStore.Json);
    }
    public void Reidentify(CloudItem item) {
        var path=SyncEngine.SafePath(root,item.Path);if(!IsPlaceholder(path))return;var identity=Identity(item);using var handle=new N.Handle(path,3);
        N.Check(N.CfUpdateIdentity(handle.Value,IntPtr.Zero,identity,(uint)identity.Length,IntPtr.Zero,0,0,IntPtr.Zero,IntPtr.Zero));
    }
    public static void KeepOffline(string path) {using var handle=new N.Handle(path,8);N.Check(N.CfSetPinState(handle.Value,1,0,IntPtr.Zero));N.Check(N.CfHydratePlaceholder(handle.Value,0,-1,0,IntPtr.Zero));}
    public static void FreeSpace(string path) {if(!Clean(path))throw new IOException("Wait for this file to finish syncing first.");using var handle=new N.Handle(path,3);N.Check(N.CfSetPinState(handle.Value,2,0,IntPtr.Zero));N.Check(N.CfDehydratePlaceholder(handle.Value,0,-1,0,IntPtr.Zero));}
    public void ProviderStatus(bool syncing,bool offline=false){if(connection!=0)N.CfUpdateSyncProviderStatus(connection,offline?0x40u:syncing?0x10u:1u);}
    void OnCancel(IntPtr info,IntPtr _) {try{var value=Marshal.PtrToStructure<N.CallbackInfo>(info);if(transfers.TryGetValue(value.Transfer,out var source))source.Cancel();}catch(Exception){} }
    void OnFetch(IntPtr pointer,IntPtr parameters) {
        N.CallbackInfo info=default;N.FetchParameters request=default;
        try {
            info=Marshal.PtrToStructure<N.CallbackInfo>(pointer);request=Marshal.PtrToStructure<N.FetchParameters>(parameters);
            if(info.IdentityLength==0||info.IdentityLength>4096)throw new IOException("Invalid cloud file identity.");
            var bytes=new byte[info.IdentityLength];Marshal.Copy(info.Identity,bytes,0,bytes.Length);
            var item=JsonSerializer.Deserialize<CloudItem>(bytes,LocalStore.Json)??throw new IOException("Invalid cloud file.");
            _=SyncEngine.SafePath(root,item.Path);
            var source=new CancellationTokenSource(TimeSpan.FromMinutes(30));
            if(!transfers.TryAdd(info.Transfer,source)){source.Dispose();return;}
            var captured=info;var range=request;
            _=Task.Run(async()=>{
                try {
                    if(disposed)throw new OperationCanceledException();Activity?.Invoke("Downloading "+item.Name);
                    await using var output=new TransferStream(captured,item.Size);
                    await api.Download(item,output,null,source.Token);await output.Finish(source.Token);
                } catch(Exception error) {Fail(captured,range);Activity?.Invoke("Download paused · "+error.Message);}
                finally{transfers.TryRemove(captured.Transfer,out _);source.Dispose();}
            });
        }catch(Exception){Fail(info,request);}
    }
    static void Fail(N.CallbackInfo info,N.FetchParameters range) {
        var operation=Operation(info);var parameters=new N.TransferParameters {Size=40,Status=unchecked((int)0xC0000001),Offset=range.Offset,Length=range.Length};
        N.CfExecute(operation,ref parameters);
    }
    static N.Operation Operation(N.CallbackInfo info)=>new(){Size=(uint)Marshal.SizeOf<N.Operation>(),Type=0,Connection=info.Connection,Transfer=info.Transfer,Request=info.Request};
    sealed class TransferStream(N.CallbackInfo info,long size) : Stream {
        readonly byte[] buffer=new byte[1024*1024];int count;long offset;
        void Transfer(int length) {
            var pin=GCHandle.Alloc(buffer,GCHandleType.Pinned);
            try{var operation=Operation(info);var parameters=new N.TransferParameters {Size=40,Buffer=pin.AddrOfPinnedObject(),Offset=offset,Length=length};N.Check(N.CfExecute(operation,ref parameters));offset+=length;}
            finally{pin.Free();}
        }
        public async Task Finish(CancellationToken ct){ct.ThrowIfCancellationRequested();if(count>0){Transfer(count);count=0;}if(size==0){Transfer(0);}await Task.CompletedTask;}
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> data,CancellationToken ct=default) {
            while(!data.IsEmpty){ct.ThrowIfCancellationRequested();int length=Math.Min(buffer.Length-count,data.Length);data.Span[..length].CopyTo(buffer.AsSpan(count));count+=length;data=data[length..];if(count==buffer.Length){Transfer(count);count=0;}}
            return ValueTask.CompletedTask;
        }
        public override void Write(byte[] data,int start,int length)=>WriteAsync(data.AsMemory(start,length)).GetAwaiter().GetResult();
        public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;public override long Length=>size;public override long Position {get=>offset+count;set=>throw new NotSupportedException();}
        public override void Flush(){} public override int Read(byte[] a,int b,int c)=>throw new NotSupportedException();public override long Seek(long a,SeekOrigin b)=>throw new NotSupportedException();public override void SetLength(long a)=>throw new NotSupportedException();
    }
    public void Dispose() {disposed=true;foreach(var source in transfers.Values)source.Cancel();if(connection!=0){N.CfDisconnectSyncRoot(connection);connection=0;}GC.KeepAlive(fetch);GC.KeepAlive(cancel);}
    public void UnregisterForTest()=>StorageProviderSyncRootManager.Unregister(RegistrationId);
}
