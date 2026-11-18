using CarArchive.Core;
using CarArchive.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;

namespace CarArchive;

public sealed partial class MainPage
{
    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in VisualDescendants(child))yield return nested;}
    }
    private static T? VisualChild<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T match) return match;
        if(root is Popup popup && popup.Child is { } content && VisualChild<T>(content) is { } found) return found;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            if(VisualChild<T>(VisualTreeHelper.GetChild(root,i)) is { } child) return child;
        return null;
    }
    private static void EnglishInput(AutoSuggestBox box)
    {
        var text=box.Text;var english=LatinText.English(text);if(text==english) return;
        var edit=VisualChild<TextBox>(box);var caret=Math.Min(edit?.SelectionStart ?? text.Length,text.Length);
        var position=LatinText.English(text[..caret]).Length;box.Text=english;edit?.Select(Math.Min(position,english.Length),0);
    }
    private void FocusCurrentGroup()
    {
        var group=VM.Selected;if(group is null) return;
        DispatcherQueue.TryEnqueue(()=> { if(VM.Selected==group && VM.Groups.Contains(group)) GroupList.ScrollIntoView(group); });
    }
    private async void Thumbnail_Loaded(object sender,RoutedEventArgs args)
    {
        if(sender is Image loaded){TrackThumbnail(loaded);ApplyThumbnailSize(loaded);}
        if(sender is not Image { DataContext:Photo photo } || PhotoService.CurrentThumbnail(photo.ThumbnailPath) || !File.Exists(photo.Path)) return;
        try
        {
            await VM.Images.EnsureThumbnailAsync(photo,default);
            if(photo.Asset is not null) VM.SaveAsset(photo.Asset);
            if(displayed?.Photos.Any(p=>p.AssetId==photo.AssetId)==true) displayed.Refresh();
        }
        catch(Exception error) { CrashJournal.Trace("Thumbnail.Upgrade."+error.GetType().Name); }
    }
    private void Thumbnail_Opened(object sender,RoutedEventArgs args)
    {
        if(sender is Image { DataContext:Photo photo } && !PhotoService.CurrentThumbnail(photo.ThumbnailPath) && File.Exists(photo.Path))
        { Thumbnail_Loaded(sender,args);return; }
        if(sender is Image image)ApplyThumbnailSize(image);
    }
    private readonly Dictionary<Image,long> thumbnailSubscriptions=new();
    private void TrackThumbnail(Image image)
    {
        if(thumbnailSubscriptions.ContainsKey(image))return;
        thumbnailSubscriptions[image]=image.RegisterPropertyChangedCallback(Image.SourceProperty,(_,_)=>ApplyThumbnailSize(image));
        image.Unloaded+=ThumbnailUnloaded;image.SizeChanged+=ThumbnailSizeChanged;
    }
    private void ThumbnailUnloaded(object sender,RoutedEventArgs args)
    {
        if(sender is Image image && thumbnailSubscriptions.Remove(image,out var token))
        {image.UnregisterPropertyChangedCallback(Image.SourceProperty,token);image.Unloaded-=ThumbnailUnloaded;image.SizeChanged-=ThumbnailSizeChanged;}
    }
    private void ThumbnailSizeChanged(object sender,SizeChangedEventArgs args){if(sender is Image image)ApplyThumbnailSize(image);}
    private void ApplyThumbnailSize(Image image)
    {
        if(image.Parent is not StackPanel panel)return;
        if(image.Height!=imageThumbnailHeight)image.Height=imageThumbnailHeight;
        if(image.Source is BitmapImage {PixelWidth:>0,PixelHeight:>0} bitmap)
        {
            var width=imageThumbnailHeight*bitmap.PixelWidth/bitmap.PixelHeight;
            if(double.IsNaN(panel.Width)||Math.Abs(panel.Width-width)>.01)panel.Width=width;
            image.Width=width;panel.MinWidth=width;image.Stretch=Stretch.Uniform;image.VerticalAlignment=VerticalAlignment.Top;
        }
    }
    private void ThumbnailCaption_Loaded(object sender,RoutedEventArgs args)
    {
        if(sender is Border caption && caption.DataContext is Photo photo) SetThumbnailCaptionSelection(caption,photo);
    }
    private void UpdateThumbnailCaptionSelection(Photo photo)
    {
        if(PhotoList.ContainerFromItem(photo) is ListViewItem container && VisualChild<TextBlock>(container)?.Parent is Border caption)
            SetThumbnailCaptionSelection(caption,photo);
    }
    private void SetThumbnailCaptionSelection(Border caption,Photo photo)
    {
        caption.Background=PhotoList.SelectedItems.Contains(photo)?PhotoList.Resources["ThumbnailCaptionSelectionBrush"] as Brush:null;
    }
    private static void SelectContextItem(ListView list,DependencyObject? source)
    {
        if(source is null) return;
        for(var node=source;node is not null;node=VisualTreeHelper.GetParent(node))
            if(node is ListViewItem item && list.Items.Contains(item.Content))
            { if(!item.IsSelected) list.SelectedItem=item.Content;break; }
    }
    private MenuFlyout? activeContextMenu;
    private void InitializeContextMenus()
    {
        GroupList.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler(GroupAnchorPressed),true);
        GroupList.GotFocus+=(_,_)=> {if(FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused && GroupFromNode(focused) is {} group){lastMergeSelection=group;DispatcherQueue.TryEnqueue(UpdateMergeSelection);}};
        foreach(var menu in new[]{GroupList.ContextFlyout,PhotoList.ContextFlyout}.OfType<MenuFlyout>())WireContextCommands(menu);
    }
    private void WireContextCommands(MenuFlyout menu)
    {
        PhotoGroup? owner=null;PhotoGroup[] groups=[];Photo[] photos=[];var dispatched=false;var open=false;
        menu.AreOpenCloseAnimationsEnabled=true;
        menu.Opening+=(_,_)=>{open=true;activeContextMenu=menu;owner=VM.Selected;groups=GroupList.SelectedItems.OfType<PhotoGroup>().ToArray();photos=PhotoList.SelectedItems.OfType<Photo>().ToArray();dispatched=false;};
        menu.Closed+=(_,_)=>{open=false;if(activeContextMenu==menu)activeContextMenu=null;};
        foreach(var item in menu.Items.OfType<MenuFlyoutItem>())
        {
            RoutedEventHandler? handler=item.Name switch {
                "MergeGroupsMenu"=>Merge_Click,"DuplicateGroupMenu"=>Duplicate_Click,"DeleteGroupsMenu"=>DeleteImportedGroup_Click,
                "RecycleGroupsMenu"=>RecycleGroups_Click,"DeleteArchivedMenu"=>DeleteAllArchivedGroups_Click,
                "SplitPhotosMenu"=>Split_Click,"CopyPhotosMenu"=>CopyPhotos_Click,"RemovePhotosMenu"=>RemovePhotos_Click,"RecyclePhotosMenu"=>RecyclePhotos_Click,
                _=>item.Text=="撤回本组操作"?UndoGroup_Click:item.Text=="重做本组操作"?RedoGroup_Click:null};
            if(handler is null)continue;
            void Dispatch()
            {
                if(dispatched||!item.IsEnabled)return;dispatched=true;
                var target=owner;var selectedGroups=groups;var selectedPhotos=photos;
                void QueueCommand()=>DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,()=>
                {
                    if(categoryClosed||VM.Selected!=target||target is null||!VM.Groups.Contains(target)){VM.Status="分组已切换，请重新操作。";return;}
                    if(menu==GroupList.ContextFlyout){GroupList.SelectedItems.Clear();foreach(var group in selectedGroups.Where(VM.Groups.Contains))GroupList.SelectedItems.Add(group);}
                    else {PhotoList.SelectedItems.Clear();foreach(var photo in selectedPhotos.Where(target.Photos.Contains))PhotoList.SelectedItems.Add(photo);}
                    CrashJournal.Trace("Menu.Execute."+item.Text,target.Id);handler(item,new RoutedEventArgs());
                });
                if(open){void Closed(object? sender,object args){menu.Closed-=Closed;QueueCommand();}menu.Closed+=Closed;menu.Hide();}
                else QueueCommand();
            }
            item.Click+=(_,_)=>Dispatch();
            item.AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler((_,args)=>
            {
                if(args.GetCurrentPoint(item).Properties.PointerUpdateKind==Microsoft.UI.Input.PointerUpdateKind.LeftButtonReleased){Dispatch();args.Handled=true;}
            }),true);
        }
    }
    private async Task DismissContextAsync()
    {
        if(activeContextMenu is not {} menu)return;
        var group=VM.Selected;menu.Hide();
        var ready=new TaskCompletionSource();DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,()=>ready.TrySetResult());await ready.Task;
        if(categoryClosed||VM.Selected!=group)throw new InvalidOperationException("分组已切换，请重新操作。");
    }
    private PhotoGroup? GroupFromNode(DependencyObject? node)
    {
        for(;node is not null;node=VisualTreeHelper.GetParent(node))if(node is ListViewItem {Content:PhotoGroup group})return group;
        return null;
    }
    private void GroupAnchorPressed(object sender,PointerRoutedEventArgs args)
    {
        if(args.GetCurrentPoint(GroupList).Properties.IsLeftButtonPressed && GroupFromNode(args.OriginalSource as DependencyObject) is {} group)
        {lastMergeSelection=group;DispatcherQueue.TryEnqueue(UpdateMergeSelection);}
    }
    private PhotoGroup? lastMergeSelection;
    private ScrollViewer? mergeScroll;
    private PhotoGroup? mergeAnchorGroup;
    private double mergeAnchorContentY,mergeAnchorHeight;
    private void TrackMergeSelection(SelectionChangedEventArgs args)
    {
        if(lastMergeSelection is null || !GroupList.SelectedItems.Contains(lastMergeSelection))
            lastMergeSelection=VM.Selected is {} current && GroupList.SelectedItems.Contains(current)?current:GroupList.SelectedItems.OfType<PhotoGroup>().FirstOrDefault();
    }
    private void InitializeMergeTracking()
    {
        if(VisualChild<ScrollViewer>(GroupList) is { } scroll) {mergeScroll=scroll;scroll.ViewChanged+=(_,_)=>UpdateMergeSelection();}
        GroupList.SizeChanged+=(_,_)=>UpdateMergeSelection();
        GroupList.ContainerContentChanging+=(_,_)=>DispatcherQueue.TryEnqueue(UpdateMergeSelection);
    }
    private void UpdateMergeSelection()
    {
        var groups=GroupList.SelectedItems.OfType<PhotoGroup>().ToArray();
        if(groups.Length<2 || groups.Any(group=>!group.Editable)) {MergeSelectionButton.Visibility=Visibility.Collapsed;return;}
        var last=lastMergeSelection is not null && groups.Contains(lastMergeSelection)?lastMergeSelection:groups.Last();
        double rowY,rowHeight;
        if(GroupList.ContainerFromItem(last) is ListViewItem item && item.ActualHeight>0)
        {
            rowY=item.TransformToVisual(QueuePane).TransformPoint(new Windows.Foundation.Point()).Y;rowHeight=item.ActualHeight;
            mergeAnchorGroup=last;mergeAnchorContentY=rowY+(mergeScroll?.VerticalOffset??0);mergeAnchorHeight=rowHeight;
        }
        else if(mergeAnchorGroup==last)
        {
            rowY=mergeAnchorContentY-(mergeScroll?.VerticalOffset??0);rowHeight=mergeAnchorHeight;
        }
        else {MergeSelectionButton.Visibility=Visibility.Collapsed;return;}
        var top=Math.Clamp(rowY+(rowHeight-30)/2,0,Math.Max(0,QueuePane.ActualHeight-30));
        MergeSelectionButton.Margin=new Thickness(0,top,12,0);
        if(MergeSelectionButton.Visibility!=Visibility.Visible){MergeSelectionButton.Visibility=Visibility.Visible;UiMotion.Reveal(MergeSelectionButton,false);}
    }
    private void Group_ContextRequested(UIElement sender,ContextRequestedEventArgs args)
    {
        SelectContextItem(GroupList,args.OriginalSource as DependencyObject);
        var selected=GroupList.SelectedItems.OfType<PhotoGroup>().ToArray();
        MergeGroupsMenu.IsEnabled=selected.Length>=2 && selected.All(g=>g.Editable);
        DuplicateGroupMenu.IsEnabled=VM.SelectedEditable || VM.Selected is { IsWorking:true,TaskKind:"联网识别" };
        DeleteGroupsMenu.IsEnabled=!VM.Busy && selected.Length>0 && selected.All(g=>!g.IsWorking);
        DeleteArchivedMenu.IsEnabled=VM.CanDeleteAllArchivedGroups;
        RecycleGroupsMenu.IsEnabled=selected.Length>0 && selected.All(g=>g.Editable);
        CrashJournal.Trace("Menu.Groups",VM.Selected?.Id,selected.Length);
    }
    private void Photo_ContextRequested(UIElement sender,ContextRequestedEventArgs args)
    {
        SelectContextItem(PhotoList,args.OriginalSource as DependencyObject);
        var count=PhotoList.SelectedItems.Count;var editable=VM.SelectedEditable && count>0;
        var querying=VM.Selected is { IsWorking:true,TaskKind:"联网识别" };
        SplitPhotosMenu.IsEnabled=(editable || querying && count>0 && PhotoList.SelectedItems.OfType<Photo>().All(p=>!VM.Selected!.UploadingPhotoIds.Contains(p.Id))) && count<VM.Selected!.Photos.Count;
        CopyPhotosMenu.IsEnabled=editable || querying && count>0;RemovePhotosMenu.IsEnabled=editable;
        RecyclePhotosMenu.IsEnabled=editable;
        CrashJournal.Trace("Menu.Photos",VM.Selected?.Id,count);
    }
    private async void CopyPhotos_Click(object sender,RoutedEventArgs e) { try {var group=RequireGroup();var selected=PhotoList.SelectedItems.OfType<Photo>().ToArray();await DismissContextAsync();FlushEditor();var before=new Dictionary<string,string>();var copied=await VM.DuplicatePhotosAsync(group,selected,default);VM.RecordEdits(before,new(){copied.Id});RenderSelection(); }catch(Exception error) { VM.Status=error.Message; } }
    private void RemovePhotos_Click(object sender,RoutedEventArgs e)=>ExecuteCommand("removePhotos",sender,e);
    private async Task RemovePhotosCoreAsync()
    {
        try
        {
            var group=RequireGroup();var selected=PhotoList.SelectedItems.OfType<Photo>().ToArray();
            await DismissContextAsync();GroupPhotoEditor.Validate(group,selected);
            if(!await ConfirmAsync("removePhotos","从本组移除照片",new TextBlock { Text=$"仅移除本组中选中的 {selected.Length} 张照片引用，源照片和其他分组保持不变。",TextWrapping=TextWrapping.Wrap },"移除")) return;
            LocalAction(()=>VM.RemovePhotos(group,selected));
        } catch(Exception error) { VM.Status=error.Message; }
    }
    private async void CrashLogs_Click(object sender,RoutedEventArgs e)
    {
        try { var directory=Path.Combine(ViewModels.MainViewModel.DataDirectory,"crashes");Directory.CreateDirectory(directory);await Launcher.LaunchFolderPathAsync(directory); }
        catch(Exception error) { VM.Status=error.Message; }
    }
}
