#if DEBUG
using System.Text.Json;
using CarArchive.Core;
using Microsoft.UI.Xaml;
using Windows.System;

namespace CarArchive;

public sealed partial class MainPage
{
    private async Task RunCategoryVerificationAsync(string directory)
    {
        directory=Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        var reportPath=Path.Combine(directory,"category-verification.json");
        try
        {
            if(!Path.GetFullPath(ViewModels.MainViewModel.DataDirectory).StartsWith(directory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) || VM.Groups.Count>0)
                throw new InvalidOperationException("Category verification needs a new isolated data directory.");
            var root=Path.Combine(directory,"library");
            foreach(var path in new[] { "Audi/A4 Avant (B9 Facelift)","Audi/A4 Sedan (B9)","Benz/S-Class (W223)","Other/A4 Avant (B9 Facelift)" })
                Directory.CreateDirectory(Path.Combine(root,path.Replace('/',Path.DirectorySeparatorChar)));
            var alternateRoot=Path.Combine(directory,"alternate");Directory.CreateDirectory(Path.Combine(alternateRoot,"Honda","Civic (FK8)"));
            var source=Path.Combine(directory,"source.jpg");await File.WriteAllBytesAsync(source,await Services.PhotoService.ConnectivityImageAsync());
            await VM.ImportAsync([source],true,default);await VM.BackgroundImport;
            var first=VM.Groups[0];var second=VM.Duplicate(first);
            first.Confirmed=new() { Brand="Audi",Series="A4",Code="B9" };first.FolderName="Original manual name";first.TargetBrand="Audi";first.ManualName=false;
            second.FolderName="Second independent group";second.TargetBrand="Audi";
            VM.State.Settings.ArchiveRoot=root;
            VM.State.Names.Add(new("fixture-benz","Mercedes-Benz","E-Class (W214)"));
            VM.State.Names.Add(new("fixture-audi","Audi","a4 avant (b9 facelift)"));
            VM.State.BrandFolders["Mercedes-Benz"]="Benz";
            VM.Selected=first;RenderSelection();await RefreshCategoryCatalogAsync();
            if(categoryItems.Count!=5 || first.ManualName) throw new Exception("Initial catalog merge or programmatic-load guard failed.");
            var chosen=categoryItems.First(item=>item.BrandDirectory=="Benz" && item.Exists);
            suggestionsGroupId=first.Id;ChooseCategory(chosen);
            if(first.TargetBrand!="Benz" || first.FolderName!="S-Class (W223)" || first.Confirmed.Brand!="Audi" || !first.ManualName) throw new Exception("Endpoint synchronization failed.");
            VM.Selected=second;RenderSelection();ChooseCategory(chosen);
            if(second.FolderName!="Second independent group") throw new Exception("Stale suggestions crossed groups.");
            VM.State.Settings.ArchiveRoot=alternateRoot;RenderSelection();await RefreshCategoryCatalogAsync();
            if(categoryItems.Any(item=>item.FolderName=="A4 Sedan (B9)") || categoryItems.All(item=>item.FolderName!="Civic (FK8)")) throw new Exception("Root change retained stale disk endpoints.");
            first.FolderName="Original manual name";first.TargetBrand="Audi";first.ManualName=false;
            VM.State.Settings.ArchiveRoot=root;VM.Selected=first;RenderSelection();await RefreshCategoryCatalogAsync();VM.Save();await VM.FlushWritesAsync();
            await Task.Delay(500);
            if(first.ManualName) throw new Exception("Delayed text events marked a programmatic render as a manual edit.");
            VM.Status="自动补全验证环境 · 仅使用合成图片，不联网。";
            AddShortcut(VirtualKey.L,VirtualKeyModifiers.Control|VirtualKeyModifiers.Shift,(_,_)=>RequestedTheme=ElementTheme.Light);
            AddShortcut(VirtualKey.D,VirtualKeyModifiers.Control|VirtualKeyModifiers.Shift,(_,_)=>RequestedTheme=ElementTheme.Dark);
            AddShortcut(VirtualKey.N,VirtualKeyModifiers.Control|VirtualKeyModifiers.Shift,(_,_)=> { App.MainWindow!.AppWindow.Resize(new(850,900));PaneBox.SelectedIndex=2; });
            AddShortcut(VirtualKey.W,VirtualKeyModifiers.Control|VirtualKeyModifiers.Shift,(_,_)=>App.MainWindow!.AppWindow.Resize(new(1400,950)));
            var timer=DispatcherQueue.CreateTimer();timer.Interval=TimeSpan.FromMilliseconds(250);string previous="";
            timer.Tick+=(_,_)=>
            {
                var snapshot=JsonSerializer.Serialize(new { Status="Passed",PreflightChecks=4,Root=VM.State.Settings.ArchiveRoot,Selected=displayed?.Id,
                    Input=FolderEdit.Text,Brand=TargetBrandEdit.Text,Target=TargetPathText.Text,Open=FolderEdit.IsSuggestionListOpen,
                    Suggestions=(FolderEdit.ItemsSource as IEnumerable<CategorySuggestion>)?.Select(item=>new { item.FolderName,item.BrandDirectory,item.Exists }).ToArray(),
                    Groups=VM.Groups.Select(group=>new { group.Id,group.FolderName,group.TargetBrand,group.ManualName,IdentityBrand=group.Confirmed.Brand }).ToArray(),
                    FooterBottom=ArchiveFooter.TransformToVisual(BodyGrid).TransformPoint(new(0,ArchiveFooter.ActualHeight)).Y,BodyHeight=BodyGrid.ActualHeight,
                    Theme=ActualTheme.ToString(),Narrow=narrowLayout,SourceExists=File.Exists(source),Jobs=VM.Store.Jobs().Count },StateStore.Json);
                if(snapshot!=previous) { File.WriteAllText(reportPath,snapshot);previous=snapshot; }
            };
            timer.Start();App.MainWindow!.Closed+=(_,_)=>timer.Stop();
        }
        catch(Exception error) { File.WriteAllText(reportPath,JsonSerializer.Serialize(new { Status="Failed",Error=error.ToString() },StateStore.Json));VM.Status="自动补全验证失败："+error.Message; }
    }
}
#endif
