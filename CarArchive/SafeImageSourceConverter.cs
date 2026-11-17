using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
namespace CarArchive;
public sealed class SafeImageSourceConverter:IValueConverter
{
    private readonly Dictionary<string,WeakReference<BitmapImage>> images=new(StringComparer.OrdinalIgnoreCase);
    public object Convert(object value,Type targetType,object parameter,string language)
    {
        if(value is not string path || string.IsNullOrWhiteSpace(path) || !Uri.TryCreate(path,UriKind.Absolute,out var uri))return null!;
        if(images.TryGetValue(path,out var existing) && existing.TryGetTarget(out var bitmap))return bitmap;
        bitmap=new BitmapImage(uri);images[path]=new(bitmap);
        if(images.Count>2048)foreach(var key in images.Where(pair=>!pair.Value.TryGetTarget(out _)).Select(pair=>pair.Key).ToArray())images.Remove(key);
        return bitmap;
    }
    public object ConvertBack(object value,Type targetType,object parameter,string language)=>throw new NotSupportedException();
}
