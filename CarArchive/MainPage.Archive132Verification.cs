#if DEBUG
using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace CarArchive;

public sealed partial class MainPage
{
    private async Task VerifyArchive132Async(string directory)
    {
        try
        {
            var source=await FixtureImageAsync(Path.Combine(directory,"source.jpg"),800,600);
            await VM.ImportAsync([source],true,default);
            await VM.BackgroundImport;
            var group=VM.Selected!;
            var root=Path.Combine(directory,"library");
            var target=Directory.CreateDirectory(Path.Combine(root,"BMW","M3")).FullName;
            var existing=Path.Combine(target,"renamed.jpg");
            File.Copy(source,existing);
            VM.State.Settings.ArchiveRoot=root;group.TargetBrand="BMW";group.FolderName="M3";RenderSelection();
            if(!OpenBrandButton.IsEnabled || YearsEdit.Header?.ToString()!="生产年份")throw new Exception("Brand action or years label");
            var otherSource=await FixtureImageAsync(Path.Combine(directory,"other.jpg"),640,480);
            await VM.ImportAsync([otherSource],true,default);await VM.BackgroundImport;
            var other=VM.Selected!;
            VM.Selected=group;RenderSelection();
            var task=ArchiveCoreAsync();
            for(var attempt=0;attempt<150&&!ArchiveDuplicateBar.IsOpen;attempt++)await Task.Delay(25);
            if(!ArchiveDuplicateBar.IsOpen || ModalOpen())throw new Exception("Expected a nonmodal duplicate decision");
            GroupList.SelectedItem=other;
            if(VM.Selected!=other || !GroupList.IsEnabled)throw new Exception("Group list is blocked by archiving");
            var close=VisualDescendants(ArchiveDuplicateBar).OfType<Button>().First(button=>button.Content?.ToString()=="取消本组");
            ((IInvokeProvider)new ButtonAutomationPeer(close).GetPattern(PatternInterface.Invoke)).Invoke();
            await task;
            if(!File.Exists(source)||!File.Exists(existing)||group.Archived)throw new Exception("Cancel changed files");
            VM.Selected=group;RenderSelection();
            var moving=ArchiveCoreAsync();
            for(var attempt=0;attempt<150&&!ArchiveDuplicateBar.IsOpen;attempt++)await Task.Delay(25);
            if(!ArchiveDuplicateBar.IsOpen)throw new Exception("No duplicate decision on second archive");
            GroupList.SelectedItem=other;
            var move=VisualDescendants(ArchiveDuplicateBar).OfType<Button>().First(button=>button.Content?.ToString()=="仍移动");
            ((IInvokeProvider)new ButtonAutomationPeer(move).GetPattern(PatternInterface.Invoke)).Invoke();
            await moving;
            if(File.Exists(source)||!File.Exists(existing)||!group.Archived||VM.Selected!=other)
                throw new Exception($"Archive or selection failed: source={File.Exists(source)}, comparison={File.Exists(existing)}, archived={group.Archived}, selected={VM.Selected?.Title}");
            VM.Selected=group;RenderSelection();
            if(!OpenBrandButton.IsEnabled)throw new Exception("Archived directory action disabled");
            File.WriteAllText(Path.Combine(directory,"result.txt"),"Passed");
        }
        catch(Exception error){File.WriteAllText(Path.Combine(directory,"result.txt"),error.ToString());}
    }
}
#endif
