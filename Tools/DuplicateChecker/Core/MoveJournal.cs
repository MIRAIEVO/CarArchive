using System.Text.Json;

namespace DuplicateChecker.Core;

// A record is durable before its caller changes the file system. Independent files
// share a flush, and a crash may leave one incomplete line at the end.
internal sealed class MoveJournal : IDisposable
{
    internal sealed record Entry(int Index,MoveItem Item);
    private sealed record Pending(Entry Entry,TaskCompletionSource Acknowledged);
    private const string Name="journal.jsonl";
    private readonly Lock gate=new();
    private readonly FileStream stream;
    private readonly List<Pending> pending=[];
    private Exception? failure;

    internal MoveJournal(string directory,long validLength)
    {
        var path=Path.Combine(directory,Name);
        Paths.NoLinks(path);
        stream=new FileStream(path,FileMode.OpenOrCreate,FileAccess.Write,FileShare.Read,64*1024);
        if(stream.Length!=validLength)stream.SetLength(validLength);
        stream.Position=validLength;
    }

    internal static long Replay(string directory,Action<Entry> apply)
    {
        var path=Path.Combine(directory,Name);
        Paths.NoLinks(path);
        if(!File.Exists(path))return 0;
        var data=File.ReadAllBytes(path).AsSpan();
        var offset=0;
        while(data[offset..].IndexOf((byte)'\n') is >=0 and var length)
        {
            var entry=JsonSerializer.Deserialize<Entry>(data.Slice(offset,length))
                ??throw new IOException("移动日志包含空记录。");
            apply(entry);
            offset+=length+1;
        }
        return offset;
    }

    internal static void Consolidate(string directory,IReadOnlyList<MoveItem> items)
    {
        var path=Path.Combine(directory,Name);
        var temporary=path+".new";
        Paths.NoLinks(path);Paths.NoLinks(temporary);
        try
        {
            using(var output=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None,64*1024))
            {
                for(var index=0;index<items.Count;index++)Write(output,new(index,items[index]));
                output.Flush(true);
            }
            File.Move(temporary,path,true);
        }
        finally{File.Delete(temporary);}
    }

    internal static void RemoveTemporary(string directory)
    {
        var path=Path.Combine(directory,Name+".new");
        Paths.NoLinks(path);
        File.Delete(path);
    }

    private static void Write(FileStream output,Entry entry)
    {
        output.Write(JsonSerializer.SerializeToUtf8Bytes(entry));
        output.WriteByte((byte)'\n');
    }

    internal Task SaveAsync(int index,MoveItem item)
    {
        lock(gate)
        {
            if(failure is not null)return Task.FromException(failure);
            var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Add(new(new(index,item),done));
            if(pending.Count==1)_=FlushAsync();
            return done.Task;
        }
    }

    private async Task FlushAsync()
    {
        await Task.Delay(1).ConfigureAwait(false);
        lock(gate)
        {
            try
            {
                foreach(var change in pending)Write(stream,change.Entry);
                stream.Flush(true);
                foreach(var change in pending)change.Acknowledged.SetResult();
            }
            catch(Exception error)
            {
                failure=error;
                foreach(var change in pending)change.Acknowledged.SetException(error);
            }
            finally {pending.Clear();}
        }
    }

    public void Dispose()=>stream.Dispose();
}
