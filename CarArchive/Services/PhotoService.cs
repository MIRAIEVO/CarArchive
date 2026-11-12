using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using CarArchive.Core;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace CarArchive.Services;

public sealed class PhotoService
    : IPhotoService
{
    public static bool CurrentThumbnail(string path)=>Path.GetFileName(path).EndsWith("-thumb-full-v2.jpg",StringComparison.OrdinalIgnoreCase);
    public void ClearCompletedRequests() { foreach(var entry in jobs) if(entry.Value.IsValueCreated && entry.Value.Value.IsCompleted) jobs.TryRemove(entry.Key,out _); }
    private readonly string cacheDirectory;
    private readonly SynchronizationContext? ui=SynchronizationContext.Current;
    private readonly SemaphoreSlim decoders=new(2,2);
    private readonly ConcurrentDictionary<string,Lazy<Task<string>>> jobs=new();
    public static readonly HashSet<string> Extensions=new(StringComparer.OrdinalIgnoreCase) { ".jpg",".jpeg",".png",".webp",".heic",".heif" };
    public PhotoService(string cacheDirectory) { this.cacheDirectory=cacheDirectory; Directory.CreateDirectory(cacheDirectory); }
    private void Publish(Action action) { if(ui is null || SynchronizationContext.Current==ui) action(); else ui.Post(_=>action(),null); }
    public static (ImageAsset Asset,Photo Reference) Register(string path)
    {
        path=Path.GetFullPath(path); var info=new FileInfo(path);
        var asset=new ImageAsset { OriginalPath=path,Length=info.Length,ModifiedTicks=info.LastWriteTimeUtc.Ticks };
        var photo=new Photo { Path=path,OriginalPath=path }; photo.Attach(asset); return (asset,photo);
    }
    public async Task<Photo> ImportAsync(string path,CancellationToken ct)
    {
        var pair=Register(path); await ReadMetadataAsync(pair.Reference,ct); return pair.Reference;
    }
    public async Task ReadMetadataAsync(Photo photo,CancellationToken ct)
    {
        try
        {
            var date=await Task.Run(async ()=> {
                await decoders.WaitAsync(ct);
                try {
                    var file=await StorageFile.GetFileFromPathAsync(photo.Path); using var stream=await file.OpenReadAsync(); var decoder=await BitmapDecoder.CreateAsync(stream);
                    foreach(var query in new[]{"/app1/ifd/exif/{ushort=36867}","/ifd/exif/{ushort=36867}"})
                    {
                        try {
                            var properties=await decoder.BitmapProperties.GetPropertiesAsync([query]);
                            if(properties.TryGetValue(query,out var value) && DateTime.TryParseExact(value.Value?.ToString()?.Trim('\0'),"yyyy:MM:dd HH:mm:ss",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))
                                return (DateTimeOffset?)new DateTimeOffset(DateTime.SpecifyKind(date,DateTimeKind.Unspecified),TimeZoneInfo.Local.GetUtcOffset(date));
                        } catch { }
                    }
                    return null;
                } finally { decoders.Release(); }
            },ct);
            Publish(()=> { photo.CapturedAt=date;photo.Error=""; if(photo.Asset is not null) { photo.Asset.CapturedAt=date; photo.Asset.Refresh(); } });
        }
        catch(OperationCanceledException) { throw; }
        catch(Exception e) { SetError(photo,e); }
    }
    private string Key(Photo photo,string kind)
    {
        var info=new FileInfo(photo.Path);
        var assetId=photo.AssetId.Length>0?photo.AssetId:photo.OriginalPath;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{assetId}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{kind}")));
    }
    public async Task<string> EnsureThumbnailAsync(Photo photo,CancellationToken ct)
    {
        string? key=null;
        try
        {
            key=Key(photo,"thumb-full-v2");
            var path=await jobs.GetOrAdd(key,_=>new(()=>Task.Run(async ()=> {
                await decoders.WaitAsync();
                try {
                    var target=Path.Combine(cacheDirectory,key+"-thumb-full-v2.jpg"); if(File.Exists(target)) return target;
                    var file=await StorageFile.GetFileFromPathAsync(photo.Path);
                    using var stream=await file.OpenReadAsync();var decoder=await BitmapDecoder.CreateAsync(stream);
                    // Always derive a complete oriented frame, never a cropped shell or EXIF thumbnail.
                    var data=await EncodeAsync(decoder,256,null);
                    await AtomicWriteAsync(target,data); return target;
                } finally { decoders.Release(); }
            }))).Value.WaitAsync(ct);
            Publish(()=> { photo.ThumbnailPath=path;photo.Error=""; if(photo.Asset is not null) photo.Asset.Refresh(); });
            return path;
        }
        catch(OperationCanceledException) { throw; }
        catch(Exception e) { if(key is not null) jobs.TryRemove(key,out _);SetError(photo,e); return ""; }
    }
    public async Task<string> EnsurePreviewAsync(Photo photo,CancellationToken ct)
    {
        string? key=null;
        try
        {
            ct.ThrowIfCancellationRequested();
            if(!File.Exists(photo.Path) && File.Exists(photo.PreviewPath) && (Path.GetExtension(photo.PreviewPath).Equals(".jpg",StringComparison.OrdinalIgnoreCase) || Path.GetFileName(photo.PreviewPath).EndsWith("-preview-v4.png",StringComparison.OrdinalIgnoreCase)))
            {
                Publish(()=> { photo.Error="原图位置已变化，正在显示已保存的缓存预览；分类记录保持不变。";if(photo.Asset is not null) photo.Asset.Refresh(); });
                return photo.PreviewPath;
            }
            key=Key(photo,"preview-v4");
            var path=await jobs.GetOrAdd(key,_=>new(()=>Task.Run(async ()=> {
                await decoders.WaitAsync();
                try {
                    var target=Path.Combine(cacheDirectory,key+"-preview-v4.png"); if(File.Exists(target)) return target;
                    var file=await StorageFile.GetFileFromPathAsync(photo.Path); using var stream=await file.OpenReadAsync(); var decoder=await BitmapDecoder.CreateAsync(stream);
                    await AtomicWriteAsync(target,await EncodeAsync(decoder,2048,null,BitmapEncoder.PngEncoderId)); return target;
                } finally { decoders.Release(); }
            }))).Value.WaitAsync(ct);
            Publish(()=> { photo.PreviewPath=path;photo.Error=""; if(photo.Asset is not null) photo.Asset.Refresh(); });
            return path;
        }
        catch(OperationCanceledException) { throw; }
        catch(Exception e) { if(key is not null) jobs.TryRemove(key,out _);SetError(photo,e); return ""; }
    }
    public async Task<byte[]> AnalysisJpegAsync(Photo photo,int longestEdge,CancellationToken ct)
    {
        // Encode directly at the requested size; do not build a 2048px preview for API input.
        ct.ThrowIfCancellationRequested();
        return await Task.Run(async ()=> {
            await decoders.WaitAsync(ct);
            try {
                var file=await StorageFile.GetFileFromPathAsync(photo.Path); using var stream=await file.OpenReadAsync(); var decoder=await BitmapDecoder.CreateAsync(stream);
                return await EncodeAsync(decoder,longestEdge,photo.Crop);
            } finally { decoders.Release(); }
        },ct);
    }
    private static async Task AtomicWriteAsync(string path,byte[] bytes)
    {
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { await File.WriteAllBytesAsync(temp,bytes); File.Move(temp,path,true); } finally { if(File.Exists(temp)) File.Delete(temp); }
    }
    private void SetError(Photo photo,Exception e)=>Publish(()=> { photo.Error=e is FileNotFoundException or DirectoryNotFoundException?"原图位置已变化或文件不存在；缓存及分类记录已保留。":$"无法解码 {Path.GetExtension(photo.Path)}。HEIC/HEIF 可能需要 Windows HEIF/HEVC 图像扩展。{e.GetType().Name}"; if(photo.Asset is not null) photo.Asset.Refresh(); });
    private static async Task<byte[]> EncodeAsync(BitmapDecoder decoder,int longestEdge,CropRect? crop,Guid? encoderId=null)
    {
        var orientation=await ReadOrientationAsync(decoder);
        var swapsAxes=orientation is 5 or 6 or 7 or 8;
        var orientedWidth=swapsAxes?decoder.PixelHeight:decoder.PixelWidth;
        var orientedHeight=swapsAxes?decoder.PixelWidth:decoder.PixelHeight;
        var ratio=Math.Min(1d,longestEdge/(double)Math.Max(orientedWidth,orientedHeight));
        var rawWidth=Math.Max(1u,(uint)Math.Round(decoder.PixelWidth*ratio));
        var rawHeight=Math.Max(1u,(uint)Math.Round(decoder.PixelHeight*ratio));
        var transform=new BitmapTransform { ScaledWidth=rawWidth,ScaledHeight=rawHeight,InterpolationMode=BitmapInterpolationMode.Linear };
        var decoded=await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,transform,ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.ColorManageToSRgb);
        var pixels=Orient(decoded.DetachPixelData(),rawWidth,rawHeight,orientation,out var width,out var height);
        if(crop is not null) pixels=Crop(pixels,ref width,ref height,crop);
        return await ImageAsync(pixels,width,height,encoderId??BitmapEncoder.JpegEncoderId);
    }
    private static async Task<ushort> ReadOrientationAsync(BitmapDecoder decoder)
    {
        foreach(var query in new[]{"/app1/ifd/{ushort=274}","/ifd/{ushort=274}"})
        {
            try
            {
                var properties=await decoder.BitmapProperties.GetPropertiesAsync([query]);
                if(properties.TryGetValue(query,out var value) && value.Value is ushort orientation && orientation is >=1 and <=8) return orientation;
            }
            catch(ArgumentException) { }
            catch(COMException) { }
        }
        return 1;
    }
    private static byte[] Orient(byte[] source,uint sourceWidth,uint sourceHeight,ushort orientation,out uint width,out uint height)
    {
        var swapsAxes=orientation is 5 or 6 or 7 or 8;
        width=swapsAxes?sourceHeight:sourceWidth;height=swapsAxes?sourceWidth:sourceHeight;
        if(orientation==1) return source;
        var result=new byte[checked((int)(width*height*4))];
        for(uint y=0;y<sourceHeight;y++)
        for(uint x=0;x<sourceWidth;x++)
        {
            var (dx,dy)=orientation switch
            {
                2=>(sourceWidth-1-x,y),
                3=>(sourceWidth-1-x,sourceHeight-1-y),
                4=>(x,sourceHeight-1-y),
                5=>(y,x),
                6=>(sourceHeight-1-y,x),
                7=>(sourceHeight-1-y,sourceWidth-1-x),
                8=>(y,sourceWidth-1-x),
                _=>(x,y)
            };
            System.Buffer.BlockCopy(source,checked((int)((y*sourceWidth+x)*4)),result,checked((int)((dy*width+dx)*4)),4);
        }
        return result;
    }
    private static byte[] Crop(byte[] source,ref uint width,ref uint height,CropRect crop)
    {
        var x=(uint)Math.Clamp(Math.Round(crop.X*width),0,width-1);
        var y=(uint)Math.Clamp(Math.Round(crop.Y*height),0,height-1);
        var cropWidth=(uint)Math.Clamp(Math.Round(crop.Width*width),1,width-x);
        var cropHeight=(uint)Math.Clamp(Math.Round(crop.Height*height),1,height-y);
        var result=new byte[checked((int)(cropWidth*cropHeight*4))];
        for(uint row=0;row<cropHeight;row++)
            System.Buffer.BlockCopy(source,checked((int)(((y+row)*width+x)*4)),result,checked((int)(row*cropWidth*4)),checked((int)(cropWidth*4)));
        width=cropWidth;height=cropHeight;return result;
    }
    private static async Task<byte[]> ImageAsync(byte[] pixels,uint width,uint height,Guid encoderId)
    {
        using var memory=new InMemoryRandomAccessStream(); var encoder=await BitmapEncoder.CreateAsync(encoderId,memory);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,width,height,96,96,pixels); await encoder.FlushAsync(); memory.Seek(0);
        using var reader=new DataReader(memory.GetInputStreamAt(0)); await reader.LoadAsync((uint)memory.Size); var bytes=new byte[(int)memory.Size]; reader.ReadBytes(bytes); return bytes;
    }
    public static Task<byte[]> ConnectivityImageAsync()=>ImageAsync(Enumerable.Range(0,64*64).SelectMany(_=>new byte[]{180,100,40,255}).ToArray(),64,64,BitmapEncoder.JpegEncoderId);
}
