using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace CarArchive;

public sealed partial class MainPage
{
    private bool updatingMode;
    private void UpdateModePresentation()
    {
        var settings=VM.State.Settings;
        var strength=VM.Selected is { } group
            ? group.Strength??RecognitionPlan.FromLegacy(group.Mode) : settings.DefaultStrength;
        var plan=RecognitionPlan.Create(settings,strength);
        updatingMode=true;
        try
        {
            ModeLabel.Text=ModePopupTitle.Text=plan.Strength.ToString();
            ModeModelText.Text=$"{settings.Provider} · {RecognitionPlan.Model(settings)}";
            ModeSlider.Value=(int)plan.Strength;
            ModeSlider.IsEnabled=VM.Selected is null || VM.Selected.Editable;
            ModeSearchToggle.IsOn=plan.Online;
            UploadMultipleToggle.IsOn=settings.UploadMultiple;
            var rounds=plan.Strength switch {RecognitionStrength.Xhigh=>"最多 2 次，字段不足时复判",RecognitionStrength.Max=>"2 次，独立复判",RecognitionStrength.Ultra=>"最多 3 次，有分歧时裁决",_=>"1 次请求"};
            if(settings.Provider==RecognitionProvider.OpenAI && plan.Strength>=RecognitionStrength.Medium)
                rounds+="；首轮无文本时可降档恢复一次";
            var search=!plan.Online?"搜索关闭":settings.Provider switch
            {
                RecognitionProvider.OpenAI=>"各轮搜索工具调用上限："+string.Join(" / ",plan.SearchTargets),
                RecognitionProvider.Claude=>"各轮搜索上限："+string.Join(" / ",plan.SearchTargets),
                _=>"各轮建议查询次数："+string.Join(" / ",plan.SearchTargets)+"（厂商无硬上限）"
            };
            ModeDescription.Text=$"仅返回车型字段 · 思考 {plan.Effort??"模型默认"}\n{rounds} · {search}";
        }
        finally { updatingMode=false; }
    }
    private void ModeFlyout_Opening(object sender,object e)=>UpdateModePresentation();
    private void ModeSlider_Changed(object sender,RangeBaseValueChangedEventArgs e)
    {
        if(!initialized || updatingMode) return;
        if(VM.Selected is not { } group || !group.Editable) return;
        group.Strength=(RecognitionStrength)(int)Math.Round(e.NewValue);UpdateModePresentation();VM.SaveGroup(group);
        CrashJournal.Trace("Recognition.ModeChanged");
    }
    private void ModeSearch_Changed(object sender,RoutedEventArgs e)
    {
        if(!initialized || updatingMode)return;
        var settings=VM.State.Settings;
        RecognitionPlan.SetOnline(settings,ModeSearchToggle.IsOn);
        VM.SaveHeader();UpdateModePresentation();
    }
    private void UploadMultiple_Changed(object sender,RoutedEventArgs e) { if(!initialized || updatingMode) return;VM.State.Settings.UploadMultiple=UploadMultipleToggle.IsOn;VM.SaveHeader(); }
    private void ModeSettings_Click(object sender,RoutedEventArgs e) { ModeFlyout.Hide();Settings_Click(sender,e); }
}
