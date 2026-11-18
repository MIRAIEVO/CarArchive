using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Windows.UI.ViewManagement;

namespace CarArchive;

/// <summary>Composition-only feedback leaves layout, hit targets and file operations independent of animation.</summary>
public sealed class UiMotion:DependencyObject
{
    private static readonly UISettings settings=new();
    public static bool Enabled=>settings.AnimationsEnabled;
    public static readonly DependencyProperty PressFeedbackProperty=DependencyProperty.RegisterAttached(
        "PressFeedback",typeof(bool),typeof(UiMotion),new PropertyMetadata(false,FeedbackChanged));
    public static bool GetPressFeedback(DependencyObject target)=>(bool)target.GetValue(PressFeedbackProperty);
    public static void SetPressFeedback(DependencyObject target,bool value)=>target.SetValue(PressFeedbackProperty,value);
    private static void FeedbackChanged(DependencyObject target,DependencyPropertyChangedEventArgs args)
    {
        if(target is not FrameworkElement element || element is RecognitionButton)return;
        if((bool)args.NewValue)
        {
            element.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler(Pressed),true);
            element.AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler(Released),true);
            element.PointerCanceled+=Released;element.PointerCaptureLost+=Released;element.PointerExited+=Released;
        }
        else
        {
            element.RemoveHandler(UIElement.PointerPressedEvent,new PointerEventHandler(Pressed));
            element.RemoveHandler(UIElement.PointerReleasedEvent,new PointerEventHandler(Released));
            element.PointerCanceled-=Released;element.PointerCaptureLost-=Released;element.PointerExited-=Released;
        }
    }
    private static void Pressed(object sender,PointerRoutedEventArgs args)
    {
        if(sender is FrameworkElement element && args.GetCurrentPoint(element).Properties.IsLeftButtonPressed)Scale(element,0.975f,65);
    }
    private static void Released(object sender,PointerRoutedEventArgs args)
    {
        if(sender is FrameworkElement element)Scale(element,1,95);
    }
    private static void Scale(FrameworkElement element,float scale,int milliseconds)
    {
        if(!Enabled || !element.IsLoaded)return;
        var visual=ElementCompositionPreview.GetElementVisual(element);visual.CenterPoint=new((float)element.ActualWidth/2,(float)element.ActualHeight/2,0);
        var animation=visual.Compositor.CreateVector3KeyFrameAnimation();animation.InsertKeyFrame(1,new Vector3(scale,scale,1));animation.Duration=TimeSpan.FromMilliseconds(milliseconds);visual.StartAnimation("Scale",animation);
    }
    public static void PrimeFade(FrameworkElement element)
    {
        var visual=ElementCompositionPreview.GetElementVisual(element);visual.StopAnimation("Opacity");visual.Opacity=Enabled?0:1;
    }
    public static void Fade(FrameworkElement element,float from,float to,int milliseconds)
    {
        var visual=ElementCompositionPreview.GetElementVisual(element);visual.StopAnimation("Opacity");visual.Opacity=to;
        if(!Enabled){visual.Opacity=to;return;}
        var animation=visual.Compositor.CreateScalarKeyFrameAnimation();animation.InsertKeyFrame(0,from);animation.InsertKeyFrame(1,to);animation.Duration=TimeSpan.FromMilliseconds(milliseconds);visual.StartAnimation("Opacity",animation);
    }
    public static void Reveal(FrameworkElement? element,bool slide=true)
    {
        if(element is null || !Enabled)return;
        if(!element.IsLoaded){element.Loaded-=Revealed; element.Loaded+=Revealed;return;}
        var visual=ElementCompositionPreview.GetElementVisual(element);var compositor=visual.Compositor;
        var opacity=compositor.CreateScalarKeyFrameAnimation();opacity.InsertKeyFrame(0,0.86f);opacity.InsertKeyFrame(1,1);opacity.Duration=TimeSpan.FromMilliseconds(115);visual.StartAnimation("Opacity",opacity);
        if(!slide)return;
        ElementCompositionPreview.SetIsTranslationEnabled(element,true);
        var movement=compositor.CreateVector3KeyFrameAnimation();movement.InsertKeyFrame(0,new Vector3(7,0,0));movement.InsertKeyFrame(1,Vector3.Zero,compositor.CreateCubicBezierEasingFunction(new(.2f,.8f),new(.2f,1)));movement.Duration=TimeSpan.FromMilliseconds(145);visual.StartAnimation("Translation",movement);
    }
    private static void Revealed(object sender,RoutedEventArgs args)
    {
        var element=(FrameworkElement)sender;element.Loaded-=Revealed;Reveal(element);
    }
}
