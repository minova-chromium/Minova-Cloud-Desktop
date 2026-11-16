using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using System.Reflection;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Text;
using Microsoft.Win32;

internal static class Setup {
    internal static readonly string Target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Minova Cloud");
    [STAThread] static int Main(string[] args) {
        try {
            if(args.Contains("--silent")){Install(DefaultStartup());if(!args.Contains("--no-launch"))Launch(args.Contains("--background"));return 0;}
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new SetupWindow());return 0;
        }catch(Exception error){if(!args.Contains("--silent"))MessageBox.Show(error.Message,"Minova Cloud Setup",MessageBoxButtons.OK,MessageBoxIcon.Error);else Console.Error.WriteLine(error);return 1;}
    }
    static bool DefaultStartup() {
        string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"Minova Cloud\Windows\preferences.json");
        if(File.Exists(file)){var values=new JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(file));object enabled;if(values.TryGetValue("startup",out enabled))return Convert.ToBoolean(enabled);}
        return true;
    }
    internal static void Launch(bool background=false){Process.Start(new ProcessStartInfo(Path.Combine(Target,"Minova.Cloud.exe"),background?"--background":""){UseShellExecute=true});}
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int id);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder path,ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    static string ExecutablePath(Process process) {
        IntPtr handle=OpenProcess(0x1000,false,process.Id);if(handle==IntPtr.Zero)return null;
        try{int size=32768;var path=new StringBuilder(size);return QueryFullProcessImageName(handle,0,path,ref size)?path.ToString():null;}
        finally{CloseHandle(handle);}
    }
    internal static void Install(bool startup) {
        if(IntPtr.Size!=8 || Environment.OSVersion.Version.Major<10)throw new Exception("Minova Cloud requires 64-bit Windows 10 or Windows 11.");
        string executable=Path.Combine(Target,"Minova.Cloud.exe");
        if(File.Exists(executable)) {
            using(var exit=Process.Start(new ProcessStartInfo(executable,"--exit"){UseShellExecute=false,CreateNoWindow=true})){exit.WaitForExit(15000);}
            foreach(var running in Process.GetProcessesByName("Minova.Cloud"))try{
                if(running.HasExited)continue;
                string path=ExecutablePath(running);
                if(path==null){if(running.WaitForExit(15000))continue;throw new Exception("Minova Cloud is still closing. Retry the update in a moment.");}
                if(string.Equals(path,executable,StringComparison.OrdinalIgnoreCase)&&!running.WaitForExit(15000))throw new Exception("Close Minova Cloud before installing this update.");
            }finally{running.Dispose();}
        }
        Directory.CreateDirectory(Target);
        if((File.GetAttributes(Target)&FileAttributes.ReparsePoint)!=0)throw new IOException("The Minova installation folder cannot be a link.");
        var assembly=Assembly.GetExecutingAssembly();
        using(var payload=assembly.GetManifestResourceStream("Payload.zip")) {
            string expected;using(var text=new StreamReader(assembly.GetManifestResourceStream("Payload.sha256")))expected=text.ReadToEnd().Trim();
            using(var hash=SHA256.Create())if(BitConverter.ToString(hash.ComputeHash(payload)).Replace("-","").ToLowerInvariant()!=expected)throw new IOException("The installer is incomplete. Download it again.");
            payload.Position=0;
            using(var archive=new ZipArchive(payload,ZipArchiveMode.Read)) {
                var installed=new System.Collections.Generic.List<string>();
                foreach(var entry in archive.Entries) {
                    if(string.IsNullOrEmpty(entry.Name))continue;
                    string destination=Path.GetFullPath(Path.Combine(Target,entry.FullName));
                    if(!destination.StartsWith(Target+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid installer path.");
                    for(string parent=Path.GetDirectoryName(destination);!parent.Equals(Target,StringComparison.OrdinalIgnoreCase);parent=Path.GetDirectoryName(parent))if(Directory.Exists(parent)&&(File.GetAttributes(parent)&FileAttributes.ReparsePoint)!=0)throw new IOException("A linked installation folder was found.");
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    using(var input=entry.Open())using(var output=new FileStream(destination,FileMode.Create,FileAccess.Write,FileShare.None))input.CopyTo(output);
                    installed.Add(entry.FullName);
                }
                File.WriteAllText(Path.Combine(Target,"installed-files.json"),new JavaScriptSerializer().Serialize(installed));
            }
        }
        using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){if(startup)key.SetValue("Minova Cloud","\""+executable+"\" --background");else key.DeleteValue("Minova Cloud",false);}
        string settings=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"Minova Cloud\Windows");Directory.CreateDirectory(settings);
        string preferenceFile=Path.Combine(settings,"preferences.json");var serializer=new JavaScriptSerializer();
        var preferences=File.Exists(preferenceFile)?serializer.Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(preferenceFile)):new System.Collections.Generic.Dictionary<string,object>();preferences["startup"]=startup;File.WriteAllText(preferenceFile,serializer.Serialize(preferences));
        using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\MinovaCloud")) {
            key.SetValue("DisplayName","Minova Cloud");key.SetValue("DisplayVersion",System.Diagnostics.FileVersionInfo.GetVersionInfo(executable).ProductVersion.Split('+')[0]);key.SetValue("Publisher","Minova");key.SetValue("InstallLocation",Target);key.SetValue("DisplayIcon",executable+",0");
            string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
            string uninstall="\""+powershell+"\" -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \""+Path.Combine(Target,"Uninstall.ps1")+"\"";
            key.SetValue("UninstallString",uninstall);key.SetValue("QuietUninstallString",uninstall+" -Quiet");key.SetValue("NoModify",1);key.SetValue("NoRepair",1);key.SetValue("URLInfoAbout","https://project-minova.com/");
        }
        var shellType=Type.GetTypeFromProgID("WScript.Shell");dynamic shell=Activator.CreateInstance(shellType);
        string linkPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Minova Cloud.lnk");
        dynamic shortcut=shell.CreateShortcut(linkPath);shortcut.TargetPath=executable;shortcut.WorkingDirectory=Target;shortcut.IconLocation=executable+",0";shortcut.Description="Your private files in File Explorer";shortcut.Save();Marshal.FinalReleaseComObject(shortcut);Marshal.FinalReleaseComObject(shell);
    }
    sealed class SetupWindow : Form {
        readonly Button button;readonly CheckBox startup;readonly Label status;
        public SetupWindow() {
            Text="Minova Cloud Setup";ClientSize=new Size(560,400);BackColor=Color.FromArgb(7,23,30);ForeColor=Color.FromArgb(239,246,250);Font=new Font("Segoe UI",10);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;Icon=Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            Controls.Add(new Label {Text="MINOVA / CLOUD FOR WINDOWS",AutoSize=true,Location=new Point(30,30),ForeColor=Color.FromArgb(18,210,222),Font=new Font("Segoe UI",9,FontStyle.Bold)});
            Controls.Add(new Label {Text="Your Cloud. Right here.",AutoSize=true,Location=new Point(26,68),Font=new Font("Segoe UI",26,FontStyle.Bold)});
            Controls.Add(new Label {Text="See your Minova files in File Explorer. Open online-only files, keep them offline, and sync changes across your devices.",Location=new Point(30,128),Size=new Size(492,55)});
            Controls.Add(new Label {Text="Installs for your Windows account:",AutoSize=true,Location=new Point(30,200),ForeColor=Color.FromArgb(170,181,194)});
            Controls.Add(new Label {Text=Target,Location=new Point(30,225),Size=new Size(490,38),ForeColor=Color.FromArgb(170,181,194),Font=new Font("Segoe UI",9)});
            startup=new CheckBox {Text="Start Minova Cloud when I sign in to Windows",Checked=DefaultStartup(),AutoSize=true,Location=new Point(30,274)};Controls.Add(startup);
            status=new Label {Text="Your existing Minova login connects this PC.",Location=new Point(30,325),Size=new Size(310,45),ForeColor=Color.FromArgb(170,181,194),Font=new Font("Segoe UI",9)};Controls.Add(status);
            button=new Button {Text="Install Minova Cloud",Location=new Point(351,325),Size=new Size(180,42),BackColor=Color.FromArgb(18,210,222),ForeColor=Color.FromArgb(6,27,34),FlatStyle=FlatStyle.Flat};Controls.Add(button);button.Click+=InstallClicked;
        }
        async void InstallClicked(object sender,EventArgs e) {
            button.Enabled=false;startup.Enabled=false;status.Text="Installing Minova Cloud…";bool runAtLogin=startup.Checked;
            try{await Task.Run(()=>Install(runAtLogin));status.Text="Ready. Sign in to connect your Minova folder.";button.Text="Open Minova Cloud";button.Click-=InstallClicked;button.Click+=delegate{Launch();Close();};button.Enabled=true;}
            catch(Exception error){status.Text="Installation could not finish.";MessageBox.Show(this,error.Message,"Minova Cloud",MessageBoxButtons.OK,MessageBoxIcon.Error);button.Enabled=true;startup.Enabled=true;}
        }
    }
}

