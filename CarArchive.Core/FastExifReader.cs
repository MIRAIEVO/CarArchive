using System.Buffers.Binary;
using System.Globalization;
using System.Text;
namespace CarArchive.Core;
/// <summary>Reads metadata containers without decoding image pixels. Never writes original files.</summary>
public static class FastExifReader
{
 public static bool TryRead(string path,out DateTimeOffset? date)
 {
  date=null;
  try {
   using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,4096,FileOptions.RandomAccess);
   Span<byte> head=stackalloc byte[12];if(file.Read(head)<8)return false;
   if(head[0]==255&&head[1]==216) {
    file.Position=2;
    for(var i=0;i<512&&file.Position<file.Length;i++) {
     if(file.ReadByte()!=255)return true;int marker;do{marker=file.ReadByte();}while(marker==255);
     if(marker is 0xda or 0xd9 or -1)return true;if(marker is 0x01 or >=0xd0 and <=0xd7)continue;
     var size=Read16(file,file.Position,false);if(size<2)return true;var end=file.Position+size-2;
     if(end>file.Length)return true;
     if(marker==0xe1) {var bytes=new byte[size-2];file.ReadExactly(bytes);if(bytes.AsSpan().StartsWith("Exif\0\0"u8)){using var data=new MemoryStream(bytes,6,bytes.Length-6,false);date=ReadTiff(data);return true;}}
     file.Position=end;
    }return true;
   }
   if(head[0]==(byte)'I'&&head[1]==(byte)'I'||head[0]==(byte)'M'&&head[1]==(byte)'M') {file.Position=0;date=ReadTiff(file);return true;}
   if(head[4..8].SequenceEqual("ftyp"u8)&&TryReadHeif(file,out date))return true;
   var png=head[..8].SequenceEqual(new byte[]{137,80,78,71,13,10,26,10});var webp=head[..4].SequenceEqual("RIFF"u8)&&head[8..12].SequenceEqual("WEBP"u8);
   if(!png&&!webp)return false;file.Position=png?8:12;
   Span<byte> chunk=stackalloc byte[8];
   for(var i=0;i<4096&&file.Position+8<=file.Length;i++) {
    file.ReadExactly(chunk);var length=png?BinaryPrimitives.ReadUInt32BigEndian(chunk[..4]):BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
    var exif=png?chunk[4..].SequenceEqual("eXIf"u8):chunk[..4].SequenceEqual("EXIF"u8);
    if(length>file.Length-file.Position)return true;
    if(exif&&length<=1024*1024){var bytes=new byte[(int)length];file.ReadExactly(bytes);var start=bytes.AsSpan().StartsWith("Exif\0\0"u8)?6:0;using var data=new MemoryStream(bytes,start,bytes.Length-start,false);date=ReadTiff(data);return true;}
    file.Seek(length+(png?4:length%2),SeekOrigin.Current);
   }return true;
  }catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}catch(ArgumentException){return false;}
 }
 private readonly record struct Box(string Type,long Data,long End);
 private static IEnumerable<Box> Boxes(Stream s,long start,long end)
 {
  for(var i=0;i<4096&&start+8<=end;i++) {var size=(long)Read32(s,start,false);var header=8;Span<byte> type=stackalloc byte[4];s.Position=start+4;s.ReadExactly(type);var name=Encoding.ASCII.GetString(type);
   if(size==1){Span<byte> big=stackalloc byte[8];s.ReadExactly(big);var large=BinaryPrimitives.ReadUInt64BigEndian(big);if(large>long.MaxValue)yield break;size=(long)large;header=16;}
   if(size==0)size=end-start;if(size<header||size>end-start)yield break;yield return new(name,start+header,start+size);start+=size;
  }
 }
 private static ulong Variable(Stream s,int size){if(size>8)throw new InvalidDataException();ulong result=0;for(var i=0;i<size;i++){var b=s.ReadByte();if(b<0)throw new EndOfStreamException();result=(result<<8)|(uint)b;}return result;}
 private static bool TryReadHeif(Stream s,out DateTimeOffset? date)
 {
  date=null;var meta=Boxes(s,0,s.Length).FirstOrDefault(b=>b.Type=="meta");if(meta.End==0||meta.End-meta.Data<4)return false;
  var children=Boxes(s,meta.Data+4,meta.End).ToArray();var info=children.FirstOrDefault(b=>b.Type=="iinf");var location=children.FirstOrDefault(b=>b.Type=="iloc");if(info.End==0||location.End==0)return false;
  s.Position=info.Data;var infoVersion=s.ReadByte();var entries=Boxes(s,info.Data+4+(infoVersion==0?2:4),info.End);var ids=new HashSet<uint>();
  foreach(var box in entries.Where(b=>b.Type=="infe")){s.Position=box.Data;var version=s.ReadByte();if(version is not(2 or 3))continue;var id=version==2?Read16(s,box.Data+4,false):Read32(s,box.Data+4,false);var typeAt=box.Data+4+(version==2?2:4)+2;var kind=new byte[4];s.Position=typeAt;s.ReadExactly(kind);if(kind.AsSpan().SequenceEqual("Exif"u8))ids.Add(id);}
  if(ids.Count==0)return true;
  s.Position=location.Data;var locVersion=s.ReadByte();s.Position=location.Data+4;var sizes=s.ReadByte();var baseAndIndex=s.ReadByte();var offsetSize=sizes>>4;var lengthSize=sizes&15;var baseSize=baseAndIndex>>4;var indexSize=locVersion is 1 or 2?baseAndIndex&15:0;
  var items=locVersion<2?Read16(s,s.Position,false):Read32(s,s.Position,false);if(items>4096)return false;var idat=children.FirstOrDefault(b=>b.Type=="idat");
  for(var i=0;i<items;i++) {
   var id=locVersion<2?Read16(s,s.Position,false):Read32(s,s.Position,false);var method=locVersion is 1 or 2?Read16(s,s.Position,false)&15:0;var reference=Read16(s,s.Position,false);var baseOffset=Variable(s,baseSize);var count=Read16(s,s.Position,false);if(count>4096)return false;
   var ranges=new List<(ulong Offset,ulong Length)>();for(var e=0;e<count;e++){Variable(s,indexSize);ranges.Add((Variable(s,offsetSize),Variable(s,lengthSize)));}
   var resume=s.Position;if(!ids.Contains(id)||reference!=0||method>1)continue;
   using var data=new MemoryStream();foreach(var range in ranges){var offset=checked(baseOffset+range.Offset+(method==1?(ulong)idat.Data:0));var length=range.Length;if(offset>(ulong)s.Length||length>1024*1024||length>(ulong)s.Length-offset)return false;if(data.Length+(long)length>1024*1024)return false;s.Position=(long)offset;var bytes=new byte[(int)length];s.ReadExactly(bytes);data.Write(bytes);}
   if(data.Length>=8){data.Position=0;var tiff=Read32(data,0,false)+4L;if(tiff<data.Length){data.Position=tiff;date=ReadTiff(data);if(date.HasValue)return true;}}
   s.Position=resume;
  }return true;
 }
 private static ushort Read16(Stream s,long position,bool little){Span<byte>b=stackalloc byte[2];s.Position=position;s.ReadExactly(b);return little?BinaryPrimitives.ReadUInt16LittleEndian(b):BinaryPrimitives.ReadUInt16BigEndian(b);}
 private static uint Read32(Stream s,long position,bool little){Span<byte>b=stackalloc byte[4];s.Position=position;s.ReadExactly(b);return little?BinaryPrimitives.ReadUInt32LittleEndian(b):BinaryPrimitives.ReadUInt32BigEndian(b);}
 private static DateTimeOffset? ReadTiff(Stream s)
 {
  var origin=s.Position;var little=s.ReadByte()=='I';s.Position=origin;if(Read16(s,origin,little)!=(little?0x4949:0x4d4d)||Read16(s,origin+2,little)!=42)return null;
  string? original=null,fallback=null,offset=null;var queue=new Queue<uint>();queue.Enqueue(Read32(s,origin+4,little));var seen=new HashSet<uint>();
  while(queue.Count>0&&seen.Count<8){var pointer=queue.Dequeue();if(pointer==0||!seen.Add(pointer)||origin+pointer+2>s.Length)continue;var at=origin+pointer;var count=Math.Min((int)Read16(s,at,little),4096);
   for(var i=0;i<count;i++){var entry=at+2+i*12;if(entry+12>s.Length)break;var tag=Read16(s,entry,little);if(tag==34665){queue.Enqueue(Read32(s,entry+8,little));continue;}if(tag is not(306 or 36867 or 36868 or 36880 or 36881)||Read16(s,entry+2,little)!=2)continue;
    var size=Read32(s,entry+4,little);if(size==0||size>128)continue;var start=size<=4?entry+8:origin+Read32(s,entry+8,little);if(start<origin||start+size>s.Length)continue;
    var bytes=new byte[(int)size];s.Position=start;s.ReadExactly(bytes);var text=Encoding.ASCII.GetString(bytes).Trim('\0',' ');
    if(tag==36867)original=text;else if(tag is 306 or 36868)fallback??=text;else if(tag==36881||offset is null)offset=text;
   }
  }
  var value=original??fallback;if(value is null)return null;
  if(offset is not null&&DateTimeOffset.TryParseExact(value+" "+offset,"yyyy:MM:dd HH:mm:ss zzz",CultureInfo.InvariantCulture,DateTimeStyles.None,out var zoned))return zoned;
  return DateTime.TryParseExact(value,"yyyy:MM:dd HH:mm:ss",CultureInfo.InvariantCulture,DateTimeStyles.None,out var time)?new DateTimeOffset(DateTime.SpecifyKind(time,DateTimeKind.Unspecified),TimeZoneInfo.Local.GetUtcOffset(time)):null;
 }
}
