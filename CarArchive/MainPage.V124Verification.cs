#if DEBUG
using System.Text.Json;
using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;
namespace CarArchive;
public sealed partial class MainPage
{
 private async Task RunV124VerificationAsync(string directory)
 {
  Directory.CreateDirectory(directory);var checks=new List<string>();
  void Check(bool value,string text){if(!value)throw new Exception(text);checks.Add(text);}
  void Centered()
  {
   var row=Descendants(RecognizeActionButton).OfType<StackPanel>().First(p=>p.Orientation==Orientation.Horizontal);
   var bounds=row.TransformToVisual(RecognizeActionButton).TransformPoint(new());
   Check(Math.Abs(bounds.X+row.ActualWidth/2-RecognizeActionButton.ActualWidth/2)<1,"Icon and label are centered as one compact row");
   Check(row.Spacing==8,"Icon and label retain an eight-pixel gap");
  }
  try
  {
   Check(VM.Groups.Count==0,"Empty isolated workspace");
   var image=await FixtureImageAsync(Path.Combine(directory,"fixture.jpg"),1200,800);
   await VM.ImportAsync([image],true,default);await VM.BackgroundImport;var group=VM.Selected!;RenderSelection();await Task.Delay(240);
   var width=RecognizeActionButton.ActualWidth;var x=RecognizeActionButton.TransformToVisual(this).TransformPoint(new()).X;
   Check(width>90&&width<135,"Recognition button uses compact measured width");Centered();
   Check(UiMotion.Enabled==new UISettings().AnimationsEnabled,"Motion honors Windows animation preference");
   Check(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(RecognizeActionButton)=="Gemini识别","Recognition retains accessible name");
   await CaptureVisualAsync(this,Path.Combine(directory,"recognition-light.png"));
   var task=VM.RunGroupAsync(group,"联网识别",false,ct=>Task.Delay(Timeout.Infinite,ct));await Task.Delay(150);
   Check(RecognizeActionButton.Label=="取消识别"&&RecognizeActionButton.IsEnabled,"Same button exposes cancellation during recognition");
   Check(Math.Abs(RecognizeActionButton.ActualWidth-width)<.1&&Math.Abs(RecognizeActionButton.TransformToVisual(this).TransformPoint(new()).X-x)<.1,"Cancellation preserves exact width and position");Centered();
   foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark})
   {
    RequestedTheme=theme;await Task.Delay(180);
    SetReversalPalette(ArchiveActionButton,true);ArchiveActionButton.Style=(Style)Resources["UndoMoveActionStyle"];ArchiveActionButton.Content="撤回移动";
    var normal=((SolidColorBrush)Resources["ReversalBackgroundBrush"]).Color;
    var hover=((SolidColorBrush)Resources["ReversalHoverBrush"]).Color;
    Check(normal.R>normal.G&&normal.R>normal.B&&hover.G>normal.G,"Deeper red with lighter hover in "+theme);
    Check(ReferenceEquals(RecognizeActionButton.Resources["ButtonBackgroundPointerOver"],ArchiveActionButton.Resources["ButtonBackgroundPointerOver"]),"Cancellation and undo share hover palette in "+theme);
    await CaptureVisualAsync(this,Path.Combine(directory,"cancel-"+theme+".png"));
    VisualStateManager.GoToState(RecognizeActionButton,"PointerOver",true);VisualStateManager.GoToState(ArchiveActionButton,"PointerOver",true);await Task.Delay(160);
    await CaptureVisualAsync(this,Path.Combine(directory,"hover-"+theme+".png"));
   }
   RequestedTheme=ElementTheme.Light;ExecuteCommand("recognize",this,new());await task;await Task.Delay(140);
   Check(!group.IsWorking&&RecognizeActionButton.Label=="Gemini识别","Cancellation restores normal recognition without API calls");
   UiMotion.Reveal(EditorPane);await Task.Delay(200);
   Check(Math.Abs(Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(EditorPane).Opacity-1)<.01,"Reveal settles without changing layout");
   Check(VM.State.BatchCalls==0,"No real API calls");await VM.FlushWritesAsync();
   File.WriteAllText(Path.Combine(directory,"v124-verification.json"),JsonSerializer.Serialize(new{Status="Passed",Width=width,Checks=checks},StateStore.Json));
  }
  catch(Exception error){File.WriteAllText(Path.Combine(directory,"v124-verification.json"),JsonSerializer.Serialize(new{Status="Failed",Checks=checks,Error=error.ToString()},StateStore.Json));}
 }
}
#endif
