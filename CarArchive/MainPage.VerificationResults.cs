using System.Text;
using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CarArchive;
public sealed partial class MainPage
{
    private double imageThumbnailHeight=120;
    private VerificationReport? renderedVerification;
    private void ResizeThumbnails(double height)
    {
        imageThumbnailHeight=height<600?96:120;ThumbnailRow.Height=new GridLength(imageThumbnailHeight+27);
        // Short/high-DPI windows keep classification fields scrollable and confirmation buttons pinned.
        ArchiveOptionsScroll.MaxHeight=height<600?Math.Max(64,Math.Min(height*.35,height-240)):double.PositiveInfinity;
        Resize(PhotoList);
        void Resize(DependencyObject node)
        {
            if(node is Image { Parent:StackPanel panel,Source:BitmapImage bitmap } image && bitmap.PixelHeight>0)
            {ApplyThumbnailSize(image);}
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++) Resize(VisualTreeHelper.GetChild(node,i));
        }
    }
    private void RenderVerification()
    {
        var report=displayed?.Verification;
        ApiResultExpander.Visibility=displayed?.Suggestion is not null || report is not null?Visibility.Visible:Visibility.Collapsed;
        VerificationBanner.Visibility=Visibility.Collapsed;
        VerificationBanner.Title=report?.StatusText ?? "";
        VerificationBanner.Severity=report?.Status switch { VerificationStatus.Passed=>InfoBarSeverity.Success,VerificationStatus.Failed=>InfoBarSeverity.Error,_=>InfoBarSeverity.Warning };
        AdoptVerificationButton.IsEnabled=displayed?.Editable==true && report?.SuggestedIdentity is not null && report.Status is not (VerificationStatus.NotRun or VerificationStatus.Canceled or VerificationStatus.Incomplete);
        AdoptVerificationButton.Visibility=report?.SuggestedIdentity is not null?Visibility.Visible:Visibility.Collapsed;
        VerificationSources.ItemsSource=report?.Sources;
        if(report is null)
        {
            var result=displayed?.Suggestion;
            VerificationDetails.Text=result is null?"":System.Text.Json.JsonSerializer.Serialize(result.Identity,new System.Text.Json.JsonSerializerOptions(StateStore.Json) { WriteIndented=true })+"\n\n"+result.Observations+"\n"+result.Research;
        }
        else
        {
            var text=new StringBuilder();text.AppendLine("车型："+IdentityLabel(report.Candidate));text.AppendLine(report.Summary);
            foreach(var feature in report.Features)
            {
                var check=report.Checks.LastOrDefault(c=>c.FeatureId==feature.Id);
                var outcome=check?.Outcome switch { FeatureMatch.Match=>"符合",FeatureMatch.Contradiction=>"矛盾",_=>"不可见／未证实" };
                text.AppendLine($"\n{feature.Field} · {outcome}\n资料特征：{feature.Expected}\n来源：{feature.ReferenceUrl}\n照片对照：{check?.Observation ?? "未返回对照"}");
            }
            if(report.SuggestedIdentity is not null) text.AppendLine("\n修正建议："+IdentityLabel(report.SuggestedIdentity)+"\n采用后更新字段；手动文件夹名称保留。");
            if(report.Error.Length>0) text.AppendLine("\n"+report.Error);
            text.AppendLine($"\n合计输入 {TotalToken(report,"total_input_tokens")} / 输出 {TotalToken(report,"total_output_tokens")} / 思考 {report.ThinkingTokens?.ToString()??"未知"} tokens；搜索 {report.SearchCount?.ToString()??"未知"} 次");
            foreach(var stage in report.Stages) text.AppendLine($"{stage.Name}：输入 {Token(stage.Result,"total_input_tokens")} / 输出 {Token(stage.Result,"total_output_tokens")} / 思考 {stage.Result.ThinkingTokens?.ToString()??"未知"}；{(stage.SearchEnabled?stage.Result.ActualSearch:"未启用搜索")}");
            VerificationDetails.Text=text.ToString();
            if(!ReferenceEquals(renderedVerification,report)) ApiResultExpander.IsExpanded=true;
        }
        renderedVerification=report;
    }
    private static long? TokenValue(RecognitionResult result,string field)
    {
        try
        {
            using var doc=System.Text.Json.JsonDocument.Parse(result.RawUsage);
            var usage=doc.RootElement;
            var alternate=field switch {"total_input_tokens"=>"input_tokens","total_output_tokens"=>"output_tokens",_=>field};
            return (usage.TryGetProperty(field,out var value) || usage.TryGetProperty(alternate,out value))
                && value.ValueKind==System.Text.Json.JsonValueKind.Number && value.TryGetInt64(out var tokens)?tokens:null;
        }
        catch(System.Text.Json.JsonException) { return null; }
    }
    private static string Token(RecognitionResult result,string field)=>TokenValue(result,field)?.ToString()??"未知";
    private static long? RoundToken(RecognitionRoundReport round,string field)
        => TokenValue(new RecognitionResult {RawUsage=round.Usage},field);
    private static string RecognitionUsage(RecognitionResult result,string field)
    {
        if(result.Rounds.Count==0)return Token(result,field);
        var values=result.Rounds.Select(round=>RoundToken(round,field)).ToArray();
        return values.All(value=>value.HasValue)?values.Sum().ToString()??"未知":"未知";
    }
    private static string RecognitionRoundDetails(RecognitionResult result)
    {
        var lines=result.Rounds.Select((round,index)=>
            $"第 {index+1} 轮 · 思考 {round.Effort??"模型默认"} · 输入 {RoundToken(round,"total_input_tokens")?.ToString()??"未知"} / 输出 {RoundToken(round,"total_output_tokens")?.ToString()??"未知"} / 思考 {round.ThinkingTokens?.ToString()??"未知"} · 搜索 {round.SearchCount?.ToString()??"未知"}");
        return (result.Rounds.Count==0?"":"\n"+string.Join("\n",lines))+(result.RoundNotice.Length==0?"":"\n"+result.RoundNotice);
    }
    private static string TotalToken(VerificationReport report,string field)
    {
        var values=report.Stages.Select(s=>TokenValue(s.Result,field)).ToList();return values.Count>0 && values.All(n=>n.HasValue)?values.Sum(n=>n!.Value).ToString():values.Any(n=>n.HasValue)?"未知（已知部分 "+values.Where(n=>n.HasValue).Sum(n=>n!.Value)+"）":"未知";
    }
    private static string IdentityLabel(CarIdentity c)=>string.Join(" · ",new[]{c.Brand,c.Series,c.Variant,c.Generation,c.Code,c.Phase,c.Package}.Where(s=>s.Length>0));
    private void AdoptVerification_Click(object sender,RoutedEventArgs e)=>LocalAction(()=>VM.ApplyVerification(RequireGroup()));
}
