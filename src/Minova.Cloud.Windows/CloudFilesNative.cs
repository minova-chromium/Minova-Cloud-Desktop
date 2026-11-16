using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace Minova.Cloud;

// ABI declarations follow Microsoft's cfapi.h; this client is built for x64 Windows.
internal static class CloudFilesNative {
    [StructLayout(LayoutKind.Sequential)] internal struct BasicInfo {public long Creation,Access,Write,Change; public uint Attributes;}
    [StructLayout(LayoutKind.Sequential)] internal struct Metadata {public BasicInfo Basic;public long Size;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct Placeholder {public string Name;public Metadata Metadata;public IntPtr Identity;public uint IdentityLength,Flags;public int Result;public long Usn;}
    [StructLayout(LayoutKind.Sequential)] internal struct Registration {public uint Type;public IntPtr Callback;}
    [StructLayout(LayoutKind.Sequential)] internal struct CallbackInfo {
        public uint Size;public long Connection;public IntPtr Context,VolumeGuid,VolumeDos;public uint VolumeSerial;
        public long RootId;public IntPtr RootIdentity;public uint RootIdentityLength;public long FileId,FileSize;
        public IntPtr Identity;public uint IdentityLength;public IntPtr NormalizedPath;public long Transfer;public byte Priority;
        public IntPtr Correlation,Process;public long Request;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct FetchParameters {public uint Size;public uint Padding;public uint Flags;public uint Padding2;public long Offset,Length,OptionalOffset,OptionalLength,LastDehydrate;public uint Reason;}
    [StructLayout(LayoutKind.Sequential)] internal struct Operation {public uint Size,Type;public long Connection,Transfer;public IntPtr Correlation,SyncStatus;public long Request;}
    // Only the TransferData arm is needed. ParamSize is the end of this arm (40 bytes).
    [StructLayout(LayoutKind.Sequential)] internal struct TransferParameters {public uint Size,Padding,Flags;public int Status;public IntPtr Buffer;public long Offset,Length;}
    [StructLayout(LayoutKind.Sequential)] internal struct AttributeTag {public uint Attributes,Tag;}
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] internal delegate void Callback(IntPtr info,IntPtr parameters);
    [DllImport("cldapi.dll",CharSet=CharSet.Unicode)] internal static extern int CfConnectSyncRoot(string root,[In] Registration[] callbacks,IntPtr context,uint flags,out long connection);
    [DllImport("cldapi.dll")] internal static extern int CfDisconnectSyncRoot(long connection);
    [DllImport("cldapi.dll",CharSet=CharSet.Unicode)] internal static extern int CfCreatePlaceholders(string root,[In,Out] Placeholder[] placeholders,uint count,uint flags,out uint processed);
    [DllImport("cldapi.dll")] internal static extern int CfExecute(in Operation operation,ref TransferParameters parameters);
    [DllImport("cldapi.dll",CharSet=CharSet.Unicode)] internal static extern int CfOpenFileWithOplock(string path,uint flags,out IntPtr handle);
    [DllImport("cldapi.dll")] internal static extern void CfCloseHandle(IntPtr handle);
    [DllImport("cldapi.dll")] internal static extern int CfUpdatePlaceholder(IntPtr handle,ref Metadata metadata,byte[] identity,uint identityLength,IntPtr ranges,uint rangeCount,uint flags,IntPtr usn,IntPtr overlapped);
    [DllImport("cldapi.dll",EntryPoint="CfUpdatePlaceholder")] internal static extern int CfUpdateIdentity(IntPtr handle,IntPtr metadata,byte[] identity,uint identityLength,IntPtr ranges,uint rangeCount,uint flags,IntPtr usn,IntPtr overlapped);
    [DllImport("cldapi.dll")] internal static extern int CfConvertToPlaceholder(IntPtr handle,byte[] identity,uint length,uint flags,IntPtr usn,IntPtr overlapped);
    [DllImport("cldapi.dll")] internal static extern int CfSetInSyncState(IntPtr handle,uint state,uint flags,IntPtr usn);
    [DllImport("cldapi.dll")] internal static extern int CfHydratePlaceholder(IntPtr handle,long offset,long length,uint flags,IntPtr overlapped);
    [DllImport("cldapi.dll")] internal static extern int CfSetPinState(IntPtr handle,uint state,uint flags,IntPtr overlapped);
    [DllImport("cldapi.dll")] internal static extern int CfDehydratePlaceholder(IntPtr handle,long offset,long length,uint flags,IntPtr overlapped);
    [DllImport("cldapi.dll")] internal static extern int CfGetPlaceholderInfo(IntPtr handle,uint infoClass,byte[] buffer,uint length,out uint returned);
    [DllImport("cldapi.dll")] internal static extern uint CfGetPlaceholderStateFromAttributeTag(uint attributes,uint tag);
    [DllImport("cldapi.dll")] internal static extern int CfUpdateSyncProviderStatus(long connection,uint status);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFileW(string path,uint access,uint sharing,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle file,int type,out AttributeTag info,uint size);
    public static void Check(int result){if(result<0)Marshal.ThrowExceptionForHR(result);}
    public static uint State(string path) {
        using var file=CreateFileW(path,0x80,7,IntPtr.Zero,3,0x02200000,IntPtr.Zero);
        if(file.IsInvalid)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if(!GetFileInformationByHandleEx(file,9,out var tag,8))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var state=CfGetPlaceholderStateFromAttributeTag(tag.Attributes,tag.Tag);return state==uint.MaxValue?0:state;
    }
    public sealed class Handle : IDisposable {
        public IntPtr Value {get;}
        public Handle(string path,uint flags=0){Check(CfOpenFileWithOplock(path,flags,out var value));Value=value;}
        public void Dispose()=>CfCloseHandle(Value);
    }
}
