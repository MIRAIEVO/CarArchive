using System.Text.Json;
using CarArchive.Core;
using CarArchive.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace CarArchive;

public sealed partial class MainPage
{
    private PhotoGroup? displayed;
    private Photo? currentPhoto;
    private bool rendering;
    private bool initialized;
    private bool drawing;
    private Point cropStart;
    private int previewVersion;
    private bool widgetReady;
    private bool narrowLayout;
    private readonly DispatcherTimer editorSaveTimer=new() { Interval=TimeSpan.FromMilliseconds(250) };
    private CancellationTokenSource? previewCancellation;
    private nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow!);

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        CrashJournal.Trace("Page.Loaded");CrashJournal.Current?.Flush();
        if (initialized) return; initialized = true;
        App.MainWindow!.FitToDisplay(XamlRoot.RasterizationScale);
        UpdateModePresentation();
        WireSuggestions(FolderEdit,OpenFolderSuggestions,CloseFolderSuggestions);
        WireSuggestions(TargetBrandEdit,OpenBrandSuggestions,CloseBrandSuggestions);
        editorSaveTimer.Tick+=(_,_)=> { editorSaveTimer.Stop();if(displayed is not null && displayed.Editable) VM.SaveGroup(displayed); };
        VM.GroupUpdated+=group=> { if(group.TaskState==GroupTaskState.Completed && group.TaskKind is "归档" or "恢复归档" or "撤销") _=RefreshCategoryCatalogAsync(true); if(VM.Selected!=group) return; if(group.TaskState is GroupTaskState.Completed or GroupTaskState.Failed or GroupTaskState.Canceled) RenderSelection(); };
        VM.StructureUpdated+=()=> { if(displayed!=VM.Selected) { FlushEditor();RenderSelection(); } };
        VM.PhotoUpdated+=photo=> { if(currentPhoto?.AssetId==photo.AssetId && MainImage.Source is null) _=ShowPhotoAsync(currentPhoto); };
        AddShortcut(VirtualKey.Enter, VirtualKeyModifiers.Control, Archive_Click);
        AddShortcut(VirtualKey.Left, VirtualKeyModifiers.Menu, Previous_Click);
        AddShortcut(VirtualKey.Right, VirtualKeyModifiers.Menu, Next_Click);
        AddShortcut(VirtualKey.Z, VirtualKeyModifiers.Control, Undo_Click);
        App.MainWindow!.Closed += (_, _) => { categoryClosed=true;categoryCancellation?.Cancel();previewCancellation?.Cancel();FlushEditor();VM.Close();CrashJournal.Current?.Complete(); };
        RenderSelection();
        _ = InitializeNamingGuideAsync();
        VM.PropertyChanged+=(_,args)=> { if(args.PropertyName==nameof(VM.Selected)) DispatcherQueue.TryEnqueue(()=> { if(!rendering && displayed!=VM.Selected) { FlushEditor();RenderSelection(); } }); };
