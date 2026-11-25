using System.Text.Json;
using CarArchive.Core;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
namespace CarArchive;
public sealed partial class MainPage
{
    private Action<DataPackage> clipboardWriter=Clipboard.SetContent;
    private async Task WarmClipboardAsync() {try {await VM.Images.WarmClipboardEncoderAsync();await Task.Run(()=>{var package=new DataPackage();package.RequestedOperation=DataPackageOperation.Copy;_ = Clipboard.IsHistoryEnabled();});}catch{}}

    private async Task CopyVerificationAsync()
    {
        CapturePreviewVideoPosition();var group=RequireGroup();var photos=group.Photos.Where(p=>!p.Excluded && p.Crop is not null).Select(p=>p.Snapshot()).ToList();
        var prompt=BuildVerificationPrompt(group.Confirmed);
        if(photos.Count==0) photos=UploadSelection.Select(group);
        var directory=Path.Combine(ViewModels.MainViewModel.DataDirectory,"clipboard",group.Id);Directory.CreateDirectory(directory);
        VM.Status="正在准备框选图片…";await Task.Yield();var files=new List<IStorageItem>();
        foreach(var photo in photos) { var file=Path.Combine(directory,Guid.NewGuid().ToString("N")+".jpg");await File.WriteAllBytesAsync(file,await VM.Images.ClipboardJpegAsync(photo,default));files.Add(await StorageFile.GetFileFromPathAsync(file)); }
        var package=new DataPackage();package.SetStorageItems(files);package.SetText(prompt);clipboardWriter(package);
        VM.Status=$"已复制 {files.Count} 张验证图片及提示词；可粘贴到 Gemini 网页，若未带文字请再点复制提示词。未调用 API。";
    }

    private static string BuildVerificationPrompt(CarIdentity identity)=>$"""
            请独立重新识别图片中的同一目标车辆，并同时核验候选。待验证候选：{JsonSerializer.Serialize(identity,StateStore.Json)}
            回复第一行必须直接写结论：完全通过、部分通过、通过但有补充、不通过、证据不足，五选一。候选全部正确且没有实质补充时用「完全通过」；能确认的同时仍有部分不符或不确定时用「部分通过」；候选正确且有新的已核实细节时用「通过但有补充」。
            查阅制造商资料，对照车系、具体型号（含高阶版本或特别版）、代次、底盘代号、改款和车身的可见辨识特征。核对同市场及相近车型年款，尤其核查官方外观套件是否能让其他版本呈现相同外观。逐项列出符合、矛盾或不可见，并附真实来源。
            判定采用疑罪从无：当图中足以辨别车型的可见外观与目标版本完全吻合、没有任何矛盾，且没有能复现该外观的官方选装或外观套件时，判定「通过」，即使尾标未拍到或无法读出。仅在照片确实可见非原厂改装痕迹、配件冲突等依据时，才考虑第三方改装；不要因理论上可以改装而降级。
            可见特征与目标有明确冲突时，判定「不通过」。照片缺少有区分力的角度、关键细节不可辨，或其他版本确有外观完全相同的官方套件而无法从图中区分时，判定「证据不足」，可分别报告车系和具体版本的结果。
            若为特别版或高阶版本，还须报告厂商公布的限量台数，并说明全球、地区或分批口径及来源。确认非限量时说明非限量；找不到可信台数时说明未公布或无法核实，不猜测。
            无论上述结论为何，本次回复最后都必须交付一个独立、可直接粘贴的完整 JSON 对象，重建实际拍到的车辆记录。不要输出空对象、只列修改项、仅复核候选、请求确认或询问是否需要完整字段。结合图片及可靠资料，填入所有可核实的具体信息，包括原候选遗漏的细节；错误字段直接改正。所有字段都要出现且值为字符串：Brand、Series、Variant、Body、OfficialName、Generation、Code、Phase、Package、ProductionYears。仅确实未知的单个字段留空。Variant 写从车系开始的完整英文营销型号；无法区分配置时仍填写能确认的完整车系。ProductionYears 填所识别改款阶段的起止年份（含改款前阶段），格式 YYYY-YYYY；阶段或起止年份不确定则留空。
            """+"\n"+IdentityFieldContract.Instructions;

    private void CopyVerificationPrompt_Click(object sender,RoutedEventArgs e)
    {
        if(VM.Selected is not { } group){VM.Status="请先选择分组。";return;}
        var package=new DataPackage();package.SetText(BuildVerificationPrompt(group.Confirmed));clipboardWriter(package);
        VM.Status="验证提示词已复制。";
    }
}
