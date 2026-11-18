#if DEBUG
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System.Text.Json;
namespace CarArchive;
public sealed partial class MainPage
{
 private async Task VerifyNoFlashAsync(string directory)
 {
  Directory.CreateDirectory(directory);var checks=new List<string>();void Check(bool value,string label){if(!value)throw new Exception(label);checks.Add(label);}
  try
  {
   var a=await FixtureImageAsync(Path.Combine(directory,"portrait.jpg"),800,1600);await VM.ImportAsync([a],true,default);await VM.BackgroundImport;var first=VM.Selected!;RenderSelection();await Task.Delay(600);
   var b=await FixtureImageAsync(Path.Combine(directory,"landscape.jpg"),1600,800);await VM.ImportAsync([b],true,default);await VM.BackgroundImport;var second=VM.Selected!;RenderSelection();await Task.Delay(600);
   PreviewZoom.ChangeView(120,180,2.5f,true);await Task.Delay(220);
   var oldZoom=PreviewZoom.ZoomFactor;var oldX=PreviewZoom.HorizontalOffset;var oldY=PreviewZoom.VerticalOffset;
   Check(oldZoom>1&&oldX>0&&oldY>0,"The original viewport is zoomed and panned");
   var oldSource=MainImage.Source;var oldBounds=ImageRect();
   GroupList.SelectedItem=first;
   Check(PreviewZoom.ZoomFactor==oldZoom&&PreviewZoom.HorizontalOffset==oldX&&PreviewZoom.VerticalOffset==oldY,"Old zoom and pan remain until the cover is presented");
   var image=PreviewTransitionCover.Children.OfType<Microsoft.UI.Xaml.Controls.Image>().FirstOrDefault();
   Check(image is not null&&ReferenceEquals(image.Source,oldSource)&&Math.Abs(image.Width-oldBounds.Width*oldZoom)<1&&Math.Abs(Canvas.GetLeft(image)-(oldBounds.X*oldZoom-oldX))<2&&Math.Abs(Canvas.GetTop(image)-(oldBounds.Y*oldZoom-oldY))<2,"Cover reuses the decoded image at the same zoom and pan");
   Check(ThumbnailTransitionCover.Visibility==Visibility.Visible&&ThumbnailTransitionCover.Children.Count>0,"Old thumbnail strip remains covering items-source rebuild");
   Check(PreviewTransitionCover.Visibility==Visibility.Visible&&PreviewTransitionCover.Background is SolidColorBrush {Color.A:255},"The full cover includes opaque letterboxing");
   Check(ElementCompositionPreview.GetElementVisual(PreviewSurface).Opacity==1&&ElementCompositionPreview.GetElementVisual(ThumbnailSurface).Opacity==1,"Live preview surfaces never fade to transparent");
   await Task.Delay(600);Check(PreviewTransitionCover.Visibility==Visibility.Collapsed&&ThumbnailTransitionCover.Visibility==Visibility.Collapsed,"Covers removed after new frames render");
   var noBlankFrame=true;
   for(var i=0;i<240;i++){GroupList.SelectedItem=i%2==0?second:first;noBlankFrame&=MainImage.Source is not null;await Task.Delay(i%12==0?250:8);}
   Check(noBlankFrame,"240 selection changes retain a decoded bitmap");
   await Task.Delay(700);Check(VM.Selected==first&&loadedPhotoId==first.Photos[0].Id&&PreviewHost.IsHitTestVisible,"Latest frame and crop remain active after rapid switches");
   Check(PreviewTransitionCover.Children.Count==0&&ThumbnailTransitionCover.Children.Count==0,"Overlays release their images after the final switch");
   Check(VM.State.BatchCalls==0,"Zero API calls");File.WriteAllText(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new{Status="Passed",Checks=checks},StateStore.Json));
  }
  catch(Exception e){File.WriteAllText(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new{Status="Failed",Checks=checks,Error=e.ToString()},StateStore.Json));}
 }
}
#endif
