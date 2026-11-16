using System.Security.Cryptography;
namespace Minova.Cloud;

public sealed class LocalStore {
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {WriteIndented=true,DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull};
    public string DirectoryPath {get;}
    public LocalStore(string? directory = null) {
        DirectoryPath = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Minova Cloud","Windows");
        Directory.CreateDirectory(DirectoryPath);
    }
    public string AccountPath(string userId) {
        if(!Guid.TryParse(userId,out _))throw new IOException("Invalid Minova account identity.");
        var result=Path.Combine(DirectoryPath,"Accounts",userId);Directory.CreateDirectory(result);return result;
    }
    public T? Read<T>(string file) => File.Exists(file) ? JsonSerializer.Deserialize<T>(File.ReadAllText(file),Json) : default;
    public void Write<T>(string file,T value) {
        var temporary=file+".new";File.WriteAllText(temporary,JsonSerializer.Serialize(value,Json));File.Move(temporary,file,true);
    }
    public Preferences LoadPreferences() => Read<Preferences>(Path.Combine(DirectoryPath,"preferences.json")) ?? new();
    public void SavePreferences(Preferences value) => Write(Path.Combine(DirectoryPath,"preferences.json"),value);
    public Session? LoadSession() {
        var file=Path.Combine(DirectoryPath,"account.dpapi");if(!File.Exists(file))return null;
        try{return JsonSerializer.Deserialize<Session>(ProtectedData.Unprotect(File.ReadAllBytes(file),null,DataProtectionScope.CurrentUser),Json);}
        catch(CryptographicException){return null;}
    }
    public void SaveSession(Session value) {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(value,Json);
        var encrypted=ProtectedData.Protect(bytes,null,DataProtectionScope.CurrentUser);CryptographicOperations.ZeroMemory(bytes);
        var file=Path.Combine(DirectoryPath,"account.dpapi");File.WriteAllBytes(file+".new",encrypted);File.Move(file+".new",file,true);
    }
    public void ForgetSession(){File.Delete(Path.Combine(DirectoryPath,"account.dpapi"));}
}
