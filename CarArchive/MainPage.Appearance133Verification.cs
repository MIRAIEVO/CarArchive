#if DEBUG
using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace CarArchive;

public sealed partial class MainPage
{
    private async Task VerifyAppearance133Async(string directory)
    {
        try
        {
            var file=await FixtureImageAsync(Path.Combine(directory,"sample.jpg"),900,600);
            await VM.ImportAsync([file],true,default);
            var root=Path.Combine(directory,"library");
            Directory.CreateDirectory(Path.Combine(root,"BMW","M3"));
            VM.State.Settings.ArchiveRoot=root;VM.Selected!.TargetBrand="BMW";VM.Selected.FolderName="M3";RenderSelection();
            Settings_Click(this,new());
            for(var attempt=0;attempt<100&&!ModalOpen();attempt++)await Task.Delay(25);
            var dialog=OpenNativeDialog();
            var page=VisualDescendants(dialog).OfType<Button>().First(button=>button.Content is Grid row &&
                VisualDescendants(row).OfType<TextBlock>().Any(text=>text.Text=="外观"));
            ((IInvokeProvider)new ButtonAutomationPeer(page).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(150);
            var theme=VisualDescendants(dialog).OfType<ComboBox>().Single(box=>box.Header?.ToString()=="界面主题");
            foreach(var (mode,effective) in new[]{(AppearanceMode.Dark,ElementTheme.Dark),(AppearanceMode.Light,ElementTheme.Light)})
            {
                theme.SelectedIndex=(int)mode;await Task.Delay(80);
                if(ActualTheme!=effective || dialog.ActualTheme!=effective)throw new Exception("Appearance did not apply to the workspace and settings");
            }
            theme.SelectedIndex=(int)AppearanceMode.Dark;
            await VM.FlushWritesAsync();
            var saved=new StateStore(ViewModels.MainViewModel.DataDirectory).Load();
            if(saved.Settings.Appearance!=AppearanceMode.Dark)throw new Exception("Appearance not persisted");
            dialog.Hide();await Task.Delay(80);
            if(!OpenBrandButton.IsEnabled || TargetPathText.Text!="BMW／M3")throw new Exception("Archive footer path or action");
            await CaptureVisualAsync(this,Path.Combine(directory,"dark.png"));
            File.WriteAllText(Path.Combine(directory,"appearance.txt"),"Passed");
        }
        catch(Exception error){File.WriteAllText(Path.Combine(directory,"appearance.txt"),error.ToString());}
    }
}
#endif