#if DEBUG
        var args=Environment.GetCommandLineArgs(); var verifyIndex=Array.IndexOf(args,"--verify");
        if(verifyIndex>=0 && verifyIndex+1<args.Length) _=RunVerificationAsync(args[verifyIndex+1]);
        var categoryVerifyIndex=Array.IndexOf(args,"--verify-categories");
        if(categoryVerifyIndex>=0 && categoryVerifyIndex+1<args.Length) _=RunCategoryVerificationAsync(args[categoryVerifyIndex+1]);
        var visualIndex=Array.IndexOf(args,"--verify-visual");
        if(visualIndex>=0 && visualIndex+1<args.Length) _=RunVisualVerificationAsync(args[visualIndex+1]);
        var groundingIndex=Array.IndexOf(args,"--verify-grounding");
        if(groundingIndex>=0 && groundingIndex+1<args.Length) _=RunGroundingDiagnosticAsync(args[groundingIndex+1]);
        var liveIndex=Array.IndexOf(args,"--verify-live");
        if(liveIndex>=0 && liveIndex+1<args.Length) _=RunLiveVerificationAsync(args[liveIndex+1]);
        var v13Index=Array.IndexOf(args,"--verify-v13");
        if(v13Index>=0 && v13Index+1<args.Length) _=RunV13VerificationAsync(args[v13Index+1]);
        var usabilityIndex=Array.IndexOf(args,"--verify-usability");
        if(usabilityIndex>=0 && usabilityIndex+1<args.Length) _=RunUsabilityVerificationAsync(args[usabilityIndex+1]);
        var crashIndex=Array.IndexOf(args,"--verify-crash");
        if(crashIndex>=0 && crashIndex+1<args.Length && VM.Groups.Count==0 && Path.GetFullPath(ViewModels.MainViewModel.DataDirectory).StartsWith(Path.GetFullPath(args[crashIndex+1])+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            DispatcherQueue.TryEnqueue(()=> { CrashJournal.Trace("Verification.BeforeSyntheticCrash");CrashJournal.Current?.Flush();throw new InvalidOperationException("Synthetic crash for isolated diagnostics verification"); });
        var namingVerifyIndex=Array.IndexOf(args,"--verify-naming");
        if(namingVerifyIndex>=0 && namingVerifyIndex+1<args.Length) _=RunNamingVerificationAsync(args[namingVerifyIndex+1]);
#endif
    }
    private void AddShortcut(VirtualKey key, VirtualKeyModifiers modifiers, RoutedEventHandler action)
    {
        var shortcut = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        shortcut.Invoked += (_, args) => { action(this, new RoutedEventArgs());args.Handled=true; };
        KeyboardAccelerators.Add(shortcut);
    }
    private async Task RunAsync(Func<CancellationToken,Task> task)
    {
        if (VM.Busy) return; FlushEditor(); VM.Busy = true; Notice.Severity = InfoBarSeverity.Informational;
        using var cts = new CancellationTokenSource(); VM.Cancellation = cts;
        try { await task(cts.Token); }
        catch (OperationCanceledException) { VM.Status = "任务已取消。已完成的文件操作保留记录，可恢复或撤销。"; }
        catch (Exception e) { VM.Status = e.Message; Notice.Severity = InfoBarSeverity.Warning; }
        finally { VM.Busy = false; VM.Cancellation = null; VM.Save(); RenderSelection(); }
    }
    private void FlushEditor()
    {
        if (rendering || displayed is null || !displayed.Editable) return;
        var oldIdentity=displayed.Confirmed.NamingKey;
        displayed.Confirmed = new() { Brand=BrandEdit.Text.Trim(), Series=SeriesEdit.Text.Trim(), Variant=VariantEdit.Text.Trim(), Body=BodyEdit.Text.Trim(), OfficialName=OfficialNameEdit.Text.Trim(), Generation=GenerationEdit.Text.Trim(), Code=CodeEdit.Text.Trim(), Phase=PhaseEdit.Text.Trim(), Package=PackageEdit.Text.Trim(),ProductionYears=YearsEdit.Text.Trim() };
        if(oldIdentity!=displayed.Confirmed.NamingKey && displayed.Verification is not null) { displayed.VerificationHistory.Add(displayed.Verification);displayed.Verification=null; }
        // Archive text is committed exclusively by native UserInput handlers or explicit category selection.
        // A focus/selection transition must not write programmatically loaded or stale text back to the group.
        displayed.Refresh();
    }
    private void RenderSelection()
    {
        var previousGroup=displayed;
        CloseFolderSuggestions();CloseBrandSuggestions();
        rendering = true;
        try
        {
            displayed = VM.Selected; var car = displayed?.Confirmed ?? new();
            BrandEdit.Text=car.Brand; SeriesEdit.Text=car.Series; VariantEdit.Text=car.Variant; BodyEdit.Text=car.Body;OfficialNameEdit.Text=car.OfficialName;
            GenerationEdit.Text=car.Generation; CodeEdit.Text=car.Code; PhaseEdit.Text=car.Phase; PackageEdit.Text=car.Package;
            YearsEdit.Text=car.ProductionYears;
            FolderEdit.Text=displayed?.FolderName ?? ""; TargetBrandEdit.Text=displayed?.TargetBrand ?? ""; RuleText.Text=displayed?.AppliedRule ?? "";
            ToolTipService.SetToolTip(RuleText,RuleText.Text);
            RootText.Text = VM.State.Settings.ArchiveRoot.Length > 0 ? VM.State.Settings.ArchiveRoot : "尚未选择归档根目录";

            PhotoList.ItemsSource = displayed?.Photos;
            if (displayed is not null && !GroupList.SelectedItems.Contains(displayed)) GroupList.SelectedItem = displayed;
            currentPhoto = displayed?.Photos.FirstOrDefault(p=>p.Id==displayed.LastViewedId) ?? displayed?.Photos.FirstOrDefault(p => p.Id == displayed.RepresentativeId) ?? displayed?.Photos.FirstOrDefault();
            PhotoList.SelectedItem = currentPhoto;
            var result = displayed?.Suggestion;
            VerificationText.Text = displayed?.Archived==true?"已归档 · 撤销后可修改":result is null ? "尚未识别 · 可手动填写" : result.Grounded ? "已获得联网依据 · 等待你的最终复核" : "未完成查证 · 请核对不确定项";
            ObservationsText.Text = result?.Observations ?? "";
            UncertaintyText.Text = result is null ? "" : result.Uncertainties + (result.Alternatives.Count > 0 ? "\n备选："+string.Join("；",result.Alternatives) : "");
            ResearchText.Text = result?.Research ?? ""; QueriesText.Text = result is null ? "" : "实际搜索词：\n"+string.Join("\n",result.Queries);
            SourceList.ItemsSource = result?.Sources;
            UsageText.Text = result is null ? "" : $"{result.Model}\n输入 {Token(result,"total_input_tokens")} / 输出 {Token(result,"total_output_tokens")} / 思考 {result.ThinkingTokens?.ToString()??"未知"} tokens\n实际搜索 {result.SearchCount?.ToString()??"未知"} 次 · {result.ActualSearch}\n{result.CreatedAt:yyyy-MM-dd HH:mm}";
            SearchWidgetExpander.Visibility = result?.SearchSuggestionsHtml.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (result?.SearchSuggestionsHtml.Length > 0) _ = ShowWidgetAsync(result.SearchSuggestionsHtml);
            RenderVerification();
            UpdateExistingCategories();
            UpdateTargetPath();
        }
        finally { rendering = false; }
        _ = RefreshCategoryCatalogAsync();
        if(displayed!=previousGroup) FocusCurrentGroup();
        _ = ShowPhotoAsync(currentPhoto);
    }
    private async Task ShowWidgetAsync(string html)
    {
        try
        {
            await SearchWidget.EnsureCoreWebView2Async();
            if (!widgetReady)
            {
                widgetReady = true; SearchWidget.CoreWebView2.Settings.IsScriptEnabled = false;
                SearchWidget.CoreWebView2.Settings.AreDevToolsEnabled = false;
                SearchWidget.CoreWebView2.NavigationStarting += async (_, args) =>
                {
                    if (args.Uri.StartsWith("about:",StringComparison.OrdinalIgnoreCase)) return;
                    args.Cancel = true;
                    if (Uri.TryCreate(args.Uri,UriKind.Absolute,out var uri) && uri.Scheme is "http" or "https") await Launcher.LaunchUriAsync(uri);
                };
            }
            SearchWidget.NavigateToString("<!doctype html><html><head><meta charset='utf-8'><meta http-equiv='Content-Security-Policy' content=\"default-src 'none'; style-src 'unsafe-inline'; img-src https: data:;\"></head><body>"+html+"</body></html>");
        }
        catch { SearchWidgetExpander.Visibility = Visibility.Collapsed; VM.Status = "搜索建议组件无法显示；来源链接仍可查看。请检查 WebView2 Runtime。"; }
    }
    private async Task ShowPhotoAsync(Photo? photo)
    {
        previewCancellation?.Cancel();previewCancellation?.Dispose();previewCancellation=new();var ct=previewCancellation.Token;
        var version = ++previewVersion; CropOutline.Visibility = Visibility.Collapsed;
        PhotoCaption.Text = photo is null ? "支持拖入 JPG、PNG、WebP、HEIC/HEIF" : photo.Caption + (photo.Excluded ? " · 已排除" : "");
        if (photo is null) { MainImage.Source = null; EmptyPreview.Visibility = Visibility.Visible; EmptyPreview.Text = "拖入一组照片，或批量导入文件夹"; return; }
        MainImage.Source=null;EmptyPreview.Visibility=Visibility.Visible;EmptyPreview.Text=photo.Error.Length>0?photo.Error:"正在加载预览…";
        var thumbnailPath=photo.ThumbnailPath;
        if(!PhotoService.CurrentThumbnail(thumbnailPath) && File.Exists(photo.Path))
            thumbnailPath=File.Exists(photo.Path)?await VM.Images.EnsureThumbnailAsync(photo,ct):"";
        if(thumbnailPath.Length>0 && File.Exists(thumbnailPath)) { MainImage.Source=new BitmapImage(new Uri(thumbnailPath));EmptyPreview.Visibility=Visibility.Collapsed; }
        try
        {
            await Task.Delay(100,ct);
            var preview=await VM.Images.EnsurePreviewAsync(photo,ct);ct.ThrowIfCancellationRequested();if(preview.Length==0) throw new IOException(photo.Error);
            var file = await StorageFile.GetFileFromPathAsync(preview); using var stream = await file.OpenReadAsync();
            var image = new BitmapImage(); await image.SetSourceAsync(stream);
            if (version != previewVersion) return;
            MainImage.Source = image;PhotoCaption.Text=photo.Caption; EmptyPreview.Visibility = Visibility.Collapsed; DrawSavedCrop();
            if(photo.Asset is not null) VM.SaveAsset(photo.Asset);
            var next=displayed?.Photos.SkipWhile(p=>p.Id!=photo.Id).Skip(1).FirstOrDefault();if(next is not null && version==previewVersion) _=PrefetchAsync(next,ct);
        }
        catch(OperationCanceledException) { }
        catch { if (version == previewVersion) { MainImage.Source = null; EmptyPreview.Visibility = Visibility.Visible; EmptyPreview.Text = photo.Error.Length>0?photo.Error:"预览暂不可用，可重选照片重试；原图未修改。"; } }
    }
    private async Task PrefetchAsync(Photo photo,CancellationToken ct) { try { await Task.Delay(150,ct);await VM.Images.EnsurePreviewAsync(photo,ct); } catch { } }
    private void Group_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (rendering) return;
        var group = e.AddedItems.OfType<PhotoGroup>().LastOrDefault(); if (group is null || group == displayed) return;
        editorSaveTimer.Stop();FlushEditor();if(displayed is not null) VM.SaveGroup(displayed);VM.MarkTouched(group); VM.Selected = group; VM.SaveGroup(group); RenderSelection();
    }
    private void Photo_Changed(object sender, SelectionChangedEventArgs e)
    {
        foreach(var changedPhoto in e.RemovedItems.OfType<Photo>().Concat(e.AddedItems.OfType<Photo>())) UpdateThumbnailCaptionSelection(changedPhoto);
        if (rendering) return;
        FocusCurrentGroup();
        var photo = e.AddedItems.OfType<Photo>().LastOrDefault(); if (photo is not null && displayed is not null) { currentPhoto = photo;VM.SelectPhoto(displayed,photo); _ = ShowPhotoAsync(photo); }
    }
    private void Editor_Changed(object sender,TextChangedEventArgs e) { if(!initialized || rendering || displayed?.Editable!=true) return;CrashJournal.Trace("Editor.Change",displayed.Id);FlushEditor();if(ReferenceEquals(sender,TargetBrandEdit)) { CloseFolderSuggestions();UpdateExistingCategories();UpdateTargetPath(); } VM.MarkTouched(displayed);editorSaveTimer.Stop();editorSaveTimer.Start(); }
    private void Existing_Changed(object sender, SelectionChangedEventArgs e) { if (!rendering && displayed?.Editable==true && ExistingBox.SelectedItem is string name) { ApplyCategory(new(TargetBrandEdit.Text.Trim(),name,"",true)); } }
    private void TargetBrand_LostFocus(object sender,RoutedEventArgs e)
    {
        if(rendering) return;
        try { FlushEditor(); UpdateExistingCategories();UpdateTargetPath(); VM.Save(); }
        catch(Exception error) { VM.Status=error.Message; }
    }

    private async Task<IReadOnlyList<string>> PickPhotosAsync()
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.Thumbnail }; WinRT.Interop.InitializeWithWindow.Initialize(picker,WindowHandle);
        foreach (var ext in PhotoService.Extensions) picker.FileTypeFilter.Add(ext);
        return (await picker.PickMultipleFilesAsync()).Select(f=>f.Path).ToList();
    }
    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker(); picker.FileTypeFilter.Add("*"); WinRT.Interop.InitializeWithWindow.Initialize(picker,WindowHandle);
        return (await picker.PickSingleFolderAsync())?.Path;
    }
    private async void ImportGroup_Click(object sender, RoutedEventArgs e) => await RunAsync(async ct => { var paths=await PickPhotosAsync(); if(paths.Count>0) await VM.ImportAsync(paths,true,ct); });
    private async void ImportBatch_Click(object sender, RoutedEventArgs e) => await RunAsync(async ct => {
        var folder=await PickFolderAsync();if(folder is null || !await BatchOptionsAsync()) return;
        var files=await Task.Run(()=>Directory.EnumerateFiles(folder).ToList(),ct);await VM.ImportAsync(files,false,ct);
    });
    private async Task<bool> BatchOptionsAsync()
    {
        var gap=new NumberBox { Header="时间分组间隔（秒）",Value=VM.State.Settings.GroupGapSeconds,Minimum=1,Maximum=3600,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact };
        if(await DialogAsync("批量导入",gap,"导入",null,"取消")!=ContentDialogResult.Primary) return false;
        if(double.IsNaN(gap.Value)) throw new ArgumentException("请填写有效的分组间隔。");VM.State.Settings.GroupGapSeconds=(int)gap.Value;VM.SaveHeader();return true;
    }
    private async void DeleteImportedGroup_Click(object sender,RoutedEventArgs e)
    {
        if(VM.Busy) return;
        var groups=GroupList.SelectedItems.OfType<PhotoGroup>().Distinct().ToList();
        if(groups.Count==0 || groups.Any(group=>group.IsWorking)) return;
        var names=string.Join("\n",groups.Take(8).Select(group=>"• "+group.Title));
        if(groups.Count>8) names+=$"\n…以及另外 {groups.Count-8} 个分组";
        var confirmation=new TextBlock
        {
            Text=$"将从分组列表和本地记录中移除选中的 {groups.Count} 个分组：\n{names}\n\n不会删除源照片或已归档目录，也不会影响其他分组。删除后无法通过本应用撤销这些分组的归档。",
            TextWrapping=TextWrapping.Wrap
        };
        if(await DialogAsync("删除选中分组",confirmation,"删除")!=ContentDialogResult.Primary) return;
        try { editorSaveTimer.Stop();FlushEditor();VM.RemoveGroups(groups);RenderSelection(); }
        catch(Exception error) { VM.Status=error.Message; }
    }
    private async void DeleteAllArchivedGroups_Click(object sender,RoutedEventArgs e)
    {
        if(VM.Busy) return;
        var groups=VM.Groups.Where(group=>group.Archived).ToList();
        if(groups.Count==0) return;
        var confirmation=new TextBlock
        {
            Text=$"将移除全部 {groups.Count} 个已归档分组记录。不会删除原照片或已归档目录。移除后，无法再通过本应用撤销这些分组的归档。",
            TextWrapping=TextWrapping.Wrap
        };
        if(await DialogAsync("删除所有已归档分组",confirmation,"删除全部")!=ContentDialogResult.Primary) return;
        try { editorSaveTimer.Stop();FlushEditor();VM.RemoveGroups(groups);RenderSelection(); }
        catch(Exception error) { VM.Status=error.Message; }
    }
    private async void ChooseRoot_Click(object sender, RoutedEventArgs e) => await RunAsync(async _ => { var root=await PickFolderAsync(); if(root is not null) { VM.State.Settings.ArchiveRoot=root; VM.Save(); VM.Status="归档根目录已选择；已有目录不会被重命名。"; } });
    private void Page_DragOver(object sender, DragEventArgs e) { if(!VM.Busy && e.DataView.Contains(StandardDataFormats.StorageItems)) { e.AcceptedOperation=DataPackageOperation.Copy; e.DragUIOverride.Caption="导入照片（原图暂不移动）"; } }
    private async void Page_Drop(object sender, DragEventArgs e)
    {
        if(VM.Busy || !e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var deferral=e.GetDeferral();
        try
        {
            var items=await e.DataView.GetStorageItemsAsync(); var paths=new List<string>();
            foreach(var item in items) { if(item is StorageFile f) paths.Add(f.Path); else if(item is StorageFolder folder) paths.AddRange(Directory.EnumerateFiles(folder.Path)); }
            var choice=await DialogAsync("如何导入这些照片？",new TextBlock { Text="同一辆车请选择“一组”；包含多辆车请选择“批量分组”。",TextWrapping=TextWrapping.Wrap },"作为一组","批量分组","取消");
            if(choice!=ContentDialogResult.None && (choice==ContentDialogResult.Primary || await BatchOptionsAsync())) await RunAsync(ct=>VM.ImportAsync(paths,choice==ContentDialogResult.Primary,ct));
        }
        catch(Exception error) { VM.Status=error.Message; }
        finally { deferral.Complete(); }
    }
    private async void Recognize_Click(object sender,RoutedEventArgs e) => await QueryGroupAsync(false);
    private async void Deep_Click(object sender,RoutedEventArgs e) => await QueryGroupAsync(true);
    private async Task QueryGroupAsync(bool deep)
    {
        try { FlushEditor();var group=RequireGroup();if(!group.Editable) throw new InvalidOperationException("本组正在执行任务或已归档。");if(deep && group.Confirmed.Series.Length==0 && group.Confirmed.Variant.Length==0) throw new InvalidOperationException("请先联网识别或填写待查证车型。");var settings=VM.CaptureSettings();VM.SaveGroup(group);await VM.RunGroupAsync(group,deep?"深入查证":"联网识别",false,ct=>VM.RecognizeAsync(group,deep,ct,settings));if(VM.Selected==group && narrowLayout && group.Suggestion is not null) PaneBox.SelectedIndex=2; }
        catch(Exception error) { VM.Status=error.Message; }
    }
    private void Cancel_Click(object sender,RoutedEventArgs e) { if(VM.Selected is not null) VM.Tasks.Cancel(VM.Selected.Id); }
    private PhotoGroup RequireGroup() => VM.Selected ?? throw new InvalidOperationException("请先导入并选择一组照片。");
    private void ConfirmGroup_Click(object sender,RoutedEventArgs e) => LocalAction(()=> { var g=RequireGroup(); if(g.Archived) return; g.GroupConfirmed=true; g.State="待识别 · 分组已确认"; VM.Status="本组已确认，可开始联网识别。"; });
    private async void Generate_Click(object sender,RoutedEventArgs e)
    {
        try { FlushEditor();var group=RequireGroup();var revision=group.Revision;await VM.PrepareNamingGuideAsync();if(group.Revision!=revision || !group.Editable) { VM.Status="分组已改变，请重新生成名称。";return; }group.PreviousFolderName=group.FolderName;VM.GenerateName(group);VM.SaveGroup(group);if(VM.Selected==group) RenderSelection(); }
        catch(Exception error) { VM.Status="本地命名失败："+error.Message; }
    }
    private void RestoreName_Click(object sender,RoutedEventArgs e) => LocalAction(()=> { var g=RequireGroup();if(!g.Editable) return;var old=g.FolderName;g.FolderName=g.PreviousFolderName;g.PreviousFolderName=old;g.ManualName=true;VM.SaveGroup(g); });
    private void Duplicate_Click(object sender,RoutedEventArgs e) => LocalAction(()=>VM.Duplicate(RequireGroup()));
    private void Merge_Click(object sender,RoutedEventArgs e) => LocalAction(()=>VM.Merge(GroupList.SelectedItems.OfType<PhotoGroup>().OrderBy(g=>VM.Groups.IndexOf(g)).ToList()));
    private void Split_Click(object sender,RoutedEventArgs e) => LocalAction(()=>VM.Split(RequireGroup(),PhotoList.SelectedItems.OfType<Photo>().ToList()));
    private void Exclude_Click(object sender,RoutedEventArgs e) => LocalAction(()=> { var g=RequireGroup(); if(!g.Editable) throw new InvalidOperationException("本组暂不可编辑。"); foreach(var p in PhotoList.SelectedItems.OfType<Photo>()) p.Excluded=!p.Excluded;VM.MarkTouched(g); InvalidateEvidence(g,"照片范围已改变"); });
    private void Representative_Click(object sender,RoutedEventArgs e) => LocalAction(()=> { if(currentPhoto is not null) { RequireGroup().RepresentativeId=currentPhoto.Id; VM.Status="代表图已设置；识别时优先使用这张照片。"; } });
    private async void OpenPhoto_Click(object sender,RoutedEventArgs e) => await RunAsync(async _=> { if(currentPhoto is not null) await Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(currentPhoto.Path)); });
    private void Previous_Click(object sender,RoutedEventArgs e) => LocalAction(()=>VM.Navigate(-1));
    private void Next_Click(object sender,RoutedEventArgs e) => LocalAction(()=>VM.Navigate(1));
    private void LocalAction(Action action)
    {
        CrashJournal.Trace("UI."+action.Method.Name,displayed?.Id);editorSaveTimer.Stop();FlushEditor();
        try { if(displayed is not null) VM.SaveGroup(displayed);action();if(VM.Selected is not null) VM.SaveGroup(VM.Selected); RenderSelection(); } catch(Exception e) { VM.Status=e.Message; Notice.Severity=InfoBarSeverity.Warning; }
    }
    private async void Learn_Click(object sender,RoutedEventArgs e)
    {
        if(VM.Busy) return;
        try { FlushEditor(); var g=RequireGroup(); NamingService.SafeSegment(g.FolderName); if(g.Confirmed.Brand.Length==0 || g.Confirmed.Series.Length==0) throw new InvalidOperationException("学习前请填写品牌、车系和目录名称。"); var rule=VM.Learn(g,true); VM.Status="已记住此车型名称。"; await ConsiderRuleAsync(rule); RenderSelection(); } catch(Exception error) { VM.Status=error.Message; }
    }
    private async Task ConsiderRuleAsync(IReadOnlyList<LocalNamingRule> rules)
    {
        foreach(var rule in rules.Where(r=>r.Conflict.Length==0))
            if(await DialogAsync("采用这条本地命名习惯？",Paragraph(rule.Description+"\n依据 "+rule.SampleIds.Count+" 条已确认样本。只影响相应范围，可在命名学习中撤回。"),"采用",null,"稍后处理")==ContentDialogResult.Primary)
            { LocalNamingLearning.ValidateRule(rule);rule.Approved=true;LocalNamingLearning.Reconcile(VM.State);VM.SaveHeader(); }
    }
    private async void Archive_Click(object sender,RoutedEventArgs e)
    {
        FlushEditor();
        try
        {
            var group=RequireGroup();if(!group.Editable) throw new InvalidOperationException("本组正在执行任务或已归档。"); NamingService.SafeSegment(group.FolderName); NamingService.SafeSegment(group.TargetBrand);
            if(VM.State.Settings.ArchiveRoot.Length==0) throw new InvalidOperationException("请先选择归档根目录。");
            var root=VM.State.Settings.ArchiveRoot;var target=Path.Combine(root,group.TargetBrand,group.FolderName);
            var message=$"归档 {group.Photos.Count(p=>!p.Excluded)} 张原照片到：\n{target}\n\n"+(group.Suggestion?.Grounded==true ? "请确认车型与照片分组正确。" : "此组尚未完成联网查证。确认表示你已人工核对分类与分组。")+"\n共享原图保留至所有使用它的分组归档。可通过操作记录撤销。";
            if(await DialogAsync("确认这次分类并移动",new TextBlock { Text=message,TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true },"确认并移动",null,"返回修改")!=ContentDialogResult.Primary) return;
            await VM.RunGroupAsync(group,"归档",true,ct=>VM.ArchiveAsync(group,ct,root));
            if(group.Archived) { await ConsiderRuleAsync(VM.Learn(group,false));await VM.EnsureNamingGuideAsync(true); _=RefreshCategoryCatalogAsync(); }
        }
        catch(Exception error) { VM.Status=error.Message; Notice.Severity=InfoBarSeverity.Warning; }
    }
    private async void Undo_Click(object sender,RoutedEventArgs e)
    {
        try { FlushEditor();var job=VM.Store.Jobs().FirstOrDefault(j=>j.Direction=="Archive" && j.Status=="Completed") ?? throw new InvalidOperationException("没有可撤销的归档。");var group=VM.Groups.First(g=>g.Id==job.GroupId);await VM.RunGroupAsync(group,"撤销",true,ct=>VM.ResumeOrUndoAsync(job,true,ct)); }
        catch(Exception error) { VM.Status=error.Message; }
    }
    private Task<ContentDialogResult> DialogAsync(string title,object content,string? primary=null,string? secondary=null,string close="关闭")
        => new ContentDialog { XamlRoot=XamlRoot,Title=title,Content=content,PrimaryButtonText=primary??"",SecondaryButtonText=secondary??"",CloseButtonText=close,DefaultButton=ContentDialogButton.Close }.ShowAsync().AsTask();
    private async void Queue_Click(object sender,RoutedEventArgs e)
    {
        if(VM.Busy) return;
        var list=new ListView { ItemsSource=VM.Groups.Select(g=>g.Title+" · "+g.Subtitle).ToList(),SelectionMode=ListViewSelectionMode.Single,MaxHeight=500,MinWidth=300 };
        if(await DialogAsync("选择照片分组",list,"打开")==ContentDialogResult.Primary && list.SelectedIndex>=0) LocalAction(()=>VM.Selected=VM.Groups[list.SelectedIndex]);
    }
    private void Body_SizeChanged(object sender,SizeChangedEventArgs e)
    {
        var narrow=e.NewSize.Width<920; narrowLayout=narrow;
        QueueColumn.Width=new GridLength(narrow?0:e.NewSize.Width<1150?180:210);
        EditorColumn.Width=new GridLength(narrow?0:e.NewSize.Width<1150?320:350);
        NarrowRow.Height=new GridLength(0); PaneBox.Visibility=narrow?Visibility.Visible:Visibility.Collapsed;
        Grid.SetColumn(EditorPane,narrow?1:2); Grid.SetRow(EditorPane,0); Grid.SetColumn(QueuePane,narrow?1:0);
        ApplyPaneVisibility();
        ResizeThumbnails(e.NewSize.Height);
        DrawSavedCrop();
    }
    private void Pane_Changed(object sender,SelectionChangedEventArgs e) { if(QueuePane is not null) ApplyPaneVisibility(); }
    private void ApplyPaneVisibility()
    {
        QueuePane.Visibility=!narrowLayout || PaneBox.SelectedIndex==0?Visibility.Visible:Visibility.Collapsed;
        PreviewPane.Visibility=!narrowLayout || PaneBox.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;
        EditorPane.Visibility=!narrowLayout || PaneBox.SelectedIndex==2?Visibility.Visible:Visibility.Collapsed;
        if(QueuePane.Visibility==Visibility.Visible) FocusCurrentGroup();
    }
    private Rect ImageRect()
    {
        if(MainImage.Source is not BitmapImage image || image.PixelWidth==0 || image.PixelHeight==0) return new();
        var ratio=Math.Min(PreviewHost.ActualWidth/image.PixelWidth,PreviewHost.ActualHeight/image.PixelHeight);
        var w=image.PixelWidth*ratio; var h=image.PixelHeight*ratio;
        return new((PreviewHost.ActualWidth-w)/2,(PreviewHost.ActualHeight-h)/2,w,h);
    }
    private void CropToggle_Click(object sender,RoutedEventArgs e) { VM.Status=""; }
    private void Crop_Pressed(object sender,PointerRoutedEventArgs e)
    {
        if(CropToggle.IsChecked!=true || currentPhoto is null || displayed?.Editable!=true) return;
        var p=e.GetCurrentPoint(PreviewHost); var rect=ImageRect(); if(!p.Properties.IsLeftButtonPressed || !rect.Contains(p.Position)) return;
        cropStart=p.Position; drawing=true; PreviewHost.CapturePointer(e.Pointer); DrawBox(cropStart,cropStart); e.Handled=true;
    }
    private Point ClampPoint(Point p) { var r=ImageRect(); return new(Math.Clamp(p.X,r.X,r.Right),Math.Clamp(p.Y,r.Y,r.Bottom)); }
    private void Crop_Moved(object sender,PointerRoutedEventArgs e) { if(drawing) DrawBox(cropStart,ClampPoint(e.GetCurrentPoint(PreviewHost).Position)); }
    private void DrawBox(Point a,Point b) { Canvas.SetLeft(CropOutline,Math.Min(a.X,b.X)); Canvas.SetTop(CropOutline,Math.Min(a.Y,b.Y)); CropOutline.Width=Math.Abs(a.X-b.X); CropOutline.Height=Math.Abs(a.Y-b.Y); CropOutline.Visibility=Visibility.Visible; }
    private void Crop_Released(object sender,PointerRoutedEventArgs e)
    {
        if(!drawing || currentPhoto is null) return; drawing=false; var end=ClampPoint(e.GetCurrentPoint(PreviewHost).Position); PreviewHost.ReleasePointerCapture(e.Pointer);
        var r=ImageRect(); if(r.Width<=0 || Math.Abs(end.X-cropStart.X)<8 || Math.Abs(end.Y-cropStart.Y)<8) { DrawSavedCrop(); return; }
        currentPhoto.Crop=new((Math.Min(cropStart.X,end.X)-r.X)/r.Width,(Math.Min(cropStart.Y,end.Y)-r.Y)/r.Height,Math.Abs(end.X-cropStart.X)/r.Width,Math.Abs(end.Y-cropStart.Y)/r.Height);
        CrashJournal.Trace("Photo.Crop",displayed?.Id);
        if(displayed is not null) InvalidateEvidence(displayed,"目标框选已改变");
        if(displayed is not null) { VM.MarkTouched(displayed);VM.SaveGroup(displayed);VerificationText.Text="目标已改变，需要重新识别或人工核对。"; } PhotoCaption.Text=currentPhoto.Caption; VM.Status="目标已框选；点击联网识别重新分析。";
    }
    private void Crop_Canceled(object sender,PointerRoutedEventArgs e) { drawing=false; DrawSavedCrop(); }
    private void DrawSavedCrop() { if(CropOutline is null) return; if(currentPhoto?.Crop is not { } c) { CropOutline.Visibility=Visibility.Collapsed; return; } var r=ImageRect(); DrawBox(new(r.X+c.X*r.Width,r.Y+c.Y*r.Height),new(r.X+(c.X+c.Width)*r.Width,r.Y+(c.Y+c.Height)*r.Height)); }
    private void ClearCrop_Click(object sender,RoutedEventArgs e) => LocalAction(()=> { if(currentPhoto is not null && displayed?.Editable==true) { currentPhoto.Crop=null; InvalidateEvidence(displayed,"目标框选已改变");VM.MarkTouched(displayed); } });
    private static void InvalidateEvidence(PhotoGroup group,string reason)
    {
        if(group.Suggestion is not null) { group.Suggestion.Conflict=true; group.Suggestion.Uncertainties+="\n"+reason+"，原识别结果仅供参考，需要重新识别或人工核对。"; }
        if(group.Verification is not null) { group.VerificationHistory.Add(group.Verification);group.Verification=null; }
        group.State=reason+" · 请复核";
    }
}
