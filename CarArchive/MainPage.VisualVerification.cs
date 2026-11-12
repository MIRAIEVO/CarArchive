#if DEBUG
using System.Net;
using System.Text.Json;
using CarArchive.Core;
using CarArchive.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace CarArchive;

public sealed partial class MainPage
{
    private async Task RunVisualVerificationAsync(string directory)
    {
        directory=Path.GetFullPath(directory);Directory.CreateDirectory(directory);var checks=new List<string>();
        void Check(bool value,string label) { if(!value) throw new Exception(label);checks.Add(label); }
        try
        {
            Check(ViewModels.MainViewModel.DataDirectory.StartsWith(directory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) && VM.Groups.Count==0,"Isolated data.");
            var samplesFile=Path.Combine(directory,"samples.json");var originals=File.Exists(samplesFile)?JsonSerializer.Deserialize<string[]>(File.ReadAllText(samplesFile))!:[];
            var sources=new List<string>();var hashes=new Dictionary<string,string>();
            foreach(var original in originals)
            {
                hashes[original]=await ArchiveService.HashAsync(original);var target=Path.Combine(directory,Path.GetFileName(original));File.Copy(original,target);sources.Add(target);
            }
            if(sources.Count==0)
            {
                sources.Add(await FixtureImageAsync(Path.Combine(directory,"portrait.jpg"),120,240));
                sources.Add(await FixtureImageAsync(Path.Combine(directory,"landscape.jpg"),240,120));
            }
            await VM.ImportAsync(sources,true,default);await VM.BackgroundImport;var group=VM.Groups[0];
            var legacy=Path.Combine(directory,"legacy-square-thumb.jpg");await File.WriteAllBytesAsync(legacy,await PhotoService.ConnectivityImageAsync());
            PhotoList.ItemsSource=null;group.Photos[0].ThumbnailPath=legacy;VM.Selected=group;RenderSelection();if(narrowLayout) PaneBox.SelectedIndex=1;
            for(var attempt=0;attempt<80 && !PhotoService.CurrentThumbnail(group.Photos[0].ThumbnailPath);attempt++) await Task.Delay(50);
            Check(PhotoService.CurrentThumbnail(group.Photos[0].ThumbnailPath),"Legacy cropped thumbnail is automatically replaced with a full-frame version.");
            foreach(var photo in group.Photos)
            {
                var thumb=await DimensionsAsync(photo.ThumbnailPath);var preview=await DimensionsAsync(await VM.Images.EnsurePreviewAsync(photo,default));
                Check(Math.Abs(thumb.W/(double)thumb.H-preview.W/(double)preview.H)<.02,"Thumbnail preserves preview aspect: "+photo.Name);
            }
            group.Photos[0].Crop=new(.2,.2,.5,.5);
            using var handler=new VisualImageHandler();using var http=new HttpClient(handler);var backend=new GeminiService(http,VM.Images,()=>"synthetic-key",()=>{});
            foreach(var mode in Enum.GetValues<RecognitionMode>())
            {
                handler.Images.Clear();await backend.RecognizeAsync(group.Photos.ToArray(),new() { Mode=mode },true,default);
                Check(handler.Images.Count==(mode==RecognitionMode.Accurate?2:1),"Every stage uses only cropped representative in "+mode);
                var cropped=await ImageDimensionsAsync(handler.Images[0]);var full=group.Photos[0].CopyReference(true,false);var uncropped=await ImageDimensionsAsync(await VM.Images.AnalysisJpegAsync(full,1280,default));
                Check(Math.Abs(cropped.W-uncropped.W*.5)<=1 && Math.Abs(cropped.H-uncropped.H*.5)<=1,"API payload contains actual cropped pixels in "+mode);
            }
            foreach(var source in hashes) Check(await ArchiveService.HashAsync(source.Key)==source.Value,"Sample original remains byte-identical: "+Path.GetFileName(source.Key));
            Check(!AnalysisBar.PrimaryCommands.OfType<AppBarButton>().Any(b=>b.Label=="上一组"),"Navigation remains separate from recognition actions.");
            Check(AnalysisBar.PrimaryCommands.OfType<AppBarButton>().Any(b=>b.Label=="联网识别") && AnalysisBar.PrimaryCommands.OfType<AppBarButton>().Any(b=>b.Label=="深入查证") && AnalysisBar.PrimaryCommands.OfType<AppBarButton>().Count(b=>b.Label=="取消本组")==1,"Recognition and verification use the central image toolbar.");
            ModeFlyout.ShowAt(ModeButton);await Task.Delay(100);ModeSlider.Value=3;Check(VM.State.Settings.Mode==RecognitionMode.Accurate,"Mode popup slider selects accurate mode.");
            ModeSlider.Value=0;Check(VM.State.Settings.Mode==RecognitionMode.FieldsOnly,"Mode popup slider selects fields-only mode.");ModeFlyout.Hide();
            VM.State.Settings.ArchiveRoot=Path.Combine(directory,"library");Directory.CreateDirectory(Path.Combine(VM.State.Settings.ArchiveRoot,"Mercedes-Benz","S-Class (W223)"));
            group.Confirmed=new() { Brand="Mercedes-Benz",Series="S-Class",Variant="S-Class",Generation="7th",Code="W223" };RenderSelection();
            foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark})
            {
                RequestedTheme=theme;if(narrowLayout) PaneBox.SelectedIndex=2;await Task.Delay(150);
                Check(Application.Current.Resources["TextControlBorderBrush"] is Microsoft.UI.Xaml.Media.SolidColorBrush,"Uniform input stroke in "+theme);Check(Application.Current.Resources["ButtonBorderBrush"] is Microsoft.UI.Xaml.Media.SolidColorBrush,"Uniform button stroke in "+theme);
                await CaptureVisualAsync(this,Path.Combine(directory,"review-"+theme+".png"));
            }
            RequestedTheme=ElementTheme.Light;
            if(narrowLayout) PaneBox.SelectedIndex=1;await Task.Delay(100);await CaptureVisualAsync(this,Path.Combine(directory,"photos-Light.png"));
            ModeFlyout.ShowAt(ModeButton);await Task.Delay(200);
            var modePopup=Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).FirstOrDefault(p=>p.Child is not null && VisualChild<Slider>(p.Child)==ModeSlider);
            if(ModeSlider.Parent is UIElement popupContent) await CaptureVisualAsync(popupContent,Path.Combine(directory,"mode-popup.png"));
            ModeFlyout.Hide();
            AddShortcut(Windows.System.VirtualKey.L,Windows.System.VirtualKeyModifiers.Control|Windows.System.VirtualKeyModifiers.Shift,(_,_)=>RequestedTheme=ElementTheme.Light);
            AddShortcut(Windows.System.VirtualKey.D,Windows.System.VirtualKeyModifiers.Control|Windows.System.VirtualKeyModifiers.Shift,(_,_)=>RequestedTheme=ElementTheme.Dark);
            AddShortcut(Windows.System.VirtualKey.P,Windows.System.VirtualKeyModifiers.Control|Windows.System.VirtualKeyModifiers.Shift,(_,_)=>PaneBox.SelectedIndex=1);
            AddShortcut(Windows.System.VirtualKey.E,Windows.System.VirtualKeyModifiers.Control|Windows.System.VirtualKeyModifiers.Shift,(_,_)=>PaneBox.SelectedIndex=2);
            VM.Status="";CropToggle.IsChecked=true;
            if(narrowLayout) PaneBox.SelectedIndex=1;await VM.FlushWritesAsync();
            File.WriteAllText(Path.Combine(directory,"visual-verification.json"),JsonSerializer.Serialize(new { Status="Passed",Checks=checks,RealApiCalls=0,Samples=originals.Length },new JsonSerializerOptions(StateStore.Json) { WriteIndented=true }));
            App.MainWindow!.Title="CarArchive 1.1.1 · 视觉验证";
        }
        catch(Exception error) { File.WriteAllText(Path.Combine(directory,"visual-verification.json"),JsonSerializer.Serialize(new { Status="Failed",Error=error.ToString(),Checks=checks },StateStore.Json));VM.Status=error.Message; }
    }
    private static async Task<(uint W,uint H)> DimensionsAsync(string path)
    { var file=await StorageFile.GetFileFromPathAsync(path);using var stream=await file.OpenReadAsync();var image=await BitmapDecoder.CreateAsync(stream);return (image.PixelWidth,image.PixelHeight); }
    private static async Task<(uint W,uint H)> ImageDimensionsAsync(byte[] data)
    {
        using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(data);await writer.StoreAsync(); }
        stream.Seek(0);var image=await BitmapDecoder.CreateAsync(stream);return (image.PixelWidth,image.PixelHeight);
    }
    private static async Task CaptureVisualAsync(UIElement element,string path)
    {
        // RenderTargetBitmap omits the window's Mica backdrop; use an opaque test backdrop.
        var theme=(element as FrameworkElement)?.ActualTheme ?? ElementTheme.Light;
        var backdrop=new Microsoft.UI.Xaml.Media.SolidColorBrush(theme==ElementTheme.Dark?Windows.UI.Color.FromArgb(255,32,32,32):Windows.UI.Color.FromArgb(255,244,247,250));
        Microsoft.UI.Xaml.Media.Brush? previous=null;
        if(element is Control control) { previous=control.Background;control.Background=backdrop; }
        else if(element is Panel panel) { previous=panel.Background;panel.Background=backdrop; }
        var image=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        try { await image.RenderAsync(element); }
        finally { if(element is Control c) c.Background=previous;else if(element is Panel p) p.Background=previous; }
        if(image.PixelWidth==0 || image.PixelHeight==0) return; // Native popup surfaces can be outside this renderer's visual root.
        var buffer=await image.GetPixelsAsync();
        using var pixels=DataReader.FromBuffer(buffer);var bytes=new byte[buffer.Length];pixels.ReadBytes(bytes);
        using var stream=new InMemoryRandomAccessStream();var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)image.PixelWidth,(uint)image.PixelHeight,96,96,bytes);await encoder.FlushAsync();
        stream.Seek(0);using var reader=new DataReader(stream.GetInputStreamAt(0));await reader.LoadAsync((uint)stream.Size);var output=new byte[(int)stream.Size];reader.ReadBytes(output);await File.WriteAllBytesAsync(path,output);
    }
    private sealed class VisualImageHandler : HttpMessageHandler
    {
        public List<byte[]> Images { get; }=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Images.AddRange(json.RootElement.GetProperty("input").EnumerateArray().Where(p=>p.GetProperty("type").GetString()=="image").Select(p=>Convert.FromBase64String(p.GetProperty("data").GetString()!)));
            return new(HttpStatusCode.OK) { Content=new StringContent("{\"output_text\":\"{\\\"brand\\\":\\\"Example\\\",\\\"variant\\\":\\\"Model\\\"}\"}") };
        }
    }
}
#endif
