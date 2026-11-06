$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder'
$path = Join-Path $root 'Services\ConfigurationBackupService.cs'
if (-not (Test-Path -LiteralPath $path)) { throw 'Configuration backup export/import is not implemented.' }
$header = @'
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using OrielFolder.Models;
using OrielFolder.Services;
using OrielFolder.Helpers;
namespace OrielFolder.Helpers {
    public static class AppIdentity { public static string LocalDataRoot => throw new Exception("Real user data must not be used by tests."); }
    public static class DebugLogger { public static void Log(string text) {} }
}
namespace Windows.Storage {
    public class Container { public IDictionary<string,object> Values = new Dictionary<string,object>(); }
    public class ApplicationData { public static ApplicationData Current = new(); public Container LocalSettings = new(); }
}
namespace OrielFolder.Models { public class FavoriteItem { public string Path=""; } }
'@
$sources = foreach($file in @('Models\ViewMode.cs','Models\TabStateDto.cs','Models\WorkspaceDto.cs','Services\WorkspaceService.cs','Services\PreviewLabelPresets.cs','Services\PreviewToolbarOrder.cs','Services\ConfigurationBackupService.cs')) {
    $code = [regex]::Replace([IO.File]::ReadAllText((Join-Path $root $file)), '(?m)^using .*;\r?\n','')
    if ($code -match '(?m)^namespace ([\w.]+);') { $code = [regex]::Replace($code,'(?m)^namespace ([\w.]+);','namespace $1 {') + "`n}" }
    $code
}
$favoriteSource=[IO.File]::ReadAllText((Join-Path $root 'Services\FavoritesService.cs'))
$saveFavorites=[regex]::Match($favoriteSource,'(?sm)^        public void SaveFavorites\(.*?^        }').Value
$applyOrder=[regex]::Match($favoriteSource,'(?sm)^        (?:private|internal) static List<FavoriteItem> ApplySavedOrder\(.*?^        }').Value
$vmSource=[IO.File]::ReadAllText((Join-Path $root 'ViewModels\MainViewModel.cs'))
$refreshOrder=[regex]::Match($vmSource,'(?sm)^        public void RefreshImportedFavoriteOrder\(.*?^        }').Value
if (-not $refreshOrder) { $refreshOrder='public void RefreshImportedFavoriteOrder() {}' }
$lifecycle='namespace OrielFolder.Services { public class FavoritesService { private const string FavoritesOrderKey="FavoritesOrderJson"; public static List<FavoriteItem> Reorder(List<FavoriteItem> items) => ApplySavedOrder(items);' + "`n"+$saveFavorites+$applyOrder+"`n} } namespace OrielFolder.ViewModels { public class MainViewModel { public ObservableCollection<FavoriteItem> Favorites = new();`n"+$refreshOrder+"`n} }"
$driver = @'
public class FailOnceSettings : Dictionary<string,object>, IDictionary<string,object> {
    public string? FailOn;
    object IDictionary<string,object>.this[string key] {
        get => base[key];
        set { if(FailOn==key) { FailOn=null; throw new IOException("fixture write failure"); } base[key]=value; }
    }
}
public static class BackupTests {
    public static async Task<string[]> Run(string root) {
        var results = new List<string>();
        void Check(bool ok,string text) { results.Add((ok ? "PASS: " : "FAIL: ")+text); }
        bool Rejected(string json) { try { ConfigurationBackupService.Parse(json); return false; } catch(InvalidDataException) { return true; } }
        var prefs = new Dictionary<string,object> {
            ["Theme"]="dark",["Language"]="zh-CN",["ShowHiddenFiles"]=true,["MillerFolderOpenAlignment"]=1,
            ["DefaultViewMode"]=4,["LastUsedFolderViewMode"]=(int)ViewMode.List,["ZipLegacyCodePage"]=949,
            ["CustomSidebarWidth"]=275.5,["KeyBindingsJson"]="{\"version\":1,\"overrides\":[]}",
            ["FavoritesOrderJson"]="[\"D:\\\\example\"]",["Password"]="synthetic-test-value",["SentryDsn"]="synthetic-test-value",
            ["PreviewLabelPresetsJson"]="[{\"text\":\"待核对\",\"color\":\"#123456\"},{\"text\":\"重点\",\"color\":\"#abcdef\"}]",
            ["PreviewToolbarOrderJson"]="[\"exportPdf\",\"tool-pen\"]"
        };
        var workspace = new WorkspaceDto("one","测试工作区",new List<TabStateDto>{new("t1","示例",@"D:\example",0,3)},0,DateTime.UtcNow,DateTime.UtcNow);
        prefs["ProductIcon"]="classic";
        string json=ConfigurationBackupService.Export(prefs,new List<WorkspaceDto>{workspace});
        Check(!Rejected(json.Replace("OrielFolder Configuration", "SPAN Dev Configuration")),"legacy development backup remains importable");
        Check(ConfigurationBackupService.Parse(json).Preferences["ProductIcon"].GetString()=="classic","chosen product icon survives backup round trip");
        var backup=ConfigurationBackupService.Parse(json);
        Check(backup.Preferences["ZipLegacyCodePage"].GetInt32()==949,"chosen Korean ZIP encoding survives portable backup round trip");
        foreach(int codePage in new[]{0,949,936,950,932,1252}) {
            var encodingBackup=ConfigurationBackupService.Parse(json);
            encodingBackup.Preferences["ZipLegacyCodePage"]=JsonSerializer.SerializeToElement(codePage);
            Check(!Rejected(JsonSerializer.Serialize(encodingBackup,ConfigurationBackupService.JsonOptions)),"supported ZIP encoding can be restored: "+codePage);
        }
        foreach(int codePage in new[]{-1,437,65001,99999}) {
            var invalidEncoding=ConfigurationBackupService.Parse(json);
            invalidEncoding.Preferences["ZipLegacyCodePage"]=JsonSerializer.SerializeToElement(codePage);
            Check(Rejected(JsonSerializer.Serialize(invalidEncoding,ConfigurationBackupService.JsonOptions)),"invalid ZIP encoding rejected before settings writes: "+codePage);
        }
        Check(backup.Preferences["PreviewToolbarOrderJson"].GetString()==(string)prefs["PreviewToolbarOrderJson"],"custom tool order survives portable backup round trip");
        var invalidOrder=ConfigurationBackupService.Parse(json);
        invalidOrder.Preferences["PreviewToolbarOrderJson"]=JsonSerializer.SerializeToElement("[\"tool-pen\",\"tool-pen\"]");
        Check(Rejected(JsonSerializer.Serialize(invalidOrder,ConfigurationBackupService.JsonOptions)),"duplicate tool order rejected before settings writes");
        Check(backup.Preferences["PreviewLabelPresetsJson"].GetString()==(string)prefs["PreviewLabelPresetsJson"],"preview label text, color and order survive backup round trip");
        var invalidLabels=ConfigurationBackupService.Parse(json);
        invalidLabels.Preferences["PreviewLabelPresetsJson"]=JsonSerializer.SerializeToElement("[{\"text\":\"a\",\"color\":\"red\"}]");
        Check(Rejected(JsonSerializer.Serialize(invalidLabels,ConfigurationBackupService.JsonOptions)),"invalid preview label color rejected before settings writes");
        Check(backup.Preferences.ContainsKey("LastUsedFolderViewMode") && backup.Preferences["LastUsedFolderViewMode"].GetInt32()==(int)ViewMode.List && backup.Preferences["DefaultViewMode"].GetInt32()==4,"follow last option and actual last folder mode survive backup round trip");
        foreach(int invalid in new[]{(int)ViewMode.Home,(int)ViewMode.Settings,(int)ViewMode.RecycleBin,999}) {
            var invalidBackup=ConfigurationBackupService.Parse(json);
            invalidBackup.Preferences["LastUsedFolderViewMode"]=JsonSerializer.SerializeToElement(invalid);
            Check(Rejected(JsonSerializer.Serialize(invalidBackup,ConfigurationBackupService.JsonOptions)),"invalid last folder view is rejected: "+invalid);
        }
        var invalidDefault=ConfigurationBackupService.Parse(json);
        invalidDefault.Preferences["DefaultViewMode"]=JsonSerializer.SerializeToElement(5);
        Check(Rejected(JsonSerializer.Serialize(invalidDefault,ConfigurationBackupService.JsonOptions)),"undefined default combo index is rejected");
        Check(!backup.Preferences.ContainsKey("Password") && !backup.Preferences.ContainsKey("SentryDsn"),"export excludes credentials and unknown runtime keys");
        Check(backup.Workspaces.Count==1 && backup.Workspaces[0].Name==workspace.Name,"Unicode workspace round trip");
        var target = new Dictionary<string,object>{["Theme"]="light",["ExistingOtherSetting"]="keep"};
        await ConfigurationBackupService.ApplyAsync(backup,target,_=>Task.CompletedTask);
        Check(target["ShowHiddenFiles"] is bool && target["MillerFolderOpenAlignment"] is int && target["CustomSidebarWidth"] is double,"import preserves Windows settings value types");
        Check((string)target["Theme"]=="dark" && (string)target["ExistingOtherSetting"]=="keep","import restores included preferences and preserves unrelated settings");
        Check(Rejected("not json") && Rejected("{}") && Rejected(json.Replace("\"version\": 1","\"version\": 99")),"malformed and unsupported backup formats are rejected");
        backup.Preferences["Password"]=JsonSerializer.SerializeToElement("synthetic");
        Check(Rejected(JsonSerializer.Serialize(backup,ConfigurationBackupService.JsonOptions)),"untrusted import cannot add a credential key");
        backup.Preferences.Remove("Password");
        backup.Preferences["ShowHiddenFiles"]=JsonSerializer.SerializeToElement("yes");
        Check(Rejected(JsonSerializer.Serialize(backup,ConfigurationBackupService.JsonOptions)),"wrong value types are rejected before import");
        backup=ConfigurationBackupService.Parse(json);
        backup.Preferences["KeyBindingsJson"]=JsonSerializer.SerializeToElement("{\"version\":1,\"overrides\":[null]}");
        Check(Rejected(JsonSerializer.Serialize(backup,ConfigurationBackupService.JsonOptions)),"malformed nested shortcut entries are rejected");
        backup=ConfigurationBackupService.Parse(json);
        backup.Preferences["MillerFolderOpenAlignment"]=JsonSerializer.SerializeToElement(999);
        Check(Rejected(JsonSerializer.Serialize(backup,ConfigurationBackupService.JsonOptions)),"invalid layout choice is rejected");
        backup=ConfigurationBackupService.Parse(json);
        var failing = new FailOnceSettings{["Theme"]="light",["Language"]="en-US",FailOn="Language"};
        try { await ConfigurationBackupService.ApplyAsync(backup,failing,_=>Task.CompletedTask); Check(false,"expected settings failure"); } catch(IOException) {}
        Check((string)failing["Theme"]=="light" && failing.Count==2,"partial settings write failure rolls back earlier changes");
        target = new Dictionary<string,object>{["Theme"]="light"};
        try { await ConfigurationBackupService.ApplyAsync(backup,target,_=>Task.FromException(new IOException("fixture workspace failure"))); Check(false,"expected workspace failure"); } catch(IOException) {}
        Check((string)target["Theme"]=="light" && target.Count==1,"workspace persistence failure rolls back preference writes");
        var service = new WorkspaceService(root);
        await service.SaveWorkspaceAsync(workspace);
        await service.SaveWorkspaceAsync(workspace with {Id="keep",Name="existing"});
        await service.MergeImportedWorkspacesAsync(new List<WorkspaceDto>{workspace with {Name="restored"}});
        var all=await service.GetWorkspacesAsync();
        Check(all.Count==2 && all.Single(w=>w.Id=="one").Name=="restored","workspace import merges by ID without deleting other workspaces");
        await service.SaveWorkspaceAsync(workspace with {Id="_autosave"});
        var snapshot=await service.GetWorkspacesForBackupAsync();
        Check(snapshot.Count==2 && snapshot.All(w=>w.Id!="_autosave"),"saved workspace backup excludes temporary autosave sessions");
        Directory.CreateDirectory(Path.Combine(root,"workspaces.json.tmp"));
        try { await service.MergeImportedWorkspacesAsync(new List<WorkspaceDto>{workspace with {Name="must-not-apply"}}); Check(false,"expected disk write failure"); } catch(IOException) {} catch(UnauthorizedAccessException) {}
        Check((await service.GetWorkspacesAsync()).Single(w=>w.Id=="one").Name=="restored","failed workspace write leaves in-memory workspace unchanged");
        backup.Workspaces[0]=workspace with { Tabs=new List<TabStateDto>{new("t","invalid",@"D:\test",999,0)} };
        Check(Rejected(JsonSerializer.Serialize(backup,ConfigurationBackupService.JsonOptions)),"invalid workspace view mode is rejected");
        bool secretPathRejected=false;
        try { ConfigurationBackupService.Export(prefs,new List<WorkspaceDto>{workspace with { Tabs=new List<TabStateDto>{new("t","remote","ftp://user:synthetic@host/folder",0,3)} }}); } catch(InvalidDataException) { secretPathRejected=true; }
        Check(secretPathRejected,"embedded URI credentials are not written into a backup");
        var live=Windows.Storage.ApplicationData.Current.LocalSettings.Values;
        live["FavoritesOrderJson"]="[\"D:\\\\b\",\"D:\\\\a\"]";
        var vm=new OrielFolder.ViewModels.MainViewModel();
        vm.Favorites.Add(new FavoriteItem{Path=@"D:\a"}); vm.Favorites.Add(new FavoriteItem{Path=@"D:\b"});
        vm.RefreshImportedFavoriteOrder();
        new FavoritesService().SaveFavorites(vm.Favorites.ToList());
        Check(JsonSerializer.Deserialize<List<string>>((string)live["FavoritesOrderJson"])![0]==@"D:\b","normal shutdown preserves imported favorites order");
        string exportPath=Path.Combine(root,"export.json");
        await ConfigurationBackupService.WriteFileAsync(exportPath,json);
        Check(ConfigurationBackupService.Parse(await File.ReadAllTextAsync(exportPath)).Preferences.Count>0,"backup file can be written and parsed from disk");
        bool invalidWrite=false;
        try { await ConfigurationBackupService.WriteFileAsync(exportPath,"broken"); } catch(InvalidDataException) { invalidWrite=true; }
        Check(invalidWrite && await File.ReadAllTextAsync(exportPath)==json,"invalid export cannot overwrite an existing backup");
        File.SetAttributes(exportPath,FileAttributes.ReadOnly);
        bool readonlyFailure=false;
        try { await ConfigurationBackupService.WriteFileAsync(exportPath,json.Replace("dark","light")); } catch(IOException) { readonlyFailure=true; } catch(UnauthorizedAccessException) { readonlyFailure=true; }
        Check(readonlyFailure && await File.ReadAllTextAsync(exportPath)==json,"failed export preserves an existing read-only backup");
        File.SetAttributes(exportPath,FileAttributes.Normal);
        string damaged=Path.Combine(root,"damaged"); Directory.CreateDirectory(damaged);
        string damagedFile=Path.Combine(damaged,"workspaces.json"); await File.WriteAllTextAsync(damagedFile,"broken existing workspace file");
        var damagedService=new WorkspaceService(damaged);
        bool strictRead=false,strictMerge=false;
        var reader=typeof(WorkspaceService).GetMethod("GetWorkspacesForBackupAsync") ?? typeof(WorkspaceService).GetMethod("GetWorkspacesAsync")!;
        try { await (Task<List<WorkspaceDto>>)reader.Invoke(damagedService,null)!; } catch(Exception) { strictRead=true; }
        try { await damagedService.MergeImportedWorkspacesAsync(new List<WorkspaceDto>{workspace}); } catch(Exception) { strictMerge=true; }
        Check(strictRead,"backup refuses an unreadable or corrupt existing workspace store");
        Check(strictMerge && await File.ReadAllTextAsync(damagedFile)=="broken existing workspace file","merge never overwrites a corrupt existing workspace store");
        return results.ToArray();
    }
}
'@
Add-Type -TypeDefinition ($header + ($sources -join "`n") + $lifecycle + $driver)
$fixture=Join-Path 'D:\codex\Orielfolder-development\manual-data' ('backup-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
$results=[BackupTests]::Run($fixture).GetAwaiter().GetResult()
$results
if (@($results | Where-Object {$_ -like 'FAIL:*'}).Count) { throw 'Configuration backup regressions failed.' }
