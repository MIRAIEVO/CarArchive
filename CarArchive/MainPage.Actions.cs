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
        if (initialized) return; initialized = true;
        ApplyAppearance();StartCrashHeartbeat();
        CrashJournal.Trace("Page.Loaded");CrashJournal.Current?.Flush();
        App.MainWindow!.FitToDisplay(XamlRoot.RasterizationScale);
        InteractionSettings.Normalize(VM.State.Settings);
        UpdateModePresentation();
        WireSuggestions(FolderEdit,OpenFolderSuggestions,CloseFolderSuggestions);
        WireSuggestions(TargetBrandEdit,OpenBrandSuggestions,CloseBrandSuggestions);
        editorSaveTimer.Tick+=(_,_)=> { editorSaveTimer.Stop();if(displayed is not null && displayed.Editable) VM.SaveGroup(displayed); };
        VM.GroupUpdated+=group=> { if(group.TaskState==GroupTaskState.Completed && group.TaskKind is "归档" or "恢复归档" or "撤销") _=RefreshCategoryCatalogAsync(true); if(VM.Selected!=group) return; RefreshActionPresentation();UpdateMergeSelection();if(group.TaskState is GroupTaskState.Completed or GroupTaskState.Failed or GroupTaskState.Canceled) DispatcherQueue.TryEnqueue(()=> { if(!categoryClosed && VM.Selected==group) RenderSelection(); }); };
        VM.StructureUpdated+=()=> { if(displayed!=VM.Selected) { FlushEditor();RenderSelection(); } };
        VM.PhotoUpdated+=photo=> { if(currentPhoto?.AssetId==photo.AssetId && MainImage.Source is null) _=ShowPhotoAsync(currentPhoto); };
        PreviewKeyDown+=Commands_KeyDown;
        RefreshCommandHints();InitializeContextMenus();InitializeMergeTracking();InitializeActionPalette();
        _=PrepareIndependentPhotosAsync();_ = WarmClipboardAsync();
        App.MainWindow!.Closed += (_, _) => { categoryClosed=true;categoryCancellation?.Cancel();previewCancellation?.Cancel();FlushEditor();VM.Close();CrashJournal.Current?.Complete(); };
        RenderSelection();
        _ = InitializeNamingGuideAsync();
        VM.PropertyChanged+=(_,args)=> { if(args.PropertyName==nameof(VM.Selected)) DispatcherQueue.TryEnqueue(()=> { if(!rendering && displayed!=VM.Selected) { FlushEditor();RenderSelection(); } }); };
