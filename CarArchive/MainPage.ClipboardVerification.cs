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
        verificationPrompt="请独立验证这些图片中的同一目标车辆。候选信息仅为待核实假设，不是事实："+JsonSerializer.Serialize(group.Confirmed,StateStore.Json)+"。请联网查询制造商资料，比较车系、具体型号、代次、底盘代号、改款、车身和套件的可见辨识特征。逐项列出符合、矛盾或不可见及真实来源；改装件和尾标不独立证明性能版本。不确定时留空，明确给出通过、不通过或证据不足，并在最后给出一个独立 JSON 代码块，字段必须为 Brand、Series、Variant、Body、OfficialName、Generation、Code、Phase、Package、ProductionYears，值均为字符串，未知留空；Variant 写从车系开始的完整营销型号，不生成文件夹名。";
        verificationPrompt+="\n"+IdentityFieldContract.Instructions;
        var package=new DataPackage();package.SetStorageItems(files);package.SetText(verificationPrompt);clipboardWriter(package);
        VM.Status=$"已复制 {files.Count} 张验证图片及提示词；可粘贴到 Gemini 网页，若未带文字请再点复制提示词。未调用 API。";
    }
    private void CopyVerificationPrompt_Click(object sender,RoutedEventArgs e)
    { if(verificationPrompt.Length==0){VM.Status="请先点击复制框选图片生成图片和提示词。";return;}var package=new DataPackage();package.SetText(verificationPrompt);clipboardWriter(package);VM.Status="验证提示词已复制。"; }
}
