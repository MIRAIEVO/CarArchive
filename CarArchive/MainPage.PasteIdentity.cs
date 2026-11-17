using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace CarArchive;
public sealed partial class MainPage
{
 private async void PasteIdentity_Click(object sender,RoutedEventArgs e)
 {
  try {
   var group=RequireGroup();if(!group.Editable)return;VM.MarkTouched(group);await VM.PrepareNamingGuideAsync();if(VM.Selected!=group||!group.Editable)return;
   var input=new TextBox{AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=220,PlaceholderText="粘贴 Gemini 网页返回的车型 JSON，可包含代码围栏和说明文字"};
   var preview=Paragraph("只导入车型字段；不会把网页结论标记为联网验证通过。Key 等计算字段会忽略。");
   var panel=new StackPanel{Spacing=10,MinWidth=380,MaxWidth=600};panel.Children.Add(input);panel.Children.Add(preview);
   var dialog=new ContentDialog{XamlRoot=XamlRoot,Title="粘贴网页车型 JSON",RequestedTheme=RequestedTheme,Content=new ScrollViewer{Content=panel,MaxHeight=600},PrimaryButtonText="确认",CloseButtonText="取消",IsPrimaryButtonEnabled=false};CarIdentity? identity=null;
   input.TextChanged+=(_,_)=>{try{identity=PastedIdentity.Parse(input.Text);var name=VM.Naming.Suggest(identity,VM.State,[]);preview.Text=System.Text.Json.JsonSerializer.Serialize(identity,new System.Text.Json.JsonSerializerOptions(StateStore.Json){WriteIndented=true})+"\n\n目录："+VM.Naming.ResolveBrand(identity,VM.State)+" / "+name.Name;dialog.IsPrimaryButtonEnabled=true;}catch(Exception error){identity=null;dialog.IsPrimaryButtonEnabled=false;preview.Text=error.Message;}};
   if(await dialog.ShowAsync()!=ContentDialogResult.Primary||identity is null)return;
   if(VM.Selected!=group||!group.Editable)throw new InvalidOperationException("当前分组已改变，请重新粘贴。");
   ApplyPastedIdentity(group,identity);RenderSelection();
  }catch(Exception error){VM.Status=error.Message;}
 }
 private void ApplyPastedIdentity(PhotoGroup group,CarIdentity identity)
 {
  var before=VM.CaptureEdits([group]);if(group.Verification is not null)group.VerificationHistory.Add(group.Verification);
  group.Suggestion=null;group.Verification=null;group.Confirmed=identity.Clone();group.NamingOriginalIdentity=identity.Clone();group.PreviousFolderName=group.FolderName;group.ManualName=false;group.ManualTargetBrand=false;
  VM.GenerateName(group);group.TaskState=GroupTaskState.Idle;group.TaskKind="";group.State="网页 JSON 已导入 · 请人工核对";VM.MarkTouched(group);VM.RecordEdits(before,new(){group.Id});VM.SaveGroup(group);VM.Status="已导入车型字段并按本地指南生成名称；未调用 API。";
 }
}
