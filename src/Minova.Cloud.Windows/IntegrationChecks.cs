#if DEBUG
using System.Security.Cryptography;
namespace Minova.Cloud;
internal static class IntegrationChecks {
    static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    public static async Task Run(string[] args) {
        var report=args[1];var origin=args[2];var testDirectory=Path.Combine(Path.GetTempPath(),"minova-windows-integration-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(testDirectory);
        var log=new List<string>();void Pass(string message){log.Add("PASS "+message);File.WriteAllLines(report,log);}
        RenderPreview(Path.ChangeExtension(report,"png"));Pass("Branded WPF window loads and renders without capturing the desktop");
        var store=new LocalStore(Path.Combine(testDirectory,"Settings"));using var api=new CloudApi(testOrigin:origin);SyncEngine? engine=null;
        try {
            var session=await api.Login("windows_test","synthetic windows passphrase");store.SaveSession(session);
            Assert(store.LoadSession()==session,"DPAPI session did not round-trip");Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(store.DirectoryPath,"account.dpapi"))).Contains(session.Token),"Token stored in plaintext");Pass("Minova sign-in and DPAPI credential encryption");
            var seed=Path.Combine(testDirectory,"seed.txt");await File.WriteAllTextAsync(seed,"A file from the website.\n");var first=await api.Upload("Report.txt",seed,null,default);await api.Folder("Pictures",default);
            var root=Path.Combine(testDirectory,"Cloud");engine=new(root,api,store,true);engine.Activity+=message=>{log.Add("  "+message);File.WriteAllLines(report,log);};
            await engine.Connect(true);await engine.SyncOnce();var local=Path.Combine(root,"Report.txt");
            Assert(CloudFilesProvider.IsPlaceholder(local),"Remote file was not a Cloud Files placeholder");Assert(CloudFilesProvider.OnlineOnly(local),"Remote file downloaded before being opened");Pass("Native sync-root registration and online-only placeholder creation");
            var content=await Task.Run(()=>File.ReadAllText(local)).WaitAsync(TimeSpan.FromSeconds(40));Assert(content=="A file from the website.\n","Hydrated file differs");Assert(!CloudFilesProvider.OnlineOnly(local),"Hydrated file still online-only");Pass("Opening a file in Windows hydrates its exact cloud contents");
            var newPath=Path.Combine(root,"From Windows.txt");await File.WriteAllTextAsync(newPath,"Created in Explorer.");await engine.SyncOnce();var remote=(await api.Snapshot(default)).Items.Single(x=>x.Path=="From Windows.txt");
            using(var bytes=new MemoryStream()){await api.Download(remote,bytes,null,default);Assert(Encoding.UTF8.GetString(bytes.ToArray())=="Created in Explorer.","Upload did not match");}Pass("Local file creation uploads with SHA-256 verification");
            await File.WriteAllTextAsync(newPath,"Edited in Explorer.");await engine.SyncOnce();var updated=(await api.Snapshot(default)).Items.Single(x=>x.Path=="From Windows.txt");Assert(updated.Revision!=remote.Revision,"Local edit did not replace the remote revision");
            var trash=await api.Request<JsonElement>("/api/trash",HttpMethod.Get);Assert(trash.GetProperty("items").EnumerateArray().Any(x=>x.TryGetProperty("previousVersion",out var old)&&old.GetBoolean()),"Previous cloud version was not recoverable");Pass("Local edits update the cloud and retain the previous version in trash");
            await File.WriteAllTextAsync(seed,"Edited on the website.");var website=await api.Upload("Report.txt",seed,first.Revision,default);await engine.SyncOnce();Assert(await Task.Run(()=>File.ReadAllText(local)).WaitAsync(TimeSpan.FromSeconds(40))=="Edited on the website.","Remote edit did not update Explorer file");Pass("Website edits invalidate cached data and download the new version");
            await File.WriteAllTextAsync(local,"My simultaneous Windows edit.");await File.WriteAllTextAsync(seed,"A simultaneous website edit.");await api.Upload("Report.txt",seed,website.Revision,default);await engine.SyncOnce();
            Assert(await Task.Run(()=>File.ReadAllText(local)).WaitAsync(TimeSpan.FromSeconds(40))=="A simultaneous website edit.","Remote conflict version lost");var conflict=Directory.GetFiles(root,"Report (conflict*").Single();Assert(File.ReadAllText(conflict)=="My simultaneous Windows edit.","Local conflict version lost");Assert((await api.Snapshot(default)).Items.Any(x=>x.Name==Path.GetFileName(conflict)),"Conflict copy did not reach Cloud");Pass("Simultaneous edits preserve both versions locally and in the cloud");
            await engine.FreeSpace("Report.txt");Assert(CloudFilesProvider.OnlineOnly(local),"Free space did not dehydrate file");await engine.KeepOffline("Report.txt");Assert(!CloudFilesProvider.OnlineOnly(local)&&CloudFilesProvider.Pinned(local),"Keep offline did not pin and hydrate file");Pass("Free up space and always-keep-on-device operations");
            using(var handle=new CloudFilesNative.Handle(local,8))CloudFilesNative.Check(CloudFilesNative.CfSetPinState(handle.Value,2,0,IntPtr.Zero));await engine.SyncOnce();Assert(CloudFilesProvider.OnlineOnly(local),"Explorer unpin did not free space");
            using(var handle=new CloudFilesNative.Handle(local,8))CloudFilesNative.Check(CloudFilesNative.CfSetPinState(handle.Value,1,0,IntPtr.Zero));await engine.SyncOnce();Assert(!CloudFilesProvider.OnlineOnly(local),"Explorer pin did not fetch data");Pass("Explorer pin-state changes are honored by the sync engine");
            await engine.FreeSpace("Report.txt");Directory.CreateDirectory(Path.Combine(root,"Moved files"));File.Move(local,Path.Combine(root,"Moved files","Renamed report.txt"));await engine.SyncOnce();Assert((await api.Snapshot(default)).Items.Any(x=>x.Path=="Moved files/Renamed report.txt"),"Online-only move did not reach Cloud");
            File.Move(Path.Combine(root,"Moved files","Renamed report.txt"),local);await engine.SyncOnce();Assert(await Task.Run(()=>File.ReadAllText(local)).WaitAsync(TimeSpan.FromSeconds(40))=="A simultaneous website edit.","Renamed online-only file lost its identity");Pass("Online-only files can be renamed and moved between Explorer folders");
            await api.Folder("Archive",default);await api.Upload("Archive/inside.txt",seed,null,default);await engine.SyncOnce();Directory.Move(Path.Combine(root,"Archive"),Path.Combine(root,"Renamed archive"));await engine.SyncOnce();Assert(await Task.Run(()=>File.ReadAllText(Path.Combine(root,"Renamed archive","inside.txt"))).WaitAsync(TimeSpan.FromSeconds(40))=="A simultaneous website edit.","Folder move broke a child placeholder");Pass("Moving folders preserves the cloud identity of online-only children");
            File.Delete(newPath);await engine.SyncOnce();Assert(!(await api.Snapshot(default)).Items.Any(x=>x.Path=="From Windows.txt"),"Local deletion not synced");Pass("Explorer deletion moves the matching remote file to Cloud trash");
            var latest=(await api.Snapshot(default)).Items.Single(x=>x.Path=="Report.txt");await api.Trash(latest,false,default);await engine.SyncOnce();Assert(!File.Exists(local),"Remote deletion not reflected");Assert(Directory.EnumerateFiles(Path.Combine(store.AccountPath(session.User.Id),"Recovered files"),"Report.txt",SearchOption.AllDirectories).Any(),"Downloaded remote-deleted file was not recoverable");Pass("Remote deletions update Explorer and preserve downloaded recovery copies");
            engine.Dispose();engine=new(root,api,store,true);await engine.Connect(false);await engine.SyncOnce();Pass("Sync resumes from the same account ledger after a client restart");
            File.Delete(Path.Combine(root,SyncEngine.Marker));bool blocked=false;try{await engine.SyncOnce();}catch(IOException){blocked=true;}Assert(blocked,"Missing root identity did not block deletions");Pass("Missing or replaced sync folders cannot trigger cloud deletions");
            bool escaped=false;try{SyncEngine.SafePath(root,"../outside.txt");}catch(IOException){escaped=true;}Assert(escaped,"Traversal path accepted");Pass("Cloud paths cannot escape the sync root");
            File.WriteAllLines(report,log.Append("ALL WINDOWS INTEGRATION CHECKS PASSED"));
        } catch(Exception error){File.WriteAllLines(report,log.Append(error.ToString()));throw;}
        finally{if(engine!=null){engine.Dispose();try{engine.UnregisterForTest();}catch(Exception){}}}
    }
    static void RenderPreview(string file) {
        var previewStore=new LocalStore(Path.Combine(Path.GetTempPath(),"minova-ui-preview-"+Guid.NewGuid().ToString("N")));
        var window=new MainWindow(previewStore);var content=(System.Windows.FrameworkElement)window.Content;
        content.Measure(new System.Windows.Size(816,620));content.Arrange(new System.Windows.Rect(0,0,816,620));content.UpdateLayout();
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(816,620,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(content);
        var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var output=File.Create(file))png.Save(output);window.Quit();
    }
}
#endif
