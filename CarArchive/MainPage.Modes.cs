using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace CarArchive;

public sealed partial class MainPage
{
    private bool updatingMode;
    private PhotoGroup[] ModeTargets()
    {
        var selected=GroupList.SelectedItems.OfType<PhotoGroup>().ToArray();
        return selected.Length>0?selected:VM.Selected is { } group?[group]:[];
    }
    private void UpdateModePresentation()
    {
        var settings=VM.State.Settings;
        var strength=VM.Selected is { } group
            ? group.Strength??RecognitionPlan.FromLegacy(group.Mode) : settings.DefaultStrength;
        var targets=ModeTargets();
        var preview=VM.Selected is { } active && targets.Contains(active)?active:targets.FirstOrDefault();
        if(preview is not null)strength=preview.Strength??RecognitionPlan.FromLegacy(preview.Mode);
        var plan=RecognitionPlan.Create(settings,strength,preview is null?null:RecognitionPlan.IsOnline(settings,preview));
        updatingMode=true;
        try
        {
            ModeLabel.Text=ModePopupTitle.Text=plan.Strength.ToString();
            ModeModelText.Text=$"{settings.Provider} · {RecognitionPlan.Model(settings)}";
            ModeSlider.Value=(int)plan.Strength;
            ModeSlider.IsEnabled=targets.Any(item=>item.Editable);
            ApplySelectedMode.Visibility=targets.Length>1 && ModeSlider.IsEnabled?Visibility.Visible:Visibility.Collapsed;
            ModeSearchToggle.IsEnabled=targets.Length==0 || ModeSlider.IsEnabled;
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
            var mixed=targets.Any(item=>(item.Strength??RecognitionPlan.FromLegacy(item.Mode))!=plan.Strength || RecognitionPlan.IsOnline(settings,item)!=plan.Online);
            ModeDescription.Text=$"仅返回车型字段 · 思考 {plan.Effort??"模型默认"}\n{rounds} · {search}"+
                (targets.Length>1?$"\n已选 {targets.Length} 组 · 可修改 {targets.Count(item=>item.Editable)} 组"+(mixed?" · 当前设置不一致":""):"");
        }
        finally { updatingMode=false; }
    }
    private void ModeFlyout_Opening(object sender,object e)=>UpdateModePresentation();
    private void ApplySelectedMode_Click(object sender,RoutedEventArgs e)
    {
        var provider=VM.State.Settings.Provider;
        foreach(var group in ModeTargets().Where(item=>item.Editable))
        {
            group.Strength=(RecognitionStrength)(int)Math.Round(ModeSlider.Value);
            group.SearchOverrides[provider]=ModeSearchToggle.IsOn;VM.MarkTouched(group);VM.SaveGroup(group);
        }
        UpdateModePresentation();
    }
    private void ModeSlider_Changed(object sender,RangeBaseValueChangedEventArgs e)
    {
        if(!initialized || updatingMode) return;
        foreach(var group in ModeTargets().Where(item=>item.Editable))
        {
            group.Strength=(RecognitionStrength)(int)Math.Round(e.NewValue);VM.MarkTouched(group);VM.SaveGroup(group);
        }
        UpdateModePresentation();
        CrashJournal.Trace("Recognition.ModeChanged");
    }
    private void ModeSearch_Changed(object sender,RoutedEventArgs e)
    {
        if(!initialized || updatingMode)return;
        var settings=VM.State.Settings;
        var targets=ModeTargets();
        if(targets.Length==0){RecognitionPlan.SetOnline(settings,ModeSearchToggle.IsOn);VM.SaveHeader();}
        else foreach(var group in targets.Where(item=>item.Editable))
        {
            group.SearchOverrides[settings.Provider]=ModeSearchToggle.IsOn;VM.MarkTouched(group);VM.SaveGroup(group);
        }
        UpdateModePresentation();
    }
    private void UploadMultiple_Changed(object sender,RoutedEventArgs e) { if(!initialized || updatingMode) return;VM.State.Settings.UploadMultiple=UploadMultipleToggle.IsOn;VM.SaveHeader(); }
    private void ModeSettings_Click(object sender,RoutedEventArgs e) { ModeFlyout.Hide();Settings_Click(sender,e); }
}
