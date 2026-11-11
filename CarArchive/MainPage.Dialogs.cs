using System.Text.Json;
using CarArchive.Core;
using CarArchive.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace CarArchive;

public sealed partial class MainPage
{
    private static TextBlock Paragraph(string text) => new() { Text=text, TextWrapping=TextWrapping.Wrap, IsTextSelectionEnabled=true };
    private async void Settings_Click(object sender,RoutedEventArgs e)
    {
        if(VM.Busy) return;
        try
        {
            var key=new PasswordBox { Header="Gemini API Key（Windows 当前用户加密保存）",Password=VM.Credentials.Read() };
            var model=new TextBox { Header="模型 ID",Text=VM.State.Settings.Model };
            var search=new ComboBox { Header="检索方式（只使用当前 Gemini API Key）",HorizontalAlignment=HorizontalAlignment.Stretch };
            search.Items.Add(new ComboBoxItem { Content="自动：优先已验证的图片资料能力，否则普通搜索" });
            search.Items.Add(new ComboBoxItem { Content="普通 Google 搜索" });
            var nativeImageItem=new ComboBoxItem { Content="Gemini 图片资料检索（非 Lens）",IsEnabled=VM.State.Settings.ImageSearchVerifiedModel==model.Text };
            search.Items.Add(nativeImageItem);search.Items.Add(new ComboBoxItem { Content="Google Lens 以图搜图：当前 Gemini 接口不提供",IsEnabled=false });
            search.SelectedIndex=(int)VM.State.Settings.Search;
            var checkImage=new Button { Content="检查当前模型的图片资料检索能力",HorizontalAlignment=HorizontalAlignment.Stretch };
            model.TextChanged+=(_,_)=> { nativeImageItem.IsEnabled=VM.State.Settings.ImageSearchVerifiedModel==model.Text.Trim();if(!nativeImageItem.IsEnabled && search.SelectedIndex==2) search.SelectedIndex=0; };
            var budget=new NumberBox { Header="每批调用次数上限（包含分组、检查和核验）",Minimum=1,Maximum=10000,Value=VM.State.Settings.BatchCallLimit,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact };
            var gap=new NumberBox { Header="时间分组间隔（秒）",Minimum=1,Maximum=3600,Value=VM.State.Settings.GroupGapSeconds,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact };
            var learning=new ComboBox { Header="命名学习模式",HorizontalAlignment=HorizontalAlignment.Stretch,ItemsSource=new[] { "记住车型，通用规则另行确认", "自动学习并应用用词规则", "仅在点击学习时记住" },SelectedIndex=(int)VM.State.Settings.Learning };
            var check=new Button { Content="保存并检查多图 + Google Search 能力",HorizontalAlignment=HorizontalAlignment.Stretch };
            var reset=new Button { Content="开始新批次（重置调用计数）",HorizontalAlignment=HorizontalAlignment.Stretch };
            var checkStatus=Paragraph("检查会提交两张合成测试图并实际联网，可能计费用量。不会发送你的照片。\n只保存 Key 不会调用 API。识别时派生图片去除 EXIF，原图保持不变。");
            var links=new StackPanel { Spacing=6 };
            var panel=new StackPanel { Spacing=12,MinWidth=410,MaxWidth=560 };
            foreach(var element in new FrameworkElement[] { key,model,search,checkImage,budget,gap,learning,check,reset,checkStatus,links,Paragraph("本机数据："+ViewModels.MainViewModel.DataDirectory+"\n精简：1图，仅8字段，一次请求，不追加核验。准确：最多8图；速度：最多3图；费用：1图。\n图片资料检索不等于上传照片到 Lens。不会自动切换模型，也不需要额外 API。Gemini 内部搜索次数不能保证固定为一次。") }) panel.Children.Add(element);
            var dialog=new ContentDialog { XamlRoot=XamlRoot,Title="设置与联网检查",Content=new ScrollViewer { Content=panel,MaxHeight=620 },PrimaryButtonText="保存",CloseButtonText="关闭",DefaultButton=ContentDialogButton.Close };
            using var checkCts=new CancellationTokenSource();
            void SaveSettings()
            {
                if(string.IsNullOrWhiteSpace(model.Text)) throw new ArgumentException("请填写模型 ID。");
                if(double.IsNaN(budget.Value) || double.IsNaN(gap.Value)) throw new ArgumentException("请填写有效的调用上限和时间间隔。");
                VM.Credentials.Save(key.Password); VM.State.Settings.Model=model.Text.Trim();
                VM.State.Settings.BatchCallLimit=(int)budget.Value; VM.State.Settings.GroupGapSeconds=(int)gap.Value;
                VM.State.Settings.Learning=(LearningMode)Math.Max(0,learning.SelectedIndex); VM.Save();
                VM.State.Settings.Search=(SearchPreference)Math.Clamp(search.SelectedIndex,0,2);VM.SaveHeader();
            }
            check.Click+=async (_,_)=>
            {
                check.IsEnabled=false;checkImage.IsEnabled=false; reset.IsEnabled=false; dialog.IsPrimaryButtonEnabled=false; VM.Busy=true;
                try
                {
                    SaveSettings(); checkStatus.Text="正在验证账号访问权限、多图输入与实际搜索记录…";
                    var sample=await PhotoService.ConnectivityImageAsync();var settings=VM.CaptureSettings();var result=await VM.Tasks.WithRecognitionSlotAsync(ct=>VM.Gemini.CheckAsync(settings,sample,ct),checkCts.Token);
                    checkStatus.Text=result.Grounded ? $"检查通过：多图输入、Google Search 与来源引用均已返回。\n输入 {result.InputTokens} / 输出 {result.OutputTokens} tokens。" : "多图请求已返回，但缺少实际搜索或来源记录。请更换模型或重试。";
                    links.Children.Clear(); foreach(var source in result.Sources) links.Children.Add(new HyperlinkButton { Content=source.Title,NavigateUri=new Uri(source.Url),Padding=new Thickness(0) });
                    if(result.SearchSuggestionsHtml.Length>0)
                    {
                        var web=new WebView2 { Height=160 }; links.Children.Add(web); await web.EnsureCoreWebView2Async(); web.CoreWebView2.Settings.IsScriptEnabled=false; web.NavigateToString(result.SearchSuggestionsHtml);
                    }
                }
                catch(OperationCanceledException) { checkStatus.Text="检查已取消。"; }
                catch(Exception error) { checkStatus.Text=error.Message; }
                finally { check.IsEnabled=true;checkImage.IsEnabled=true; reset.IsEnabled=true; dialog.IsPrimaryButtonEnabled=true; VM.Busy=false; }
            };
            checkImage.Click+=async (_,_)=>
            {
                check.IsEnabled=false;checkImage.IsEnabled=false;dialog.IsPrimaryButtonEnabled=false;
                try {
                    SaveSettings();var probe=JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(VM.State.Settings))!;probe.Search=SearchPreference.ImageReference;probe.ImageSearchVerifiedModel="";
                    checkStatus.Text="正在用两张合成图检查当前模型能否返回图片资料检索及纯文本字段…不会更换模型。";
                    var sample=await PhotoService.ConnectivityImageAsync();var result=await VM.Tasks.WithRecognitionSlotAsync(ct=>VM.Gemini.CheckAsync(probe,sample,ct),checkCts.Token);
                    if(result.Grounded && result.ActualSearch.Contains("图片资料")) { VM.State.Settings.ImageSearchVerifiedModel=probe.Model;nativeImageItem.IsEnabled=true;VM.SaveHeader();checkStatus.Text="检查通过，可选择 Gemini 图片资料检索。此能力不是 Lens 以图搜图。"; }
                    else { checkStatus.Text="当前模型未返回可验证的图片检索结果。请使用自动或普通 Google 搜索。"; }
                } catch(Exception error) { checkStatus.Text="图片能力未通过检查："+error.Message+"\n没有自动重试或切换模型。"; }
                finally { check.IsEnabled=true;checkImage.IsEnabled=true;dialog.IsPrimaryButtonEnabled=true; }
            };
            reset.Click+=(_,_)=> { VM.State.BatchCalls=0; VM.State.BatchId=Guid.NewGuid().ToString("N"); VM.Save(); checkStatus.Text="已开始新批次，调用计数归零。"; };
            dialog.PrimaryButtonClick+=(_,args)=> { try { SaveSettings(); VM.Status="设置已保存。"; } catch(Exception error) { args.Cancel=true; checkStatus.Text=error.Message; } };
            dialog.Closing+=(_,_)=>checkCts.Cancel();
            await dialog.ShowAsync(); RenderSelection();
        }
        catch(Exception error) { VM.Status=error.Message; }
    }
    private async void History_Click(object sender,RoutedEventArgs e)
    {
        if(VM.Busy) return;
        try
        {
            var jobs=VM.Store.Jobs();
            var list=new ListView { ItemsSource=jobs.Select(j=>$"{j.CreatedAt:MM-dd HH:mm} · {(j.Direction=="Archive"?"归档":"撤销")} · {j.Status} · {j.Entries.Count(x=>x.Stage=="Done")}/{j.Entries.Count} 张").ToList(),MaxHeight=220,MinWidth=500 };
            var details=Paragraph("选择记录可查看每一张照片的源位置、目标位置、阶段和错误。中断任务不会在启动时自动执行。");
            list.SelectionChanged+=(_,_)=> { if(list.SelectedIndex>=0) { var j=jobs[list.SelectedIndex]; details.Text=string.Join("\n\n",j.Entries.Select(x=>$"{x.Stage}\n{x.Source}\n→ {x.Destination}"+(x.Error.Length>0?"\n错误："+x.Error:""))); } };
            var panel=new StackPanel { Spacing=12 }; panel.Children.Add(list); panel.Children.Add(new ScrollViewer { Content=details,MaxHeight=260 });
            var result=await DialogAsync("文件操作记录",panel,"恢复中断任务","撤销选中归档");
            if(list.SelectedIndex>=0 && result!=ContentDialogResult.None)
            {
                var job=jobs[list.SelectedIndex];
                if(result==ContentDialogResult.Primary && job.Status is not ("Pending" or "Interrupted")) { VM.Status="该记录没有需要恢复的中断任务。"; return; }
                var group=VM.Groups.FirstOrDefault(g=>g.Id==job.GroupId);
                if(group is not null) await VM.RunGroupAsync(group,result==ContentDialogResult.Secondary?"撤销":"恢复归档",true,ct=>VM.ResumeOrUndoAsync(job,result==ContentDialogResult.Secondary,ct));
            }
        }
        catch(Exception error) { VM.Status=error.Message; }
    }
    private async void Rules_Click(object sender,RoutedEventArgs e)
    {
        if(VM.Busy) return;
        try
        {
            var learning=new ComboBox { Header="学习模式（三选一）",ItemsSource=new[] { "记住车型，规则另行确认", "自动学习并应用规则", "仅手动点击时学习" },SelectedIndex=(int)VM.State.Settings.Learning,HorizontalAlignment=HorizontalAlignment.Stretch };
            learning.SelectionChanged+=(_,_)=> { VM.State.Settings.Learning=(LearningMode)Math.Max(0,learning.SelectedIndex); VM.Save(); };
            var rules=new ListView { MaxHeight=130,MinWidth=450 };
            var brand=new TextBox { Header="规则品牌（空白表示全局）" }; var from=new TextBox { Header="替换词" }; var to=new TextBox { Header="替换为" };
            var enabled=new ToggleSwitch { Header="规则状态",OnContent="启用",OffContent="停用",IsOn=true };
            var add=new Button { Content="添加为新规则" }; var edit=new Button { Content="更新选中规则" }; var delete=new Button { Content="删除选中规则" };
            void ReloadRules() => rules.ItemsSource=VM.State.Rules.Select(r=>r.Description).ToList();
            ReloadRules();
            rules.SelectionChanged+=(_,_)=> { if(rules.SelectedIndex<0) return; var r=VM.State.Rules[rules.SelectedIndex]; brand.Text=r.Brand; from.Text=r.From; to.Text=r.To; enabled.IsOn=r.Enabled; };
            var feedback=Paragraph("规则只替换命名用词；精确车型名称优先于规则。修改即时保存，可随时删除。");
            NamingRule ReadRule() { if(string.IsNullOrWhiteSpace(from.Text)) throw new ArgumentException("替换词不能为空。"); return new() { Brand=brand.Text.Trim(),From=from.Text.Trim(),To=to.Text.Trim(),Enabled=enabled.IsOn }; }
            add.Click+=(_,_)=> { try { VM.State.Rules.Add(ReadRule()); VM.Save(); ReloadRules(); feedback.Text="已添加规则。"; } catch(Exception error) { feedback.Text=error.Message; } };
            edit.Click+=(_,_)=> { try { var i=rules.SelectedIndex; if(i<0) return; var r=ReadRule(); r.Id=VM.State.Rules[i].Id; VM.State.Rules[i]=r; VM.Save(); ReloadRules(); feedback.Text="规则已更新。"; } catch(Exception error) { feedback.Text=error.Message; } };
            delete.Click+=(_,_)=> { if(rules.SelectedIndex>=0) { VM.State.Rules.RemoveAt(rules.SelectedIndex); VM.Save(); ReloadRules(); } };
            var actions=new StackPanel { Orientation=Orientation.Horizontal,Spacing=8 }; actions.Children.Add(add); actions.Children.Add(edit); actions.Children.Add(delete);
            var rulePanel=new StackPanel { Spacing=8 }; foreach(var item in new FrameworkElement[] { rules,brand,from,to,enabled,actions }) rulePanel.Children.Add(item);
            var names=new ListView { MaxHeight=200,MinWidth=450 }; var nameEdit=new TextBox { Header="已确认的完整车型名称" }; var nameKey=Paragraph("");
            var updateName=new Button { Content="更新选中名称" }; var removeName=new Button { Content="删除选中名称" };
            void ReloadNames() => names.ItemsSource=VM.State.Names.Select(n=>n.Brand+" / "+n.FolderName).ToList();
            ReloadNames(); names.SelectionChanged+=(_,_)=> { if(names.SelectedIndex>=0) { var n=VM.State.Names[names.SelectedIndex]; nameEdit.Text=n.FolderName; nameKey.Text=n.IdentityKey; } };
            updateName.Click+=(_,_)=> { try { if(names.SelectedIndex<0) return; var n=VM.State.Names[names.SelectedIndex]; VM.State.Names[names.SelectedIndex]=n with { FolderName=NamingService.SafeSegment(nameEdit.Text) }; VM.Save(); ReloadNames(); feedback.Text="车型名称已更新。"; } catch(Exception error) { feedback.Text=error.Message; } };
            removeName.Click+=(_,_)=> { if(names.SelectedIndex>=0) { VM.State.Names.RemoveAt(names.SelectedIndex); VM.Save(); ReloadNames(); } };
            var nameActions=new StackPanel { Orientation=Orientation.Horizontal,Spacing=8 }; nameActions.Children.Add(updateName); nameActions.Children.Add(removeName);
            var namePanel=new StackPanel { Spacing=8 }; foreach(var item in new FrameworkElement[] { names,nameEdit,nameKey,nameActions }) namePanel.Children.Add(item);
            var export=new Button { Content="导出全部命名规则与车型名称（JSON）",HorizontalAlignment=HorizontalAlignment.Stretch };
            export.Click+=async (_,_)=> { try { var picker=new FileSavePicker { SuggestedFileName="CarArchive-naming" }; picker.FileTypeChoices.Add("JSON",new List<string>{".json"}); WinRT.Interop.InitializeWithWindow.Initialize(picker,WindowHandle); var file=await picker.PickSaveFileAsync(); if(file is not null) { await FileIO.WriteTextAsync(file,JsonSerializer.Serialize(new { rules=VM.State.Rules,names=VM.State.Names },StateStore.Json)); feedback.Text="已导出命名记录，不包含照片或 API Key。"; } } catch(Exception error) { feedback.Text=error.Message; } };
            var panel=new StackPanel { Spacing=12,MaxWidth=570 }; panel.Children.Add(learning);
            panel.Children.Add(new Expander { Header="用词规则 · 查看、编辑、停用、删除",Content=rulePanel,IsExpanded=true,HorizontalAlignment=HorizontalAlignment.Stretch });
            panel.Children.Add(new Expander { Header="已确认的车型名称",Content=namePanel,HorizontalAlignment=HorizontalAlignment.Stretch });
            panel.Children.Add(export); panel.Children.Add(feedback);
            await DialogAsync("命名学习",new ScrollViewer { Content=panel,MaxHeight=620 }); RenderSelection();
        }
        catch(Exception error) { VM.Status=error.Message; }
    }
}
