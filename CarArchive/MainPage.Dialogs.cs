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
            var settings=VM.State.Settings;
            var providers=Enum.GetValues<RecognitionProvider>();
            var providerBox=new ComboBox { Header="识别厂商",ItemsSource=providers.Select(p=>p.ToString()).ToArray(),HorizontalAlignment=HorizontalAlignment.Stretch };
            var key=new PasswordBox { Header="API Key（由 Windows 当前用户加密）" };
            var model=new ComboBox { Header="模型 ID（可选择或自行输入）",IsEditable=true,HorizontalAlignment=HorizontalAlignment.Stretch };
            var prompt=new TextBox {Header="补充提示词（可留空）",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MaxLength=6000,MinHeight=100,MaxHeight=180};
            var providerInfo=Paragraph("");
            var strengths=Enum.GetValues<RecognitionStrength>();
            var defaultStrength=new ComboBox {Header="新分组默认强度",ItemsSource=strengths.Select(value=>value.ToString()).ToArray(),
                SelectedIndex=Array.IndexOf(strengths,settings.DefaultStrength),HorizontalAlignment=HorizontalAlignment.Stretch};
            var defaultSearch=new ToggleSwitch {Header="联网搜索（当前厂商默认）",OffContent="关闭",OnContent="开启"};
            var budget=new NumberBox { Header="每批生成请求上限",Minimum=1,Maximum=10000,Value=VM.State.Settings.BatchCallLimit,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact };
            var check=new Button { Content="读取账号可用模型（不生成内容）",HorizontalAlignment=HorizontalAlignment.Stretch };
            var reset=new Button { Content="开始新批次（重置调用计数）",HorizontalAlignment=HorizontalAlignment.Stretch };
            var checkStatus=Paragraph("读取模型清单不发送照片、不生成内容。请选支持图片输入的模型；清单不代表账号具备生成或联网权限。");
            var links=new StackPanel { Spacing=6 };
            var tokenPolicy=new ToggleSwitch { Header="识别提示策略",OffContent="兼容提示",OnContent="优化提示",IsOn=VM.State.Settings.OptimizeRecognitionTokens };
            var advanced=new StackPanel { Spacing=8 };foreach(var element in new FrameworkElement[]{check,checkStatus,tokenPolicy,prompt,Paragraph("可用中文或英文补充识别重点；程序仍要求返回车型 JSON，并在本机生成归档名称。")}) advanced.Children.Add(element);
            var recognition=new StackPanel { Spacing=10 };foreach(var element in new FrameworkElement[]{providerBox,key,model,providerInfo,budget,Paragraph($"本批已调用 {VM.State.BatchCalls} 次。复制框选图片不调用 API。"),reset,new Expander { Header="模型清单与提示设置",Content=advanced,HorizontalAlignment=HorizontalAlignment.Stretch }}) recognition.Children.Add(element);
            var openLearning=new Button { Content="管理学习模式与规则" };var openGuide=new Button { Content="查看／更新车型指南" };
            var naming=new StackPanel { Spacing=8 };naming.Children.Add(Paragraph("全部在本机学习，不发送历史名称、规则或目录清单至 API。"));naming.Children.Add(openLearning);naming.Children.Add(openGuide);
            var openData=new Button { Content="打开本版本数据目录" };var importData=new Button { Content="导入旧版本数据…" };var clearCache=new Button { Content="清理可重建缓存" };var logs=new Button { Content="打开崩溃日志" };
            var openTrash=new Button { Content="打开回收站" };openTrash.Click+=OpenRecycleBin_Click;
            var maintenanceStatus=Paragraph("");var exportNaming=new Button{Content="导出记住名称日志 CSV（最近 1000 条）"};naming.Children.Add(exportNaming);exportNaming.Click+=async(_,_)=>{try{await ExportNamingLogAsync();}catch(Exception error){maintenanceStatus.Text=error.Message;}};
            var maintenance=new StackPanel { Spacing=8 };foreach(var element in new FrameworkElement[]{Paragraph(ViewModels.MainViewModel.DataDirectory),openData,importData,clearCache,logs,openTrash,maintenanceStatus}) maintenance.Children.Add(element);
            var clearNames=new Button {Content="删除现有记住命名数据"};naming.Children.Add(clearNames);
            var clearCrashes=new Button {Content="删除崩溃日志"};maintenance.Children.Add(clearCrashes);
            var appearance=new ComboBox {Header="界面主题",ItemsSource=new[]{"跟随系统","浅色","深色"},
                SelectedIndex=(int)settings.Appearance,HorizontalAlignment=HorizontalAlignment.Stretch};
            var settingsPages=BuildSettingsNavigation(recognition,naming,maintenance,BuildInteractionSettingsPanel(),BuildConfirmationSettingsPanel(),appearance);
            var dialog=new ContentDialog { XamlRoot=XamlRoot,Title="设置",RequestedTheme=RequestedTheme,Content=settingsPages,CloseButtonText="确定",DefaultButton=ContentDialogButton.Close };
            appearance.SelectionChanged+=(_,_)=>{
                if(appearance.SelectedIndex<0)return;
                settings.Appearance=(AppearanceMode)appearance.SelectedIndex;
                ApplyAppearance();dialog.RequestedTheme=RequestedTheme;VM.SaveHeader();
            };
            Action? afterClose=null;
            clearNames.Click+=(_,_)=> {afterClose=()=>ClearRememberedNames_Click(sender,e);dialog.Hide();};
            clearCrashes.Click+=(_,_)=> {afterClose=()=>ClearCrashLogs_Click(sender,e);dialog.Hide();};
            openLearning.Click+=(_,_)=> { afterClose=()=>Rules_Click(sender,e);dialog.Hide(); };
            openGuide.Click+=(_,_)=> { afterClose=()=>NamingGuide_Click(sender,e);dialog.Hide(); };
            openData.Click+=async(_,_)=> { try { await Windows.System.Launcher.LaunchFolderPathAsync(ViewModels.MainViewModel.DataDirectory); } catch(Exception error) { maintenanceStatus.Text=error.Message; } };
            logs.Click+=(_,_)=> { dialog.Hide();afterClose=()=>CrashLogs_Click(sender,e); };
            importData.Click+=async(_,_)=> { try { var source=await PickFolderAsync();if(source is null) return;maintenanceStatus.Text=File.Exists(Path.Combine(source,"cararchive.db"))?"已选择："+source+"\n当前版本已打开数据库，不会覆盖或合并。请解压一份新的便携包，启动时选择此目录导入。迁移前关闭本版本。":"该目录没有 cararchive.db，请选择旧版本 Data 或原本机数据目录。"; } catch(Exception error) { maintenanceStatus.Text=error.Message; } };
            clearCache.Click+=async(_,_)=> { clearCache.IsEnabled=false;try { maintenanceStatus.Text=await ClearRebuildableCacheAsync(); } catch(Exception error) { maintenanceStatus.Text=error.Message; } finally { clearCache.IsEnabled=true; } };
            using var checkCts=new CancellationTokenSource();
            var refreshing=false;
            var provider=Enum.IsDefined(settings.Provider)?settings.Provider:RecognitionProvider.Gemini;
            var profile=settings.Profile(provider);
            string CurrentModel()=>provider==RecognitionProvider.Gemini?settings.Model:profile.Model;
            void RefreshProvider()
            {
                refreshing=true;
                try
                {
                    profile=settings.Profile(provider);
                    if(provider!=RecognitionProvider.Gemini) settings.ProviderProfiles[provider]=profile;
                    key.Password=VM.Credentials.Read(provider);
                    defaultSearch.IsOn=RecognitionPlan.IsOnline(settings);
                    model.ItemsSource=ProviderCatalog.Models(provider).Append(CurrentModel()).Distinct().ToArray();
                    model.SelectedItem=CurrentModel();model.Text=CurrentModel();
                    prompt.Text=provider==RecognitionProvider.Gemini?settings.GeminiPrompt:profile.Prompt;
                    tokenPolicy.Visibility=provider==RecognitionProvider.Gemini?Visibility.Visible:Visibility.Collapsed;
                    providerInfo.Text=provider switch {
                        RecognitionProvider.Gemini=>"使用 Google Search；搜索结果以实际引用为准。",
                        RecognitionProvider.DeepSeek=>"图片识别请选择 deepseek-flash。在识别强度弹窗中开启 DeepSeek 搜索。",
                        _=>$"在识别强度弹窗中开启 {provider} 搜索。"
                    };
                    checkStatus.Text="读取模型清单不发送照片，不生成内容。";
                }
                finally {refreshing=false;}
            }
            providerBox.SelectedIndex=Array.IndexOf(providers,provider);
            RefreshProvider();
            recognition.Children.Insert(3,defaultStrength);
            recognition.Children.Insert(4,defaultSearch);
            defaultStrength.SelectionChanged+=(_,_)=> {
                if(defaultStrength.SelectedIndex<0)return;
                settings.DefaultStrength=strengths[defaultStrength.SelectedIndex];VM.SaveHeader();UpdateModePresentation();
            };
            defaultSearch.Toggled+=(_,_)=> {
                if(refreshing)return;
                RecognitionPlan.SetOnline(settings,defaultSearch.IsOn);VM.SaveHeader();UpdateModePresentation();
            };
            providerBox.SelectionChanged+=(_,_)=> {
                if(providerBox.SelectedIndex<0)return;
                provider=providers[providerBox.SelectedIndex];settings.Provider=provider;
                RefreshProvider();VM.SaveHeader();
            };
            void SaveModel(string value)
            {
                if(refreshing || string.IsNullOrWhiteSpace(value) || value.Trim()==CurrentModel())return;
                if(provider==RecognitionProvider.Gemini)settings.Model=value.Trim();else profile.Model=value.Trim();
                VM.SaveHeader();UpdateModePresentation();
            }
            void SaveBudget(string value)
            {
                if(int.TryParse(value,out var amount) && amount is >=1 and <=10000 && amount!=VM.State.Settings.BatchCallLimit)
                {VM.State.Settings.BatchCallLimit=amount;VM.SaveHeader();}
            }
            key.PasswordChanged+=(_,_)=> {if(refreshing)return;try {VM.Credentials.Save(provider,key.Password);}catch(Exception error){checkStatus.Text=error.Message;}};
            model.SelectionChanged+=(_,_)=> {if(model.SelectedItem is string value)SaveModel(value);};
            var modelWired=false;model.Loaded+=(_,_)=> {if(!modelWired && VisualChild<TextBox>(model) is { } input) {modelWired=true;input.TextChanged+=(_,_)=>SaveModel(input.Text);}};
            model.TextSubmitted+=(_,args)=>SaveModel(args.Text);
            budget.ValueChanged+=(_,args)=>SaveBudget(args.NewValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var budgetWired=false;budget.Loaded+=(_,_)=> {if(!budgetWired && VisualChild<TextBox>(budget) is { } input) {budgetWired=true;input.TextChanged+=(_,_)=>SaveBudget(input.Text);}};
            tokenPolicy.Toggled+=(_,_)=> {VM.State.Settings.OptimizeRecognitionTokens=tokenPolicy.IsOn;VM.SaveHeader();};
            prompt.TextChanged+=(_,_)=> {if(refreshing)return;if(provider==RecognitionProvider.Gemini)settings.GeminiPrompt=prompt.Text;else profile.Prompt=prompt.Text;VM.SaveHeader();};
            check.Click+=async (_,_)=> {
                check.IsEnabled=false;var requestedProvider=provider;
                try {
                    using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(30)};
                    var available=await ProviderCatalog.FetchAsync(client,requestedProvider,key.Password,checkCts.Token);
                    if(checkCts.IsCancellationRequested || requestedProvider!=provider)return;
                    var current=CurrentModel();refreshing=true;
                    try {model.ItemsSource=ProviderCatalog.Models(provider).Concat(available).Append(current).Distinct().ToArray();model.SelectedItem=current;model.Text=current;}
                    finally {refreshing=false;}
                    checkStatus.Text=$"已读取 {available.Count} 个模型；未发送照片，未生成内容。";
                }catch(Exception error){if(!checkCts.IsCancellationRequested && requestedProvider==provider)checkStatus.Text=error.Message;}
                finally{check.IsEnabled=true;}
            };
            reset.Click+=(_,_)=> { VM.State.BatchCalls=0; VM.State.BatchId=Guid.NewGuid().ToString("N"); VM.Save(); checkStatus.Text="已开始新批次，调用计数归零。"; };

            dialog.Closing+=(_,_)=>checkCts.Cancel();
            await dialog.ShowAsync();UpdateModePresentation();RenderSelection();if(afterClose is not null) DispatcherQueue.TryEnqueue(()=>afterClose());
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
            panel.Children.Add(new Expander { Header="独立副本与回收记录",Content=BuildPhotoOperationHistoryPanel(),HorizontalAlignment=HorizontalAlignment.Stretch });
            var result=await DialogAsync("文件操作记录",new ScrollViewer { Content=panel,MaxHeight=590 },"恢复中断任务","撤销选中归档");
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
            LocalNamingLearning.Reconcile(VM.State);
            var learning=new ComboBox { Header="学习模式",ItemsSource=new[]{"车型记忆，推广规则需确认","自动学习并应用","仅显式点击时学习"},SelectedIndex=(int)VM.State.Settings.Learning,HorizontalAlignment=HorizontalAlignment.Stretch };
            learning.SelectionChanged+=(_,_)=> { VM.State.Settings.Learning=(LearningMode)Math.Max(0,learning.SelectedIndex);VM.SaveHeader(); };
            var list=new ListView { MaxHeight=190,MinWidth=350 };
            var brand=new TextBox { Header="品牌" };var family=new TextBox { Header="车系范围（空白为品牌）" };var from=new TextBox { Header="原用词／模板" };var to=new TextBox { Header="新名称／用词／模板" };
            var enabled=new ToggleSwitch { Header="启用",IsOn=true };var evidence=Paragraph("选择规则查看适用范围和确认样本。");var preview=Paragraph("");
            var approve=new Button { Content="批准／保存修改" };var disable=new Button { Content="停用／撤回" };var delete=new Button { Content="删除规则" };
            void Reload() { var id=(list.SelectedItem as LocalNamingRule)?.Id;list.ItemsSource=null;list.ItemsSource=VM.State.LocalNamingRules;list.DisplayMemberPath="Description";if(id is not null) list.SelectedItem=VM.State.LocalNamingRules.FirstOrDefault(r=>r.Id==id); }
            void ShowRule()
            {
                if(list.SelectedItem is not LocalNamingRule rule) return;
                brand.Text=rule.Brand;family.Text=rule.Family;from.Text=rule.From;to.Text=rule.To;enabled.IsOn=rule.Enabled;
                var samples=VM.State.NamingSamples.Where(s=>rule.SampleIds.Contains(s.Id)).ToList();
                evidence.Text=rule.Kind+" · "+(rule.IdentityKey.Length>0?rule.IdentityKey:rule.Family.Length>0?"仅同车系":"整个品牌")+"\n"+rule.Conflict+"\n\n"+string.Join("\n\n",samples.Select(s=>$"{s.ConfirmedAt:g} · {IdentityLabel(s.Identity)}\n{s.GeneratedName} → {s.FinalName}\n依据：{s.AppliedRule}"));
                preview.Text=VM.Selected is null?"先选择分组可预览当前规则效果。":"当前组生成预览："+VM.Naming.Suggest(VM.Selected.Confirmed,VM.State,[]).Name;
            }
            list.SelectionChanged+=(_,_)=>ShowRule();Reload();
            var feedback=Paragraph("车系规则至少两个不同配置，品牌规则至少三个不同车系。规则只影响命名，不构成识别证据。");
            approve.Click+=(_,_)=> { try { if(list.SelectedItem is not LocalNamingRule rule) return;var edited=JsonSerializer.Deserialize<LocalNamingRule>(JsonSerializer.Serialize(rule,StateStore.Json),StateStore.Json)!;edited.Brand=brand.Text.Trim();edited.Family=family.Text.Trim();edited.From=from.Text.Trim();edited.To=LatinText.English(to.Text.Trim());edited.Enabled=enabled.IsOn;LocalNamingLearning.ValidateRule(edited);edited.Approved=true;edited.ManuallyEdited=true;var i=VM.State.LocalNamingRules.IndexOf(rule);VM.State.LocalNamingRules[i]=edited;LocalNamingLearning.Reconcile(VM.State);VM.SaveHeader();Reload();list.SelectedItem=edited;feedback.Text="已保存；明确补充要求始终优先。"; } catch(Exception error) { feedback.Text=error.Message; } };
            disable.Click+=(_,_)=> { if(list.SelectedItem is LocalNamingRule rule) { rule.Enabled=false;rule.Approved=false;LocalNamingLearning.Reconcile(VM.State);VM.SaveHeader();Reload();feedback.Text="已撤回，不再影响新名称。"; } };
            delete.Click+=(_,_)=> { if(list.SelectedItem is LocalNamingRule rule) { VM.State.LocalNamingRules.Remove(rule);LocalNamingLearning.Reconcile(VM.State);VM.SaveHeader();Reload(); } };
            var actions=new StackPanel { Orientation=Orientation.Horizontal,Spacing=6 };foreach(var button in new[]{approve,disable,delete}) actions.Children.Add(button);
            var export=new Button { Content="导出命名样本与规则（JSON）" };
            export.Click+=async(_,_)=> { try { var picker=new FileSavePicker { SuggestedFileName="CarArchive-naming" };picker.FileTypeChoices.Add("JSON",new List<string>{".json"});WinRT.Interop.InitializeWithWindow.Initialize(picker,WindowHandle);var file=await picker.PickSaveFileAsync();if(file is not null) await FileIO.WriteTextAsync(file,LocalNamingLearning.Export(VM.State)); } catch(Exception error) { feedback.Text=error.Message; } };
            var guide=new Button { Content="查看／更新本地车型指南" };var panel=new StackPanel { Spacing=8,MaxWidth=570 };
            foreach(var item in new FrameworkElement[]{learning,list,brand,family,from,to,enabled,actions,new Expander { Header="确认样本与适用范围",Content=evidence,HorizontalAlignment=HorizontalAlignment.Stretch },preview,guide,export,feedback}) panel.Children.Add(item);
            var legacy=new ListView { ItemsSource=VM.State.Rules.Select(r=>r.Description).ToList(),MaxHeight=120 };
            panel.Children.Add(new Expander { Header="兼容旧版用词记录",Content=legacy,HorizontalAlignment=HorizontalAlignment.Stretch });
            var dialog=new ContentDialog { XamlRoot=XamlRoot,Title="命名学习",Content=new ScrollViewer { Content=panel,MaxHeight=620 },CloseButtonText="关闭" };var showGuide=false;
            guide.Click+=(_,_)=> { showGuide=true;dialog.Hide(); };
            await dialog.ShowAsync();RenderSelection();if(showGuide) DispatcherQueue.TryEnqueue(()=>NamingGuide_Click(sender,e));
        }
        catch(Exception error) { VM.Status=error.Message; }
    }
    private async Task<string> ClearRebuildableCacheAsync()
    {
        if(VM.Groups.Any(g=>g.IsWorking) || !VM.BackgroundImport.IsCompleted) return "请等待图片准备和本组任务结束后再清理。";
        var directory=Path.GetFullPath(Path.Combine(ViewModels.MainViewModel.DataDirectory,"cache"));
        if(!Directory.Exists(directory)) return "缓存目录为空。";
        if(File.GetAttributes(directory).HasFlag(System.IO.FileAttributes.ReparsePoint)) throw new IOException("缓存目录是链接，请移除链接后再清理；未删除任何文件。");
        var retained=VM.Groups.SelectMany(g=>g.Photos).Where(p=>!File.Exists(p.Path)).SelectMany(p=>new[]{p.PreviewPath,p.ThumbnailPath})
            .Concat(VM.State.Assets.Where(a=>!File.Exists(a.OriginalPath)).SelectMany(a=>new[]{a.PreviewPath,a.ThumbnailPath}))
            .Concat(VM.State.Assets.Select(a=>a.OriginalPath)).Concat(VM.Groups.SelectMany(g=>g.Photos).SelectMany(p=>new[]{p.Path,p.OriginalPath}))
            .Where(p=>p.Length>0).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var count=await Task.Run(()=> { var removed=0;foreach(var file in Directory.EnumerateFiles(directory)) { if(retained.Contains(Path.GetFullPath(file))) continue;try { File.Delete(file);removed++; } catch(IOException) { } }return removed; });
        VM.Images.ClearCompletedRequests();
        foreach(var asset in VM.State.Assets.Where(a=>File.Exists(a.OriginalPath))) { asset.PreviewPath="";asset.ThumbnailPath="";asset.Refresh();VM.SaveAsset(asset); }
        if(currentPhoto is not null) { await VM.Images.EnsureThumbnailAsync(currentPhoto,default);await ShowPhotoAsync(currentPhoto); }
        return $"已清理 {count} 个缓存文件；缺少原图时所需的预览已保留。";
    }
}
