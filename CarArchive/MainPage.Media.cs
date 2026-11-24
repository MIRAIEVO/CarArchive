using CarArchive.Core;
using CarArchive.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
namespace CarArchive;
public sealed partial class MainPage
{
    private void PreviewZoom_SizeChanged(object sender,SizeChangedEventArgs e) { PreviewHost.Width=Math.Max(1,e.NewSize.Width);PreviewHost.Height=Math.Max(1,e.NewSize.Height);DrawSavedCrop(); }
    private void Preview_WheelChanged(object sender,Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if(currentPhoto is null || PhotoService.IsVideo(currentPhoto)&&VideoPlayer.Visibility==Visibility.Visible) return;
        PreviewZoom.Focus(FocusState.Pointer);var point=e.GetCurrentPoint(PreviewZoom);var delta=point.Properties.MouseWheelDelta;
        var next=(float)(PreviewZoom.ZoomFactor*Math.Pow(1.2,delta/120d));var anchor=ZoomAnchor.At(PreviewZoom.HorizontalOffset,PreviewZoom.VerticalOffset,PreviewZoom.ZoomFactor,next,point.Position.X,point.Position.Y,PreviewHost.ActualWidth,PreviewHost.ActualHeight,PreviewZoom.ViewportWidth,PreviewZoom.ViewportHeight);
        PreviewZoom.ChangeView(anchor.X,anchor.Y,anchor.Zoom,true);e.Handled=true;
    }
    private async void VideoCrop_Click(object sender,RoutedEventArgs e) { if(currentPhoto is {} photo && PhotoService.IsVideo(photo)) await CaptureVideoFrameAsync(); }
    private void CapturePreviewVideoPosition()
    {
        if(currentPhoto is {} photo && PhotoService.IsVideo(photo) && VideoPlayer.Visibility==Visibility.Visible && VideoPlayer.MediaPlayer is {} player) {player.Pause();photo.VideoPositionSeconds=player.PlaybackSession.Position.TotalSeconds;}
    }
    private async System.Threading.Tasks.Task CaptureVideoFrameAsync()
    {
        if(currentPhoto is not { } photo || !PhotoService.IsVideo(photo)) return;
        var owner=displayed;CapturePreviewVideoPosition();
        var path=await VM.Images.DecodePathAsync(photo,default);
        if(owner is not null)VM.SaveGroup(owner);if(currentPhoto?.Id!=photo.Id || displayed!=owner)return;
        MainImage.Source=new BitmapImage(new System.Uri(path));MainImage.Visibility=Visibility.Visible;VideoPlayer.Visibility=Visibility.Collapsed;DrawSavedCrop();
    }
    private string? originalPreviewId;
    private bool originalPreviewLoading;
    private async void PreviewZoom_Changed(object sender,ScrollViewerViewChangedEventArgs e)
    {
        if(e.IsIntermediate || !PreviewHost.IsHitTestVisible || PreviewZoom.ZoomFactor<=1 || currentPhoto is not { } photo || PhotoService.IsVideo(photo) || originalPreviewId==photo.Id || originalPreviewLoading) return;
        originalPreviewLoading=true;
        var version=previewVersion;var token=previewCancellation?.Token??default;
        try
        {
            var file=await StorageFile.GetFileFromPathAsync(photo.Path);using var stream=await file.OpenReadAsync();
            token.ThrowIfCancellationRequested();var image=new BitmapImage();await image.SetSourceAsync(stream).AsTask(token);
            while(drawing && version==previewVersion)await Task.Delay(16,token);
            if(version==previewVersion && currentPhoto?.Id==photo.Id && PreviewZoom.ZoomFactor>1)
            {MainImage.Source=image;originalPreviewId=photo.Id;DrawSavedCrop();}
        }
        catch(OperationCanceledException) { }
        catch(System.Exception error) { CrashJournal.Fault("Preview.ZoomDecode",error);if(version==previewVersion)VM.Status="原图缩放不可用："+error.Message; }
        finally{originalPreviewLoading=false;}
    }
}
