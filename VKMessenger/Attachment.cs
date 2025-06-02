using System.Windows.Media.Imaging;
using System.IO;

namespace VKMessenger
{
    public class Attachment
    {
        public string FilePath { get; set; }
        public string FileName { get; set; } // Изменено на обычное свойство
        public string Type { get; set; }
        public string VKAttachmentString { get; set; }
        public string PreviewUrl { get; set; } // Добавлено
        
    }
}