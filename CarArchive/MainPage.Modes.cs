using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace CarArchive;

public sealed partial class MainPage
{
    private bool updatingMode;
    private static readonly string[] ModeNames=["准确优先","速度优先","控制费用","精简字段"];
    private static readonly string[] ModeDetails=["最多八图 · 特征查证，最多三次请求","至多三张照片 · 一次联网识别","单张 · 简短证据，低预算一次请求","一张代表图 · 仅返回分类所需字段"];
    private void UpdateModePresentation()
    {
        var mode=Math.Clamp((int)VM.State.Settings.Mode,0,3);updatingMode=true;
        try { ModeLabel.Text=ModePopupTitle.Text=ModeNames[mode];ModeDescription.Text=ModeDetails[mode];ModeModelText.Text=VM.State.Settings.Model;ModeSlider.Value=3-mode; }
        finally { updatingMode=false; }
    }
    private void ModeFlyout_Opening(object sender,object e)=>UpdateModePresentation();
    private void ModeSlider_Changed(object sender,RangeBaseValueChangedEventArgs e)
    {
        if(!initialized || updatingMode) return;
        VM.State.Settings.Mode=(RecognitionMode)(3-(int)Math.Round(e.NewValue));UpdateModePresentation();VM.SaveHeader();
        CrashJournal.Trace("Recognition.ModeChanged");
    }
    private void ModeSettings_Click(object sender,RoutedEventArgs e) { ModeFlyout.Hide();Settings_Click(sender,e); }
}
