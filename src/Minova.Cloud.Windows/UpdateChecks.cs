#if DEBUG
using System.Net;
using System.Security.Cryptography;
namespace Minova.Cloud;
internal static class UpdateChecks {
    static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Reject(Action action){try{action();}catch(IOException){return;}throw new Exception("Unsafe update accepted");}
    sealed class Transport(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(respond(request));
    }
    public static async Task Run(string report) {
        File.WriteAllText(report,"");void Pass(string message)=>File.AppendAllText(report,"PASS "+message+Environment.NewLine);
        using var key=RSA.Create(3072);var publicKey=key.ExportSubjectPublicKeyInfoPem();byte[] payload=Encoding.UTF8.GetBytes("Synthetic installer fixture; never execute.");
        string hash=Convert.ToHexStringLower(SHA256.HashData(payload));
        byte[] Manifest(string platform="windows-x64",string version="9.0.0")=>JsonSerializer.SerializeToUtf8Bytes(new {version,platform,file="Minova-Cloud-Setup.exe",sha256=hash,sourceCommit=new string('a',40)});
        byte[] Sign(byte[] raw)=>Encoding.ASCII.GetBytes(Convert.ToBase64String(key.SignData(raw,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1)));
        var raw=Manifest();var signature=Sign(raw);Assert(UpdateManifest.Verify(raw,signature,publicKey).Sha256==hash,"Signed metadata rejected");Pass("Valid signed metadata verifies before use");
        Reject(()=>UpdateManifest.Verify(raw.Concat(new byte[]{32}).ToArray(),signature,publicKey));
        using(var foreign=RSA.Create(3072))Reject(()=>UpdateManifest.Verify(raw,signature,foreign.ExportSubjectPublicKeyInfoPem()));
        Reject(()=>UpdateManifest.Verify(raw,Encoding.ASCII.GetBytes("invalid signature"),publicKey));Pass("Changed metadata, foreign signing keys and malformed signatures are rejected");
        var incompatible=Manifest("android");Reject(()=>UpdateManifest.Verify(incompatible,Sign(incompatible),publicKey));Pass("An authentically signed incompatible platform is rejected");
        foreach(var url in new[]{"http://github.com/a","https://github.com.evil.example/a","https://github.com:444/a","https://user@github.com/a","https://example.com/a"})Assert(!DesktopUpdater.SafeUrl(new Uri(url)),"Untrusted URL accepted");Pass("Redirects reject HTTP, unrelated hosts, credentials and nonstandard ports");
        var root=Path.Combine(Path.GetTempPath(),"minova-update-checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try {
            var store=new LocalStore(root);var sentinel=Path.Combine(root,"profile-sentinel.txt");File.WriteAllText(sentinel,"Keep account and files");bool busy=true;int installed=0;
            HttpResponseMessage Reply(HttpRequestMessage request){string path=request.RequestUri!.AbsolutePath;return new(HttpStatusCode.OK){Content=new ByteArrayContent(path.EndsWith(".sig")?signature:path.EndsWith(".exe")?payload:raw)};}
            using(var updater=new DesktopUpdater(store,()=>!busy,new Transport(Reply),publicKey,new Version(1,0,0),(path,background)=>{Assert(File.ReadAllBytes(path).SequenceEqual(payload),"Wrong payload");installed++;})) {
                await updater.Check(true);Assert(updater.Ready,"Valid download not ready");await updater.InstallReady();Assert(installed==0,"Busy synchronization was interrupted");busy=false;await updater.InstallReady();Assert(installed==1,"Verified update did not launch");Assert(File.ReadAllText(sentinel)=="Keep account and files","Profile changed");
            }Pass("A verified download waits for idle and retains profile data");
            string cached=Path.Combine(root,"Updates","9.0.0","Minova-Cloud-Setup.exe");
            using(var updater=new DesktopUpdater(store,()=>true,new Transport(Reply),publicKey,new Version(1,0,0),(_,_)=>installed++)) {await updater.Check(true);File.AppendAllText(cached,"tampered");await updater.InstallReady();Assert(installed==1,"A modified cached installer ran");}Pass("The installer is checked again immediately before launch");
            File.Delete(cached);
            using(var updater=new DesktopUpdater(store,()=>true,new Transport(request=>request.RequestUri!.AbsolutePath.EndsWith(".exe")?new(HttpStatusCode.OK){Content=new ByteArrayContent(new byte[]{1,2,3})}:Reply(request)),publicKey,new Version(1,0,0),(_,_)=>installed++)){await updater.Check(true);Assert(!updater.Ready&&!File.Exists(cached)&&!File.Exists(cached+".part"),"Corrupt download retained");}Pass("Corrupt downloads cannot become ready and incomplete files are removed");
            using(var updater=new DesktopUpdater(store,()=>true,new Transport(_=>new(HttpStatusCode.Found){Headers={Location=new Uri("https://example.com/payload")}}),publicKey,new Version(1,0,0),(_,_)=>installed++)){await updater.Check(true);Assert(!updater.Ready,"Foreign redirect accepted");}Pass("The HTTP transport blocks a redirect outside GitHub release hosts");
            using(var updater=new DesktopUpdater(store,()=>true,new Transport(Reply),publicKey,new Version(10,0,0),(_,_)=>installed++)){await updater.Check(true);Assert(!updater.Ready&&updater.AvailableVersion==null,"Downgrade accepted");}Pass("Older releases never become install candidates");
            using var pinned=RSA.Create();pinned.ImportFromPem(UpdateManifest.PublicKey());Assert(pinned.KeySize==3072,"Embedded trust key missing");Pass("The shipped desktop binary contains the production public trust key");
        } finally {Directory.Delete(root,true);}
    }
}
#endif
