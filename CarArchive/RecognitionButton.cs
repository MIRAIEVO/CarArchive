using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Windows.Foundation;

namespace CarArchive;

/// <summary>A normal native button in an AppBarElementContainer, with tightly grouped centered content.</summary>
public sealed class RecognitionButton:Button
{
    private readonly StackPanel row=new() {Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    private readonly TextBlock label=new() {Text="Gemini识别",FontSize=12,VerticalAlignment=VerticalAlignment.Center};
    private IconElement? icon;
    public RecognitionButton()
    {
        row.Children.Add(label);Content=row;Padding=new Thickness(10,6,10,6);Height=36;
        HorizontalContentAlignment=HorizontalAlignment.Center;VerticalContentAlignment=VerticalAlignment.Center;
        label.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        Width=MinWidth=MaxWidth=Math.Ceiling(label.DesiredSize.Width)+18+8+20;
        Icon=new SymbolIcon(Symbol.Find);AutomationProperties.SetName(this,label.Text);
    }
    public string Label
    {
        get=>label.Text;
        set {if(label.Text==value)return;label.Text=value;AutomationProperties.SetName(this,value);}
    }
    public IconElement? Icon
    {
        get=>icon;
        set
        {
            if(icon is not null)row.Children.Remove(icon);
            icon=value;
            if(icon is not null){icon.Width=18;icon.Height=18;row.Children.Insert(0,icon);}
        }
    }
}
