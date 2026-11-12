using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

internal static class Launcher
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var program=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Program","CarArchive.exe");
            if(!File.Exists(program)) throw new FileNotFoundException("请先完整解压便携包，保留 Program 子目录。",program);
            var arguments=new StringBuilder();
            foreach(var arg in args) { if(arguments.Length>0) arguments.Append(' ');arguments.Append(Quote(arg)); }
            Process.Start(new ProcessStartInfo(program,arguments.ToString()) { WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory,UseShellExecute=true });
            return 0;
        }
        catch(Exception error) { MessageBox.Show(error.Message,"CarArchive",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1; }
    }
    private static string Quote(string text)
    {
        var result=new StringBuilder("\"");var slashes=0;
        foreach(var c in text)
        {
            if(c=='\\') { slashes++;continue; }
            result.Append('\\',c=='"'?slashes*2+1:slashes);result.Append(c);slashes=0;
        }
        result.Append('\\',slashes*2);return result.Append('"').ToString();
    }
}
