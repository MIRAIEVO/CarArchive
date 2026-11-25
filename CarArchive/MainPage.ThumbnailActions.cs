using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace CarArchive;

public sealed partial class MainPage
{
    private Photo? lastThumbnailSelection;
    private Photo? cachedThumbnailAnchor;
    private double thumbnailAnchorRight;
    private ScrollViewer? thumbnailActionScroll;

    private (bool Copy,bool Split) AvailablePhotoActions()
    {
        if(VM.Selected is not { } group || PhotoList.SelectedItems.Count==0) return (false,false);
        var querying=group is { IsWorking:true,TaskKind:"联网识别" };
        var canCopy=VM.SelectedEditable || querying;
        var canSplit=canCopy && PhotoList.SelectedItems.Count<group.Photos.Count &&
            (!querying || PhotoList.SelectedItems.OfType<Photo>().All(photo=>!group.UploadingPhotoIds.Contains(photo.Id)));
        return (canCopy,canSplit);
    }

    private void InitializeThumbnailSelectionTracking()
    {
        PhotoList.AddHandler(PointerPressedEvent,new PointerEventHandler((_,args)=>
        {
            if(args.GetCurrentPoint(PhotoList).Properties.IsLeftButtonPressed)
                TrackThumbnailAnchor(args.OriginalSource as DependencyObject);
        }),true);
        PhotoList.GettingFocus+=(_,args)=>
        {
            if(args.InputDevice==FocusInputDeviceKind.Keyboard) TrackThumbnailAnchor(args.NewFocusedElement);
        };
        PhotoList.SizeChanged+=(_,_)=>UpdateThumbnailSelectionActions();
        PhotoList.ContainerContentChanging+=(_,_)=>DispatcherQueue.TryEnqueue(UpdateThumbnailSelectionActions);
        if(VisualChild<ScrollViewer>(PhotoList) is { } scroll)
        {
            thumbnailActionScroll=scroll;
            scroll.ViewChanged+=(_,_)=>UpdateThumbnailSelectionActions();
        }
    }

    private void TrackThumbnailAnchor(DependencyObject? node)
    {
        for(;node is not null;node=VisualTreeHelper.GetParent(node))
        {
            if(node is not ListViewItem {Content:Photo photo}) continue;
            lastThumbnailSelection=photo;
            DispatcherQueue.TryEnqueue(UpdateThumbnailSelectionActions);
            return;
        }
    }

    private void UpdateThumbnailSelectionActions()
    {
        var (copy,split)=AvailablePhotoActions();
        if(rendering || !copy || PhotoList.SelectedItems.Count<2 || ThumbnailSurface.ActualWidth<=0)
        {
            ThumbnailSelectionActions.Visibility=Visibility.Collapsed;
            cachedThumbnailAnchor=null;
            return;
        }
        var anchor=lastThumbnailSelection is { } clicked && PhotoList.SelectedItems.Contains(clicked)
            ? clicked : PhotoList.SelectedItems.OfType<Photo>().Last();
        var offset=thumbnailActionScroll?.HorizontalOffset??0;
        if(PhotoList.ContainerFromItem(anchor) is ListViewItem { ActualWidth:>0 } item)
        {
            var right=item.TransformToVisual(ThumbnailSurface).TransformPoint(new Windows.Foundation.Point(item.ActualWidth,0)).X;
            cachedThumbnailAnchor=anchor;
            thumbnailAnchorRight=right+offset;
        }
        if(cachedThumbnailAnchor!=anchor)
        {
            ThumbnailSelectionActions.Visibility=Visibility.Collapsed;
            return;
        }
        var maxLeft=Math.Max(0,ThumbnailSurface.ActualWidth-ThumbnailSelectionActions.Width-6);
        var left=Math.Clamp(thumbnailAnchorRight-offset-ThumbnailSelectionActions.Width-4,Math.Min(6,maxLeft),maxLeft);
        ThumbnailSelectionActions.Margin=new Thickness(left,6,0,0);
        CopySelectedThumbnails.IsEnabled=copy;
        SplitSelectedThumbnails.IsEnabled=split;
        if(ThumbnailSelectionActions.Visibility!=Visibility.Visible)
        {
            ThumbnailSelectionActions.Visibility=Visibility.Visible;
            UiMotion.Reveal(ThumbnailSelectionActions,false);
        }
    }
}
