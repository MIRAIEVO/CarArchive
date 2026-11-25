#if DEBUG
using System.Text.Json;
using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
namespace CarArchive;
public sealed partial class MainPage
{
 private async Task PrepareManual125Async(string directory)
 {
  Directory.CreateDirectory(directory);App.MainWindow!.Title="CarArchive 1.2.5 — 隔离鼠标测试";
  for(var g=0;g<6;g++)
  {
   var files=new List<string>();for(var i=0;i<3;i++)files.Add(await FixtureImageAsync(Path.Combine(directory,$"group{g}-photo{i}.jpg"),i==1?800u:1600u,i==1?1600u:800u));
   await VM.ImportAsync(files,true,default);await VM.BackgroundImport;
  }
  VM.Selected=VM.Groups[0];RenderSelection();await VM.FlushWritesAsync();File.WriteAllText(Path.Combine(directory,"ready.txt"),"Ready");
 }
 private async Task RunPolish125Async(string directory)
 {
  Directory.CreateDirectory(directory);var checks=new List<string>();
  void Check(bool value,string label){if(!value)throw new Exception(label);checks.Add(label);}
  try
  {
   var gate=new TaskCompletionSource();previewReadinessGate=gate.Task;
   var path=await FixtureImageAsync(Path.Combine(directory,"portrait.jpg"),800,1600);
   await VM.ImportAsync([path],true,default);await VM.BackgroundImport;var group=VM.Selected!;RenderSelection();for(var i=0;i<100&&(!PreviewHost.IsHitTestVisible||MainImage.Source is null);i++)await Task.Delay(20);
   Check(PreviewHost.IsHitTestVisible&&MainImage.Source is not null&&previewLoading,"Thumbnail permits cropping while high-resolution decode is held");
   group.Photos[0].Crop=new(.1,.2,.5,.4);DrawSavedCrop();var normalized=group.Photos[0].Crop;gate.SetResult();previewReadinessGate=null;
   for(var i=0;i<100&&previewLoading;i++)await Task.Delay(20);
   Check(group.Photos[0].Crop==normalized&&CropOutline.Visibility==Visibility.Visible,"Normalized crop survives high-resolution replacement");
   var content=Descendants(RecognizeActionButton).OfType<Grid>().Single(g=>g.Name=="ContentRoot");
   var pos=content.TransformToVisual(RecognizeActionButton).TransformPoint(new());
   Check(Math.Abs(pos.X+content.ActualWidth/2-RecognizeActionButton.ActualWidth/2)<1,"Icon and AI label centered together");
   Check(content.ColumnDefinitions[0].Width.IsAuto,"Native icon-label gap is not expanded by a star column");
   Check(RecognizeActionButton.Label=="AI识别","AI recognition label");var width=RecognizeActionButton.ActualWidth;
   var job=VM.RunGroupAsync(group,"联网识别",false,ct=>Task.Delay(Timeout.Infinite,ct));await Task.Delay(100);
   Check(RecognizeActionButton.Label=="取消识别"&&RecognizeActionButton.ActualWidth==width,"Fixed cancellation size");
   ExecuteCommand("recognize",this,new());await job;await Task.Delay(150);
   var source=MainImage.Source;ApplyPastedIdentity(group,new(){Brand="Audi",Series="A4",Variant="A4 Avant",Generation="5th"});RenderSelection();await Task.Delay(150);
   Check(ReferenceEquals(source,MainImage.Source),"JSON updates reuse the displayed bitmap");
   Check(PreviewSurface.Background is not null&&ThumbnailSurface.Background is not null,"Both full preview surfaces include theme backgrounds");
   UiMotion.Fade(PreviewSurface,0,1,120);UiMotion.Fade(ThumbnailSurface,0,1,120);await Task.Delay(180);
   Check(ElementCompositionPreview.GetElementVisual(PreviewSurface).Opacity==1&&ElementCompositionPreview.GetElementVisual(ThumbnailSurface).Opacity==1,"Full-surface fades settle at full opacity");
   recognitionHovered=true;VisualStateManager.GoToState(RecognizeActionButton,"PointerOver",false);
   var hoveredJob=VM.RunGroupAsync(group,"联网识别",false,ct=>Task.Delay(Timeout.Infinite,ct));await Task.Delay(120);RefreshActionPresentation();await Task.Delay(120);
   var actionBorder=Descendants(RecognizeActionButton).OfType<Border>().Single(b=>b.Name=="AppBarButtonInnerBorder");
   Check(actionBorder.Background is SolidColorBrush red && red.Color.R>red.Color.G,"Stationary hover turns cancellation red");
   ExecuteCommand("recognize",this,new());await hoveredJob;recognitionHovered=false;RefreshActionPresentation();
   Check(((SolidColorBrush)PreviewSurface.Background!).Color.A==255&&((SolidColorBrush)ThumbnailSurface.Background!).Color.A==255,"Preview padding backgrounds are opaque solid colors");
   var wide=await FixtureImageAsync(Path.Combine(directory,"wide.jpg"),1600,800);await VM.ImportAsync([wide],false,default);await VM.BackgroundImport;
   VM.Selected=group;RenderSelection();await Task.Delay(100);
   var secondGroup=VM.Groups.First(g=>g!=group);GroupList.SelectedItems.Clear();GroupList.SelectedItems.Add(group);GroupList.SelectedItems.Add(secondGroup);lastMergeSelection=group;
   TrackMergeSelection(new SelectionChangedEventArgs(new object[]{},new object[]{group,secondGroup}));
   Check(lastMergeSelection==group,"Shift range additions cannot override clicked anchor");
   Merge_Click(this,new());for(var i=0;i<60&&(VM.Groups.Count>1||mergingGroups);i++)await Task.Delay(30);
   Check(VM.Groups.Count==1&&!QueuePane.Children.OfType<Canvas>().Any(),"Snapshot merge executes once and cleans up");group=VM.Selected!;RenderSelection();await Task.Delay(150);
   var selected=group.Photos[0];PhotoList.SelectedItem=selected;await Task.Delay(100);
   var item=(ListViewItem)PhotoList.ContainerFromItem(selected);var thumb=VisualChild<Image>(item)!;ApplyThumbnailSize(thumb);
   var originalWidth=((StackPanel)thumb.Parent).Width;PhotoList.SelectedItem=group.Photos[1];PhotoList.SelectedItem=selected;await Task.Delay(100);
   Check(Math.Abs(((StackPanel)thumb.Parent).Width-originalWidth)<.1&&thumb.Height==imageThumbnailHeight,"Selection and cached bindings retain thumbnail size");
   var anchor=group.Photos[1];
   lastThumbnailSelection=anchor;PhotoList.SelectedItems.Add(anchor);await Task.Delay(100);
   UpdateThumbnailSelectionActions();await Task.Delay(80);
   var anchorImage=VisualChild<Image>((ListViewItem)PhotoList.ContainerFromItem(anchor))!;
   var anchorPosition=anchorImage.TransformToVisual(ThumbnailSurface).TransformPoint(new());
   var actionPosition=ThumbnailSelectionActions.TransformToVisual(ThumbnailSurface).TransformPoint(new());
   Check(ThumbnailSelectionActions.Visibility==Visibility.Visible && ThumbnailSelectionActions.Orientation==Orientation.Vertical &&
       actionPosition.X>=anchorPosition.X && actionPosition.X+ThumbnailSelectionActions.ActualWidth<=anchorPosition.X+anchorImage.ActualWidth &&
       actionPosition.Y>=anchorPosition.Y && actionPosition.Y+ThumbnailSelectionActions.ActualHeight<=anchorPosition.Y+anchorImage.ActualHeight,
       "Vertical actions are inside the last selected thumbnail");
   PhotoList.SelectedItems.Remove(anchor);
   var prompt=BuildVerificationPrompt(new(){Brand="Fixture",Series="Verification"});
   Check(prompt.Contains("回复第一行")&&prompt.Contains("通过但有补充")&&prompt.Contains("限量台数")&&prompt.Contains("完整 JSON"),"Verification requests verdict, edition numbers and full JSON");
   var menu=(MenuFlyout)PhotoList.ContextFlyout;menu.ShowAt(PhotoList);await Task.Delay(140);
   ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(SplitPhotosMenu).GetPattern(PatternInterface.Invoke)).Invoke();
   for(var i=0;i<60&&VM.Groups.Count<2;i++)await Task.Delay(30);
   Check(VM.Groups.Count==2&&activeContextMenu is null,"Actual context menu split closes and executes exactly once");
   menu.ShowAt(PhotoList);await Task.Delay(100);var splitUndo=menu.Items.OfType<MenuFlyoutItem>().Single(i=>i.Text=="撤回本组操作");((IInvokeProvider)new MenuFlyoutItemAutomationPeer(splitUndo).GetPattern(PatternInterface.Invoke)).Invoke();
   for(var i=0;i<100&&VM.Groups.Count>1;i++)await Task.Delay(30);
   Check(VM.Groups.Count==1,"Actual context menu split undo restores original group");
   VM.Selected=VM.Groups.Single();RenderSelection();await Task.Delay(100);menu.ShowAt(PhotoList);await Task.Delay(100);var splitRedo=menu.Items.OfType<MenuFlyoutItem>().Single(i=>i.Text=="重做本组操作");((IInvokeProvider)new MenuFlyoutItemAutomationPeer(splitRedo).GetPattern(PatternInterface.Invoke)).Invoke();
   for(var i=0;i<100&&VM.Groups.Count<2;i++)await Task.Delay(30);
   Check(VM.Groups.Count==2,"Actual context menu redo restores split");
   VM.State.Settings.SkippedConfirmations.Add("removePhotos");VM.State.Settings.SkippedConfirmations.Add("recyclePhotos");
   var copiedGroup=await VM.DuplicateAsync(VM.Selected!);VM.Selected=copiedGroup;RenderSelection();await Task.Delay(150);PhotoList.SelectedItem=copiedGroup.Photos[0];
   menu.ShowAt(PhotoList);await Task.Delay(100);((IInvokeProvider)new MenuFlyoutItemAutomationPeer(RemovePhotosMenu).GetPattern(PatternInterface.Invoke)).Invoke();
   for(var i=0;i<60&&VM.Groups.Contains(copiedGroup);i++)await Task.Delay(30);
   Check(!VM.Groups.Contains(copiedGroup),"Actual context menu remove executes without recycling source");
   VM.Selected=VM.Groups.First();RenderSelection();await Task.Delay(100);PhotoList.SelectedItem=VM.Selected.Photos[0];var beforeCopy=VM.Groups.Count;var copyOwner=VM.Selected;
   menu.ShowAt(PhotoList);await Task.Delay(100);((IInvokeProvider)new MenuFlyoutItemAutomationPeer(CopyPhotosMenu).GetPattern(PatternInterface.Invoke)).Invoke();
   for(var i=0;i<100&&(VM.Groups.Count==beforeCopy||VM.Selected==copyOwner||VM.Selected?.Editable!=true);i++)await Task.Delay(30);await Task.Delay(120);
   Check(VM.Groups.Count==beforeCopy+1,"Actual context menu copy creates one independent group");
   var copied=VM.Selected!;var copiedPath=copied.Photos[0].Path;
   menu.ShowAt(PhotoList);await Task.Delay(100);var undoMenu=menu.Items.OfType<MenuFlyoutItem>().Single(i=>i.Text=="撤回本组操作");((IInvokeProvider)new MenuFlyoutItemAutomationPeer(undoMenu).GetPattern(PatternInterface.Invoke)).Invoke();
   for(var i=0;i<100&&VM.Groups.Contains(copied);i++)await Task.Delay(30);
   Check(!VM.Groups.Contains(copied),"Actual context menu undo restores copy operation");
   for(var i=0;i<100&&(copied.IsWorking||!VM.CanEditHistory(null,true));i++)await Task.Delay(30);
   await GlobalRedoAsync();await Task.Delay(150);RenderSelection();var redoGroup=VM.Groups.FirstOrDefault(g=>g.Id==copied.Id);Check(redoGroup is not null,"Copy redo restores independent group");
   VM.Selected=redoGroup;RenderSelection();await Task.Delay(120);PhotoList.SelectedItem=redoGroup!.Photos[0];var recycled=redoGroup.Photos[0].Path;
   menu.ShowAt(PhotoList);await Task.Delay(100);((IInvokeProvider)new MenuFlyoutItemAutomationPeer(RecyclePhotosMenu).GetPattern(PatternInterface.Invoke)).Invoke();
   for(var i=0;i<100&&File.Exists(recycled);i++)await Task.Delay(30);
   Check(!File.Exists(recycled),"Actual context menu recycle handles isolated copy only");
   Check(VisualDescendants(PreviewHost).OfType<Image>().Count()==1,"Preview contains one image layer with no stale backdrop");
   var left=(MenuFlyout)GroupList.ContextFlyout;VM.Selected=VM.Groups.First();RenderSelection();GroupList.SelectedItems.Clear();foreach(var g in VM.Groups.ToArray())GroupList.SelectedItems.Add(g);
   left.ShowAt(GroupList);await Task.Delay(120);((IInvokeProvider)new MenuFlyoutItemAutomationPeer(MergeGroupsMenu).GetPattern(PatternInterface.Invoke)).Invoke();
   for(var i=0;i<100&&(VM.Groups.Count>1||mergingGroups);i++)await Task.Delay(30);
   Check(VM.Groups.Count==1,"Left group menu merge executes with animated follow-up rows");
   var beforeLeftCopy=VM.Selected;left.ShowAt(GroupList);await Task.Delay(120);((IInvokeProvider)new MenuFlyoutItemAutomationPeer(DuplicateGroupMenu).GetPattern(PatternInterface.Invoke)).Invoke();
   for(var i=0;i<100&&(VM.Groups.Count<2||VM.Selected==beforeLeftCopy||VM.Selected?.Editable!=true);i++)await Task.Delay(30);await Task.Delay(100);
   Check(VM.Groups.Count==2&&VM.Selected!=beforeLeftCopy,"Left group menu creates independent duplicate");
   VM.State.Settings.SkippedConfirmations.Add("removeGroups");GroupList.SelectedItems.Clear();GroupList.SelectedItems.Add(VM.Selected!);var removing=VM.Selected!;
   left.ShowAt(GroupList);await Task.Delay(120);((IInvokeProvider)new MenuFlyoutItemAutomationPeer(DeleteGroupsMenu).GetPattern(PatternInterface.Invoke)).Invoke();
   for(var i=0;i<100&&VM.Groups.Contains(removing);i++)await Task.Delay(30);
   Check(!VM.Groups.Contains(removing),"Left group menu remove executes after native close");
   UiMotion.Fade(PreviewSurface,0,1,120);await Task.Delay(30);await CaptureVisualAsync(PreviewSurface,Path.Combine(directory,"fade-intermediate.png"));await Task.Delay(150);
   foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark}){RequestedTheme=theme;await Task.Delay(120);await CaptureVisualAsync(this,Path.Combine(directory,"surface-"+theme+".png"));}
   Check(VM.State.BatchCalls==0,"Zero API calls");File.WriteAllText(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new{Status="Passed",Width=width,Checks=checks},StateStore.Json));
  }
  catch(Exception e){File.WriteAllText(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new{Status="Failed",Checks=checks,Error=e.ToString()},StateStore.Json));}
 }
}
#endif
