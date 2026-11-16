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
        PreviewZoom.Focus(FocusState.Pointer);var delta=e.GetCurrentPoint(PreviewHost).Properties.MouseWheelDelta;
        PreviewZoom.ChangeView(null,null,Math.Clamp(PreviewZoom.ZoomFactor*(delta>0?1.2f:1/1.2f),1,8));e.Handled=true;
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
    private async void PreviewZoom_Changed(object sender,ScrollViewerViewChangedEventArgs e)
    {
        if(e.IsIntermediate || PreviewZoom.ZoomFactor<=1 || currentPhoto is not { } photo || PhotoService.IsVideo(photo)) return;
        try { var id=photo.Id;var file=await StorageFile.GetFileFromPathAsync(photo.Path);using var stream=await file.OpenReadAsync();var image=new BitmapImage();await image.SetSourceAsync(stream);if(currentPhoto?.Id==id) MainImage.Source=image; }
        catch(System.Exception error) { VM.Status="原图缩放不可用："+error.Message; }
    }
}
