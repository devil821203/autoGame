using System.Runtime.InteropServices;

namespace Cultivation.Persistence;

// Small Windows-only adapter around the OS SQLite C API; never probes the working directory for DLLs.
internal sealed class NativeSqlite:IDisposable
{
    private IntPtr db;
    internal static string Version=>Marshal.PtrToStringUTF8(Api.sqlite3_libversion())!;
    internal NativeSqlite(string path,bool readOnly)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("R4 SQLite currently uses Windows system winsqlite3.dll; JSON remains available");
        int code=Api.sqlite3_open_v2(path,out db,(readOnly?1:2|4)|0x10000,IntPtr.Zero);
        if(code!=0){string message=Error(code);Dispose();throw new InvalidDataException(message);}
    }
    private string Error(int code)=>$"SQLite {code}: "+(db==IntPtr.Zero?"database handle unavailable":Marshal.PtrToStringUTF8(Api.sqlite3_errmsg(db)));
    internal void Check(int code){if(code!=0)throw new InvalidDataException(Error(code));}
    internal void Exec(string sql)
    {
        int code=Api.sqlite3_exec(db,sql,IntPtr.Zero,IntPtr.Zero,out var error);
        try{if(code!=0)throw new InvalidDataException(error==IntPtr.Zero?Error(code):Marshal.PtrToStringUTF8(error));}
        finally{if(error!=IntPtr.Zero)Api.sqlite3_free(error);}
    }
    internal Statement Prepare(string sql)=>new(this,sql);
    internal string Scalar(string sql){using var statement=Prepare(sql);if(!statement.Row())throw new InvalidDataException("SQLite metadata row missing");string value=statement.Text(0);if(statement.Row())throw new InvalidDataException("Unexpected duplicate SQLite metadata");return value;}
    public void Dispose(){if(db!=IntPtr.Zero){Api.sqlite3_close_v2(db);db=IntPtr.Zero;}}
    internal sealed class Statement:IDisposable
    {
        private readonly NativeSqlite owner;private IntPtr handle;
        internal Statement(NativeSqlite owner,string sql){this.owner=owner;int code=Api.sqlite3_prepare_v2(owner.db,sql,-1,out handle,IntPtr.Zero);if(code!=0){Dispose();owner.Check(code);}}
        internal void Bind(int index,string value)=>owner.Check(Api.sqlite3_bind_text(handle,index,value,-1,new IntPtr(-1)));
        internal bool Row(){int code=Api.sqlite3_step(handle);if(code==100)return true;if(code==101)return false;throw new InvalidDataException(owner.Error(code));}
        internal string Text(int index)=>Marshal.PtrToStringUTF8(Api.sqlite3_column_text(handle,index))??throw new InvalidDataException("Unexpected NULL in SQLite snapshot");
        public void Dispose(){if(handle!=IntPtr.Zero){Api.sqlite3_finalize(handle);handle=IntPtr.Zero;}}
    }
    private static class Api
    {
        private const string Library="winsqlite3.dll";
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)]string path,out IntPtr db,int flags,IntPtr vfs);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern int sqlite3_close_v2(IntPtr db);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern IntPtr sqlite3_libversion();
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern IntPtr sqlite3_errmsg(IntPtr db);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern int sqlite3_exec(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,IntPtr callback,IntPtr arg,out IntPtr error);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern void sqlite3_free(IntPtr memory);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern int sqlite3_prepare_v2(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,int count,out IntPtr statement,IntPtr tail);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern int sqlite3_bind_text(IntPtr statement,int index,[MarshalAs(UnmanagedType.LPUTF8Str)]string value,int count,IntPtr destructor);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern int sqlite3_step(IntPtr statement);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern IntPtr sqlite3_column_text(IntPtr statement,int index);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] internal static extern int sqlite3_finalize(IntPtr statement);
    }
}
