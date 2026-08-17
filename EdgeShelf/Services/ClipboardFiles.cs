using System.Collections.Specialized;
using System.IO;
using System.Windows;

namespace EdgeShelf.Services;

/// <summary>文件剪贴板：复制 / 剪切（带 Preferred DropEffect 移动标记，资源管理器可直接粘贴）。</summary>
public static class ClipboardFiles
{
    /// <summary>把文件 / 文件夹放入系统剪贴板。</summary>
    public static void Put(IEnumerable<string> paths, bool cut)
    {
        var data = new DataObject();
        var files = new StringCollection();
        foreach (var p in paths) files.Add(p);
        data.SetFileDropList(files);
        // Preferred DropEffect：2 = 移动（剪切），5 = 复制
        using var ms = new MemoryStream(4);
        ms.WriteByte((byte)(cut ? 2 : 5));
        ms.WriteByte(0); ms.WriteByte(0); ms.WriteByte(0);
        data.SetData("Preferred DropEffect", ms);
        Clipboard.SetDataObject(data, true);
    }

    /// <summary>读取剪贴板中的文件列表；无则返回 null。cut=true 表示剪切（移动）。</summary>
    public static (List<string> Paths, bool Cut)? TryGet()
    {
        try
        {
            if (!Clipboard.ContainsFileDropList()) return null;
            var paths = Clipboard.GetFileDropList().Cast<string>().ToList();
            if (paths.Count == 0) return null;
            bool cut = false;
            try
            {
                if (Clipboard.GetData("Preferred DropEffect") is MemoryStream ms && ms.Length >= 1)
                    cut = (ms.ToArray()[0] & 2) != 0;
            }
            catch { }
            return (paths, cut);
        }
        catch { return null; }
    }
}
