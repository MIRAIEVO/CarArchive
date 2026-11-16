using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace CarArchive;

public sealed partial class MainPage
{
    private bool updatingMode;
    private static readonly string[] ModeNames=["准确优先","速度优先","控制费用","精简字段"];
    private static readonly string[] ModeDetails=["特征查证 · 最多三次请求 · 图片由单张／多张选择决定","一次联网识别 · 简短可见依据","简短证据 · 低预算一次请求","一次请求 · 仅返回分类所需字段"];
    private void UpdateModePresentation()
    {
        var mode=Math.Clamp((int)(VM.Selected?.Mode ?? RecognitionMode.FieldsOnly),0,3);updatingMode=true;
        try { ModeLabel.Text=ModePopupTitle.Text=ModeNames[mode];ModeDescription.Text=ModeDetails[mode];ModeModelText.Text=VM.State.Settings.Model;ModeSlider.Value=3-mode;UploadMultipleToggle.IsOn=VM.Selected?.UploadMultiple==true; }
        finally { updatingMode=false; }
    }
    private void ModeFlyout_Opening(object sender,object e)=>UpdateModePresentation();
    private void ModeSlider_Changed(object sender,RangeBaseValueChangedEventArgs e)
    {
        if(!initialized || updatingMode) return;
        if(VM.Selected is not { } group || !group.Editable) return;
        group.Mode=(RecognitionMode)(3-(int)Math.Round(e.NewValue));UpdateModePresentation();VM.SaveGroup(group);
        CrashJournal.Trace("Recognition.ModeChanged");
    }
    private void UploadMultiple_Changed(object sender,RoutedEventArgs e) { if(!initialized || updatingMode || VM.Selected is not { } group || !group.Editable) return;group.UploadMultiple=UploadMultipleToggle.IsOn;VM.SaveGroup(group); }
    private void ModeSettings_Click(object sender,RoutedEventArgs e) { ModeFlyout.Hide();Settings_Click(sender,e); }
}
