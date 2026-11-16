using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
namespace Minova.Cloud;

public sealed class CloudApi : IDisposable {
    public const string Origin="https://cloud.project-minova.com";
    private readonly HttpClient http;
    public Session? Session {get; private set;}
    public CloudApi(Session? session=null, string? testOrigin=null) {
        var origin=Origin;
#if DEBUG
        if(testOrigin!=null && Uri.TryCreate(testOrigin,UriKind.Absolute,out var uri) && uri.IsLoopback && uri.Scheme=="http")origin=testOrigin;
#endif
        http=new(new HttpClientHandler {AllowAutoRedirect=false,UseCookies=false}) {BaseAddress=new Uri(origin),Timeout=TimeSpan.FromMinutes(20)};
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Minova-Cloud-Windows/0.1.0");Session=session;
    }
    public static string Encode(string path)=>Uri.EscapeDataString(path);
    private HttpRequestMessage Message(string route,HttpMethod method, object? body=null, bool auth=true) {
        if(!route.StartsWith("/api/",StringComparison.Ordinal))throw new IOException("Invalid Cloud request.");
        var message=new HttpRequestMessage(method,route);
        if(body!=null)message.Content=JsonContent.Create(body,options:LocalStore.Json);
        if(auth && Session!=null){message.Headers.Authorization=new AuthenticationHeaderValue("Bearer",Session.Token);message.Headers.Add("X-Minova-CSRF",Session.Csrf);}
        return message;
    }
    private static async Task Check(HttpResponseMessage response,CancellationToken ct) {
        if(response.IsSuccessStatusCode)return;
        var message="Minova Cloud could not complete this request.";
        try {using var value=await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct),cancellationToken:ct);message=value.RootElement.GetProperty("error").GetString()??message;}catch(JsonException){}catch(KeyNotFoundException){}
        throw new ApiException((int)response.StatusCode,message);
    }
    public async Task<T> Request<T>(string route,HttpMethod method,object? body=null,CancellationToken ct=default) {
        using var request=Message(route,method,body);using var response=await http.SendAsync(request,ct);await Check(response,ct);
        return (await response.Content.ReadFromJsonAsync<T>(LocalStore.Json,ct)) ?? throw new IOException("Cloud returned an empty response.");
    }
    public async Task<Session> Login(string username,string password,CancellationToken ct=default) {
        using var request=Message("/api/login",HttpMethod.Post,new {username,password},false);
        using var response=await http.SendAsync(request,ct);await Check(response,ct);
        using var json=await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct),cancellationToken:ct);
        var cookie=response.Headers.GetValues("Set-Cookie").First().Split(';')[0];
        var csrf=json.RootElement.GetProperty("csrf").GetString()!;
        try {
            using var device=Message("/api/devices",HttpMethod.Post,new {name=Environment.MachineName+" · Windows"},false);
            device.Headers.Add("Cookie",cookie);device.Headers.Add("X-Minova-CSRF",csrf);
            using var connected=await http.SendAsync(device,ct);await Check(connected,ct);
            Session=await connected.Content.ReadFromJsonAsync<Session>(LocalStore.Json,ct) ?? throw new IOException("Could not connect this PC.");return Session;
        } finally {
            using var logout=Message("/api/logout",HttpMethod.Post,auth:false);logout.Headers.Add("Cookie",cookie);logout.Headers.Add("X-Minova-CSRF",csrf);
            try{using var ignored=await http.SendAsync(logout,CancellationToken.None);}catch(HttpRequestException){}
        }
    }
    public async Task<Snapshot> Snapshot(CancellationToken ct) {
        var value=await Request<Snapshot>("/api/sync",HttpMethod.Get,ct:ct);
        if(!value.Complete)throw new IOException("The Cloud library listing is incomplete. Sync will try again.");return value;
    }
    public async Task<CloudItem> Item(string path,CancellationToken ct) {
        // /api/files lists a directory's children; find a directory through its parent instead.
        var parent=path.Contains('/')?path[..path.LastIndexOf('/')]:"";
        var listing=await Request<JsonElement>("/api/files?path="+Encode(parent),HttpMethod.Get,ct:ct);
        return listing.GetProperty("items").EnumerateArray().Select(x=>x.Deserialize<CloudItem>(LocalStore.Json)!).Single(x=>x.Path.Equals(path,StringComparison.OrdinalIgnoreCase));
    }
    public Task<JsonElement> Folder(string path,CancellationToken ct)=>Request<JsonElement>("/api/folders",HttpMethod.Post,new {path},ct);
    public Task<JsonElement> Trash(CloudItem item,bool empty,CancellationToken ct)=>Request<JsonElement>("/api/trash",HttpMethod.Post,new {path=item.Path,revision=item.Revision,empty},ct);
    public async Task Download(CloudItem item, Stream output, IProgress<long>? progress,CancellationToken ct) {
        using var request=Message("/api/download?path="+Encode(item.Path)+"&revision="+Encode(item.Revision),HttpMethod.Get);
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);await Check(response,ct);
        await using var input=await response.Content.ReadAsStreamAsync(ct);var buffer=new byte[1024*1024];long received=0;int read;
        while((read=await input.ReadAsync(buffer,ct))>0){await output.WriteAsync(buffer.AsMemory(0,read),ct);received+=read;progress?.Report(received);}
        if(received!=item.Size)throw new IOException("The downloaded file was incomplete.");
    }
    public async Task<CloudItem> Upload(string path,string file,string? revision,CancellationToken ct) {
        await using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,true);
        var checksum=Convert.ToHexStringLower(await SHA256.HashDataAsync(stream,ct));stream.Position=0;
        var upload=await Request<JsonElement>("/api/uploads",HttpMethod.Post,new {path,size=stream.Length,sha256=checksum,replaceRevision=revision},ct);
        // Omit replaceRevision entirely for new files: explicit null is not a replacement request.
        string id=upload.GetProperty("id").GetString()!;bool completed=false;
        try {
            var buffer=new byte[upload.GetProperty("chunkSize").GetInt32()];long offset=0;
            while(offset<stream.Length){ct.ThrowIfCancellationRequested();int count=await stream.ReadAsync(buffer,ct);if(count==0)throw new IOException("The upload snapshot became incomplete.");
                for(int attempt=0;;attempt++) {
                    try{using var request=Message("/api/uploads/"+id+"?offset="+offset,HttpMethod.Put);request.Content=new ByteArrayContent(buffer,0,count);request.Content.Headers.ContentType=new MediaTypeHeaderValue("application/octet-stream");using var response=await http.SendAsync(request,ct);await Check(response,ct);offset+=count;break;}
                    catch(Exception error) when(attempt<3 && (error is HttpRequestException || error is ApiException {Status:409 or 429 or >=500})) {
                        await Task.Delay(TimeSpan.FromSeconds(attempt+1),ct);var status=await Request<JsonElement>("/api/uploads/"+id,HttpMethod.Get,ct:ct);var received=status.GetProperty("received").GetInt64();
                        if(received==offset+count){offset=received;break;}if(received!=offset)throw new IOException("The upload position changed. Sync will restart this file.");
                    }
                }
            }
            var done=await Request<JsonElement>("/api/uploads/"+id+"/complete",HttpMethod.Post,new {},ct);completed=true;return done.GetProperty("item").Deserialize<CloudItem>(LocalStore.Json)!;
        } finally {if(!completed)try{await Request<JsonElement>("/api/uploads/"+id,HttpMethod.Delete,ct:CancellationToken.None);}catch(Exception){} }
    }
    public void Dispose()=>http.Dispose();
}
