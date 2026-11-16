#if DEBUG
using System.Text.Json;
using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
namespace CarArchive;
public sealed partial class MainPage
{
 private async Task RunV121VerificationAsync(string directory)
 {
  Directory.CreateDirectory(directory);var checks=new List<string>();void Check(bool condition,string text){if(!condition)throw new Exception(text);checks.Add(text);}
  try {
   Check(VM.Groups.Count==0,"Isolated workspace");var photo=await FixtureImageAsync(Path.Combine(directory,"portrait.jpg"),800,1600);await VM.ImportAsync([photo],true,default);await VM.BackgroundImport;var group=VM.Selected!;RenderSelection();await ShowPhotoAsync(group.Photos[0]);
   var identity=PastedIdentity.Parse("```json\n{\"Brand\":\"Lexus\",\"Series\":\"LS\",\"Variant\":\"LS Coupe Concept\",\"OfficialName\":\"Lexus LS Coupe Concept\",\"Key\":\"ignored\"}\n```");
   ApplyPastedIdentity(group,identity);RenderSelection();Check(BrandEdit.Text=="Lexus"&&VariantEdit.Text=="LS Coupe Concept"&&group.FolderName.StartsWith("LS Coupe Concept")&&group.TargetBrand=="Lexus"&&!group.ManualName,"Pasted fields and local naming synchronize without trusting Key");
   Check(group.Suggestion is null&&group.Verification is null&&VM.State.BatchCalls==0,"Pasted response never becomes grounded evidence or an API call");
   PasteIdentity_Click(this,new RoutedEventArgs());for(var i=0;i<100&&!ModalOpen();i++)await Task.Delay(40);var pasteDialog=OpenNativeDialog();var pasteControls=Descendants(pasteDialog).ToArray();var edit=pasteControls.OfType<TextBox>().Single(t=>t.AcceptsReturn);edit.Text="{\"Brand\":\"Lexus\",\"Series\":\"LS\",\"Variant\":\"LS Coupe Concept\"}";
   var parseButton=pasteControls.OfType<Button>().Single(b=>b.Content?.ToString()=="解析并预览名称");((IInvokeProvider)new ButtonAutomationPeer(parseButton).GetPattern(PatternInterface.Invoke)).Invoke();Check(pasteDialog.IsPrimaryButtonEnabled,"Paste popup parses and previews before enabling apply");
   var applyButton=Descendants(pasteDialog).OfType<Button>().Single(b=>b.Content?.ToString()=="应用并生成名称");((IInvokeProvider)new ButtonAutomationPeer(applyButton).GetPattern(PatternInterface.Invoke)).Invoke();for(var i=0;i<100&&ModalOpen();i++)await Task.Delay(40);Check(group.Confirmed.Variant=="LS Coupe Concept","Paste popup applies to its original group");
   var automatic=group.LastGeneratedName;group.FolderName="LS Coupe Concept Personal";group.ManualName=true;VM.Learn(group,true);await VM.FlushWritesAsync();var csv=VM.ExportNamingClicksCsv();Check(csv.Contains("LS Coupe Concept Personal")&&csv.Contains(automatic)&&csv.Contains("Lexus"),"Remember-click captures original fields, automatic and final name");
   group.Photos[0].Crop=new(.1,.1,.5,.5);DataPackage? copied=null;clipboardWriter=p=>copied=p;var clock=System.Diagnostics.Stopwatch.StartNew();await CopyVerificationAsync();clock.Stop();var items=await copied!.GetView().GetStorageItemsAsync();var prompt=await copied.GetView().GetTextAsync();Check(items.Count==1&&prompt.Contains("ProductionYears")&&prompt.Contains("JSON"),"Copy provides cropped image file and reusable JSON response prompt");
   var bytes=await File.ReadAllBytesAsync(items[0].Path);using var stream=new Windows.Storage.Streams.InMemoryRandomAccessStream();using(var writer=new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0))){writer.WriteBytes(bytes);await writer.StoreAsync();}stream.Seek(0);var decoder=await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);Check(decoder.PixelWidth==400&&decoder.PixelHeight==800,"Clipboard image is the complete selected crop");
   var crop=group.Photos[0].Crop;PreviewZoom.ChangeView(120,180,2,true);await Task.Delay(120);Check(group.Photos[0].Crop==crop,"Preview zoom does not mutate API crop coordinates");
   foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark}){RequestedTheme=theme;await Task.Delay(80);await CaptureVisualAsync(this,Path.Combine(directory,"workspace-"+theme+".png"));}
   Settings_Click(this,new RoutedEventArgs());for(var i=0;i<100&&!ModalOpen();i++)await Task.Delay(40);var dialog=OpenNativeDialog();var controls=Descendants(dialog).ToArray();var model=controls.OfType<ComboBox>().Single(c=>c.Header?.ToString()?.StartsWith("模型 ID")==true);Check(model.IsEditable&&model.Items.OfType<string>().Contains("gemini-4-hydrogen")&&model.Text=="gemini-3.8-flash"&&VisualChild<TextBox>(model)?.Text=="gemini-3.8-flash","Settings provides editable official model dropdown and current default");
   var search=controls.OfType<ComboBox>().Single(c=>c.Header?.ToString()=="识别方式");Check(search.Items.Count==2&&search.SelectedIndex==0,"Only Gemini and grounded remain, default is Gemini");Check(!controls.OfType<TextBlock>().Any(t=>t.Text.Contains("Lens")||t.Text.Contains("图片资料")),"Unavailable image search and Lens settings removed");
   await CaptureVisualAsync(dialog,Path.Combine(directory,"settings.png"));dialog.Hide();await VM.FlushWritesAsync();File.WriteAllText(Path.Combine(directory,"v121-verification.json"),JsonSerializer.Serialize(new{Status="Passed",Checks=checks,ClipboardPreparationMilliseconds=clock.Elapsed.TotalMilliseconds,ActualClipboardWrites=0,RealApiCalls=0},StateStore.Json));
  }catch(Exception error){File.WriteAllText(Path.Combine(directory,"v121-verification.json"),JsonSerializer.Serialize(new{Status="Failed",Checks=checks,Error=error.ToString()},StateStore.Json));}
 }
}
#endif
