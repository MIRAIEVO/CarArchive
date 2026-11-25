using System.Text.Json;
using CarArchive.Core;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
namespace CarArchive;
public sealed partial class MainPage
{
    private string verificationPrompt="";
    private Action<DataPackage> clipboardWriter=Clipboard.SetContent;
    private async Task WarmClipboardAsync() {try {await VM.Images.WarmClipboardEncoderAsync();await Task.Run(()=>{var package=new DataPackage();package.RequestedOperation=DataPackageOperation.Copy;_ = Clipboard.IsHistoryEnabled();});}catch{}}

    private async Task CopyVerificationAsync()
    {
        CapturePreviewVideoPosition();var group=RequireGroup();var photos=group.Photos.Where(p=>!p.Excluded && p.Crop is not null).Select(p=>p.Snapshot()).ToList();
        if(photos.Count==0) photos=UploadSelection.Select(group);
        var directory=Path.Combine(ViewModels.MainViewModel.DataDirectory,"clipboard",group.Id);Directory.CreateDirectory(directory);
        VM.Status="正在准备框选图片…";await Task.Yield();var files=new List<IStorageItem>();
        foreach(var photo in photos) { var file=Path.Combine(directory,Guid.NewGuid().ToString("N")+".jpg");await File.WriteAllBytesAsync(file,await VM.Images.ClipboardJpegAsync(photo,default));files.Add(await StorageFile.GetFileFromPathAsync(file)); }
        verificationPrompt=$"""
            请独立验证这些图片中的同一目标车辆。待验证候选：{JsonSerializer.Serialize(group.Confirmed,StateStore.Json)}
            查阅制造商资料，对照车系、具体型号（含高阶版本或特别版）、代次、底盘代号、改款和车身的可见辨识特征。核对同市场及相近车型年款，尤其核查官方外观套件是否能让其他版本呈现相同外观。逐项列出符合、矛盾或不可见，并附真实来源。
            判定采用疑罪从无：当图中足以辨别车型的可见外观与目标版本完全吻合、没有任何矛盾，且没有能复现该外观的官方选装或外观套件时，判定「通过」，即使尾标未拍到或无法读出。仅在照片确实可见非原厂改装痕迹、配件冲突等依据时，才考虑第三方改装；不要因理论上可以改装而降级。
            可见特征与目标有明确冲突时，判定「不通过」。照片缺少有区分力的角度、关键细节不可辨，或其他版本确有外观完全相同的官方套件而无法从图中区分时，判定「证据不足」，可分别报告车系和具体版本的结果。
            在最后给出一个独立 JSON 代码块，字段必须为 Brand、Series、Variant、Body、OfficialName、Generation、Code、Phase、Package、ProductionYears，值均为字符串。Variant 写从车系开始的完整营销型号。未知字段留空。ProductionYears 填所识别改款阶段的起止年份（含改款前阶段），格式 YYYY-YYYY；阶段或起止年份不确定则留空。
            """;
        verificationPrompt+="\n"+IdentityFieldContract.Instructions;
        var package=new DataPackage();package.SetStorageItems(files);package.SetText(verificationPrompt);clipboardWriter(package);
        VM.Status=$"已复制 {files.Count} 张验证图片及提示词；可粘贴到 Gemini 网页，若未带文字请再点复制提示词。未调用 API。";
    }
    private void CopyVerificationPrompt_Click(object sender,RoutedEventArgs e)
    { if(verificationPrompt.Length==0){VM.Status="请先点击复制框选图片生成图片和提示词。";return;}var package=new DataPackage();package.SetText(verificationPrompt);clipboardWriter(package);VM.Status="验证提示词已复制。"; }
}