#if DEBUG
        var args=Environment.GetCommandLineArgs(); var verifyIndex=Array.IndexOf(args,"--verify");
        var appearanceIndex=Array.IndexOf(args,"--verify-appearance133");
        if(appearanceIndex>=0 && appearanceIndex+1<args.Length)_=VerifyAppearance133Async(args[appearanceIndex+1]);
        if(verifyIndex>=0 && verifyIndex+1<args.Length) _=RunVerificationAsync(args[verifyIndex+1]);
        var categoryVerifyIndex=Array.IndexOf(args,"--verify-categories");
        if(categoryVerifyIndex>=0 && categoryVerifyIndex+1<args.Length) _=RunCategoryVerificationAsync(args[categoryVerifyIndex+1]);
        var visualIndex=Array.IndexOf(args,"--verify-visual");
        var strengthIndex=Array.IndexOf(args,"--verify-strength13");
        if(strengthIndex>=0 && strengthIndex+1<args.Length) _=VerifyStrength13Async(args[strengthIndex+1]);
        var archiveIndex=Array.IndexOf(args,"--verify-archive132");
        if(archiveIndex>=0 && archiveIndex+1<args.Length) _=VerifyArchive132Async(args[archiveIndex+1]);
        if(visualIndex>=0 && visualIndex+1<args.Length) _=RunVisualVerificationAsync(args[visualIndex+1]);
        var groundingIndex=Array.IndexOf(args,"--verify-grounding");
        if(groundingIndex>=0 && groundingIndex+1<args.Length) _=RunGroundingDiagnosticAsync(args[groundingIndex+1]);
        var liveIndex=Array.IndexOf(args,"--verify-live");
        if(liveIndex>=0 && liveIndex+1<args.Length) _=RunLiveVerificationAsync(args[liveIndex+1]);
        var abIndex=Array.IndexOf(args,"--verify-token-ab");
        if(abIndex>=0 && abIndex+1<args.Length) _=RunTokenAbVerificationAsync(args[abIndex+1]);
        var manualIndex=Array.IndexOf(args,"--manual-125");
        if(manualIndex>=0&&manualIndex+1<args.Length)_=PrepareManual125Async(args[manualIndex+1]);
        var flashIndex=Array.IndexOf(args,"--verify-no-flash");
        if(flashIndex>=0&&flashIndex+1<args.Length)_=VerifyNoFlashAsync(args[flashIndex+1]);
        var polishIndex=Array.IndexOf(args,"--verify-polish125");
        if(polishIndex>=0&&polishIndex+1<args.Length)_=RunPolish125Async(args[polishIndex+1]);
        var v124Index=Array.IndexOf(args,"--verify-v124");
        if(v124Index>=0 && v124Index+1<args.Length) _=RunV124VerificationAsync(args[v124Index+1]);
        var v123Index=Array.IndexOf(args,"--verify-v123");
        if(v123Index>=0 && v123Index+1<args.Length) _=RunV123VerificationAsync(args[v123Index+1]);
        var v121Index=Array.IndexOf(args,"--verify-v121");
        if(v121Index>=0 && v121Index+1<args.Length) _=RunV121VerificationAsync(args[v121Index+1]);
        var v12Index=Array.IndexOf(args,"--verify-v12");
        if(v12Index>=0 && v12Index+1<args.Length) _=RunV12VerificationAsync(args[v12Index+1]);
        var v115Index=Array.IndexOf(args,"--verify-v115");
        if(v115Index>=0 && v115Index+1<args.Length) _=RunV115VerificationAsync(args[v115Index+1]);
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
        finally { VM.Busy = false; VM.Cancellation = null; VM.Save(); RenderSelection(); _=PrepareAfterImportAsync(VM.BackgroundImport); }
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
            displayed = VM.Selected;if(displayed!=previousGroup)BeginThumbnailTransition(); UpdateModePresentation(); var car = displayed?.Confirmed ?? new();
            BrandEdit.Text=car.Brand; SeriesEdit.Text=car.Series; VariantEdit.Text=car.Variant; BodyEdit.Text=car.Body;OfficialNameEdit.Text=car.OfficialName;
            GenerationEdit.Text=car.Generation; CodeEdit.Text=car.Code; PhaseEdit.Text=car.Phase; PackageEdit.Text=car.Package;
            YearsEdit.Text=car.ProductionYears;
            FolderEdit.Text=displayed?.FolderName ?? ""; TargetBrandEdit.Text=displayed?.TargetBrand ?? ""; RuleText.Text=displayed?.AppliedRule ?? "";
            ToolTipService.SetToolTip(RuleText,RuleText.Text);
            RootText.Text = VM.State.Settings.ArchiveRoot.Length > 0 ? VM.State.Settings.ArchiveRoot : "尚未选择归档根目录";

            if(!ReferenceEquals(PhotoList.ItemsSource,displayed?.Photos)) PhotoList.ItemsSource = displayed?.Photos;
            if (displayed is not null && !GroupList.SelectedItems.Contains(displayed)) GroupList.SelectedItem = displayed;
            currentPhoto = displayed?.Photos.FirstOrDefault(p=>p.Id==displayed.LastViewedId) ?? displayed?.Photos.FirstOrDefault(p => p.Id == displayed.RepresentativeId) ?? displayed?.Photos.FirstOrDefault();
            PhotoList.SelectedItem = currentPhoto;
            var result = displayed?.Suggestion;
            VerificationText.Text=displayed?.Archived==true?"已归档":result is not null?"AI识别完成":displayed?.State.Contains("网页 JSON")==true?"JSON 已填充":"尚未识别";
            ObservationsText.Text = result?.Observations ?? "";
            UncertaintyText.Text = result is null ? "" : result.Uncertainties + (result.Alternatives.Count > 0 ? "\n备选："+string.Join("；",result.Alternatives) : "");
            ResearchText.Text = result?.Research ?? ""; QueriesText.Text = result is null ? "" : "实际搜索词：\n"+string.Join("\n",result.Queries);
            SourceList.ItemsSource = result?.Sources;
            UsageText.Text = result is null ? "" : $"{result.Model}\n输入 {RecognitionUsage(result,"total_input_tokens")} / 输出 {RecognitionUsage(result,"total_output_tokens")} / 思考 {result.ThinkingTokens?.ToString()??"未知"} tokens\n实际搜索 {result.SearchCount?.ToString()??"未知"} 次 · {result.ActualSearch}\n{result.CreatedAt:yyyy-MM-dd HH:mm}{RecognitionRoundDetails(result)}";
            if(displayed!=previousGroup || result?.SearchSuggestionsHtml.Length is null or 0)SearchWidgetExpander.IsExpanded=false;
            SearchWidgetExpander.Visibility = result?.SearchSuggestionsHtml.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            RenderVerification();
            UpdateExistingCategories();
            UpdateTargetPath();
        }
        finally { rendering = false;UpdateMergeSelection();RefreshActionPresentation(); }
        _ = RefreshCategoryCatalogAsync();
        if(displayed!=previousGroup) {FocusCurrentGroup();_ = FadeThumbnailsAsync(displayed,thumbnailTransitionVersion);}
        _ = ShowPhotoAsync(currentPhoto);
    }
    private async void SearchWidget_Expanded(Expander sender,ExpanderExpandingEventArgs args)
    {
        await Task.Yield();
        if(displayed?.Suggestion is {SearchSuggestionsHtml.Length:>0} result)await ShowWidgetAsync(result.SearchSuggestionsHtml);
    }
    private async Task ShowWidgetAsync(string html)
    {
        var owner=displayed;
        try
        {
            CrashJournal.Trace("SearchWidget.Initialize",owner?.Id);
            await SearchWidget.EnsureCoreWebView2Async();
            if(categoryClosed || owner!=displayed || !SearchWidgetExpander.IsExpanded)return;
            if (!widgetReady)
            {
                widgetReady = true; SearchWidget.CoreWebView2.Settings.IsScriptEnabled = false;
                SearchWidget.CoreWebView2.Settings.AreDevToolsEnabled = false;
                SearchWidget.CoreWebView2.NavigationStarting += async (_, args) =>
                {
                    if (args.Uri.StartsWith("about:",StringComparison.OrdinalIgnoreCase)) return;
                    args.Cancel = true;
                    if (Uri.TryCreate(args.Uri,UriKind.Absolute,out var uri) && uri.Scheme is "http" or "https")
                        try {await Launcher.LaunchUriAsync(uri);}catch(Exception error){CrashJournal.Fault("SearchWidget.Link",error);}
                };
            }
            SearchWidget.NavigateToString("<!doctype html><html><head><meta charset='utf-8'><meta http-equiv='Content-Security-Policy' content=\"default-src 'none'; style-src 'unsafe-inline'; img-src https: data:;\"></head><body>"+html+"</body></html>");
        }
        catch(Exception error) { CrashJournal.Fault("SearchWidget.Unavailable",error,owner?.Id);if(owner==displayed && !categoryClosed){SearchWidgetExpander.Visibility = Visibility.Collapsed; VM.Status = "搜索建议组件无法显示；来源链接仍可查看。请检查 WebView2 Runtime。";} }
    }
    private int animatedPreviewVersion=-1;
    private string loadedPhotoId="";
    private bool previewLoading;
