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
        if(e.IsIntermediate || PreviewZoom.ZoomFactor<=1 || currentPhoto is not { } photo || PhotoService.IsVideo(photo) || originalPreviewId==photo.Id || originalPreviewLoading) return;
        originalPreviewLoading=true;
        try { var id=photo.Id;var file=await StorageFile.GetFileFromPathAsync(photo.Path);using var stream=await file.OpenReadAsync();var image=new BitmapImage();await image.SetSourceAsync(stream);if(currentPhoto?.Id==id) {MainImage.Source=image;originalPreviewId=id;DrawSavedCrop();} }
        catch(System.Exception error) { VM.Status="原图缩放不可用："+error.Message; }finally{originalPreviewLoading=false;}
    }
}
