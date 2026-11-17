#if DEBUG
using System.Text.Json;
using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace CarArchive;
public sealed partial class MainPage
{
    private async Task RunV115VerificationAsync(string directory)
    {
        directory=Path.GetFullPath(directory);Directory.CreateDirectory(directory);var checks=new List<string>();
        void Check(bool value,string text) { if(!value) throw new Exception(text);checks.Add(text); }
        async Task Until(Func<bool> ready) { for(var i=0;i<160 && !ready();i++) await Task.Delay(50);if(!ready()) throw new Exception("Native workflow did not complete within eight seconds."); }
        try
        {
            Check(ViewModels.MainViewModel.DataDirectory.StartsWith(directory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) && VM.Groups.Count==0,"Isolated 1.1.5 native workspace");
            var root=Path.Combine(directory,"library");for(var i=0;i<12;i++) Directory.CreateDirectory(Path.Combine(root,"Mercedes-AMG",$"S 63 Coupe Edition {i:D2} (C217)"));
            Directory.CreateDirectory(Path.Combine(root,"Mercedes-Benz","S-Class (W223)"));Directory.CreateDirectory(Path.Combine(root,"Audi","A4 Avant (B9)"));Directory.CreateDirectory(Path.Combine(root,"BMW","3 Series (G20)"));
            var photoPath=await FixtureImageAsync(Path.Combine(directory,"portrait.jpg"),240,480);var originalHash=await ArchiveService.HashAsync(photoPath);
            await VM.ImportAsync([photoPath],true,default);await VM.BackgroundImport;var group=VM.Selected!;
            VM.State.Settings.ArchiveRoot=root;VM.State.Settings.Learning=LearningMode.Manual;group.Confirmed=new() { Brand="BMW",Series="3 Series",Variant="330i",Generation="7th",Code="G20" };group.TargetBrand="BMW";group.FolderName="Reviewed folder";group.ManualName=true;group.ManualTargetBrand=false;
            var backend=new V115Recognition();VM.Recognition=backend;RenderSelection();
            ExecuteCommand("recognize",this,new RoutedEventArgs());await Until(()=>backend.Calls==1 && !group.IsWorking);
            Check(group.Confirmed.Brand=="Audi" && group.TargetBrand=="Audi" && group.FolderName=="Reviewed folder","A new recognized brand updates automatic target even with a manual folder name");
            group.TargetBrand="BMW";group.ManualTargetBrand=true;RenderSelection();ExecuteCommand("recognize",this,new RoutedEventArgs());await Until(()=>backend.Calls==2 && !group.IsWorking);RenderSelection();
            Check(group.TargetBrand=="BMW" && RuleText.Text.Contains("手动品牌目录"),"An intentional manual cross-brand directory is preserved and visibly identified");
            var copy=await VM.DuplicateAsync(group);Check(copy.Photos[0].AssetId!=group.Photos[0].AssetId && copy.Photos[0].Path!=photoPath && await ArchiveService.HashAsync(copy.Photos[0].Path)==originalHash,"Copied groups have independently removable byte-identical photos");
            VM.Selected=group;RenderSelection();ExecuteCommand("next",this,new RoutedEventArgs());Check(VM.Selected==copy,"Next-group command routes through the shared command entry");ExecuteCommand("previous",this,new RoutedEventArgs());Check(VM.Selected==group,"Previous-group command routes through the shared command entry");
            if(narrowLayout) PaneBox.SelectedIndex=2;await RefreshCategoryCatalogAsync(true);FolderEdit.Text="s63coupe";VisualChild<TextBox>(FolderEdit)!.Focus(FocusState.Programmatic);OpenFolderSuggestions();await Until(()=>SuggestionPopup(FolderEdit) is not null);
            Check(SuggestionItems(FolderEdit).Count==12 && SuggestionItems(FolderEdit)[0].FolderName.Contains("S 63 Coupe"),"Existing-directory suggestions rank compact model words without phantom categories");
            Check(InputFocused(FolderEdit) && suggestionsList!.SelectedIndex==-1,"Suggestion opening keeps typing focus and does not silently select a directory");
            var opens=suggestionOpenCount;foreach(var query in new[]{"S63 Coupe","s63coupe","S 63 coupe"}) { FolderEdit.Text=query;UpdateFolderSuggestions();await Task.Delay(30); }
            Check(suggestionOpenCount==opens && SuggestionOpen(FolderEdit),"Typing updates the same native flyout without replaying its opening animation");
            MoveSuggestionSelection(1);Check(suggestionsList!.SelectedIndex==0,"Arrow navigation explicitly selects the closest directory");
            var chosen=(CategorySuggestion)suggestionsList.SelectedItem;AcceptSuggestion(FolderEdit,chosen);Check(group.FolderName==chosen.FolderName && group.TargetBrand==chosen.BrandDirectory && group.ManualTargetBrand && group.Confirmed.Brand=="Audi","Choosing a category changes only the explicit archive target, not recognition fields");
            foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark})
            {
                RequestedTheme=theme;await Task.Delay(80);FolderEdit.Text="s63coupe";VisualChild<TextBox>(FolderEdit)!.Focus(FocusState.Programmatic);OpenFolderSuggestions();await Until(()=>SuggestionPopup(FolderEdit) is not null);await Task.Delay(80);
                var popup=SuggestionPopup(FolderEdit)!;var bounds=SuggestionBounds(popup);Check(bounds.Left>=-4 && bounds.Top>=-4 && bounds.Right<=XamlRoot.Size.Width+4 && bounds.Bottom<=XamlRoot.Size.Height+4,"Suggestion bounds remain in the window in "+theme);
                var presenter=VisualChild<FlyoutPresenter>(popup.Child);Check(presenter is not null && presenter.CornerRadius.TopLeft==8 && suggestionsList!.MaxHeight<=200,"Native corners and five-row height are consistent in "+theme);
                await CaptureVisualAsync(popup.Child,Path.Combine(directory,"suggestions-"+theme+".png"));CloseAllSuggestions();await Task.Delay(80);
            }
            TargetBrandEdit.Text="mer";VisualChild<TextBox>(TargetBrandEdit)!.Focus(FocusState.Programmatic);OpenBrandSuggestions();await Until(()=>SuggestionPopup(TargetBrandEdit) is not null);
            Check(SuggestionItems(TargetBrandEdit).Any(x=>x.BrandDirectory=="Mercedes-AMG"),"Brand input uses the same native fuzzy selection surface");
            VM.Selected=copy;RenderSelection();await Task.Delay(120);Check(!SuggestionOpen(FolderEdit) && !SuggestionOpen(TargetBrandEdit),"Switching groups closes both suggestion owners without stale reopening");
            VM.Selected=group;RenderSelection();
            VM.State.Settings.SkippedConfirmations.Remove("removePhotos");var cancel=ConfirmAsync("removePhotos","Native cancellation test",Paragraph("Synthetic confirmation"));await Until(ModalOpen);var dialog=OpenNativeDialog();Descendants(dialog).OfType<CheckBox>().Single().IsChecked=true;dialog.Hide();Check(!await cancel && !VM.State.Settings.SkippedConfirmations.Contains("removePhotos"),"Cancel never persists the do-not-show choice");
            var accept=ConfirmAsync("removePhotos","Native confirmation test",Paragraph("Synthetic confirmation"));await Until(ModalOpen);dialog=OpenNativeDialog();Descendants(dialog).OfType<CheckBox>().Single().IsChecked=true;await Task.Delay(60);var button=Descendants(dialog).OfType<Button>().First(b=>b.Content?.ToString()=="确认");((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            Check(await accept && VM.State.Settings.SkippedConfirmations.Contains("removePhotos"),"Positive native confirmation persists its own operation preference");
            var skipped=ConfirmAsync("removePhotos","This must not open",Paragraph("Synthetic"));Check(skipped.IsCompletedSuccessfully && await skipped && !ModalOpen(),"Remembered confirmation executes without opening another modal");
            var beforeJobs=VM.Store.Jobs().Count;group.FolderName="bad/name";VM.State.Settings.SkippedConfirmations.Add("archive");RenderSelection();ExecuteCommand("archive",this,new RoutedEventArgs());await Task.Delay(100);
            Check(VM.Store.Jobs().Count==beforeJobs && !group.Archived && File.Exists(photoPath),"Skipping confirmation never bypasses archive path validation");
            group.FolderName="Reviewed folder";VM.State.Settings.Shortcuts["recognize"]=new(78,3);RefreshCommandHints();Check(AnalysisBar.PrimaryCommands.OfType<AppBarButton>().Where(b=>b.Label=="Gemini识别").All(b=>ToolTipService.GetToolTip(b)?.ToString()?.Contains("Ctrl+Alt+N")==true),"Visible command hints follow changed bindings");
            VM.State.Settings.Shortcuts=InteractionSettings.Defaults();RefreshCommandHints();VM.State.Settings.SkippedConfirmations.Clear();RenderSelection();
            App.MainWindow!.AppWindow.Resize(new(850,900));PaneBox.SelectedIndex=2;await Task.Delay(200);Check(EditorScroll.ViewportHeight>=80 && ArchiveActionButton.ActualHeight>0,"Narrow review keeps editable fields and fixed archive actions usable");
            FolderEdit.Text="s63coupe";VisualChild<TextBox>(FolderEdit)!.Focus(FocusState.Programmatic);OpenFolderSuggestions();await Until(()=>SuggestionPopup(FolderEdit)?.Child is FrameworkElement {ActualWidth:>0});var narrowBounds=SuggestionBounds(SuggestionPopup(FolderEdit)!);Check(narrowBounds.Left>=-4 && narrowBounds.Right<=XamlRoot.Size.Width+4,"Narrow suggestions use an in-window fallback");await CaptureVisualAsync(this,Path.Combine(directory,"review-narrow.png"));CloseAllSuggestions();
            Check(await ArchiveService.HashAsync(photoPath)==originalHash && await ArchiveService.HashAsync(copy.Photos[0].Path)==originalHash,"Synthetic source and independent copy bytes remain unchanged");
            await VM.FlushWritesAsync();File.WriteAllText(Path.Combine(directory,"v115-verification.json"),JsonSerializer.Serialize(new { Status="Passed",Checks=checks,RealApiCalls=0 },new JsonSerializerOptions(StateStore.Json) { WriteIndented=true }));App.MainWindow.Title="CarArchive 1.1.5 · 交互验证";
        }
        catch(Exception error) { File.WriteAllText(Path.Combine(directory,"v115-verification.json"),JsonSerializer.Serialize(new { Status="Failed",Error=error.ToString(),Checks=checks },StateStore.Json));VM.Status=error.Message; }
    }
    private ContentDialog OpenNativeDialog()=>VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Select(p=>p.Child is null?null:VisualChild<ContentDialog>(p.Child)).First(x=>x is not null)!;
    private Rect SuggestionBounds(Microsoft.UI.Xaml.Controls.Primitives.Popup popup)
    {
        var child=(FrameworkElement)popup.Child;var rect=child.TransformToVisual(this).TransformBounds(new Rect(0,0,child.ActualWidth,child.ActualHeight));var origin=TransformToVisual(null).TransformPoint(new Point());return new(rect.X+origin.X,rect.Y+origin.Y,rect.Width,rect.Height);
    }
    private sealed class V115Recognition:IRecognitionService
    {
        public int Calls;
        public Task<RecognitionResult> RecognizeAsync(IReadOnlyList<Photo> photos,AppSettings settings,bool again,CancellationToken ct) { Calls++;return Task.FromResult(new RecognitionResult { Identity=new() { Brand="Audi",Series="A4",Variant="A4 Avant",Generation="5th",Code="B9" } }); }
        public Task<VerificationReport> VerifyAsync(IReadOnlyList<Photo> photos,AppSettings settings,CarIdentity candidate,string questions,CancellationToken ct)=>Task.FromResult(new VerificationReport { Candidate=candidate.Clone(),Status=VerificationStatus.Insufficient });
    }
}
#endif
