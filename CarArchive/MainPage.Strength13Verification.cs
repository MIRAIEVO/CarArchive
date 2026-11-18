#if DEBUG
using CarArchive.Core;
using Microsoft.UI.Xaml;

namespace CarArchive;

public sealed partial class MainPage
{
    private async Task VerifyStrength13Async(string directory)
    {
        var outcome="Passed";
        try
        {
            await Task.Delay(250);
            void Check(bool condition,string name)
            {
                if(!condition)throw new InvalidOperationException(name);
            }
            var fixture=await FixtureImageAsync(Path.Combine(directory,"fixture.jpg"),800,600);
            await VM.ImportAsync([fixture],true,default);
            await VM.BackgroundImport;
            var group=VM.Selected!;
            Check(group.Strength==RecognitionStrength.Low,"New group defaults to Low");
            group.Strength=null;
            group.Mode=RecognitionMode.Accurate;
            RenderSelection();
            Check(ModeLabel.Text=="Max","Legacy group mapping");
            ModeFlyout.ShowAt(ModeButton);
            await Task.Delay(100);
            ModeSlider.Value=6;
            Check(group.Strength==RecognitionStrength.Ultra && ModePopupTitle.Text=="Ultra","Seven-level slider");
            VM.State.Settings.Provider=RecognitionProvider.OpenAI;
            VM.State.Settings.ProviderProfiles[RecognitionProvider.OpenAI]=new(){Model="gpt-6.2-sol"};
            UpdateModePresentation();
            ModeSearchToggle.IsOn=true;
            Check(ModeModelText.Text=="OpenAI · gpt-6.2-sol" && ModeDescription.Text.Contains("2 / 2 / 2"),"Provider-specific effort and search");
            await VM.FlushWritesAsync();
            var state=new StateStore(ViewModels.MainViewModel.DataDirectory).Load();
            Check(state.Settings.Profile(RecognitionProvider.OpenAI).Search && state.Settings.Search==SearchPreference.Gemini
                && state.Groups.Single().Strength==RecognitionStrength.Ultra,"Search and strength persist separately");
            Check(state.BatchCalls==0,"No API");
            ModeFlyout.Hide();
        }
        catch(Exception error){outcome=error.ToString();}
        File.WriteAllText(Path.Combine(directory,"strength-result.txt"),outcome);
    }
}
#endif
