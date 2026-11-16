using System.Windows;
using System.Windows.Input;
namespace Minova.Cloud;
public partial class MainWindow : Window {
    readonly LocalStore store;Preferences preferences;CloudApi? api;SyncEngine? engine;
    bool working,quitting;
    public MainWindow(LocalStore store) {InitializeComponent();VersionLabel.Text=Product.Version+" \u00b7 Private by default. Shared by choice.";this.store=store;preferences=store.LoadPreferences();Closing+=(_,e)=>{if(!quitting){e.Cancel=true;Hide();}};}
    public async Task Restore() {
        var session=store.LoadSession();if(session==null)return;
        api=new(session);try{await Connect(false);}catch(Exception error){ShowError(error.Message);}
    }
    void PasswordKeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Enter)SignIn(sender,e);}
    async void SignIn(object sender,RoutedEventArgs e) {
        if(working)return;working=true;SignInButton.IsEnabled=false;LoginError.Text="Connecting…";
        try {
            engine?.Dispose();engine=null;api?.Dispose();api=new();var session=await api.Login(Username.Text.Trim(),Password.Password);Password.Clear();
            preferences=store.LoadPreferences();if(preferences.UserId!=session.User.Id){preferences.UserId=session.User.Id;preferences.Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Minova Cloud - "+session.User.Username);}
            store.SaveSession(session);store.SavePreferences(preferences);await Connect(true);
        }catch(Exception error){ShowError(error is HttpRequestException?"Could not reach Minova Cloud. Check your internet connection.":error.Message);}
        finally{working=false;SignInButton.IsEnabled=true;}
    }
    async Task Connect(bool first) {
        var session=api!.Session!;
        if(preferences.UserId!=session.User.Id||preferences.Root==null)throw new IOException("Reconnect this PC to choose your Minova folder.");
        engine?.Dispose();engine=new(preferences.Root,api,store);engine.Status+=value=>Dispatcher.Invoke(()=>Display(value));engine.Activity+=value=>Dispatcher.Invoke(()=>AddActivity(value));
        await engine.Connect(first);ShellIntegration.Startup(preferences.Startup);
        LoginPanel.Visibility=Visibility.Collapsed;ConnectedPanel.Visibility=Visibility.Visible;Greeting.Text="Your Cloud, "+session.User.Name+".";AccountLabel.Text="@"+session.User.Username+" · This PC";FolderPath.Text=preferences.Root;StartupBox.IsChecked=preferences.Startup;
        AddActivity("Connected · "+preferences.Root);engine.Start();LoginError.Text="";
    }
    void Display(SyncStatus value) {StatusTitle.Text=value.Title;StatusDetail.Text=value.Detail;FileCount.Text=value.Files+" files"+(value.LastSync==null?"":" · Last checked "+value.LastSync.Value.ToLocalTime().ToString("HH:mm"));((App)System.Windows.Application.Current).SetTrayStatus(value.Title);if(value.Title=="Sign in again"){ConnectedPanel.Visibility=Visibility.Collapsed;LoginPanel.Visibility=Visibility.Visible;LoginError.Text="This PC’s connection expired. Sign in again to resume syncing.";}}
    void AddActivity(string value){ActivityList.Items.Insert(0,DateTime.Now.ToString("HH:mm")+"  "+value);while(ActivityList.Items.Count>100)ActivityList.Items.RemoveAt(ActivityList.Items.Count-1);}
    void ShowError(string message){LoginError.Text=message;if(ConnectedPanel.Visibility==Visibility.Visible)Display(new("Needs attention",message,Error:true));}
    void OpenWeb(object sender,RoutedEventArgs e)=>ShellIntegration.Open(CloudApi.Origin);
    void OpenFolder(object sender,RoutedEventArgs e){if(engine!=null)ShellIntegration.OpenFolder(engine.Root);}
    async void SyncNow(object sender,RoutedEventArgs e){if(engine==null)return;SyncButton.IsEnabled=false;try{await Task.Run(()=>engine.SyncOnce());}catch(Exception error){Display(new("Sync paused",error.Message,Error:true));}finally{SyncButton.IsEnabled=true;}}
    void PauseSync(object sender,RoutedEventArgs e){engine?.TogglePause();PauseButton.Content=engine?.Paused==true?"Resume sync":"Pause sync";}
    void StartupChanged(object sender,RoutedEventArgs e){preferences=store.LoadPreferences();preferences.Startup=StartupBox.IsChecked==true;store.SavePreferences(preferences);ShellIntegration.Startup(preferences.Startup);}
    async void Disconnect(object sender,RoutedEventArgs e) {
        var result=System.Windows.MessageBox.Show(this,"Disconnect this PC from Minova Cloud? Downloaded files stay on this PC. Online-only files need Minova Cloud to be connected.","Disconnect Minova Cloud",MessageBoxButton.YesNo,MessageBoxImage.Question);
        if(result!=MessageBoxResult.Yes)return;
        try{if(api!=null)await api.Request<JsonElement>("/api/logout",HttpMethod.Post,new {});}catch(ApiException error) when(error.Status==401){}catch(Exception error){ShowError("Could not disconnect: "+error.Message);return;}
        engine?.Dispose();engine=null;api?.Dispose();api=null;store.ForgetSession();ConnectedPanel.Visibility=Visibility.Collapsed;LoginPanel.Visibility=Visibility.Visible;LoginError.Text="This PC is disconnected.";
    }
    public bool BusyForUpdate=>working||(engine?.IsBusy??false)||IsActive;
    void OpenUpdates(object sender,RoutedEventArgs e)=>((App)System.Windows.Application.Current).ShowUpdates();
    public void Quit(){quitting=true;engine?.Dispose();api?.Dispose();Close();}
}
