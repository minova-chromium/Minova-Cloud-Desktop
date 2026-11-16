using System.Net;
using Windows.Networking.Connectivity;
namespace Minova.Cloud;

public sealed class DesktopUpdater : IDisposable {
    const string Latest="https://github.com/"+UpdateRelease.Repository+"/releases/latest/download/release.json";
    readonly LocalStore store;readonly Func<bool> canApply;readonly HttpClient http;readonly string publicKey;readonly Version current;
    readonly Action<string,bool>? installOverride;readonly SemaphoreSlim gate=new(1,1);readonly CancellationTokenSource lifetime=new();
    UpdateRelease? ready;string? readyFile;DateTimeOffset lastCheck=DateTimeOffset.MinValue,idleSince=DateTimeOffset.MaxValue;bool requested,installing;
    public event Action<string>? StatusChanged;public string Status {get;private set;}="Automatic updates are enabled.";
    public string? AvailableVersion {get;private set;}
    public bool Ready=>ready!=null;
    public DesktopUpdater(LocalStore store,Func<bool> canApply):this(store,canApply,null,null,null,null){}
    internal DesktopUpdater(LocalStore store,Func<bool> canApply,HttpMessageHandler? handler,string? key,Version? version,Action<string,bool>? install) {
        this.store=store;this.canApply=canApply;publicKey=key??UpdateManifest.PublicKey();current=version??Version.Parse(Product.Version);installOverride=install;
        http=new HttpClient(handler??new HttpClientHandler {AllowAutoRedirect=false}) {Timeout=TimeSpan.FromMinutes(20)};http.DefaultRequestHeaders.UserAgent.ParseAdd("Minova-Cloud-Desktop-Updater/"+Product.Version);
    }
    void Report(string message){Status=message;StatusChanged?.Invoke(message);}
    public void Start(){_ =Task.Run(async()=>{try{await Task.Delay(TimeSpan.FromSeconds(30),lifetime.Token);while(!lifetime.IsCancellationRequested){if(DateTimeOffset.UtcNow-lastCheck>TimeSpan.FromHours(6))await Check(false);await TryInstall(false);await Task.Delay(TimeSpan.FromSeconds(10),lifetime.Token);}}catch(OperationCanceledException) when(lifetime.IsCancellationRequested){} });}
    public static bool SafeUrl(Uri url)=>url.Scheme=="https"&&url.IsDefaultPort&&string.IsNullOrEmpty(url.UserInfo)&&url.Host is "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com";
    async Task<HttpResponseMessage> Open(string path,CancellationToken ct) {
        var url=new Uri(path);
        for(int count=0;count<6;count++) {
            if(!SafeUrl(url))throw new IOException("The update download left the trusted release service.");
            using var request=new HttpRequestMessage(HttpMethod.Get,url);var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
            if((int)response.StatusCode is >=300 and <400){var location=response.Headers.Location;response.Dispose();if(location==null)throw new IOException("The update redirect was incomplete.");url=location.IsAbsoluteUri?location:new Uri(url,location);continue;}
            if(!response.IsSuccessStatusCode){var status=(int)response.StatusCode;response.Dispose();throw new IOException("Update service unavailable ("+status+"). Your current app is kept.");}return response;
        }throw new IOException("Too many update redirects.");
    }
    async Task<byte[]> Small(string path,int maximum,CancellationToken ct) {
        using var response=await Open(path,ct);await using var input=await response.Content.ReadAsStreamAsync(ct);using var output=new MemoryStream();var bytes=new byte[8192];int count;
        while((count=await input.ReadAsync(bytes,ct))>0){if(output.Length+count>maximum)throw new IOException("Update metadata is too large.");output.Write(bytes,0,count);}return output.ToArray();
    }
    static bool Metered(){try{var profile=NetworkInformation.GetInternetConnectionProfile();if(profile==null)return false;var cost=profile.GetConnectionCost();return cost.Roaming||cost.OverDataLimit||cost.NetworkCostType is NetworkCostType.Fixed or NetworkCostType.Variable;}catch(Exception){return false;}}
    public async Task Check(bool manual) {
        if(!await gate.WaitAsync(0,lifetime.Token))return;
        try {
            lastCheck=DateTimeOffset.UtcNow;Report("Checking for updates…");
            var raw=await Small(Latest,65536,lifetime.Token);
            // Version is used only to locate its detached signature; it is not trusted until RSA verification succeeds.
            using var untrusted=JsonDocument.Parse(Encoding.UTF8.GetString(raw).TrimStart('\uFEFF'));var text=untrusted.RootElement.GetProperty("version").GetString()??"";
            if(!System.Text.RegularExpressions.Regex.IsMatch(text,@"^\d{1,6}\.\d{1,6}\.\d{1,6}$"))throw new IOException("Invalid update version.");
            if(Version.Parse(text)<=current){AvailableVersion=null;Report("You are up to date - "+Product.Version);return;}
            var signature=await Small(new UpdateRelease(text,"","").Asset("release.json.sig"),8192,lifetime.Token);var release=UpdateManifest.Verify(raw,signature,publicKey);
            if(Version.Parse(release.Version)<=current){AvailableVersion=null;Report("You're up to date · "+Product.Version);return;}
            AvailableVersion=release.Version;
            if(!manual&&!store.LoadPreferences().AutoUpdates){Report("Version "+release.Version+" is available. Automatic installation is off.");return;}
            if(!manual&&Metered()){Report("Version "+release.Version+" is available. Waiting for an unmetered connection.");return;}
            string directory=Path.Combine(store.DirectoryPath,"Updates",release.Version);Directory.CreateDirectory(directory);var path=Path.Combine(directory,"Minova-Cloud-Setup.exe");
            bool valid=false;if(File.Exists(path)){try{await UpdateManifest.VerifyFile(path,release.Sha256,lifetime.Token);valid=true;}catch(IOException){}}
            if(!valid) {
                Report("Downloading Minova Cloud "+release.Version+"…");string partial=path+".part";
                try{using var response=await Open(release.Asset("Minova-Cloud-Setup.exe"),lifetime.Token);if(response.Content.Headers.ContentLength>512L*1024*1024)throw new IOException("The desktop update is too large.");await using(var input=await response.Content.ReadAsStreamAsync(lifetime.Token))await using(var output=new FileStream(partial,FileMode.Create,FileAccess.Write,FileShare.None,1024*1024,true)){byte[] bytes=new byte[1024*1024];long total=0;int count;while((count=await input.ReadAsync(bytes,lifetime.Token))>0){total+=count;if(total>512L*1024*1024)throw new IOException("The desktop update is too large.");await output.WriteAsync(bytes.AsMemory(0,count),lifetime.Token);}}await UpdateManifest.VerifyFile(partial,release.Sha256,lifetime.Token);File.Move(partial,path,true);}
                finally{if(File.Exists(partial))File.Delete(partial);}
            }
            File.WriteAllBytes(Path.Combine(directory,"release.json"),raw);File.WriteAllBytes(Path.Combine(directory,"release.json.sig"),signature);ready=release;readyFile=path;idleSince=DateTimeOffset.MaxValue;Report("Version "+release.Version+" is ready. It will install when sync is idle.");
        }catch(OperationCanceledException) when(lifetime.IsCancellationRequested){}catch(Exception error){Report("Update paused · "+error.Message);}finally{gate.Release();}
    }
    public async Task InstallReady(){requested=true;await TryInstall(true);}
    async Task TryInstall(bool manual) {
        if(ready==null||readyFile==null||installing||(!requested&&!store.LoadPreferences().AutoUpdates))return;
        if(!canApply()){idleSince=DateTimeOffset.MaxValue;Report("Update ready · waiting for Minova Cloud to be idle.");return;}
        if(!manual){if(idleSince==DateTimeOffset.MaxValue){idleSince=DateTimeOffset.UtcNow;return;}if(DateTimeOffset.UtcNow-idleSince<TimeSpan.FromSeconds(5))return;}
        if(!await gate.WaitAsync(0,lifetime.Token))return;
        try {
            await UpdateManifest.VerifyFile(readyFile,ready.Sha256,lifetime.Token);installing=true;Report("Installing Minova Cloud "+ready.Version+". Your files and sign-in are kept.");
            if(installOverride!=null)installOverride(readyFile,!manual);
            else {var start=new ProcessStartInfo(readyFile){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};start.ArgumentList.Add("--silent");if(!manual)start.ArgumentList.Add("--background");if(Process.Start(start)==null)throw new IOException("The update installer could not start.");}
        }catch(Exception error){installing=false;Report("Update paused · "+error.Message);}finally{gate.Release();}
    }
    public void Dispose(){lifetime.Cancel();http.Dispose();}
}
