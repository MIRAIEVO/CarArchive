using System.Numerics;
using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
namespace CarArchive;
public sealed partial class MainPage
{
 private async Task AnimateMergeAsync(IReadOnlyList<PhotoGroup> groups,Action merge)
 {
  if(!UiMotion.Enabled){merge();return;}
  var originals=new Dictionary<string,double>();var moving=new List<UIElement>();
  foreach(var group in VM.Groups)if(GroupList.ContainerFromItem(group) is ListViewItem row)originals[group.Id]=row.TransformToVisual(QueuePane).TransformPoint(new()).Y;
  var overlay=new Canvas {IsHitTestVisible=false,Clip=new RectangleGeometry {Rect=new(0,0,QueuePane.ActualWidth,QueuePane.ActualHeight)}};
  var survivor=GroupMergePolicy.Choose(groups);var target=originals.GetValueOrDefault(survivor.Id);
  EventHandler<object>? layout=null;var ready=new TaskCompletionSource();
  try
  {
   // Capture everything first. Never hide a live row while another snapshot is awaiting rendering.
   foreach(var group in groups)
   {
    if(GroupList.ContainerFromItem(group) is not ListViewItem row||row.ActualWidth<1)continue;
    var origin=row.TransformToVisual(QueuePane).TransformPoint(new());if(origin.Y+row.ActualHeight<0||origin.Y>QueuePane.ActualHeight)continue;
    var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(row);var card=new Image {Source=bitmap,Width=row.ActualWidth,Height=row.ActualHeight};Canvas.SetLeft(card,origin.X);Canvas.SetTop(card,origin.Y);overlay.Children.Add(card);
   }
   QueuePane.Children.Add(overlay);
   layout=(_,_)=>
   {
    GroupList.LayoutUpdated-=layout;
    foreach(var group in VM.Groups)
    {
     if(GroupList.ContainerFromItem(group) is not ListViewItem row)continue;
     var y=row.TransformToVisual(QueuePane).TransformPoint(new()).Y;
     ElementCompositionPreview.SetIsTranslationEnabled(row,true);var visual=ElementCompositionPreview.GetElementVisual(row);visual.StopAnimation("Translation");visual.Properties.InsertVector3("Translation",Vector3.Zero);
     if(originals.TryGetValue(group.Id,out var old)&&Math.Abs(old-y)>.1)
     {var animation=visual.Compositor.CreateVector3KeyFrameAnimation();animation.InsertKeyFrame(0,new(0,(float)(old-y),0));animation.InsertKeyFrame(1,Vector3.Zero,visual.Compositor.CreateCubicBezierEasingFunction(new(.2f,.8f),new(.2f,1)));animation.Duration=TimeSpan.FromMilliseconds(180);visual.StartAnimation("Translation",animation);moving.Add(row);}
     if(group.Id==survivor.Id)UiMotion.Fade(row,0,1,160);
    }
    ready.TrySetResult();
   };
   GroupList.LayoutUpdated+=layout;merge();await Task.WhenAny(ready.Task,Task.Delay(250));
   foreach(Image card in overlay.Children)
   {ElementCompositionPreview.SetIsTranslationEnabled(card,true);var visual=ElementCompositionPreview.GetElementVisual(card);var animation=visual.Compositor.CreateVector3KeyFrameAnimation();animation.InsertKeyFrame(1,new(0,(float)Math.Clamp(target-Canvas.GetTop(card),-64,64),0));animation.Duration=TimeSpan.FromMilliseconds(160);visual.StartAnimation("Translation",animation);UiMotion.Fade(card,1,0,160);}
   await Task.Delay(190);
  }
  finally
  {
   if(layout is not null)GroupList.LayoutUpdated-=layout;
   QueuePane.Children.Remove(overlay);
   foreach(var row in moving){var visual=ElementCompositionPreview.GetElementVisual(row);visual.StopAnimation("Translation");visual.Properties.InsertVector3("Translation",Vector3.Zero);}
  }
 }
}
