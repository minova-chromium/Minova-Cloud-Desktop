using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Minova.Cloud;
public sealed class UpdateWindow : Window {
    public UpdateWindow(DesktopUpdater updater,LocalStore store) {
        Title="Minova Cloud updates";Width=510;Height=370;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(7,23,30));
        var panel=new StackPanel {Margin=new Thickness(26)};Content=panel;panel.Children.Add(new TextBlock {Text="Minova Cloud updates",FontSize=25,FontWeight=FontWeights.SemiBold});panel.Children.Add(new TextBlock {Text="Installed version "+Product.Version,Margin=new Thickness(0,8,0,18),Foreground=Brushes.LightSlateGray});var status=new TextBlock {Text=updater.Status,TextWrapping=TextWrapping.Wrap,MinHeight=58};panel.Children.Add(status);
        var automatic=new CheckBox {Content="Automatically download and install updates",IsChecked=store.LoadPreferences().AutoUpdates};panel.Children.Add(automatic);automatic.Click+=(_,_)=>{var settings=store.LoadPreferences();settings.AutoUpdates=automatic.IsChecked==true;store.SavePreferences(settings);};
        var check=new Button {Content="Check for updates",Margin=new Thickness(0,16,0,8)};panel.Children.Add(check);check.Click+=async(_,_)=>{check.IsEnabled=false;await updater.Check(true);check.IsEnabled=true;};var install=new Button {Content="Install downloaded update",IsEnabled=updater.Ready};panel.Children.Add(install);install.Click+=async(_,_)=>await updater.InstallReady();
        Action<string> changed=message=>Dispatcher.BeginInvoke(()=>{status.Text=message;install.IsEnabled=updater.Ready;});updater.StatusChanged+=changed;Closed+=(_,_)=>updater.StatusChanged-=changed;
    }
}
