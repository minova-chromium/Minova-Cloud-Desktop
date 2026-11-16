using Microsoft.Win32;
namespace Minova.Cloud;
public static class ShellIntegration {
    public static void Startup(bool enabled) {
        using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if(enabled)key.SetValue("Minova Cloud",'"'+Environment.ProcessPath+"\" --background");else key.DeleteValue("Minova Cloud",false);
    }
    public static void Open(string path)=>Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
    public static void OpenFolder(string path)=>Process.Start(new ProcessStartInfo("explorer.exe"){ArgumentList={path},UseShellExecute=true});
}
