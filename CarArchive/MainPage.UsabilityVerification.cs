#if DEBUG
using System.Text.Json;
using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace CarArchive;

public sealed partial class MainPage
{
    private async Task RunUsabilityVerificationAsync(string directory)
    {
        directory=Path.GetFullPath(directory);Directory.CreateDirectory(directory);var checks=new List<string>();
        void Check(bool value,string name) { if(!value) throw new Exception(name);checks.Add(name); }
        try
        {
            Check(ViewModels.MainViewModel.DataDirectory.StartsWith(directory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) && VM.Groups.Count==0,"Isolated test data.");
            Check(File.Exists(MainWindow.AppIconPath) && File.Exists(MainWindow.TitleBarIconPath) && App.MainWindow!.HasTitleBarIcon,"Window and title-bar icons resolve to published image assets.");
            var root=Path.Combine(directory,"library");
            for(var i=0;i<30;i++) Directory.CreateDirectory(Path.Combine(root,"Mercedes-AMG","S 63 Coupe Edition "+i+" (C217)"));
            Directory.CreateDirectory(Path.Combine(root,"BMW"));Directory.CreateDirectory(Path.Combine(root,"BMX"));
            var wide=await FixtureImageAsync(Path.Combine(directory,"wide.jpg"),160,80);var portrait=await FixtureImageAsync(Path.Combine(directory,"portrait.jpg"),60,120);
            var hashes=new[] { await ArchiveService.HashAsync(wide),await ArchiveService.HashAsync(portrait) };
            await VM.ImportAsync([wide,portrait],true,default);await VM.BackgroundImport;var group=VM.Groups[0];
            VM.State.Settings.ArchiveRoot=root;group.TargetBrand="Mercedes-AMG";group.FolderName="s63coupe";RenderSelection();await RefreshCategoryCatalogAsync(true);
            await Task.Delay(100);
            var modeCenter=ModeButton.TransformToVisual(TopBar).TransformPoint(new(0,0)).Y+ModeButton.ActualHeight/2;
            var importCenter=ImportGroupButton.TransformToVisual(TopBar).TransformPoint(new(0,0)).Y+ImportGroupButton.ActualHeight/2;
            var batchCenter=BatchImportButton.TransformToVisual(TopBar).TransformPoint(new(0,0)).Y+BatchImportButton.ActualHeight/2;
            Check(Math.Abs(TopBar.ActualHeight-40)<.5 && Math.Abs(modeCenter-importCenter)<1 && Math.Abs(modeCenter-batchCenter)<1,"Top commands align vertically in a compact 40-pixel row: "+JsonSerializer.Serialize(new { BarHeight=TopBar.ActualHeight,ModeHeight=ModeButton.ActualHeight,ModeCenter=modeCenter,ImportHeight=ImportGroupButton.ActualHeight,ImportCenter=importCenter,BatchHeight=BatchImportButton.ActualHeight,BatchCenter=batchCenter }));
            Check(Math.Abs(ImportGroupLabel.FontSize-ModeDescription.FontSize)<.1 && Math.Abs(BatchImportLabel.FontSize-ModeDescription.FontSize)<.1,"Import labels use the reduced font size shared by other secondary text.");
            var moreMenu=MoreButton.Flyout as MenuFlyout;
            Check(moreMenu is not null && moreMenu.Items.Count==4,"The compact command row retains settings, naming, history, and undo actions.");
            var folderInput=VisualChild<TextBox>(FolderEdit);
            Check(folderInput is not null && IsInsideSuggestionInput(FolderEdit,folderInput) && !IsInsideSuggestionInput(FolderEdit,FolderEdit),"Suggestion activation is limited to the inner text-box hit target, not the surrounding control.");
            var actionButtons=AnalysisBar.PrimaryCommands.OfType<FrameworkElement>().ToArray();
            Check(actionButtons.Length==6,"Preview toolbar retains all six actions.");
            var actionStart=actionButtons[0].TransformToVisual(PreviewPane).TransformPoint(new(0,0)).X;
            var actionEnd=actionButtons[^1].TransformToVisual(PreviewPane).TransformPoint(new(0,0)).X+actionButtons[^1].ActualWidth;
            Check(Math.Abs((actionStart+actionEnd)/2-PreviewHost.ActualWidth/2)<2,"Preview toolbar commands are horizontally centered to the image.");
            if(narrowLayout) PaneBox.SelectedIndex=2;await Task.Delay(100);
            VisualChild<TextBox>(FolderEdit)?.Focus(FocusState.Programmatic);DispatcherQueue.TryEnqueue(OpenFolderSuggestions);await Task.Delay(80);await categoryRefresh;await Task.Delay(450);
            var suggestions=(FolderEdit.ItemsSource as IEnumerable<CategorySuggestion>)?.ToArray() ?? [];
            var expectedSuggestions=categorySearch.Match(FolderEdit.Text,TargetBrandEdit.Text);
            Check(FolderEdit.IsSuggestionListOpen && suggestions.Length==20 && suggestions.SequenceEqual(expectedSuggestions) && suggestions[0].FolderName.Contains("S 63 Coupe"),"Folder suggestions retain descending relevance order: "+JsonSerializer.Serialize(new { Open=FolderEdit.IsSuggestionListOpen,Count=suggestions.Length,First=suggestions.FirstOrDefault()?.FolderName,Input=FolderEdit.Text,Items=categoryItems.Count,Active=folderSuggestionsActive,Focus=FolderEdit.FocusState.ToString(),Editable=group.Editable,Status=VM.Status }));
            var popup=SuggestionList(FolderEdit);var scroll=popup is null?null:VisualChild<ScrollViewer>(popup);
            var popupWindow=SuggestionPopup(FolderEdit);var textInput=VisualChild<TextBox>(FolderEdit);
            var popupHooked=popupWindow is not null && suggestionPopups.Contains(popupWindow);
            var popupBounds=popupWindow?.Child is FrameworkElement popupContent && textInput is not null?popupContent.TransformToVisual(textInput).TransformBounds(new(0,0,popupContent.ActualWidth,popupContent.ActualHeight)):new Windows.Foundation.Rect();
            var popupTransition=popupWindow?.ChildTransitions?.OfType<Microsoft.UI.Xaml.Media.Animation.PopupThemeTransition>().FirstOrDefault();
            Check(scroll is not null && scroll.ScrollableHeight>0 && Math.Abs(scroll.VerticalOffset)<2 && Math.Abs(FolderEdit.MaxSuggestionListHeight-150)<.1 && scroll.ViewportHeight<=150 && popupWindow is not null && KeepSuggestionsLeftAndTop(FolderEdit,popupWindow,false) && popupHooked && popupWindow.Opacity>=1 && popupTransition?.FromHorizontalOffset>0,"Popup opens to the left with its first row aligned to the input, animates horizontally, and stays within five rows: "+JsonSerializer.Serialize(new { List=popup?.Name,ListHeight=popup?.ActualHeight,LeftAndTopAligned=popupWindow is not null && KeepSuggestionsLeftAndTop(FolderEdit,popupWindow,false),PopupFound=popupWindow is not null,PopupHooked=popupHooked,PopupOpacity=popupWindow?.Opacity,PopupTop=popupBounds.Top,PopupRight=popupBounds.Right,AnimationOffset=popupTransition?.FromHorizontalOffset,HorizontalOffset=popupWindow?.HorizontalOffset,VerticalOffset=popupWindow?.VerticalOffset,Popups=Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Select(open=>new { Child=open.Child?.GetType().Name,ListCount=open.Child is null?0:VisualChild<ListView>(open.Child)?.Items.Count,First=open.Child is null?null:VisualChild<ListView>(open.Child)?.Items.FirstOrDefault()?.GetType().Name }).ToArray(),Found=scroll is not null,Max=scroll?.ScrollableHeight,Offset=scroll?.VerticalOffset,Viewport=scroll?.ViewportHeight,MaxSuggestionListHeight=FolderEdit.MaxSuggestionListHeight,Editor=EditorPane.Visibility.ToString(),BoxHeight=FolderEdit.ActualHeight,Visible=App.MainWindow!.AppWindow.IsVisible }));
            var item=popup?.ContainerFromIndex(0) as ListViewItem;var label=item is null?null:VisualChild<TextBlock>(item);var labelRuns=label?.Inlines.OfType<Run>().ToArray() ?? [];
            Check(labelRuns.Length==2 && labelRuns[1].Text==" / "+suggestions[0].BrandDirectory && labelRuns[1].Foreground is not null,"Suggestion brand suffix is separately styled in a secondary color.");
            FolderEdit.Text="";OpenFolderSuggestions();await Task.Delay(100);
            Check(!FolderEdit.IsSuggestionListOpen && FolderEdit.ItemsSource is null,"Empty folder input leaves suggestions closed.");
            FolderEdit.Text="s63coupe";OpenFolderSuggestions();await categoryRefresh;await Task.Delay(450);
            CloseFolderSuggestions();VisualChild<TextBox>(TargetBrandEdit)?.Focus(FocusState.Programmatic);TargetBrandEdit.Text="";OpenBrandSuggestions();await Task.Delay(100);
            Check(!TargetBrandEdit.IsSuggestionListOpen && TargetBrandEdit.ItemsSource is null,"Empty brand input leaves suggestions closed.");
            TargetBrandEdit.Text="bm";OpenBrandSuggestions();await categoryRefresh;await Task.Delay(450);
            var brandSuggestions=(TargetBrandEdit.ItemsSource as IEnumerable<CategorySuggestion>)?.ToArray() ?? [];
            var expectedBrandSuggestions=brandSearch.Match(TargetBrandEdit.Text,TargetBrandEdit.Text);
            var brand=brandSuggestions.FirstOrDefault();
            var brandPopup=SuggestionPopup(TargetBrandEdit);
            Check(brand?.BrandDirectory=="BMW" && brandSuggestions.SequenceEqual(expectedBrandSuggestions) && brandSuggestions.Select(item=>item.BrandDirectory).SequenceEqual(["BMW","BMX"]) && brandPopup is not null && KeepSuggestionsLeftAndTop(TargetBrandEdit,brandPopup,false) && suggestionPopups.Contains(brandPopup) && brandPopup.Opacity>=1 && brandPopup.ChildTransitions.OfType<Microsoft.UI.Xaml.Media.Animation.PopupThemeTransition>().Any(transition=>transition.FromHorizontalOffset>0),"Brand suggestions appear to the left with the first result at the top and the matching horizontal animation: "+JsonSerializer.Serialize(new { Items=brandSuggestions.Select(item=>item.BrandDirectory),Chosen=brand?.BrandDirectory,Input=TargetBrandEdit.Text,Open=TargetBrandEdit.IsSuggestionListOpen,LeftAndTopAligned=brandPopup is not null && KeepSuggestionsLeftAndTop(TargetBrandEdit,brandPopup,false),PopupHooked=brandPopup is not null && suggestionPopups.Contains(brandPopup),Opacity=brandPopup?.Opacity,Active=brandSuggestionsActive,Brands=brandItems.Count }));
            ChooseBrand(brand!);Check(group.TargetBrand=="BMW" && group.Confirmed.Brand=="","Choosing an archive brand does not alter recognition fields.");
            FolderEdit.Text="Coupé Édition Æther";EnglishInput(FolderEdit);Check(FolderEdit.Text=="Coupe Edition AEther","Folder input normalizes accented and ligature letters.");
            RenderSelection();if(narrowLayout) PaneBox.SelectedIndex=1;await Task.Delay(200);
            var first=PhotoList.ContainerFromItem(group.Photos[0]) as ListViewItem;var second=PhotoList.ContainerFromItem(group.Photos[1]) as ListViewItem;
            var image1=first is null?null:VisualChild<Image>(first);var image2=second is null?null:VisualChild<Image>(second);
            Check(image1?.Stretch==Microsoft.UI.Xaml.Media.Stretch.Uniform && image2?.Stretch==Microsoft.UI.Xaml.Media.Stretch.Uniform,"Thumbnail images use complete-image fitting.");
            Check(image1?.Parent is StackPanel w && image2?.Parent is StackPanel h && w.Width>h.Width*2,"Thumbnail cells pack horizontally at original aspect ratios.");
            var groupSelection=GroupList.Resources["ListViewItemBackgroundSelected"] as Microsoft.UI.Xaml.Media.SolidColorBrush;
            var photoSelection=PhotoList.Resources["ThumbnailCaptionSelectionBrush"] as Microsoft.UI.Xaml.Media.SolidColorBrush;
            PhotoList.SelectedItem=group.Photos[0];
            Check(PhotoList.SelectedItem==group.Photos[0] && groupSelection is not null && photoSelection is not null && groupSelection.Color==photoSelection.Color && Math.Abs(groupSelection.Opacity-photoSelection.Opacity)<.001,"Selected thumbnails use the same accent fill as selected groups.");
            await Task.Delay(80);first=PhotoList.ContainerFromItem(group.Photos[0]) as ListViewItem;image1=first is null?null:VisualChild<Image>(first);var caption=first is null?null:VisualChild<TextBlock>(first)?.Parent as Border;
            Check(first is not null && image1 is not null && caption is not null && caption.Background is Microsoft.UI.Xaml.Media.SolidColorBrush && Math.Abs(caption.ActualWidth-image1.ActualWidth)<1,"Selected thumbnail caption fill exactly matches the displayed image width: "+JsonSerializer.Serialize(new { CaptionWidth=caption?.ActualWidth,ImageWidth=image1?.ActualWidth }));
            PhotoList.SelectedItem=null;
            var copies=Enumerable.Range(0,65).Select(_=>GroupPhotoEditor.Copy(group,group.Photos.ToArray())).ToArray();foreach(var copy in copies) VM.Groups.Add(copy);
            if(narrowLayout) PaneBox.SelectedIndex=0;VM.Selected=copies[^1];RenderSelection();await Task.Delay(180);var container=GroupList.ContainerFromItem(copies[^1]) as ListViewItem;
            Check(container is not null && copies[^1].CurrentHighlightOpacity==1 && group.CurrentHighlightOpacity==0,"Changing groups scrolls the selected group into view with a distinct highlight.");
            var chosen=VM.DuplicatePhotos(group,[group.Photos[1]]);Check(chosen.Photos.Count==1 && group.Photos.Count==2 && chosen.Photos[0].AssetId==group.Photos[1].AssetId,"Selected-photo copy creates an independent shared-original group.");
            VM.RemovePhotos(chosen,[chosen.Photos[0]]);Check(!VM.Groups.Contains(chosen),"Removing a group's last reference removes only that group.");
            Check(await ArchiveService.HashAsync(wide)==hashes[0] && await ArchiveService.HashAsync(portrait)==hashes[1] && VM.Store.Jobs().Count==0,"Context edits preserve source bytes and create no archive jobs.");
            if(narrowLayout) PaneBox.SelectedIndex=2;
            foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark}) { RequestedTheme=theme;await Task.Delay(80);Check(ArchiveFooter.ActualHeight>0,"Footer remains usable in "+theme+" theme."); }
            App.MainWindow!.AppWindow.Resize(new(850,900));PaneBox.SelectedIndex=2;await Task.Delay(150);Check(narrowLayout && EditorPane.Visibility==Visibility.Visible,"Narrow review workspace remains usable.");
            await VM.FlushWritesAsync();File.WriteAllText(Path.Combine(directory,"usability-verification.json"),JsonSerializer.Serialize(new { Status="Passed",Checks=checks,ApiCalls=VM.State.BatchCalls },new JsonSerializerOptions(StateStore.Json) { WriteIndented=true }));
            VM.Status="交互验证通过 · 合成图片 · 不联网";
        }
        catch(Exception error) { File.WriteAllText(Path.Combine(directory,"usability-verification.json"),JsonSerializer.Serialize(new { Status="Failed",Error=error.ToString(),Checks=checks },StateStore.Json));VM.Status=error.Message; }
    }
    private static async Task<string> FixtureImageAsync(string path,uint width,uint height)
    {
        using var memory=new InMemoryRandomAccessStream();var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId,memory);
        var pixels=Enumerable.Repeat((byte)160,checked((int)(width*height*4))).ToArray();encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,width,height,96,96,pixels);await encoder.FlushAsync();
        using var reader=new DataReader(memory.GetInputStreamAt(0));await reader.LoadAsync((uint)memory.Size);var data=new byte[(int)memory.Size];reader.ReadBytes(data);await File.WriteAllBytesAsync(path,data);return path;
    }
}
#endif
