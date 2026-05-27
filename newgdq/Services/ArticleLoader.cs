using System;
using System.IO;
using System.Text;
using System.Windows;

namespace newgdq.Services
{
    /// <summary>
    /// 文章加载（目前只支持嵌入资源，后续 P4 加文件/剪贴板）。
    /// </summary>
    public static class ArticleLoader
    {
        public static string LoadInternal(string fileName)
        {
            var uri = new Uri($"pack://application:,,,/Resources/TXT/{fileName}", UriKind.Absolute);
            var info = Application.GetResourceStream(uri);
            if (info?.Stream == null) throw new FileNotFoundException(fileName);
            using (var sr = new StreamReader(info.Stream, Encoding.UTF8))
            {
                return sr.ReadToEnd();
            }
        }
    }
}
