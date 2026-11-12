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
    private static T? VisualChild<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T match) return match;
        if(root is Popup popup && popup.Child is { } content && VisualChild<T>(content) is { } found) return found;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            if(VisualChild<T>(VisualTreeHelper.GetChild(root,i)) is { } child) return child;
        return null;
    }
    private void WireSuggestions(AutoSuggestBox box,Action open,Action close)
    {
        box.Loaded+=(_,_)=>
        {
            HideSuggestionPopupUntilPositioned(box);
        };
        box.PreviewKeyDown+=(_,args)=> { if(args.Key==VirtualKey.Escape) close(); };
        box.GotFocus+=(_,_)=>
        {
            if(box.FocusState==FocusState.Pointer) return;
            var group=displayed?.Id;
            DispatcherQueue.TryEnqueue(()=> { if(displayed?.Id==group && InputFocused(box)) open(); });
        };
        box.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((_,args)=>
        {
            if(IsInsideSuggestionInput(box,args.OriginalSource as DependencyObject))
            {
                var group=displayed?.Id;
                DispatcherQueue.TryEnqueue(()=> { if(displayed?.Id==group) open(); });
            }
        }),true);
    }
    private static bool IsInsideSuggestionInput(AutoSuggestBox box,DependencyObject? source)
    {
        if(VisualChild<TextBox>(box) is not { } input) return false;
        for(var node=source;node is not null;node=VisualTreeHelper.GetParent(node))
        {
            if(ReferenceEquals(node,input)) return true;
            if(ReferenceEquals(node,box)) return false;
        }
        return false;
    }
    private static bool InputFocused(AutoSuggestBox box)=>VisualChild<TextBox>(box) is { } input && input.FocusState!=FocusState.Unfocused;
    private readonly Dictionary<AutoSuggestBox,int> popupVersions=[];
    private readonly HashSet<Popup> suggestionPopups=[];
    private void HideSuggestionPopupUntilPositioned(AutoSuggestBox box)
    {
        if(VisualChild<Popup>(box) is not { } popup || !suggestionPopups.Add(popup)) return;
        popup.DesiredPlacement=PopupPlacementMode.LeftEdgeAlignedTop;
        popup.ChildTransitions=new TransitionCollection { new PopupThemeTransition { FromHorizontalOffset=16 } };
        popup.Opacity=0;
        popup.Opened+=(_,_)=>
        {
            popup.DesiredPlacement=PopupPlacementMode.LeftEdgeAlignedTop;
            popup.Opacity=0;
        };
        popup.Closed+=(_,_)=>
        {
            popup.Opacity=0;
        };
    }
    private static Popup? SuggestionPopup(AutoSuggestBox box)
    {
        if(box.XamlRoot is not null && box.ItemsSource is IEnumerable<CategorySuggestion> source)
        {
            var items=source.ToArray();
            foreach(var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(box.XamlRoot))
                if(popup.Child is { } child && VisualChild<ListView>(child) is { } list && list.Items.Count>0 && list.Items[0] is CategorySuggestion candidate && items.Contains(candidate)) return popup;
        }
        return null;
    }
    private static ListView? SuggestionList(AutoSuggestBox box)
        => SuggestionPopup(box)?.Child is { } child?VisualChild<ListView>(child):VisualChild<ListView>(box);
    private static bool KeepSuggestionsLeftAndTop(AutoSuggestBox box,Popup popup,bool adjustOffset)
    {
        if(popup.Child is not FrameworkElement popupChild || VisualChild<TextBox>(box) is not { } input) return false;
        var bounds=popupChild.TransformToVisual(input).TransformBounds(new(0,0,popupChild.ActualWidth,popupChild.ActualHeight));
        if(Math.Abs(bounds.Right)<1 && Math.Abs(bounds.Top)<1) return true;
        if(adjustOffset) { popup.HorizontalOffset-=bounds.Right;popup.VerticalOffset-=bounds.Top; }
        return false;
    }
    private void PresentSuggestions(AutoSuggestBox box,IReadOnlyList<CategorySuggestion> matches)
    {
        var items=matches.ToArray();
        box.ItemsSource=items;box.IsSuggestionListOpen=items.Length>0;
        var version=popupVersions.GetValueOrDefault(box)+1;popupVersions[box]=version;
        if(items.Length==0) return;
        var timer=DispatcherQueue.CreateTimer();timer.Interval=TimeSpan.FromMilliseconds(20);var tries=0;var positionStable=0;var scrollStable=0;Popup? popup=null;Popup? adjustedPopup=null;var adjustedHorizontalOffset=0d;var adjustedVerticalOffset=0d;
        timer.Tick+=(_,_)=>
        {
            if(categoryClosed || !box.IsSuggestionListOpen || popupVersions.GetValueOrDefault(box)!=version || ++tries>25) { timer.Stop();return; }
            popup=SuggestionPopup(box);
            if(popup?.Child is not FrameworkElement popupContent) return;
            popupContent.UpdateLayout();
            var adjustOffset=!ReferenceEquals(adjustedPopup,popup) || Math.Abs(popup.HorizontalOffset-adjustedHorizontalOffset)>.5 || Math.Abs(popup.VerticalOffset-adjustedVerticalOffset)>.5;
            if(!KeepSuggestionsLeftAndTop(box,popup,adjustOffset))
            {
                if(adjustOffset) { adjustedPopup=popup;adjustedHorizontalOffset=popup.HorizontalOffset;adjustedVerticalOffset=popup.VerticalOffset; }
                positionStable=0;scrollStable=0;return;
            }
            positionStable++;
            var list=VisualChild<ListView>(popupContent);if(list is null) return;
            list.UpdateLayout();
            if(VisualChild<ScrollViewer>(list) is not { } scroll || scroll.ViewportHeight<=0) return;
            scroll.ChangeView(null,0,null,true);
            scrollStable=Math.Abs(scroll.VerticalOffset)<1?scrollStable+1:0;
            if(tries>=8 && positionStable>=2 && scrollStable>=2) { popup.Opacity=1;timer.Stop(); }
        };
        timer.Start();
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
        if(sender is Image { Source:BitmapImage bitmap,Parent:StackPanel panel } && bitmap.PixelHeight>0)
            { imageThumbnailHeight=(BodyGrid.ActualHeight<600?96d:120d);((Image)sender).Height=imageThumbnailHeight;panel.Width=imageThumbnailHeight*bitmap.PixelWidth/bitmap.PixelHeight; }
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
    private void Group_ContextRequested(UIElement sender,ContextRequestedEventArgs args)
    {
        SelectContextItem(GroupList,args.OriginalSource as DependencyObject);
        var selected=GroupList.SelectedItems.OfType<PhotoGroup>().ToArray();
        MergeGroupsMenu.IsEnabled=selected.Length>=2 && selected.All(g=>g.Editable);
        DuplicateGroupMenu.IsEnabled=VM.SelectedEditable;
        DeleteGroupsMenu.IsEnabled=!VM.Busy && selected.Length>0 && selected.All(g=>!g.IsWorking);
        DeleteArchivedMenu.IsEnabled=VM.CanDeleteAllArchivedGroups;
        CrashJournal.Trace("Menu.Groups",VM.Selected?.Id,selected.Length);
    }
    private void Photo_ContextRequested(UIElement sender,ContextRequestedEventArgs args)
    {
        SelectContextItem(PhotoList,args.OriginalSource as DependencyObject);
        var count=PhotoList.SelectedItems.Count;var editable=VM.SelectedEditable && count>0;
        SplitPhotosMenu.IsEnabled=editable && count<VM.Selected!.Photos.Count;
        CopyPhotosMenu.IsEnabled=ExcludePhotosMenu.IsEnabled=RemovePhotosMenu.IsEnabled=editable;
        CrashJournal.Trace("Menu.Photos",VM.Selected?.Id,count);
    }
    private void CopyPhotos_Click(object sender,RoutedEventArgs e)=>LocalAction(()=>VM.DuplicatePhotos(RequireGroup(),PhotoList.SelectedItems.OfType<Photo>().ToArray()));
    private async void RemovePhotos_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            var group=RequireGroup();var selected=PhotoList.SelectedItems.OfType<Photo>().ToArray();
            GroupPhotoEditor.Validate(group,selected);
            if(await DialogAsync("从本组移除照片",new TextBlock { Text=$"仅移除本组中选中的 {selected.Length} 张照片引用，源照片和其他分组保持不变。",TextWrapping=TextWrapping.Wrap },"移除")!=ContentDialogResult.Primary) return;
            LocalAction(()=>VM.RemovePhotos(group,selected));
        } catch(Exception error) { VM.Status=error.Message; }
    }
    private async void CrashLogs_Click(object sender,RoutedEventArgs e)
    {
        try { var directory=Path.Combine(ViewModels.MainViewModel.DataDirectory,"crashes");Directory.CreateDirectory(directory);await Launcher.LaunchFolderPathAsync(directory); }
        catch(Exception error) { VM.Status=error.Message; }
    }
}
