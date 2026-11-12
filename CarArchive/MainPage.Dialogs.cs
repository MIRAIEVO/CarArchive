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
            var budget=new NumberBox { Header="每批调用次数上限（含能力检查与查证）",Minimum=1,Maximum=10000,Value=VM.State.Settings.BatchCallLimit,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact };
            var check=new Button { Content="保存并检查多图 + Google Search 能力",HorizontalAlignment=HorizontalAlignment.Stretch };
            var reset=new Button { Content="开始新批次（重置调用计数）",HorizontalAlignment=HorizontalAlignment.Stretch };
            var checkStatus=Paragraph("检查会提交两张合成测试图并实际联网，可能计费用量。不会发送你的照片。\n只保存 Key 不会调用 API。识别时派生图片去除 EXIF，原图保持不变。");
            var links=new StackPanel { Spacing=6 };
            var panel=new StackPanel { Spacing=12,MinWidth=410,MaxWidth=560 };
            var advanced=new StackPanel { Spacing=8 };foreach(var element in new FrameworkElement[]{checkImage,check,checkStatus,links}) advanced.Children.Add(element);
            var recognition=new StackPanel { Spacing=10 };foreach(var element in new FrameworkElement[]{key,model,search,budget,Paragraph($"本批已调用 {VM.State.BatchCalls} 次。深入查证默认两次，必要时最多三次。"),reset,new Expander { Header="高级 · 能力检查（会实际调用 API）",Content=advanced,HorizontalAlignment=HorizontalAlignment.Stretch }}) recognition.Children.Add(element);
            var openLearning=new Button { Content="管理学习模式与规则" };var openGuide=new Button { Content="查看／更新车型指南" };
            var naming=new StackPanel { Spacing=8 };naming.Children.Add(Paragraph("全部在本机学习，不发送历史名称、规则或目录清单至 API。"));naming.Children.Add(openLearning);naming.Children.Add(openGuide);
            var openData=new Button { Content="打开本版本数据目录" };var importData=new Button { Content="导入旧版本数据…" };var clearCache=new Button { Content="清理可重建缓存" };var logs=new Button { Content="打开崩溃日志" };
            var maintenanceStatus=Paragraph("");
            var maintenance=new StackPanel { Spacing=8 };foreach(var element in new FrameworkElement[]{Paragraph(ViewModels.MainViewModel.DataDirectory),openData,importData,clearCache,logs,maintenanceStatus}) maintenance.Children.Add(element);
            panel.Children.Add(new Expander { Header="识别与费用",Content=recognition,IsExpanded=true,HorizontalAlignment=HorizontalAlignment.Stretch });
            panel.Children.Add(new Expander { Header="命名学习",Content=naming,HorizontalAlignment=HorizontalAlignment.Stretch });
            panel.Children.Add(new Expander { Header="数据与维护",Content=maintenance,HorizontalAlignment=HorizontalAlignment.Stretch });
            var dialog=new ContentDialog { XamlRoot=XamlRoot,Title="设置",Content=new ScrollViewer { Content=panel,MaxHeight=620 },PrimaryButtonText="保存",CloseButtonText="关闭",DefaultButton=ContentDialogButton.Close };
            Action? afterClose=null;
            openLearning.Click+=(_,_)=> { afterClose=()=>Rules_Click(sender,e);dialog.Hide(); };
            openGuide.Click+=(_,_)=> { afterClose=()=>NamingGuide_Click(sender,e);dialog.Hide(); };
            openData.Click+=async(_,_)=> { try { await Windows.System.Launcher.LaunchFolderPathAsync(ViewModels.MainViewModel.DataDirectory); } catch(Exception error) { maintenanceStatus.Text=error.Message; } };
            logs.Click+=(_,_)=> { dialog.Hide();afterClose=()=>CrashLogs_Click(sender,e); };
            importData.Click+=async(_,_)=> { try { var source=await PickFolderAsync();if(source is null) return;maintenanceStatus.Text=File.Exists(Path.Combine(source,"cararchive.db"))?"已选择："+source+"\n当前版本已打开数据库，不会覆盖或合并。请解压一份新的便携包，启动时选择此目录导入。迁移前关闭本版本。":"该目录没有 cararchive.db，请选择旧版本 Data 或原本机数据目录。"; } catch(Exception error) { maintenanceStatus.Text=error.Message; } };
            clearCache.Click+=async(_,_)=> { clearCache.IsEnabled=false;try { maintenanceStatus.Text=await ClearRebuildableCacheAsync(); } catch(Exception error) { maintenanceStatus.Text=error.Message; } finally { clearCache.IsEnabled=true; } };
            using var checkCts=new CancellationTokenSource();
            void SaveSettings()
            {
                if(string.IsNullOrWhiteSpace(model.Text)) throw new ArgumentException("请填写模型 ID。");
                if(double.IsNaN(budget.Value)) throw new ArgumentException("请填写有效的调用上限。");
                VM.Credentials.Save(key.Password); VM.State.Settings.Model=model.Text.Trim();
                VM.State.Settings.BatchCallLimit=(int)budget.Value;
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
