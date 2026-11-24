using CarArchive.Core;
using Microsoft.UI.Xaml;

namespace CarArchive;

public sealed partial class MainPage
{
    private void ApplyAppearance()
    {
        var theme=VM.State.Settings.Appearance switch
        {
            AppearanceMode.Light=>ElementTheme.Light,
            AppearanceMode.Dark=>ElementTheme.Dark,
            _=>ElementTheme.Default
        };
        RequestedTheme=theme;
        if(App.MainWindow?.Content is FrameworkElement shell)shell.RequestedTheme=theme;
    }

    private void StartCrashHeartbeat()
    {
        if(App.MainWindow is not { } window)return;
        var heartbeat=DispatcherQueue.CreateTimer();
        heartbeat.Interval=TimeSpan.FromSeconds(1);
        heartbeat.Tick+=(_,_)=>CrashJournal.Current?.PulseUi();
        heartbeat.Start();
        window.Closed+=(_,_)=>heartbeat.Stop();
    }
}
