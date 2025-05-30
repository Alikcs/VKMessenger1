using System.Windows.Media.Imaging;

namespace VKMessenger
{
    public class Attachment
    {
        public string FilePath { get; set; }
        public string FileName => System.IO.Path.GetFileName(FilePath);
        public string Type { get; set; }
        public string VKAttachmentString { get; set; }
    }
}