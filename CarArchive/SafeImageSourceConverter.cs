using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CarArchive;

public sealed class SafeImageSourceConverter:IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,string language)
    {
        if(value is string path && !string.IsNullOrWhiteSpace(path) && Uri.TryCreate(path,UriKind.Absolute,out var uri)) return new BitmapImage(uri);
        return null!;
    }
    public object ConvertBack(object value,Type targetType,object parameter,string language)=>throw new NotSupportedException();
}
