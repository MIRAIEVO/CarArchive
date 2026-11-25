using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace CarArchive;

public sealed partial class MainPage
{
    private Photo? lastThumbnailSelection;

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
            return;
        }
        var anchor=lastThumbnailSelection is { } clicked && PhotoList.SelectedItems.Contains(clicked)
            ? clicked : PhotoList.SelectedItems.OfType<Photo>().Last();
        if(PhotoList.ContainerFromItem(anchor) is not ListViewItem { ActualWidth:>0 } item ||
            VisualChild<Image>(item) is not { ActualWidth:>0,ActualHeight:>0 } image)
        {
            ThumbnailSelectionActions.Visibility=Visibility.Collapsed;
            return;
        }
        var origin=image.TransformToVisual(ThumbnailSurface).TransformPoint(new Windows.Foundation.Point());
        var leftEdge=Math.Max(0,origin.X);
        var rightEdge=Math.Min(ThumbnailSurface.ActualWidth,origin.X+image.ActualWidth);
        var width=Math.Min(52,rightEdge-leftEdge-6);
        if(width<28)
        {
            ThumbnailSelectionActions.Visibility=Visibility.Collapsed;
            return;
        }
        ThumbnailSelectionActions.Width=width;
        ThumbnailSelectionActions.Margin=new Thickness(leftEdge+(rightEdge-leftEdge-width)/2,
            Math.Max(0,origin.Y+(image.ActualHeight-64)/2),0,0);
        CopySelectedThumbnails.IsEnabled=copy;
        SplitSelectedThumbnails.IsEnabled=split;
        if(ThumbnailSelectionActions.Visibility!=Visibility.Visible)
        {
            ThumbnailSelectionActions.Visibility=Visibility.Visible;
            UiMotion.Reveal(ThumbnailSelectionActions,false);
        }
    }
}
