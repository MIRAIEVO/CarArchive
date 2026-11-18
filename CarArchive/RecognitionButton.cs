using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
namespace CarArchive;
/// <summary>Native toolbar chrome; auto columns center the icon and label as one unit.</summary>
public sealed class RecognitionButton:AppBarButton
{
 public RecognitionButton()
 {
  var measure=new TextBlock {Text="取消识别",FontSize=12};measure.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
  Width=MinWidth=MaxWidth=Math.Ceiling(measure.DesiredSize.Width)+56;
 }
 protected override void OnApplyTemplate()
 {
  base.OnApplyTemplate();
  if(GetTemplateChild("ContentRoot") is Grid root && root.ColumnDefinitions.Count>0)
  {root.ColumnDefinitions[0].Width=GridLength.Auto;root.HorizontalAlignment=HorizontalAlignment.Center;}
 }
}
