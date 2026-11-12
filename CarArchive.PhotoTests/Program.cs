using CarArchive.Core;
using CarArchive.Services;
using System.Security.Cryptography;
using System.Text;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

var root=Path.Combine(Path.GetTempPath(),"CarArchive-image-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
void Check(bool value,string text) { if(!value) throw new Exception(text); }
async Task<byte[]> FixtureAsync(bool rotate)
{
    using var stream=new InMemoryRandomAccessStream();var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId,stream);var pixels=new byte[400*200*4];
    for(var y=0;y<200;y++) for(var x=0;x<400;x++) { var i=(y*400+x)*4;pixels[i]=(byte)(x<200?0:240);pixels[i+1]=10;pixels[i+2]=(byte)(x<200?240:0);pixels[i+3]=255; }
    encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,400,200,96,96,pixels);
    if(rotate) await encoder.BitmapProperties.SetPropertiesAsync(new BitmapPropertySet { ["/app1/ifd/{ushort=274}"]=new BitmapTypedValue((ushort)6,PropertyType.UInt16) });
    await encoder.FlushAsync();stream.Seek(0);using var reader=new DataReader(stream.GetInputStreamAt(0));await reader.LoadAsync((uint)stream.Size);var bytes=new byte[(int)stream.Size];reader.ReadBytes(bytes);return bytes;
}
async Task<(uint W,uint H,byte[] Pixels)> DecodeAsync(byte[] bytes)
{
    using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes);await writer.StoreAsync(); }
    stream.Seek(0);var decoder=await BitmapDecoder.CreateAsync(stream);var pixels=await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,new(),ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage);
    return (decoder.PixelWidth,decoder.PixelHeight,pixels.DetachPixelData());
}
var ordinary=Path.Combine(root,"ordinary.jpg");await File.WriteAllBytesAsync(ordinary,await FixtureAsync(false));
var oriented=Path.Combine(root,"oriented.jpg");await File.WriteAllBytesAsync(oriented,await FixtureAsync(true));
var service=new PhotoService(Path.Combine(root,"cache"));
var pair=PhotoService.Register(ordinary);var duplicate=pair.Reference.CopyReference();
var thumbs=await Task.WhenAll(service.EnsureThumbnailAsync(pair.Reference,default),service.EnsureThumbnailAsync(duplicate,default));
Check(thumbs[0]==thumbs[1] && Directory.GetFiles(Path.Combine(root,"cache")).Length==1 && Path.GetExtension(thumbs[0])==".jpg","Shared thumbnail requests did not coalesce or were not JPEG thumbnails.");Console.WriteLine("PASS shared JPEG thumbnail cache");
var thumbnail=await DecodeAsync(await File.ReadAllBytesAsync(thumbs[0]));Check(thumbnail.W<=256 && thumbnail.H<=256,"JPEG thumbnail was not regenerated from decoded source pixels.");Console.WriteLine("PASS JPEG thumbnail decoded from source pixels");
var legacyCache=Path.Combine(root,"legacy-cache");var legacyService=new PhotoService(legacyCache);var legacyPhoto=PhotoService.Register(ordinary).Reference;var info=new FileInfo(ordinary);
var oldKey=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{legacyPhoto.AssetId}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|preview-v3")));
var stalePreview=Path.Combine(legacyCache,oldKey+".png");await File.WriteAllBytesAsync(stalePreview,[1,2,3]);
var refreshedPreview=await legacyService.EnsurePreviewAsync(legacyPhoto,default);var refreshed=await DecodeAsync(await File.ReadAllBytesAsync(refreshedPreview));
Check(refreshedPreview!=stalePreview && refreshed.W==400 && refreshed.H==200 && Path.GetExtension(refreshedPreview)==".png","Legacy preview cache was reused or preview was not regenerated as PNG.");
Console.WriteLine("PASS legacy preview cache invalidated and lossless PNG preview generated");
var p=PhotoService.Register(oriented).Reference;var before=await ArchiveService.HashAsync(oriented);var image=await DecodeAsync(await service.AnalysisJpegAsync(p,2048,default));
Check(image.W==200 && image.H==400,$"Orientation dimensions failed: {image.W}x{image.H}");
var top=(50*(int)image.W+50)*4;var bottom=(350*(int)image.W+50)*4;
Check(image.Pixels[top+2]>180 && image.Pixels[top]<80 && image.Pixels[bottom]>180 && image.Pixels[bottom+2]<80,"Orientation pixels were scrambled or rotated incorrectly.");
var orientedPreviewPath=await service.EnsurePreviewAsync(p,default);var orientedPreview=await DecodeAsync(await File.ReadAllBytesAsync(orientedPreviewPath));
var previewTop=(50*(int)orientedPreview.W+50)*4;var previewBottom=(350*(int)orientedPreview.W+50)*4;
Check(orientedPreview.W==200 && orientedPreview.H==400 && orientedPreview.Pixels[previewTop+2]>180 && orientedPreview.Pixels[previewTop]<80 && orientedPreview.Pixels[previewBottom]>180 && orientedPreview.Pixels[previewBottom+2]<80,"Oriented preview pixels were scrambled or rotated incorrectly.");
Check(before==await ArchiveService.HashAsync(oriented),"Analysis changed original bytes.");Console.WriteLine("PASS oriented JPEG preview, analysis and original preservation");
var portraitThumbnail=await DecodeAsync(await File.ReadAllBytesAsync(await service.EnsureThumbnailAsync(p,default)));
Check(portraitThumbnail.W<portraitThumbnail.H && portraitThumbnail.W*2==portraitThumbnail.H,"Portrait thumbnail lost its complete oriented aspect ratio.");
var portraitTop=(20*(int)portraitThumbnail.W+20)*4;var portraitBottom=(((int)portraitThumbnail.H-20)*(int)portraitThumbnail.W+20)*4;
Check(portraitThumbnail.Pixels[portraitTop+2]>180 && portraitThumbnail.Pixels[portraitBottom]>180,"Portrait thumbnail discarded an image edge.");
Console.WriteLine("PASS complete portrait thumbnail orientation and edge pixels");
var poison=Path.Combine(root,"old-cropped-thumb.jpg");await File.WriteAllBytesAsync(poison,await PhotoService.ConnectivityImageAsync());p.ThumbnailPath=poison;
var rebuilt=await service.EnsureThumbnailAsync(p,default);Check(rebuilt!=poison && PhotoService.CurrentThumbnail(rebuilt),"Legacy cropped thumbnail was reused.");
Console.WriteLine("PASS legacy cropped thumbnail replaced by versioned full-frame cache");
pair.Reference.Crop=new(.25,0,.5,1);var cropped=await DecodeAsync(await service.AnalysisJpegAsync(pair.Reference,2048,default));Check(cropped.W==200 && cropped.H==200,"Normalized crop dimensions failed.");Console.WriteLine("PASS normalized crop");
var retryService=new PhotoService(Path.Combine(root,"retry-cache"));var retry=PhotoService.Register(ordinary).Reference;
using(var locked=new FileStream(ordinary,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) Check(await retryService.EnsurePreviewAsync(retry,default)=="","Expected locked-file preview failure.");
Check((await retryService.EnsurePreviewAsync(retry,default)).Length>0 && retry.Error.Length==0,"Failed preview task could not be retried.");Console.WriteLine("PASS preview retry after file lock");
var cached=await retryService.EnsurePreviewAsync(retry,default);File.Move(ordinary,Path.Combine(root,"relocated.jpg"));
Check(await retryService.EnsurePreviewAsync(retry,default)==cached && retry.Error.Contains("位置"),"Moving an original broke existing cached previews or mislabeled it as a codec failure.");Console.WriteLine("PASS cached history preview after original relocation");
Console.WriteLine("10/10 native image tests passed. "+root);
if(args.Length>0)
{
    var probe=new PhotoService(Path.Combine(root,"provided-image"));var photo=PhotoService.Register(args[0]).Reference;
    var thumbnailPath=await probe.EnsureThumbnailAsync(photo,default);var previewPath=await probe.EnsurePreviewAsync(photo,default);
    async Task<string> DimensionsAsync(string path)
    {
        var file=await Windows.Storage.StorageFile.GetFileFromPathAsync(path);using var input=await file.OpenReadAsync();var decoder=await BitmapDecoder.CreateAsync(input);
        return $"{decoder.PixelWidth}x{decoder.PixelHeight} oriented={decoder.OrientedPixelWidth}x{decoder.OrientedPixelHeight}";
    }
    Console.WriteLine($"SOURCE={photo.Path}");
    Console.WriteLine($"THUMB={thumbnailPath} {await DimensionsAsync(thumbnailPath)}");
    Console.WriteLine($"PREVIEW={previewPath} {await DimensionsAsync(previewPath)}");
    var sourceFile=await Windows.Storage.StorageFile.GetFileFromPathAsync(photo.Path);using var sourceStream=await sourceFile.OpenReadAsync();var sourceDecoder=await BitmapDecoder.CreateAsync(sourceStream);
    var rawTransform=new BitmapTransform { ScaledWidth=1024,ScaledHeight=768,InterpolationMode=BitmapInterpolationMode.Linear };
    var rawPixels=await sourceDecoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,rawTransform,ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.ColorManageToSRgb);
    using var rawImage=new InMemoryRandomAccessStream();var rawEncoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,rawImage);
    rawEncoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,1024,768,96,96,rawPixels.DetachPixelData());await rawEncoder.FlushAsync();rawImage.Seek(0);
    using(var reader=new DataReader(rawImage.GetInputStreamAt(0))) { await reader.LoadAsync((uint)rawImage.Size);var bytes=new byte[(int)rawImage.Size];reader.ReadBytes(bytes);var rawPath=Path.Combine(root,"provided-image","raw-ignore-orientation.png");await File.WriteAllBytesAsync(rawPath,bytes);Console.WriteLine($"RAW_IGNORE_ORIENTATION={rawPath}"); }
}
