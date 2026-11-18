using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace CarArchive;
public sealed partial class MainPage
{
 private Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase? pasteIdentityFlyout;
 private bool pasteIdentityOpen;
 private async void PasteIdentity_Click(object sender,RoutedEventArgs e)
 {
  try
  {
   if(pasteIdentityOpen)return;
   var group=RequireGroup();if(!group.Editable)return;
   VM.MarkTouched(group);await VM.PrepareNamingGuideAsync();if(VM.Selected!=group||!group.Editable||pasteIdentityOpen)return;
   var input=new TextBox {AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,PlaceholderText="粘贴车型 JSON",VerticalAlignment=VerticalAlignment.Stretch};
   ScrollViewer.SetVerticalScrollBarVisibility(input,ScrollBarVisibility.Auto);
   ScrollViewer.SetHorizontalScrollBarVisibility(input,ScrollBarVisibility.Disabled);
   var preview=Paragraph("粘贴后自动解析");preview.MaxLines=2;preview.TextTrimming=TextTrimming.CharacterEllipsis;
   var automatic=new ToggleSwitch {OffContent="手动确定",OnContent="粘贴即确定",IsOn=VM.State.Settings.AutoApplyPastedJson};
   automatic.Toggled+=(_,_)=> {VM.State.Settings.AutoApplyPastedJson=automatic.IsOn;VM.SaveHeader();};
   var apply=new Button {Content="确定",IsEnabled=false,HorizontalAlignment=HorizontalAlignment.Right};
   var outerWidth=Math.Min(520,Math.Max(1,XamlRoot.Size.Width-48));
   var panel=new Grid {MinWidth=0,HorizontalAlignment=HorizontalAlignment.Stretch,Height=Math.Min(420,Math.Max(180,XamlRoot.Size.Height-120)),RowSpacing=12};
   panel.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});panel.RowDefinitions.Add(new RowDefinition());
   panel.RowDefinitions.Add(new RowDefinition {Height=new GridLength(54)});panel.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
   panel.Children.Add(new TextBlock {Text="粘贴 JSON",FontSize=18,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold});
   Grid.SetRow(input,1);panel.Children.Add(input);Grid.SetRow(preview,2);panel.Children.Add(preview);var actions=new Grid();actions.ColumnDefinitions.Add(new ColumnDefinition());actions.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});actions.Children.Add(automatic);Grid.SetColumn(apply,1);actions.Children.Add(apply);Grid.SetRow(actions,3);panel.Children.Add(actions);
   var flyout=new Flyout {Content=panel,Placement=Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight,ShouldConstrainToRootBounds=true};
   var presenter=new Style {TargetType=typeof(FlyoutPresenter)};
   presenter.Setters.Add(new Setter(FrameworkElement.WidthProperty,outerWidth));
   presenter.Setters.Add(new Setter(FrameworkElement.MinWidthProperty,outerWidth));
   presenter.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty,outerWidth));
   presenter.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(16)));
   presenter.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));
   presenter.Setters.Add(new Setter(Control.CornerRadiusProperty,new CornerRadius(14)));
   presenter.Setters.Add(new Setter(ScrollViewer.HorizontalScrollBarVisibilityProperty,ScrollBarVisibility.Disabled));
   presenter.Setters.Add(new Setter(ScrollViewer.VerticalScrollBarVisibilityProperty,ScrollBarVisibility.Auto));
   flyout.FlyoutPresenterStyle=presenter;
   pasteIdentityFlyout=flyout;CarIdentity? identity=null;
   void Apply()
   {
    if(identity is null)return;
    if(VM.Selected!=group || !group.Editable){VM.Status="当前分组已改变，请重新粘贴。";flyout.Hide();return;}
    ApplyPastedIdentity(group,identity);flyout.Hide();RenderSelection();
   }
   input.TextChanged+=(_,_)=>
   {
    try
    {
     identity=PastedIdentity.Parse(input.Text);var name=VM.Naming.Suggest(identity,VM.State,[]);
     preview.Text=VM.Naming.ResolveBrand(identity,VM.State)+" / "+name.Name;apply.IsEnabled=true;
     if(automatic.IsOn)
     {
      var pastedText=input.Text;
      DispatcherQueue.TryEnqueue(()=> {if(pasteIdentityOpen && ReferenceEquals(pasteIdentityFlyout,flyout) && automatic.IsOn && input.Text==pastedText && apply.IsEnabled)Apply();});
     }
    }
    catch {identity=null;apply.IsEnabled=false;preview.Text=input.Text.Length==0?"粘贴后自动解析":"请粘贴有效的车型 JSON";}
   };
   apply.Click+=(_,_)=>Apply();
   flyout.Opened+=(_,_)=>input.Focus(FocusState.Programmatic);
   flyout.Closed+=(_,_)=> {pasteIdentityOpen=false;if(ReferenceEquals(pasteIdentityFlyout,flyout))pasteIdentityFlyout=null;};
   CloseAllSuggestions();pasteIdentityOpen=true;flyout.ShowAt(PasteIdentityButton);
  }
  catch(Exception error){pasteIdentityOpen=false;VM.Status=error.Message;}
 }
 private void ApplyPastedIdentity(PhotoGroup group,CarIdentity identity)
 {
  var before=VM.CaptureEdits([group]);if(group.Verification is not null)group.VerificationHistory.Add(group.Verification);
  group.Suggestion=null;group.Verification=null;group.Confirmed=identity.Clone();group.NamingOriginalIdentity=identity.Clone();group.PreviousFolderName=group.FolderName;group.ManualName=false;group.ManualTargetBrand=false;
  VM.GenerateName(group);group.TaskState=GroupTaskState.Idle;group.TaskKind="";group.State="网页 JSON 已导入 · 请人工核对";VM.MarkTouched(group);VM.RecordEdits(before,new(){group.Id});VM.SaveGroup(group);VM.Status="已导入车型字段并按本地指南生成名称；未调用 API。";
 }
}
