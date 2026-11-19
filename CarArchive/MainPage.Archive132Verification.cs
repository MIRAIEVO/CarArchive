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
            var task=ArchiveCoreAsync();
            for(var attempt=0;attempt<150&&!ModalOpen();attempt++)await Task.Delay(25);
            var dialog=OpenNativeDialog();
            if(dialog.Title?.ToString()!="发现相同内容")throw new Exception("Duplicate dialog missing");
            var close=VisualDescendants(dialog).OfType<Button>().First(button=>button.Content?.ToString()=="取消本组");
            ((IInvokeProvider)new ButtonAutomationPeer(close).GetPattern(PatternInterface.Invoke)).Invoke();
            await task;
            if(!File.Exists(source)||!File.Exists(existing)||group.Archived)throw new Exception("Cancel changed files");
            await VM.RunGroupAsync(group,"归档",true,ct=>VM.ArchiveAsync(group,ct,decide:(_,_)=>Task.FromResult(ArchiveDuplicateChoice.Move)));
            if(File.Exists(source)||!File.Exists(existing)||!group.Archived||!OpenBrandButton.IsEnabled)
                throw new Exception("Move or archived directory action failed");
            File.WriteAllText(Path.Combine(directory,"result.txt"),"Passed");
        }
        catch(Exception error){File.WriteAllText(Path.Combine(directory,"result.txt"),error.ToString());}
    }
}
#endif
