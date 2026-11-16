using System.Windows;
using System.Drawing;
using System.Windows.Threading;
using Forms=System.Windows.Forms;
namespace Minova.Cloud;
public partial class App : System.Windows.Application {
    DesktopUpdater? updater;LocalStore? updateStore;Mutex? mutex;Forms.NotifyIcon? tray;MainWindow? window;EventWaitHandle? wake,exit;RegisteredWaitHandle? wakeWait,exitWait;
    public static string Scope=>System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
    protected override async void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);
#if DEBUG
        if(e.Args.Contains("--update-tests")){try{await UpdateChecks.Run(e.Args[1]);Shutdown(0);}catch(Exception error){File.AppendAllText(e.Args[1],Environment.NewLine+error);Shutdown(1);}return;}
        if(e.Args.Contains("--integration-test")){try{await IntegrationChecks.Run(e.Args);Shutdown(0);}catch(Exception error){File.AppendAllText(e.Args[1],Environment.NewLine+error);Shutdown(1);}return;}
#endif
        var name="Local\\MinovaCloud-"+Scope;
        if(e.Args.Contains("--exit")){try{using var signal=EventWaitHandle.OpenExisting(name+"-exit");signal.Set();}catch(WaitHandleCannotBeOpenedException){}Shutdown();return;}
        mutex=new(true,name,out bool first);
        if(!first){try{using var signal=EventWaitHandle.OpenExisting(name+"-wake");signal.Set();}catch(WaitHandleCannotBeOpenedException){}Shutdown();return;}
        if(e.Args.Contains("--remove-integration")) {
            try {
                var store=new LocalStore();var preferences=store.LoadPreferences();var session=store.LoadSession();
                if(preferences.UserId!=null) {
                    var id=$"MinovaCloud!{Scope}!{preferences.UserId}";
                    var roots=global::Windows.Storage.Provider.StorageProviderSyncRootManager.GetCurrentSyncRoots();
                    if(roots.Any(x=>x.Id==id))global::Windows.Storage.Provider.StorageProviderSyncRootManager.Unregister(id);
                    var ledgerFile=Path.Combine(store.AccountPath(preferences.UserId),"sync.json");var ledger=store.Read<SyncLedger>(ledgerFile);
                    if(ledger!=null){ledger.Items.Clear();ledger.Pins.Clear();ledger.Move=null;store.Write(ledgerFile,ledger);}
                }
                if(session!=null){using var cloud=new CloudApi(session);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));try{await cloud.Request<JsonElement>("/api/logout",HttpMethod.Post,new {},timeout.Token);}catch(Exception){}}
                store.ForgetSession();ShellIntegration.Startup(false);Shutdown(0);
            }catch(Exception error){File.WriteAllText(Path.Combine(new LocalStore().DirectoryPath,"uninstall-error.txt"),error.Message);Shutdown(1);}return;
        }
        wake=new(false,EventResetMode.AutoReset,name+"-wake");exit=new(false,EventResetMode.AutoReset,name+"-exit");
        DispatcherUnhandledException+=(_,args)=>{System.Windows.MessageBox.Show(args.Exception.Message,"Minova Cloud");args.Handled=true;};
        window=new(new LocalStore());MainWindow=window;
        wakeWait=ThreadPool.RegisterWaitForSingleObject(wake,(_,_)=>Dispatcher.BeginInvoke(ShowWindow),null,Timeout.Infinite,false);
        exitWait=ThreadPool.RegisterWaitForSingleObject(exit,(_,_)=>Dispatcher.BeginInvoke(Quit),null,Timeout.Infinite,false);
        tray=new(){Text="Minova Cloud",Icon=Icon.ExtractAssociatedIcon(Environment.ProcessPath!)??SystemIcons.Application,Visible=true};
        var menu=new Forms.ContextMenuStrip();menu.Items.Add("Open Minova Cloud",null,(_,_)=>Dispatcher.Invoke(ShowWindow));menu.Items.Add("Open website",null,(_,_)=>ShellIntegration.Open(CloudApi.Origin));menu.Items.Add("Updates",null,(_,_)=>Dispatcher.Invoke(ShowUpdates));menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("Quit",null,(_,_)=>Dispatcher.Invoke(Quit));tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(ShowWindow);
        if(!e.Args.Contains("--background"))ShowWindow();await window.Restore();updateStore=new LocalStore();updater=new DesktopUpdater(updateStore,()=>Dispatcher.Invoke(()=>window!=null&&!window.BusyForUpdate));updater.StatusChanged+=message=>Dispatcher.BeginInvoke(()=>{if(message.StartsWith("Version ")&&message.Contains("ready")&&tray!=null){tray.BalloonTipTitle="Minova Cloud update";tray.BalloonTipText=message;tray.ShowBalloonTip(4000);}});updater.Start();
    }
    public void ShowUpdates(){if(updater!=null&&updateStore!=null)new UpdateWindow(updater,updateStore){Owner=window}.Show();}
    void ShowWindow(){if(window==null)return;window.Show();window.WindowState=WindowState.Normal;window.Activate();}
    public void SetTrayStatus(string message){var text="Minova Cloud · "+message;if(tray!=null)tray.Text=text[..Math.Min(63,text.Length)];}
    void Quit(){window?.Quit();Shutdown();}
    protected override void OnExit(ExitEventArgs e){updater?.Dispose();tray?.Dispose();wakeWait?.Unregister(null);exitWait?.Unregister(null);wake?.Dispose();exit?.Dispose();mutex?.Dispose();base.OnExit(e);}
}

