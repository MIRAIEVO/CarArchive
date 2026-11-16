using System.Runtime.InteropServices;
using CarArchive.Core;

namespace CarArchive.Services;

/// <summary>Shell recycle-only operation on an STA worker. No permanent-delete fallback exists.</summary>
public sealed class WindowsPhotoRecycler : IPhotoRecycler
{
    public Task<string> RecycleAsync(PhotoFileEntry entry,CancellationToken cancellationToken)
    {
        if(!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("回收站操作仅支持 Windows。");
        cancellationToken.ThrowIfCancellationRequested();
        var completion=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>
        {
            try { completion.TrySetResult(Recycle(entry,cancellationToken)); }
            catch(OperationCanceledException) { completion.TrySetCanceled(cancellationToken); }
            catch(Exception error) { completion.TrySetException(error); }
        }) { IsBackground=true,Name="CarArchive Recycle" };
        thread.SetApartmentState(ApartmentState.STA);thread.Start();return completion.Task;
    }
    private static string Recycle(PhotoFileEntry entry,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();PhotoLifecycleService.ValidateUnchanged(entry);
        var path=Path.GetFullPath(entry.Source);
        if(path.StartsWith(@"\\",StringComparison.Ordinal)) throw new PhotoRecycleRejectedException("网络位置不能保证移入回收站，未删除照片。");
        var root=Path.GetPathRoot(path)!;var drive=new DriveInfo(root);
        if(!drive.IsReady || drive.DriveType!=DriveType.Fixed || drive.DriveFormat is not ("NTFS" or "ReFS"))
            throw new PhotoRecycleRejectedException("该磁盘不支持可验证的 Windows 回收站，未删除照片。");
        for(var parent=Path.GetDirectoryName(path);parent is not null;parent=Path.GetDirectoryName(parent))
            if(File.GetAttributes(parent).HasFlag(FileAttributes.ReparsePoint)) throw new PhotoRecycleRejectedException("不通过符号链接或联接回收照片。");
        var info=new RecycleBinInfo { Size=(uint)Marshal.SizeOf<RecycleBinInfo>() };
        if(SHQueryRecycleBin(root,ref info)<0) throw new PhotoRecycleRejectedException("无法使用该磁盘的 Windows 回收站，未删除照片。");
        var operation=(IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"),true)!)!;
        IShellItem? item=null;var sink=new RecycleSink(entry,ct);uint cookie=0;
        try
        {
            var iid=typeof(IShellItem).GUID;Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path,IntPtr.Zero,ref iid,out item));
            // FOFX_RECYCLEONDELETE sends the item to Recycle Bin rather than permanently deleting.
            // https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags
            const uint flags=0x00080000 /*RECYCLEONDELETE*/ | 0x20000000 /*ADDUNDORECORD*/ | 0x00100000 /*EARLYFAILURE*/ | 0x0400 /*NOERRORUI*/ | 0x0004 /*SILENT*/ | 0x0010 /*NOCONFIRMATION*/;
            Marshal.ThrowExceptionForHR(operation.SetOperationFlags(flags));
            Marshal.ThrowExceptionForHR(operation.Advise(sink,out cookie));
            Marshal.ThrowExceptionForHR(operation.DeleteItem(item!,null));
            var result=operation.PerformOperations();
            operation.GetAnyOperationsAborted(out var aborted);
            // A positive, non-null recycled shell item is the receipt. Never infer success from absence.
            // https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperationprogresssink-postdeleteitem
            if(sink.Identity.Length>0 && sink.DeleteResult>=0) return sink.Identity;
            ct.ThrowIfCancellationRequested();
            if(result<0) Marshal.ThrowExceptionForHR(result);
            if(sink.DeleteResult<0) Marshal.ThrowExceptionForHR(sink.DeleteResult);
            throw new IOException(aborted?"Windows 已中止回收照片，文件引用已保留。":"Windows 未确认照片已进入回收站，请检查照片操作记录。");
        }
        finally
        {
            if(cookie!=0) operation.Unadvise(cookie);
            if(item is not null) Marshal.FinalReleaseComObject(item);
            Marshal.FinalReleaseComObject(operation);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct RecycleBinInfo { public uint Size;public long Bytes;public long Items; }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern int SHQueryRecycleBin(string root,ref RecycleBinInfo info);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=true)] private static extern int SHCreateItemFromParsingName(string path,IntPtr bindContext,ref Guid iid,[MarshalAs(UnmanagedType.Interface)] out IShellItem? item);

    [ComVisible(true),ClassInterface(ClassInterfaceType.None)]
    private sealed class RecycleSink(PhotoFileEntry entry,CancellationToken ct) : IFileOperationProgressSink
    {
        public string Identity { get; private set; }="";
        public int DeleteResult { get; private set; }=unchecked((int)0x80004005);
        private static int Abort=>unchecked((int)0x80004004);
        public int StartOperations()=>ct.IsCancellationRequested?Abort:0;
        public int FinishOperations(int result)=>0;
        public int PreRenameItem(uint f,IShellItem i,string n)=>Abort;
        public int PostRenameItem(uint f,IShellItem i,string n,int h,IShellItem? c)=>0;
        public int PreMoveItem(uint f,IShellItem i,IShellItem d,string n)=>Abort;
        public int PostMoveItem(uint f,IShellItem i,IShellItem d,string n,int h,IShellItem? c)=>0;
        public int PreCopyItem(uint f,IShellItem i,IShellItem d,string n)=>Abort;
        public int PostCopyItem(uint f,IShellItem i,IShellItem d,string n,int h,IShellItem? c)=>0;
        public int PreDeleteItem(uint flags,IShellItem item)
        {
            if(ct.IsCancellationRequested || (flags&0x80)==0) return Abort; // TSF_DELETE_RECYCLE_IF_POSSIBLE is required.
            try { PhotoLifecycleService.ValidateUnchanged(entry);return 0; }catch { return Abort; }
        }
        public int PostDeleteItem(uint flags,IShellItem item,int result,IShellItem? recycled)
        {
            DeleteResult=result;
            if(result<0 || recycled is null) return result<0?result:Abort;
            var displayResult=recycled.GetDisplayName(0x80028000,out var pointer); // SIGDN_DESKTOPABSOLUTEPARSING
            if(displayResult<0) return displayResult;
            try { Identity=Marshal.PtrToStringUni(pointer) ?? ""; }finally { Marshal.FreeCoTaskMem(pointer); }
            return Identity.Length>0?0:Abort;
        }
        public int PreNewItem(uint f,IShellItem d,string n)=>Abort;
        public int PostNewItem(uint f,IShellItem d,string n,string t,uint a,int h,IShellItem? c)=>0;
        public int UpdateProgress(uint total,uint completed)=>ct.IsCancellationRequested?Abort:0;
        public int ResetTimer()=>0;public int PauseTimer()=>0;public int ResumeTimer()=>0;
    }
    [ComImport,Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr context,ref Guid handler,ref Guid iid,out IntPtr result);
        [PreserveSig] int GetParent(out IShellItem parent);
        [PreserveSig] int GetDisplayName(uint name,out IntPtr text);
        [PreserveSig] int GetAttributes(uint mask,out uint attributes);
        [PreserveSig] int Compare(IShellItem item,uint hint,out int order);
    }
    [ComImport,Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        [PreserveSig] int Advise(IFileOperationProgressSink sink,out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOperationFlags(uint flags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        [PreserveSig] int SetProgressDialog(IntPtr dialog);
        [PreserveSig] int SetProperties(IntPtr properties);
        [PreserveSig] int SetOwnerWindow(IntPtr window);
        [PreserveSig] int ApplyPropertiesToItem(IShellItem item);
        [PreserveSig] int ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object items);
        [PreserveSig] int RenameItem(IShellItem item,[MarshalAs(UnmanagedType.LPWStr)] string name,IFileOperationProgressSink? sink);
        [PreserveSig] int RenameItems([MarshalAs(UnmanagedType.IUnknown)] object items,[MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int MoveItem(IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)] string? name,IFileOperationProgressSink? sink);
        [PreserveSig] int MoveItems([MarshalAs(UnmanagedType.IUnknown)] object items,IShellItem destination);
        [PreserveSig] int CopyItem(IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)] string? name,IFileOperationProgressSink? sink);
        [PreserveSig] int CopyItems([MarshalAs(UnmanagedType.IUnknown)] object items,IShellItem destination);
        [PreserveSig] int DeleteItem(IShellItem item,IFileOperationProgressSink? sink);
        [PreserveSig] int DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object items);
        [PreserveSig] int NewItem(IShellItem destination,uint attributes,[MarshalAs(UnmanagedType.LPWStr)] string name,[MarshalAs(UnmanagedType.LPWStr)] string? template,IFileOperationProgressSink? sink);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
    [ComVisible(true),Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();[PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags,IShellItem item,[MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostRenameItem(uint flags,IShellItem item,[MarshalAs(UnmanagedType.LPWStr)] string name,int result,IShellItem? created);
        [PreserveSig] int PreMoveItem(uint flags,IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostMoveItem(uint flags,IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)] string name,int result,IShellItem? created);
        [PreserveSig] int PreCopyItem(uint flags,IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostCopyItem(uint flags,IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)] string name,int result,IShellItem? created);
        [PreserveSig] int PreDeleteItem(uint flags,IShellItem item);
        [PreserveSig] int PostDeleteItem(uint flags,IShellItem item,int result,IShellItem? created);
        [PreserveSig] int PreNewItem(uint flags,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostNewItem(uint flags,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)] string name,[MarshalAs(UnmanagedType.LPWStr)] string template,uint attributes,int result,IShellItem? created);
        [PreserveSig] int UpdateProgress(uint total,uint completed);[PreserveSig] int ResetTimer();[PreserveSig] int PauseTimer();[PreserveSig] int ResumeTimer();
    }
}
