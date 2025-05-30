using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace VKMessenger
{
    public partial class MainWindow : Window
    {
        public class Attachment
        {
            public string FilePath { get; set; }
            public string FileName => Path.GetFileName(FilePath);
            public string Type { get; set; }
            public string VKAttachmentString { get; set; }
        }

        public class Message
        {
            public string Id { get; set; }
            public string Sender { get; set; }
            public string Text { get; set; }
            public string Date { get; set; }
            public string SenderId { get; set; }
        }

        public ObservableCollection<Message> Messages { get; } = new ObservableCollection<Message>();
        public ObservableCollection<Attachment> Attachments { get; } = new ObservableCollection<Attachment>();

        private string _currentUserId = "";

        public MainWindow()
        {
            InitializeComponent();
            MessagesList.ItemsSource = Messages;
            AttachmentsList.ItemsSource = Attachments;

            // Подписываемся на изменение коллекции сообщений
            Messages.CollectionChanged += Messages_CollectionChanged;
        }

        private async void LoadHistory_Click(object sender, RoutedEventArgs e)
        {
            await LoadHistory();
        }

        private async void RefreshHistory_Click(object sender, RoutedEventArgs e)
        {
            await LoadHistory();
        }

        private void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            Messages.Clear();
        }

        private async Task LoadHistory()
        {
            string token = TokenBox.Password;
            string recipient = RecipientBox.Text;

            if (string.IsNullOrWhiteSpace(token))
            {
                MessageBox.Show("Введите токен доступа!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(recipient))
            {
                MessageBox.Show("Введите ID получателя!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                // Получаем ID текущего пользователя
                if (string.IsNullOrEmpty(_currentUserId))
                {
                    _currentUserId = await GetCurrentUserId(token);
                }

                var messages = await GetMessagesHistory(token, recipient);

                // Очищаем и добавляем сообщения в прямом порядке (новые сверху)
                Messages.Clear();
                foreach (var msg in messages)
                {
                    Messages.Add(msg);
                }

                // Прокрутка к самому новому сообщению (первому в списке)
                if (Messages.Count > 0)
                {
                    MessagesList.ScrollIntoView(Messages[0]);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки истории: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Получение ID текущего пользователя
        private async Task<string> GetCurrentUserId(string token)
        {
            using (HttpClient client = new HttpClient())
            {
                var response = await client.GetAsync(
                    $"https://api.vk.com/method/users.get?access_token={token}&v=5.199");

                string content = await response.Content.ReadAsStringAsync();
                JObject json = JObject.Parse(content);

                if (json["response"] == null)
                    throw new Exception("Не удалось получить ID пользователя: " + content);

                return json["response"][0]["id"].ToString();
            }
        }

        private async Task<List<Message>> GetMessagesHistory(string token, string peerId)
        {
            using (HttpClient client = new HttpClient())
            {
                var response = await client.GetAsync(
                    $"https://api.vk.com/method/messages.getHistory?" +
                    $"access_token={token}&v=5.199&peer_id={peerId}&count=50");

                string content = await response.Content.ReadAsStringAsync();
                JObject json = JObject.Parse(content);

                if (json["error"] != null)
                    throw new Exception(json["error"]["error_msg"].ToString());

                var messages = new List<Message>();
                var profiles = new Dictionary<string, string>();

                // Сбор информации о пользователях
                if (json["response"]["profiles"] != null)
                {
                    foreach (var profile in json["response"]["profiles"])
                    {
                        string userId = profile["id"].ToString();
                        string name = $"{profile["first_name"]} {profile["last_name"]}";
                        profiles[userId] = name;
                    }
                }

                // Обработка сообщений
                foreach (var item in json["response"]["items"])
                {
                    string fromId = item["from_id"]?.ToString() ?? "";
                    string senderName;

                    // Определяем отправителя
                    if (fromId == _currentUserId)
                    {
                        senderName = "Я";
                    }
                    else if (profiles.ContainsKey(fromId))
                    {
                        senderName = profiles[fromId];
                    }
                    else
                    {
                        senderName = $"Пользователь {fromId}";
                    }

                    // Конвертация даты из Unix timestamp
                    DateTime date = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                        .AddSeconds(item["date"].ToObject<long>())
                        .ToLocalTime();

                    messages.Add(new Message
                    {
                        Id = item["id"].ToString(),
                        Sender = senderName,
                        Text = item["text"]?.ToString(),
                        Date = date.ToString("g"),
                        SenderId = fromId
                    });
                }

                return messages;
            }
        }
        private void Messages_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                // Прокрутка к новому сообщению при добавлении
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (Messages.Count > 0)
                    {
                        MessagesList.ScrollIntoView(Messages[0]);
                    }
                }));
            }
        }

        private void AddAttachment_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Все файлы|*.*",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (string filePath in openFileDialog.FileNames)
                {
                    var fileInfo = new FileInfo(filePath);

                    // Проверка размера файла
                    if (fileInfo.Length > 50 * 1024 * 1024)
                    {
                        MessageBox.Show($"Файл {fileInfo.Name} слишком большой (максимум 50 МБ)", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                        continue;
                    }

                    var attachment = new Attachment
                    {
                        FilePath = filePath,
                        Type = GetAttachmentType(filePath)
                    };
                    Attachments.Add(attachment);
                }
            }
        }

        private string GetAttachmentType(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower();

            if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".gif")
                return "photo";

            if (ext == ".mp3" || ext == ".wav" || ext == ".ogg")
                return "audio";

            return "doc";
        }

        private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is Attachment attachment)
            {
                Attachments.Remove(attachment);
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            string token = TokenBox.Password;
            string recipient = RecipientBox.Text;
            string message = MessageInputBox.Text;

            // Проверка токена
            if (string.IsNullOrWhiteSpace(token))
            {
                MessageBox.Show("Введите токен доступа!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                TokenBox.Focus();
                return;
            }

            // Проверка получателя
            if (string.IsNullOrWhiteSpace(recipient))
            {
                MessageBox.Show("Введите ID получателя!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                RecipientBox.Focus();
                return;
            }

            // Проверка на пустое сообщение
            if (string.IsNullOrWhiteSpace(message) && Attachments.Count == 0)
            {
                MessageBox.Show("Введите текст сообщения или добавьте вложение!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                MessageInputBox.Focus();
                return;
            }

            try
            {
                // Загружаем вложения
                foreach (var attachment in Attachments.ToList())
                {
                    if (string.IsNullOrEmpty(attachment.VKAttachmentString))
                    {
                        attachment.VKAttachmentString = await UploadAttachment(token, attachment);
                    }
                }

                // Формируем строку вложений
                string attachmentsString = string.Join(",",
                    Attachments.Select(a => a.VKAttachmentString));

                // Отправляем сообщение
                await SendMessage(token, recipient, message, attachmentsString);

                // Обновляем историю сообщений
                await LoadHistory();

                // Очищаем поля
                MessageInputBox.Text = string.Empty;
                Attachments.Clear();

                
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка отправки: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<string> UploadAttachment(string token, Attachment attachment)
        {
            using (HttpClient client = new HttpClient())
            {
                // 1. Получаем URL для загрузки
                string uploadUrl = await GetUploadServer(token, attachment.Type);

                // 2. Загружаем файл на сервер VK
                var uploadResponse = await UploadFile(uploadUrl, attachment.FilePath);

                // 3. Сохраняем файл в VK
                return await SaveAttachment(token, attachment.Type, uploadResponse);
            }
        }

        private async Task<string> GetUploadServer(string token, string type)
        {
            string method = "";
            if (type == "photo") method = "photos.getMessagesUploadServer";
            else if (type == "doc") method = "docs.getMessagesUploadServer";
            else if (type == "audio") method = "audio.getUploadServer";
            else throw new ArgumentException("Unsupported attachment type");

            using (HttpClient client = new HttpClient())
            {
                var response = await client.GetAsync(
                    $"https://api.vk.com/method/{method}?access_token={token}&v=5.199");

                string content = await response.Content.ReadAsStringAsync();
                JObject json = JObject.Parse(content);

                return json["response"]?["upload_url"]?.ToString() ??
                    throw new Exception("Upload URL not found: " + content);
            }
        }

        private async Task<JObject> UploadFile(string uploadUrl, string filePath)
        {
            using (HttpClient client = new HttpClient())
            using (var form = new MultipartFormDataContent())
            using (var fileStream = File.OpenRead(filePath))
            {
                var fileContent = new StreamContent(fileStream);
                form.Add(fileContent, "file", Path.GetFileName(filePath));

                var response = await client.PostAsync(uploadUrl, form);
                string content = await response.Content.ReadAsStringAsync();
                return JObject.Parse(content);
            }
        }

        private async Task<string> SaveAttachment(string token, string type, JObject uploadResponse)
        {
            if (type == "photo")
            {
                return await SavePhotoAttachment(token, uploadResponse);
            }
            else if (type == "doc")
            {
                return await SaveDocAttachment(token, uploadResponse);
            }
            else if (type == "audio")
            {
                return await SaveAudioAttachment(token, uploadResponse);
            }

            throw new ArgumentException("Unsupported attachment type");
        }

        private async Task<string> SavePhotoAttachment(string token, JObject uploadResponse)
        {
            using (HttpClient client = new HttpClient())
            {
                string server = uploadResponse["server"]?.ToString() ?? "";
                string photo = uploadResponse["photo"]?.ToString() ?? "";
                string hash = uploadResponse["hash"]?.ToString() ?? "";

                // Исправление проблемы с кавычками
                if (photo.StartsWith("\"")) photo = photo.Trim('"');

                // Используем POST с form-data вместо GET
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("access_token", token),
                    new KeyValuePair<string, string>("server", server),
                    new KeyValuePair<string, string>("photo", photo),
                    new KeyValuePair<string, string>("hash", hash),
                    new KeyValuePair<string, string>("v", "5.199")
                });

                var response = await client.PostAsync(
                    "https://api.vk.com/method/photos.saveMessagesPhoto",
                    content);

                string responseContent = await response.Content.ReadAsStringAsync();
                JObject json = JObject.Parse(responseContent);

                if (json["response"] == null)
                    throw new Exception("Photo save error: " + responseContent);

                var photoObj = json["response"][0];
                return $"photo{photoObj["owner_id"]}_{photoObj["id"]}";
            }
        }

        private async Task<string> SaveDocAttachment(string token, JObject uploadResponse)
        {
            using (HttpClient client = new HttpClient())
            {
                string file = uploadResponse["file"]?.ToString() ?? "";

                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("access_token", token),
                    new KeyValuePair<string, string>("file", file),
                    new KeyValuePair<string, string>("v", "5.199")
                });

                var response = await client.PostAsync(
                    "https://api.vk.com/method/docs.save",
                    content);

                string responseContent = await response.Content.ReadAsStringAsync();
                JObject json = JObject.Parse(responseContent);

                if (json["response"]?["doc"] == null)
                    throw new Exception("Document save error: " + responseContent);

                var doc = json["response"]["doc"];
                return $"doc{doc["owner_id"]}_{doc["id"]}";
            }
        }

        private async Task<string> SaveAudioAttachment(string token, JObject uploadResponse)
        {
            using (HttpClient client = new HttpClient())
            {
                string server = uploadResponse["server"]?.ToString() ?? "";
                string audio = uploadResponse["audio"]?.ToString() ?? "";
                string hash = uploadResponse["hash"]?.ToString() ?? "";
                string artist = uploadResponse["artist"]?.ToString() ?? "Unknown";
                string title = uploadResponse["title"]?.ToString() ?? "Unknown";

                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("access_token", token),
                    new KeyValuePair<string, string>("server", server),
                    new KeyValuePair<string, string>("audio", audio),
                    new KeyValuePair<string, string>("hash", hash),
                    new KeyValuePair<string, string>("artist", artist),
                    new KeyValuePair<string, string>("title", title),
                    new KeyValuePair<string, string>("v", "5.199")
                });

                var response = await client.PostAsync(
                    "https://api.vk.com/method/audio.save",
                    content);

                string responseContent = await response.Content.ReadAsStringAsync();
                JObject json = JObject.Parse(responseContent);

                if (json["response"] == null)
                    throw new Exception("Audio save error: " + responseContent);

                var audioObj = json["response"];
                return $"audio{audioObj["owner_id"]}_{audioObj["id"]}";
            }
        }

        // Метод для отправки сообщения
        private async Task SendMessage(string token, string peerId, string message, string attachments)
        {
            using (HttpClient client = new HttpClient())
            {
                var parameters = new Dictionary<string, string>
                {
                    {"peer_id", peerId},
                    {"message", message},
                    {"random_id", new Random().Next(1000000).ToString()},
                    {"access_token", token},
                    {"v", "5.199"}
                };

                // Добавляем вложения, если они есть
                if (!string.IsNullOrEmpty(attachments))
                {
                    parameters.Add("attachment", attachments);
                }

                var content = new FormUrlEncodedContent(parameters);
                var response = await client.PostAsync(
                    "https://api.vk.com/method/messages.send", content);

                string responseContent = await response.Content.ReadAsStringAsync();
                JObject json = JObject.Parse(responseContent);

                if (json["error"] != null)
                {
                    string errorMsg = $"Ошибка {json["error"]["error_code"]}: {json["error"]["error_msg"]}";
                    throw new Exception(errorMsg);
                }
            }
        }

        // Обработка нажатия Enter в поле сообщения
        private void MessageInputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
            {
                SendButton_Click(sender, e);
                e.Handled = true;
            }
        }
    }
}