#if DEBUG
    private Task? previewReadinessGate;
#endif
    private async Task FadeThumbnailsAsync(PhotoGroup? group,int version)
    {
        await NextLayoutFrameAsync();
        for(var i=0;i<30&&version==thumbnailTransitionVersion&&group is {Photos.Count:>0}&&!VisualDescendants(PhotoList).OfType<Image>().Any(image=>image.Source is BitmapImage {PixelHeight:>0});i++)await Task.Delay(16);
        if(version==thumbnailTransitionVersion)await FinishThumbnailTransitionAsync(version);
    }
    private static async Task NextLayoutFrameAsync()
    {
        var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);EventHandler<object>? frame=null;
        frame=(_,_)=>{Microsoft.UI.Xaml.Media.CompositionTarget.Rendering-=frame;ready.TrySetResult();};
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering+=frame;
        try {await Task.WhenAny(ready.Task,Task.Delay(200));}
        finally {Microsoft.UI.Xaml.Media.CompositionTarget.Rendering-=frame;}
    }
    private void RevealPhoto() {if(animatedPreviewVersion==previewVersion)return;animatedPreviewVersion=previewVersion;_ = FinishPreviewTransitionAsync(previewVersion);}
    private async Task ShowPhotoAsync(Photo? photo)
    {
        if(photo is not null && loadedPhotoId==photo.Id && (MainImage.Source is not null || previewLoading)) {DrawSavedCrop();return;}
        loadedPhotoId=photo?.Id??"";previewLoading=photo is not null;
        previewCancellation?.Cancel();previewCancellation?.Dispose();previewCancellation=new();var ct=previewCancellation.Token;
        var version=++previewVersion;
        var owner=displayed?.Id;
        CrashJournal.Trace("Preview.Switch.Begin",owner,version);
        if(drawing){drawing=false;PreviewHost.ReleasePointerCaptures();}
        PreviewHost.IsHitTestVisible=false;
        try
        {
            if(photo is not null)await BeginPreviewTransitionAsync(version);else ClearTransition(PreviewTransitionCover);
        }
        catch(Exception error)
        {
            CrashJournal.Fault("Preview.Transition",error,owner);
            if(version==previewVersion)ClearTransition(PreviewTransitionCover);
        }
        if(version!=previewVersion||ct.IsCancellationRequested)return;
        originalPreviewId=null;MainImage.Visibility=Visibility.Visible;PreviewZoom.ChangeView(0,0,1,true);VideoPlayer.MediaPlayer?.Pause();VideoPlayer.Source=null;VideoPlayer.Visibility=Visibility.Collapsed;
        CropOutline.Visibility = Visibility.Collapsed;
        PhotoCaption.Text = photo is null ? "支持拖入 JPG、PNG、WebP、HEIC/HEIF" : photo.Caption + (photo.Excluded ? " · 已排除" : "");
        if (photo is null) {ClearTransition(PreviewTransitionCover); PreviewHost.IsHitTestVisible=true;MainImage.Source = null; EmptyPreview.Visibility = Visibility.Visible; EmptyPreview.Text = "拖入一组照片，或批量导入文件夹"; return; }
        PreviewHost.IsHitTestVisible=false;
        EmptyPreview.Visibility=MainImage.Source is null?Visibility.Visible:Visibility.Collapsed;EmptyPreview.Text=photo.Error.Length>0?photo.Error:"正在加载预览…";
        var thumbnailPath=photo.ThumbnailPath;
        try {if(!PhotoService.CurrentThumbnail(thumbnailPath) && File.Exists(photo.Path))
            thumbnailPath=await VM.Images.EnsureThumbnailAsync(photo,ct);} catch(OperationCanceledException){return;}
        if(version!=previewVersion || ct.IsCancellationRequested)return;
        try {
        if(thumbnailPath.Length>0 && File.Exists(thumbnailPath)) { var thumbFile=await StorageFile.GetFileFromPathAsync(thumbnailPath);using var thumbStream=await thumbFile.OpenReadAsync();ct.ThrowIfCancellationRequested();var thumb=new BitmapImage();await thumb.SetSourceAsync(thumbStream).AsTask(ct);if(version!=previewVersion)return;MainImage.Source=thumb;EmptyPreview.Visibility=Visibility.Collapsed;await NextLayoutFrameAsync();if(version!=previewVersion)return;CrashJournal.Trace("Preview.Thumbnail.Ready",owner,version);PreviewHost.IsHitTestVisible=PreviewTransitionCover.Visibility!=Visibility.Visible;DrawSavedCrop();RevealPhoto();await Task.Delay(UiMotion.Enabled?130:0,ct); }
        } catch(OperationCanceledException){return;} catch(Exception error){CrashJournal.Fault("Thumbnail.Display",error,owner);}

        try
        {
            await Task.Delay(100,ct);
            #if DEBUG
            if(previewReadinessGate is {} gate)await gate.WaitAsync(ct);
#endif
            var preview=await VM.Images.EnsurePreviewAsync(photo,ct);ct.ThrowIfCancellationRequested();if(preview.Length==0) throw new IOException(photo.Error);
            var file = await StorageFile.GetFileFromPathAsync(preview); using var stream = await file.OpenReadAsync();
            ct.ThrowIfCancellationRequested();
            var image = new BitmapImage(); await image.SetSourceAsync(stream).AsTask(ct);
            if (version != previewVersion) return;
            while(drawing && version==previewVersion)await Task.Delay(16,ct);if(version!=previewVersion)return;
            if(originalPreviewId!=photo.Id)MainImage.Source = image;
            await NextLayoutFrameAsync();if(version!=previewVersion)return;RevealPhoto();CrashJournal.Trace("Preview.Image.Ready",owner,version);
            if(PhotoService.IsVideo(photo)) { VideoPlayer.Source=Windows.Media.Core.MediaSource.CreateFromUri(new Uri(photo.Path));VideoPlayer.Visibility=Visibility.Visible;MainImage.Visibility=Visibility.Collapsed;if(VideoPlayer.MediaPlayer is { } player) { player.AutoPlay=false;player.PlaybackSession.Position=TimeSpan.FromSeconds(photo.VideoPositionSeconds); } }
            PhotoCaption.Text=photo.Caption; EmptyPreview.Visibility = Visibility.Collapsed; DrawSavedCrop();
            if(photo.Asset is not null) VM.SaveAsset(photo.Asset);
            var next=displayed?.Photos.SkipWhile(p=>p.Id!=photo.Id).Skip(1).FirstOrDefault();if(next is not null && version==previewVersion) _=PrefetchAsync(next,ct);
        }
        catch(OperationCanceledException) { }
        catch(Exception error) { CrashJournal.Fault("Preview.Decode",error,owner);if (version == previewVersion) {ClearTransition(PreviewTransitionCover); MainImage.Source = null; EmptyPreview.Visibility = Visibility.Visible; EmptyPreview.Text = photo.Error.Length>0?photo.Error:"预览暂不可用，可重选照片重试；原图未修改。"; } }
        finally {if(version==previewVersion){previewLoading=false;PreviewHost.IsHitTestVisible=true;DrawSavedCrop();}}
    }
    private async Task PrefetchAsync(Photo photo,CancellationToken ct) { try { await Task.Delay(150,ct);await VM.Images.EnsurePreviewAsync(photo,ct); } catch { } }
    private void Group_Changed(object sender, SelectionChangedEventArgs e)
    {
        TrackMergeSelection(e);
        UpdateMergeSelection();
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
    private void Editor_Changed(object sender,TextChangedEventArgs e) { if(!initialized || rendering || displayed?.Editable!=true) return;CrashJournal.Trace("Editor.Change",displayed.Id);var before=VM.CaptureEdits([displayed]);FlushEditor();VM.RecordEdits(before,new(){displayed.Id},true);if(ReferenceEquals(sender,TargetBrandEdit)) { CloseFolderSuggestions();UpdateExistingCategories();UpdateTargetPath(); } VM.MarkTouched(displayed);editorSaveTimer.Stop();editorSaveTimer.Start(); }
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
        var includeVideos=VM.State.Settings.ImportVideos;
        var files=await Task.Run(()=>Directory.EnumerateFiles(folder).Where(path=>includeVideos || !PhotoService.IsVideo(new Photo {Path=path})).ToList(),ct);await VM.ImportAsync(files,false,ct);
    });
    private async Task<bool> BatchOptionsAsync()
    {
        var gap=new NumberBox { Header="时间分组间隔（秒）",Value=VM.State.Settings.GroupGapSeconds,Minimum=1,Maximum=3600,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact };
        var videos=new ToggleSwitch {Header="导入视频",IsOn=VM.State.Settings.ImportVideos};
        var options=new StackPanel {Spacing=16};options.Children.Add(gap);options.Children.Add(videos);
        if(await DialogAsync("批量导入",options,"导入",null,"取消")!=ContentDialogResult.Primary) return false;
        if(double.IsNaN(gap.Value)) throw new ArgumentException("请填写有效的分组间隔。");VM.State.Settings.ImportVideos=videos.IsOn;VM.State.Settings.GroupGapSeconds=(int)gap.Value;VM.SaveHeader();return true;
    }
    private void DeleteImportedGroup_Click(object sender,RoutedEventArgs e)=>ExecuteCommand("removeGroups",sender,e);
    private async Task RemoveGroupsCoreAsync()
    {
        if(VM.Busy) return;
        await DismissContextAsync();var groups=GroupList.SelectedItems.OfType<PhotoGroup>().Distinct().ToList();
        if(groups.Count==0 || groups.Any(group=>group.IsWorking)) return;
        var names=string.Join("\n",groups.Take(8).Select(group=>"• "+group.Title));
        if(groups.Count>8) names+=$"\n…以及另外 {groups.Count-8} 个分组";
        var confirmation=new TextBlock
        {
            Text=$"将从分组列表和本地记录中移除选中的 {groups.Count} 个分组：\n{names}\n\n不会删除源照片或已归档目录，也不会影响其他分组。删除后无法通过本应用撤销这些分组的归档。",
            TextWrapping=TextWrapping.Wrap
        };
        if(!await ConfirmAsync("removeGroups","删除选中分组",confirmation,"删除")) return;
        try { editorSaveTimer.Stop();FlushEditor();var before=VM.CaptureEdits(groups);VM.RemoveGroups(groups);if(groups.All(g=>!g.Archived)) VM.RecordEdits(before,groups.Select(g=>g.Id).ToHashSet());RenderSelection(); }
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
        if(!await ConfirmAsync("clearArchived","删除所有已归档分组",confirmation,"删除全部")) return;
        try { editorSaveTimer.Stop();FlushEditor();var before=VM.CaptureEdits(groups);VM.RemoveGroups(groups);if(groups.All(g=>!g.Archived)) VM.RecordEdits(before,groups.Select(g=>g.Id).ToHashSet());RenderSelection(); }
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
    private void Recognize_Click(object sender,RoutedEventArgs e)=>ExecuteCommand("recognize",sender,e);
    private void Deep_Click(object sender,RoutedEventArgs e)=>ExecuteCommand("verify",sender,e);
    private async Task QueryGroupAsync(bool deep)
    {
        try { CapturePreviewVideoPosition();FlushEditor();var group=RequireGroup();if(!group.Editable) throw new InvalidOperationException("本组正在执行任务或已归档。");if(deep && group.Confirmed.Series.Length==0 && group.Confirmed.Variant.Length==0) throw new InvalidOperationException("请先AI识别或填写待查证车型。");var settings=VM.CaptureSettings();VM.SaveGroup(group);await VM.RunGroupAsync(group,deep?"深入查证":"联网识别",false,ct=>VM.RecognizeAsync(group,deep,ct,settings));if(VM.Selected==group && narrowLayout && group.Suggestion is not null) PaneBox.SelectedIndex=2; }
        catch(Exception error) { VM.Status=error.Message; }
    }
    private void Cancel_Click(object sender,RoutedEventArgs e) { if(VM.Selected is not null) VM.Tasks.Cancel(VM.Selected.Id); }
    private PhotoGroup RequireGroup() => VM.Selected ?? throw new InvalidOperationException("请先导入并选择一组照片。");
    private async void Generate_Click(object sender,RoutedEventArgs e)
    {
        try { FlushEditor();var group=RequireGroup();var revision=group.Revision;await VM.PrepareNamingGuideAsync();if(group.Revision!=revision || !group.Editable) { VM.Status="分组已改变，请重新生成名称。";return; }var before=VM.CaptureEdits([group]);group.PreviousFolderName=group.FolderName;VM.GenerateName(group);VM.RecordEdits(before,new(){group.Id});VM.SaveGroup(group);if(VM.Selected==group) RenderSelection(); }
        catch(Exception error) { VM.Status="本地命名失败："+error.Message; }
    }
    private void RestoreName_Click(object sender,RoutedEventArgs e) => LocalAction(()=> { var g=RequireGroup();if(!g.Editable || g.PreviousFolderName.Length==0) return;var old=g.FolderName;g.FolderName=g.PreviousFolderName;g.PreviousFolderName=old;g.ManualName=true;VM.SaveGroup(g); });
    private async void Duplicate_Click(object sender,RoutedEventArgs e) { try {var group=RequireGroup();await DismissContextAsync();FlushEditor();var before=new Dictionary<string,string>();var copied=await VM.DuplicateAsync(group,default);VM.RecordEdits(before,new(){copied.Id});RenderSelection(); }catch(Exception error) { VM.Status=error.Message; } }
    private bool mergingGroups;
    private async void Merge_Click(object sender,RoutedEventArgs e)
    {
        if(mergingGroups)return;var groups=GroupList.SelectedItems.OfType<PhotoGroup>().OrderBy(g=>VM.Groups.IndexOf(g)).ToList();
        try {mergingGroups=true;await DismissContextAsync();if(groups.Count<2||groups.Any(g=>!g.Editable||!VM.Groups.Contains(g)))throw new InvalidOperationException("请选择至少两个可编辑分组。");await AnimateMergeAsync(groups,()=>{if(groups.Any(g=>!g.Editable||!VM.Groups.Contains(g)))throw new InvalidOperationException("分组已改变，请重新合并。");FlushEditor();var before=VM.CaptureEdits(groups);VM.Merge(groups);VM.RecordEdits(before,groups.Select(g=>g.Id).ToHashSet());RenderSelection();});}
        catch(Exception error){VM.Status=error.Message;}finally{mergingGroups=false;}
    }
    private void Split_Click(object sender,RoutedEventArgs e) {var group=RequireGroup();var photos=PhotoList.SelectedItems.OfType<Photo>().ToList();LocalAction(()=>VM.Split(group,photos));}
    private void Exclude_Click(object sender,RoutedEventArgs e) => LocalAction(()=> { var g=RequireGroup(); if(!g.Editable) throw new InvalidOperationException("本组暂不可编辑。"); foreach(var p in PhotoList.SelectedItems.OfType<Photo>()) p.Excluded=!p.Excluded;VM.MarkTouched(g); InvalidateEvidence(g,"照片范围已改变"); });
    private void Representative_Click(object sender,RoutedEventArgs e) => LocalAction(()=> { if(currentPhoto is not null) { RequireGroup().RepresentativeId=currentPhoto.Id; VM.Status="代表图已设置；识别时优先使用这张照片。"; } });
    private async void OpenPhoto_Click(object sender,RoutedEventArgs e) => await RunAsync(async _=> { if(currentPhoto is not null) await Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(currentPhoto.Path)); });
    private void Previous_Click(object sender,RoutedEventArgs e)=>ExecuteCommand("previous",sender,e);
    private void Next_Click(object sender,RoutedEventArgs e)=>ExecuteCommand("next",sender,e);
    private void LocalAction(Action action,bool record=true)
    {
        if(activeContextMenu is { } menu)
        {
            var owner=VM.Selected;
            void Closed(object? sender,object args)
            {
                menu.Closed-=Closed;
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,()=>{if(VM.Selected==owner)LocalAction(action,record);else VM.Status="分组已切换，请重新操作。";});
            }
            menu.Closed+=Closed;menu.Hide();return;
        }
        CrashJournal.Trace("UI."+action.Method.Name,displayed?.Id);editorSaveTimer.Stop();FlushEditor();
        try { var ids=VM.Groups.Select(g=>g.Id).ToHashSet();var targets=GroupList.SelectedItems.OfType<PhotoGroup>().Concat(displayed is null?[]:new[]{displayed}).Distinct().ToArray();var before=record?VM.CaptureEdits(targets):null;if(displayed is not null) VM.SaveGroup(displayed);action();if(before is not null) VM.RecordEdits(before,targets.Select(g=>g.Id).Concat(VM.Groups.Where(g=>!ids.Contains(g.Id)).Select(g=>g.Id)).ToHashSet());if(VM.Selected is not null) VM.SaveGroup(VM.Selected); RenderSelection(); } catch(Exception e) { VM.Status=e.Message; Notice.Severity=InfoBarSeverity.Warning; }
    }
    private async void Learn_Click(object sender,RoutedEventArgs e)
    {
        if(VM.Busy) return;
        try { FlushEditor(); var g=RequireGroup(); NamingService.SafeSegment(g.FolderName); if(g.Confirmed.Brand.Length==0 || g.Confirmed.Series.Length==0) throw new InvalidOperationException("学习前请填写品牌、车系和目录名称。"); var rule=VM.Learn(g,true); VM.Status="已记住此车型名称。"; await ConsiderRuleAsync(rule); RenderSelection(); } catch(Exception error) { VM.Status=error.Message; }
    }
    private async Task ConsiderRuleAsync(IReadOnlyList<LocalNamingRule> rules)
    {
        foreach(var rule in rules.Where(r=>r.Conflict.Length==0))
            if(await ConfirmAsync("learnRule","采用这条本地命名习惯？",Paragraph(rule.Description+"\n依据 "+rule.SampleIds.Count+" 条已确认样本。只影响相应范围，可在命名学习中撤回。"),"采用","稍后处理"))
            { LocalNamingLearning.ValidateRule(rule);rule.Approved=true;LocalNamingLearning.Reconcile(VM.State);VM.SaveHeader(); }
    }
    private void Archive_Click(object sender,RoutedEventArgs e)=>ExecuteCommand("archive",sender,e);
    private async Task ArchiveCoreAsync()
    {
        FlushEditor();
        try
        {
            var group=RequireGroup();if(group.Archived) {await UndoSelectedMoveAsync(group);return;}if(!group.Editable) throw new InvalidOperationException("本组正在执行任务或已归档。"); NamingService.SafeSegment(group.FolderName); NamingService.SafeSegment(group.TargetBrand);
            if(VM.State.Settings.ArchiveRoot.Length==0) throw new InvalidOperationException("请先选择归档根目录。");
            var root=VM.State.Settings.ArchiveRoot;var brand=group.TargetBrand;var folder=group.FolderName;

            await VM.RunGroupAsync(group,"归档",true,ct=>VM.ArchiveAsync(group,ct,root,brand,folder,ConfirmArchiveDuplicateAsync));
            if(group.Archived) { var rules=VM.Learn(group,false);if(VM.Selected==group) await ConsiderRuleAsync(rules);await VM.EnsureNamingGuideAsync(true); _=RefreshCategoryCatalogAsync(); }
        }
        catch(Exception error) { VM.Status=error.Message; Notice.Severity=InfoBarSeverity.Warning; }
    }
    private void Undo_Click(object sender,RoutedEventArgs e)=>ExecuteCommand("undoArchive",sender,e);
    private async Task<ArchiveDuplicateChoice> ConfirmArchiveDuplicateAsync(ArchiveDuplicate duplicate,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        while(ModalOpen())await Task.Delay(150,ct);
        var result=await DialogAsync("发现相同内容",Paragraph(
            $"待移动：{duplicate.Source}\n\n相同文件：{duplicate.Existing}\n\n仍移动会保留两份；如重名，自动加序号。回收只处理待移动文件。"),
            "仍移动","回收待移动文件","取消本组");
        ct.ThrowIfCancellationRequested();
        return result switch {ContentDialogResult.Primary=>ArchiveDuplicateChoice.Move,
            ContentDialogResult.Secondary=>ArchiveDuplicateChoice.Recycle,_=>ArchiveDuplicateChoice.Cancel};
    }
    private async Task UndoCoreAsync()
    {
        try { FlushEditor();var job=VM.Store.Jobs().Where(j=>j.Direction=="Archive" && j.Status=="Completed" && VM.Groups.Any(g=>g.Id==j.GroupId)).OrderByDescending(j=>j.CreatedAt).FirstOrDefault() ?? throw new InvalidOperationException("没有可撤销的归档。");var group=VM.Groups.First(g=>g.Id==job.GroupId);await VM.RunGroupAsync(group,"撤销",true,ct=>VM.ResumeOrUndoAsync(job,true,ct)); }
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
        ResizeThumbnails(e.NewSize.Height);UpdateMergeSelection();
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
    private async void CropToggle_Click(object sender,RoutedEventArgs e) { try { await CaptureVideoFrameAsync();VM.Status=""; }catch(Exception error){VM.Status=error.Message;} }
    private void Crop_Pressed(object sender,PointerRoutedEventArgs e)
    {
        if(CropToggle.IsChecked!=true || currentPhoto is null || displayed?.Editable!=true) return;
        var p=e.GetCurrentPoint(PreviewHost); var rect=ImageRect(); if(!p.Properties.IsLeftButtonPressed || !rect.Contains(p.Position)) return;
        if(rect.Width<=0 || rect.Height<=0 || !PreviewHost.CapturePointer(e.Pointer))return;
        cropStart=p.Position;drawing=true;DrawBox(cropStart,cropStart);e.Handled=true;
    }
    private Point ClampPoint(Point p) { var r=ImageRect(); return new(Math.Clamp(p.X,r.X,r.Right),Math.Clamp(p.Y,r.Y,r.Bottom)); }
    private void Crop_Moved(object sender,PointerRoutedEventArgs e) { if(drawing) DrawBox(cropStart,ClampPoint(e.GetCurrentPoint(PreviewHost).Position)); }
    private void DrawBox(Point a,Point b) { Canvas.SetLeft(CropOutline,Math.Min(a.X,b.X)); Canvas.SetTop(CropOutline,Math.Min(a.Y,b.Y)); CropOutline.Width=Math.Abs(a.X-b.X); CropOutline.Height=Math.Abs(a.Y-b.Y); CropOutline.Visibility=Visibility.Visible; }
    private void Crop_Released(object sender,PointerRoutedEventArgs e)
    {
        if(!drawing || currentPhoto is null) return; drawing=false; var end=ClampPoint(e.GetCurrentPoint(PreviewHost).Position); PreviewHost.ReleasePointerCapture(e.Pointer);
        var r=ImageRect(); if(r.Width<=0 || r.Height<=0 || Math.Abs(end.X-cropStart.X)<8 || Math.Abs(end.Y-cropStart.Y)<8) { DrawSavedCrop(); return; }
        var before=VM.CaptureEdits(displayed is null?[]:new[]{displayed});currentPhoto.Crop=new((Math.Min(cropStart.X,end.X)-r.X)/r.Width,(Math.Min(cropStart.Y,end.Y)-r.Y)/r.Height,Math.Abs(end.X-cropStart.X)/r.Width,Math.Abs(end.Y-cropStart.Y)/r.Height);
        VM.RecordEdits(before,displayed is null?[]:new(){displayed.Id});CrashJournal.Trace("Photo.Crop",displayed?.Id);
        if(displayed is not null) InvalidateEvidence(displayed,"目标框选已改变");
        if(displayed is not null) { VM.MarkTouched(displayed);VM.SaveGroup(displayed);VerificationText.Text="目标已改变，需要重新识别或人工核对。"; } PhotoCaption.Text=currentPhoto.Caption; VM.Status="目标已框选；点击AI识别重新分析。";
    }
    private void Crop_Canceled(object sender,PointerRoutedEventArgs e) { drawing=false; DrawSavedCrop(); }
    private void DrawSavedCrop() {if(drawing || CropOutline is null)return;var r=ImageRect();if(currentPhoto?.Crop is not { } c || r.Width<=0 || r.Height<=0) { CropOutline.Visibility=Visibility.Collapsed; return; } DrawBox(new(r.X+c.X*r.Width,r.Y+c.Y*r.Height),new(r.X+(c.X+c.Width)*r.Width,r.Y+(c.Y+c.Height)*r.Height)); }
    private void ClearCrop_Click(object sender,RoutedEventArgs e) => LocalAction(()=> { if(currentPhoto is not null && displayed?.Editable==true) { currentPhoto.Crop=null; InvalidateEvidence(displayed,"目标框选已改变");VM.MarkTouched(displayed); } });
    private static void InvalidateEvidence(PhotoGroup group,string reason)
    {
        if(group.Suggestion is not null) { group.Suggestion.Conflict=true; group.Suggestion.Uncertainties+="\n"+reason+"，原识别结果仅供参考，需要重新识别或人工核对。"; }
        if(group.Verification is not null) { group.VerificationHistory.Add(group.Verification);group.Verification=null; }
        group.State=reason+" · 请复核";
    }
}
