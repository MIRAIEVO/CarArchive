using Windows.Storage.Pickers;
namespace CarArchive;
public sealed partial class MainPage
{
 private async Task ExportNamingLogAsync()
 {
  var picker=new FileSavePicker{SuggestedFileName="CarArchive-命名点击记录",DefaultFileExtension=".csv"};picker.FileTypeChoices.Add("CSV",new List<string>{".csv"});WinRT.Interop.InitializeWithWindow.Initialize(picker,WindowHandle);var file=await picker.PickSaveFileAsync();if(file is null)return;
  await VM.FlushWritesAsync();var csv=await Task.Run(VM.ExportNamingClicksCsv);await File.WriteAllTextAsync(file.Path,csv,new System.Text.UTF8Encoding(true));VM.Status="已导出最近 1000 条以内的手动记住名称记录。";
 }
}
