using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace CarArchive;

public sealed partial class MainPage
{
    private int thumbnailTransitionVersion;

    private static void ClearTransition(Canvas cover)
    {
        cover.Visibility = Visibility.Collapsed;
        cover.Children.Clear();
    }

    private static void ShowTransition(Canvas cover, FrameworkElement surface)
    {
        var visual = ElementCompositionPreview.GetElementVisual(cover);
        visual.StopAnimation("Opacity");
        visual.Opacity = 1;
        cover.Clip = new RectangleGeometry { Rect = new(0, 0, surface.ActualWidth, surface.ActualHeight) };
        cover.Visibility = Visibility.Visible;
    }

    private static void PlaceTransitionElement(Canvas cover, FrameworkElement element, Rect bounds)
    {
        element.Width = bounds.Width;
        element.Height = bounds.Height;
        Canvas.SetLeft(element, bounds.X);
        Canvas.SetTop(element, bounds.Y);
        cover.Children.Add(element);
    }

    private async Task BeginPreviewTransitionAsync(int version)
    {
        if (PreviewTransitionCover.Visibility != Visibility.Visible)
        {
            var bounds = ImageRect();
            if (MainImage.Source is null || bounds.Width <= 0 || bounds.Height <= 0) return;
            var transform = PreviewHost.TransformToVisual(PreviewSurface);
            PlaceTransitionElement(PreviewTransitionCover, new Image { Source = MainImage.Source, Stretch = Stretch.Fill }, transform.TransformBounds(bounds));
            if (CropOutline.Visibility == Visibility.Visible)
            {
                var crop = new Rect(Canvas.GetLeft(CropOutline), Canvas.GetTop(CropOutline), CropOutline.ActualWidth, CropOutline.ActualHeight);
                PlaceTransitionElement(PreviewTransitionCover, new Microsoft.UI.Xaml.Shapes.Rectangle
                {
                    Fill = CropOutline.Fill,
                    Stroke = CropOutline.Stroke,
                    StrokeThickness = CropOutline.StrokeThickness * PreviewZoom.ZoomFactor
                }, transform.TransformBounds(crop));
            }
        }
        ShowTransition(PreviewTransitionCover, PreviewSurface);
        await NextLayoutFrameAsync();
        if (version == previewVersion) await NextLayoutFrameAsync();
    }

    private void BeginThumbnailTransition()
    {
        thumbnailTransitionVersion++;
        if (ThumbnailTransitionCover.Visibility != Visibility.Visible)
        {
            var viewport = new Rect(0, 0, ThumbnailSurface.ActualWidth, ThumbnailSurface.ActualHeight);
            foreach (var item in VisualDescendants(PhotoList).OfType<ListViewItem>())
            {
                var row = item.TransformToVisual(ThumbnailSurface).TransformBounds(new Rect(0, 0, item.ActualWidth, item.ActualHeight));
                row.Intersect(viewport);
                if (row.IsEmpty || row.Width <= 0 || row.Height <= 0) continue;
                var children = VisualDescendants(item).ToArray();
                var image = children.OfType<Image>().FirstOrDefault();
                if (image?.Source is not BitmapImage { PixelHeight: > 0 }) continue;
                PlaceTransitionElement(ThumbnailTransitionCover, new Image { Source = image.Source, Stretch = image.Stretch },
                    image.TransformToVisual(ThumbnailSurface).TransformBounds(new Rect(0, 0, image.ActualWidth, image.ActualHeight)));
                if (children.OfType<TextBlock>().FirstOrDefault() is { } caption && caption.Parent is Border label)
                    PlaceTransitionElement(ThumbnailTransitionCover, new Border
                    {
                        Background = label.Background,
                        Padding = label.Padding,
                        Child = new TextBlock { Text = caption.Text, FontSize = caption.FontSize, TextTrimming = caption.TextTrimming }
                    }, label.TransformToVisual(ThumbnailSurface).TransformBounds(new Rect(0, 0, label.ActualWidth, label.ActualHeight)));
            }
        }
        if (ThumbnailTransitionCover.Children.Count > 0) ShowTransition(ThumbnailTransitionCover, ThumbnailSurface);
    }

    private async Task FinishPreviewTransitionAsync(int version)
    {
        await FinishTransitionAsync(PreviewTransitionCover, () => version == previewVersion);
        if (version == previewVersion) PreviewHost.IsHitTestVisible = true;
    }

    private Task FinishThumbnailTransitionAsync(int version) =>
        FinishTransitionAsync(ThumbnailTransitionCover, () => version == thumbnailTransitionVersion);

    private static async Task FinishTransitionAsync(Canvas cover, Func<bool> isCurrent)
    {
        if (cover.Visibility != Visibility.Visible || !isCurrent()) return;
        await NextLayoutFrameAsync();
        if (!isCurrent()) return;
        await NextLayoutFrameAsync();
        if (!isCurrent()) return;
        if (UiMotion.Enabled)
        {
            UiMotion.Fade(cover, 1, 0, 100);
            await Task.Delay(110);
        }
        if (isCurrent()) ClearTransition(cover);
    }
}
