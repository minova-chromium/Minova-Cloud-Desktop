using System.Security.Cryptography;
using System.Text.RegularExpressions;
namespace Minova.Cloud;

public sealed record UpdateRelease(string Version,string Sha256,string SourceCommit) {
    public const string Repository="minova-chromium/Minova-Cloud-Desktop";
    public string Asset(string name)=>$"https://github.com/{Repository}/releases/download/v{Version}/{name}";
}
public static class UpdateManifest {
    public static string PublicKey() {
        using var stream=typeof(UpdateManifest).Assembly.GetManifestResourceStream("Minova.Cloud.update-public.pem")??throw new IOException("Updater trust key missing.");using var reader=new StreamReader(stream);return reader.ReadToEnd();
    }
    public static UpdateRelease Verify(byte[] manifest,byte[] signature,string publicKey) {
        if(manifest.Length>65536||signature.Length>8192)throw new IOException("Invalid update manifest size.");
        using var rsa=RSA.Create();rsa.ImportFromPem(publicKey);
        byte[] detached;try{detached=Convert.FromBase64String(Encoding.UTF8.GetString(signature).Trim().TrimStart('\uFEFF'));}catch(FormatException){throw new IOException("The update signature is invalid.");}
        if(!rsa.VerifyData(manifest,detached,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))throw new IOException("This update was not signed by Minova. Your current app is kept.");
        using var document=JsonDocument.Parse(Encoding.UTF8.GetString(manifest).TrimStart('\uFEFF'));var json=document.RootElement;
        var version=json.GetProperty("version").GetString()??"";var checksum=json.GetProperty("sha256").GetString()??"";var commit=json.GetProperty("sourceCommit").GetString()??"";
        if(!Regex.IsMatch(version,@"^\d{1,6}\.\d{1,6}\.\d{1,6}$")||!System.Version.TryParse(version,out _)||!Regex.IsMatch(checksum,"^[a-f0-9]{64}$")||!Regex.IsMatch(commit,"^[a-f0-9]{40}$")||json.GetProperty("platform").GetString()!="windows-x64"||json.GetProperty("file").GetString()!="Minova-Cloud-Setup.exe")throw new IOException("The update manifest does not describe a compatible Minova desktop build.");
        return new(version,checksum,commit);
    }
    public static async Task VerifyFile(string path,string checksum,CancellationToken ct=default) {
        await using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,true);
        var actual=Convert.ToHexStringLower(await SHA256.HashDataAsync(input,ct));if(actual!=checksum)throw new IOException("The downloaded update failed verification. Your current app is kept.");
    }
}
