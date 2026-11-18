#if DEBUG
using System.Text.Json;
using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
namespace CarArchive;
public sealed partial class MainPage
{
 private async Task RunV123VerificationAsync(string directory)
 {
  Directory.CreateDirectory(directory);var checks=new List<string>();
  void Check(bool value,string text){if(!value)throw new Exception(text);checks.Add(text);}
  async Task Until(Func<bool> condition){for(var i=0;i<100&&!condition();i++)await Task.Delay(40);Check(condition(),"Wait completed");}
  void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
  Button SettingPage(ContentDialog dialog,string title)=>Descendants(dialog).OfType<Button>().First(button=>button.Content is Grid grid && Descendants(grid).OfType<TextBlock>().Any(text=>text.Text==title));
  try
  {
   Check(VM.Groups.Count==0,"Isolated empty workspace");
   var first=await FixtureImageAsync(Path.Combine(directory,"portrait.jpg"),800,1600);
   var second=await FixtureImageAsync(Path.Combine(directory,"landscape.jpg"),1600,800);
   await VM.ImportAsync([first],true,default);await VM.BackgroundImport;var empty=VM.Selected!;
   await VM.ImportAsync([second],true,default);await VM.BackgroundImport;var named=VM.Selected!;
   named.Confirmed=new(){Brand="Audi",Series="A4",Variant="A4 Avant",Generation="5th",Code="B9"};VM.GenerateName(named);VM.SaveGroup(named);RenderSelection();await Task.Delay(100);
   GroupList.SelectedItems.Clear();GroupList.SelectedItems.Add(empty);GroupList.SelectedItems.Add(named);await Task.Delay(100);
   var mergePosition=MergeSelectionButton.TransformToVisual(QueuePane).TransformPoint(new());
   Check(MergeSelectionButton.Visibility==Visibility.Visible&&MergeSelectionButton.Content?.ToString()=="合并"&&mergePosition.X>=0&&mergePosition.X+MergeSelectionButton.ActualWidth<=QueuePane.ActualWidth,"Merge action is anchored inside selected row and queue bounds");
   var spacers=Enumerable.Range(0,24).Select(_=>new PhotoGroup()).ToArray();foreach(var spacer in spacers)VM.Groups.Add(spacer);
   GroupList.SelectedItems.Clear();GroupList.SelectedItems.Add(empty);GroupList.SelectedItems.Add(named);await Task.Delay(100);
   mergeScroll!.ChangeView(null,mergeScroll.ScrollableHeight,null,true);await Task.Delay(140);UpdateMergeSelection();Check(MergeSelectionButton.Visibility==Visibility.Visible&&MergeSelectionButton.Margin.Top<1,"Merge action pins to upper edge when selected row scrolls above viewport");
   GroupList.SelectedItems.Clear();GroupList.SelectedItems.Add(empty);GroupList.SelectedItems.Add(spacers[^1]);await Task.Delay(140);mergeScroll.ChangeView(null,0,null,true);await Task.Delay(140);UpdateMergeSelection();
   Check(MergeSelectionButton.Visibility==Visibility.Visible&&MergeSelectionButton.Margin.Top>=QueuePane.ActualHeight-31,"Merge action pins to lower edge when selected row scrolls below viewport");
   VM.RemoveGroups(spacers);VM.Selected=named;RenderSelection();GroupList.SelectedItems.Clear();GroupList.SelectedItems.Add(empty);GroupList.SelectedItems.Add(named);await Task.Delay(80);
   Merge_Click(this,new());await Task.Delay(80);Check(VM.Groups.Count==1&&VM.Selected==named&&named.Confirmed.Brand=="Audi"&&named.Photos.Count==2,"Merge keeps populated group identity and combines both photos");
   named.FolderName="Old manual name";named.TargetBrand="Wrong brand";named.ManualName=named.ManualTargetBrand=true;
   VM.ApplyRecognitionResult(named,new RecognitionResult {Identity=new(){Brand="Audi",Series="A4",Variant="A4 Avant",Generation="5th",Code="B9"}});RenderSelection();
   Check(named.TargetBrand=="Audi"&&named.FolderName!="Old manual name"&&!named.ManualName&&!named.ManualTargetBrand&&named.PreviousFolderName.Length==0,"Successful recognition always regenerates name and brand without stale manual values");
   var fixedWidth=RecognizeActionButton.ActualWidth;var fixedX=RecognizeActionButton.TransformToVisual(this).TransformPoint(new()).X;
   var task=VM.RunGroupAsync(named,"联网识别",false,async ct=>await Task.Delay(Timeout.Infinite,ct));await Task.Delay(80);
   Check(RecognizeActionButton.Label=="取消识别"&&RecognizeActionButton.IsEnabled,"Recognition action becomes cancellation while task runs");
   await Task.Delay(60);Check(Math.Abs(RecognizeActionButton.ActualWidth-fixedWidth)<1&&Math.Abs(RecognizeActionButton.TransformToVisual(this).TransformPoint(new()).X-fixedX)<1,"Recognition and cancellation retain width and position");
   Check(RecognizeActionButton.Resources.ContainsKey("AppBarButtonBackgroundPointerOver"),"Cancellation keeps a dedicated red hover palette");
   var copy=await VM.DuplicateAsync(named);RenderSelection();Check(copy.Confirmed.Brand.Length==0&&copy.Suggestion is null&&copy.FolderName.Length==0&&copy.TaskState==GroupTaskState.Idle&&named.IsWorking,"Copy during recognition creates an independent unrecognized group");
   VM.Selected=named;RenderSelection();ExecuteCommand("recognize",this,new());await task;await Task.Delay(60);Check(!named.IsWorking&&RecognizeActionButton.Label=="AI识别","Same command cancels and restores recognition action");
   VM.State.Settings.ArchiveRoot=Path.Combine(directory,"archive");VM.SaveHeader();var hashes=new List<string>();foreach(var photo in named.Photos)hashes.Add(await ArchiveService.HashAsync(photo.Path));
   await ArchiveCoreAsync();await Task.Delay(80);Check(named.Archived&&!ModalOpen(),"Archive executes directly without confirmation modal");
   copy.Confirmed=new(){Brand="Audi",Series="A4",Variant="A4 Avant",Code="B9"};VM.GenerateName(copy);copy.FolderName="Other A4";copy.ManualName=true;VM.SaveGroup(copy);VM.Selected=copy;RenderSelection();await ArchiveCoreAsync();
   VM.Selected=named;RenderSelection();Check(ArchiveActionButton.Content?.ToString()=="撤回移动"&&ArchiveActionButton.IsEnabled,"Archived group exposes enabled undo move action");
   await CaptureVisualAsync(this,Path.Combine(directory,"archived-light.png"));await ArchiveCoreAsync();await Task.Delay(60);
   Check(!named.Archived&&copy.Archived&&VM.Store.Jobs().Any(job=>job.GroupId==copy.Id&&job.Status=="Completed"),"Undo move targets selected group rather than latest archived group");
   var restored=true;for(var index=0;index<named.Photos.Count;index++)restored &= await ArchiveService.HashAsync(named.Photos[index].Path)==hashes[index];Check(restored,"Undo restores original bytes");
   PasteIdentity_Click(this,new());await Until(()=>pasteIdentityOpen&&pasteIdentityFlyout is Flyout {Content:Grid {ActualWidth:>0}});
   var converter=(SafeImageSourceConverter)Application.Current.Resources["SafeImageSourceConverter"];var thumb=named.Photos[0].ThumbnailUri!;
   Check(ReferenceEquals(converter.Convert(thumb,typeof(object),null!,""),converter.Convert(thumb,typeof(object),null!,"")),"Thumbnail binding reuses bitmap objects across full preview loads");
   var flyout=(Flyout)pasteIdentityFlyout!;var panel=(Grid)flyout.Content;var text=Descendants(panel).OfType<TextBox>().Single();var size=(panel.ActualWidth,panel.ActualHeight);
   var position=panel.TransformToVisual(this).TransformPoint(new());Check(position.X>0&&position.Y>0&&position.Y<XamlRoot.Size.Height/2,"JSON flyout opens near upper right controls");
   text.Text="{\"Brand\":\"Lexus\",\"Series\":\"LS\",\"Variant\":\"LS 430\",\"Generation\":\"3rd\"}";await Task.Delay(80);
   var popupPresenter=Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Select(p=>p.Child is null?null:VisualChild<FlyoutPresenter>(p.Child)).FirstOrDefault(p=>p is not null);
   Check(popupPresenter is not null && popupPresenter.ActualWidth>=panel.ActualWidth+30 && ScrollViewer.GetHorizontalScrollBarVisibility(popupPresenter)==ScrollBarVisibility.Disabled,"JSON presenter fits fixed content width and disables horizontal scrolling");
   Check(size==(panel.ActualWidth,panel.ActualHeight)&&pasteIdentityOpen,"JSON flyout size remains fixed after parsing");await CaptureVisualAsync(panel,Path.Combine(directory,"json-flyout.png"));Invoke(Descendants(panel).OfType<Button>().Single(button=>button.Content?.ToString()=="确定"));await Until(()=>!pasteIdentityOpen);
   Check(named.Confirmed.Brand=="Lexus"&&named.FolderName.Contains("LS 430"),"Single confirmation applies pasted identity and local naming");
   PasteIdentity_Click(this,new());await Until(()=>pasteIdentityOpen&&pasteIdentityFlyout is Flyout {Content:Grid {ActualWidth:>0}});panel=(Grid)((Flyout)pasteIdentityFlyout!).Content;Descendants(panel).OfType<ToggleSwitch>().Single().IsOn=true;text=Descendants(panel).OfType<TextBox>().Single();text.Text="{";await Task.Delay(60);Check(pasteIdentityOpen,"Invalid JSON never auto-confirms");
   text.Text="{\"Brand\":\"BMW\",\"Series\":\"3 Series\",\"Variant\":\"320i\",\"Code\":\"G20\"}";await Until(()=>!pasteIdentityOpen);Check(named.Confirmed.Brand=="BMW"&&VM.State.Settings.AutoApplyPastedJson,"Auto-confirm applies valid JSON and remembers preference");
   Settings_Click(this,new());await Until(()=>OpenNativeDialogOrNull() is not null);var settings=OpenNativeDialog();Check(settings.PrimaryButtonText.Length==0&&settings.CloseButtonText=="确定","Settings has only one confirm action");
   await CaptureVisualAsync(settings,Path.Combine(directory,"settings-home.png"));Invoke(SettingPage(settings,"确认弹窗"));await Task.Delay(60);var skip=Descendants(settings).OfType<CheckBox>().First(box=>box.Content?.ToString()?.Contains("不再显示")==true);skip.IsChecked=true;await VM.FlushWritesAsync();Check(VM.State.Settings.SkippedConfirmations.Count>0,"Confirmation preference saves immediately");
   Check(!InteractionSettings.Confirmations.ContainsKey("archive"),"Archive confirmation preference removed");
   Invoke(Descendants(settings).OfType<Button>().First(button=>button.Content?.ToString()=="← 返回"));await Task.Delay(80);Invoke(SettingPage(settings,"识别与费用"));await Task.Delay(80);var combo=Descendants(settings).OfType<ComboBox>().First(box=>box.IsEditable);VisualChild<TextBox>(combo)!.Text="gemini-3.8-flash-test";await Task.Delay(60);await VM.FlushWritesAsync();Check(new StateStore(ViewModels.MainViewModel.DataDirectory).Load().Settings.Model=="gemini-3.8-flash-test","Model text persists before closing settings");
   Invoke(Descendants(settings).OfType<Button>().First(button=>button.Content?.ToString()=="确定"));await Task.Delay(100);
   await ShowPhotoAsync(named.Photos[0]);
   foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark}){RequestedTheme=theme;await Task.Delay(100);CropToggle.IsChecked=true;await CaptureVisualAsync(this,Path.Combine(directory,"workspace-"+theme+".png"));}
   Check(VM.State.BatchCalls==0,"No real API calls");await VM.FlushWritesAsync();File.WriteAllText(Path.Combine(directory,"v123-verification.json"),JsonSerializer.Serialize(new{Status="Passed",Checks=checks},StateStore.Json));
  }
  catch(Exception error){File.WriteAllText(Path.Combine(directory,"v123-verification.json"),JsonSerializer.Serialize(new{Status="Failed",Checks=checks,Error=error.ToString()},StateStore.Json));}
 }
 private ContentDialog? OpenNativeDialogOrNull()=>Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Select(p=>p.Child is null?null:VisualChild<ContentDialog>(p.Child)).FirstOrDefault(dialog=>dialog is not null);
}
#endif
