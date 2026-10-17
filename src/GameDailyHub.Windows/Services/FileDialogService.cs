using System.Runtime.InteropServices;

namespace GameDailyHub.Windows.Services;

/// <summary>Classic native dialogs support elevated unpackaged WinUI windows.</summary>
public static class FileDialogService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class OpenFileName
    {
        public int Size = Marshal.SizeOf<OpenFileName>(); public IntPtr Owner, Instance;
        public string Filter = ""; public IntPtr CustomFilter; public int MaxCustomFilter, FilterIndex = 1;
        public IntPtr File; public int MaxFile; public IntPtr FileTitle; public int MaxFileTitle;
        public string? InitialDirectory; public string Title = "选择文件";
        public int Flags; public short FileOffset, FileExtension;
        public string? DefaultExtension; public IntPtr CustomData, Hook; public string? TemplateName;
        public IntPtr Reserved; public int Reserved2, FlagsEx;
    }
    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetOpenFileName([In, Out] OpenFileName info);
    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSaveFileName([In, Out] OpenFileName info);
    public static string? Choose(IntPtr hwnd, string filter, bool save = false)
    {
        var buffer = Marshal.AllocHGlobal(32768 * 2);
        try
        {
            Marshal.WriteInt16(buffer, 0);
            var info = new OpenFileName
            {
                Owner = hwnd,
                File = buffer,
                MaxFile = 32768,
                Filter = filter,
                Title = save ? "导出 GameDailyHub 配置" : "选择文件",
                Flags = 0x00080000 | 0x00000008 | (save ? 0x2 : 0x1000),
                DefaultExtension = save ? "json" : null
            };
            return (save ? GetSaveFileName(info) : GetOpenFileName(info)) ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
