using System;
using System.Runtime.InteropServices;

// เรียกหน้าต่าง "เลือกไฟล์" ของ Windows โดยตรง (comdlg32.dll)
// ใช้ได้ทั้งใน Editor (Windows) และตอน build เป็น .exe — ไม่ต้องลง plugin
public static class WindowsFileDialog
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class OpenFileName
    {
        public int structSize = 0;
        public IntPtr dlgOwner = IntPtr.Zero;
        public IntPtr instance = IntPtr.Zero;
        public string filter = null;
        public string customFilter = null;
        public int maxCustFilter = 0;
        public int filterIndex = 0;
        public string file = null;
        public int maxFile = 0;
        public string fileTitle = null;
        public int maxFileTitle = 0;
        public string initialDir = null;
        public string title = null;
        public int flags = 0;
        public short fileOffset = 0;
        public short fileExtension = 0;
        public string defExt = null;
        public IntPtr custData = IntPtr.Zero;
        public IntPtr hook = IntPtr.Zero;
        public string templateName = null;
        public IntPtr reservedPtr = IntPtr.Zero;
        public int reservedInt = 0;
        public int flagsEx = 0;
    }

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool GetOpenFileName([In, Out] OpenFileName ofn);

    // เปิด dialog ให้เลือกรูป คืน path เต็ม (คืน null ถ้ากดยกเลิก)
    public static string OpenImageFile(string title = "เลือกรูปโปรไฟล์")
    {
        OpenFileName ofn = new OpenFileName();
        ofn.structSize = Marshal.SizeOf(ofn);

        // รูปแบบ filter: "ชื่อ\0*.นามสกุล\0...\0\0"
        ofn.filter = "Image Files\0*.png;*.jpg;*.jpeg\0All Files\0*.*\0\0";

        ofn.file = new string(new char[512]);
        ofn.maxFile = ofn.file.Length;
        ofn.fileTitle = new string(new char[128]);
        ofn.maxFileTitle = ofn.fileTitle.Length;
        ofn.title = title;
        ofn.defExt = "png";

        // EXPLORER | FILEMUSTEXIST | PATHMUSTEXIST | NOCHANGEDIR
        // NOCHANGEDIR สำคัญ: กันไม่ให้ working directory ของเกมเปลี่ยน
        ofn.flags = 0x00081808;

        if (GetOpenFileName(ofn))
            return ofn.file;

        return null; // ผู้ใช้กดยกเลิก
    }

#else
    // แพลตฟอร์มอื่นยังไม่รองรับ
    public static string OpenImageFile(string title = "")
    {
        UnityEngine.Debug.LogWarning("WindowsFileDialog รองรับเฉพาะ Windows");
        return null;
    }
#endif
}
