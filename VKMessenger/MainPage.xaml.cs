using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace VKMessenger
{
    public partial class MainPage : Page
    {

        public class Message
        {
            public string Id { get; set; }
            public string Sender { get; set; }
            public string Text { get; set; }
            public string Date { get; set; }
            public string SenderId { get; set; }
            public string PeerName { get; set; }
            public List<Attachment> Attachments { get; set; } = new List<Attachment>();
        }
        public class Dialog
        {
            public string PeerId { get; set; }
            public string Title { get; set; }
            public string PreviewText { get; set; }
            public string LastMessageDate { get; set; }            
        }
        public class UserInfo
        {
            public string Id { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }            
        }
        private string _currentPeerName = "";
        public ObservableCollection<Dialog> Dialogs { get; } = new ObservableCollection<Dialog>();

        public ObservableCollection<Message> Messages { get; } = new ObservableCollection<Message>();
        public ObservableCollection<Attachment> Attachments { get; } = new ObservableCollection<Attachment>();

        private Dialog _selectedDialog;

        private string _token;
        private string _currentUserId = "";

        public MainPage(string token)
        {
            InitializeComponent();
            _token = token;

            // Инициализация остальных компонентов
            MessagesList.ItemsSource = Messages;
            AttachmentsList.ItemsSource = Attachments;
            DialogsList.ItemsSource = Dialogs;

            Messages.CollectionChanged += Messages_CollectionChanged;

            // Загружаем диалоги при открытии страницы
            _ = LoadDialogs();
        }             

        private async Task LoadHistory(string peerId)
        {
            string token = _token;

            if (string.IsNullOrWhiteSpace(token))
            {
                MessageBox.Show("Введите токен доступа!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                // Получаем ID текущего пользователя
                if (string.IsNullOrEmpty(_currentUserId))
                {
                    _currentUserId = await GetCurrentUserId(token);
                }

                var messages = await GetMessagesHistory(token, peerId);

                // Очищаем и добавляем сообщения
                Messages.Clear();
                foreach (var msg in messages)
                {
                    // Добавляем имя текущего собеседника
                    msg.PeerName = _currentPeerName;
                    Messages.Add(msg);
                }

                // Прокрутка к последнему сообщению
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
            if (string.IsNullOrWhiteSpace(peerId))
            {
                throw new Exception("Не выбран диалог");
            }
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
                var userInfos = new Dictionary<string, UserInfo>();

                // Сбор информации о пользователях (объединяем профили и группы)
                if (json["response"]["profiles"] != null)
                {
                    foreach (var profile in json["response"]["profiles"])
                    {
                        string userId = profile["id"].ToString();
                        userInfos[userId] = new UserInfo
                        {
                            Id = userId,
                            FirstName = profile["first_name"].ToString(),
                            LastName = profile["last_name"].ToString()
                        };
                    }
                }

                if (json["response"]["groups"] != null)
                {
                    foreach (var group in json["response"]["groups"])
                    {
                        string groupId = $"-{group["id"]}"; // Группы имеют отрицательные ID
                        userInfos[groupId] = new UserInfo
                        {
                            Id = groupId,
                            FirstName = group["name"].ToString(),
                            LastName = "" // У групп нет фамилии
                        };
                    }
                }

                // Обработка сообщений
                foreach (var item in json["response"]["items"])
                {
                    string fromId = item["from_id"]?.ToString() ?? "";
                    string senderName;

                    if (fromId == _currentUserId)
                    {
                        senderName = "Я";
                    }
                    else if (userInfos.TryGetValue(fromId, out UserInfo userInfo))
                    {
                        // Пробуем получить имя из разных источников
                        senderName = GetSenderName(fromId, json);
                    }
                    else
                    {
                        // Если информации нет, пробуем найти в кэше диалогов
                        senderName = GetCachedName(fromId) ?? $"Пользователь {fromId}";
                    }

                    // Конвертация даты из Unix timestamp
                    DateTime date = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                        .AddSeconds(item["date"].ToObject<long>())
                        .ToLocalTime();
                    // Обработка вложений
                    var attachments = new List<Attachment>();
                    if (item["attachments"] != null)
                    {
                        foreach (var att in item["attachments"])
                        {
                            string type = att["type"].ToString();
                            var attachment = new Attachment { Type = type };

                            try
                            {
                                switch (type)
                                {
                                    case "photo":
                                        var photo = att["photo"];
                                        var sizes = photo["sizes"];
                                        var size = sizes.FirstOrDefault(s => s["type"].ToString() == "x") ?? sizes.Last();
                                        attachment.PreviewUrl = size["url"].ToString();
                                        attachment.VKAttachmentString = $"photo{photo["owner_id"]}_{photo["id"]}";
                                        break;

                                    case "sticker":
                                        var sticker = att["sticker"];
                                        var images = sticker["images"];
                                        var img = images.FirstOrDefault(i => i["width"].Value<int>() == 128) ?? images.Last();
                                        attachment.PreviewUrl = img["url"].ToString();
                                        attachment.VKAttachmentString = $"sticker{sticker["sticker_id"]}";
                                        break;

                                    case "doc":
                                        var doc = att["doc"];
                                        attachment.FileName = doc["title"].ToString();
                                        if (doc["preview"]?["photo"]?["sizes"] != null)
                                        {
                                            var photoSizes = doc["preview"]["photo"]["sizes"];
                                            var prevSize = photoSizes.Last();
                                            attachment.PreviewUrl = prevSize["url"].ToString();
                                        }
                                        attachment.VKAttachmentString = $"doc{doc["owner_id"]}_{doc["id"]}";
                                        break;

                                    default:
                                        attachment.VKAttachmentString = $"{type}{att[type]["owner_id"]}_{att[type]["id"]}";
                                        break;
                                }
                            }
                            catch { /* обработка ошибок */ }

                            attachments.Add(attachment);
                        }
                    }
                    messages.Add(new Message
                    {
                        Id = item["id"].ToString(),
                        Sender = senderName,
                        Text = item["text"]?.ToString(),
                        Date = date.ToString("g"),
                        SenderId = fromId,
                        Attachments = attachments
                    });

                }

                return messages;
            }
        }
        private string GetSenderName(string fromId, JObject json)
        {
            // 1. Проверяем локальные профили из текущего запроса
            if (json["response"]["profiles"] != null)
            {
                foreach (var profile in json["response"]["profiles"])
                {
                    if (profile["id"].ToString() == fromId)
                    {
                        return $"{profile["first_name"]} {profile["last_name"]}";
                    }
                }
            }

            // 2. Проверяем группы из текущего запроса
            if (json["response"]["groups"] != null)
            {
                foreach (var group in json["response"]["groups"])
                {
                    if (group["id"].ToString() == fromId.TrimStart('-'))
                    {
                        return group["name"].ToString();
                    }
                }
            }

            // 3. Проверяем кэш имен из диалогов
            if (_nameCache.TryGetValue(fromId, out string cachedName))
            {
                return cachedName;
            }

            // 4. Если ничего не найдено
            return $"Пользователь {fromId}";
        }
        private void Messages_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                // Исправленная строка:
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (Messages.Count > 0)
                    {
                        MessagesList.ScrollIntoView(Messages[0]);
                    }
                }));
            }
        }
        private async Task LoadDialogs()
        {
            string token = _token;
            if (string.IsNullOrWhiteSpace(token))
            {
                MessageBox.Show("Введите токен доступа!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                Dialogs.Clear();
                var dialogs = await GetDialogs(token);
                foreach (var dialog in dialogs)
                {
                    Dialogs.Add(dialog);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки диалогов: {ex.Message}", "Ошибка",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private async void LoadDialogsButton_Click(object sender, RoutedEventArgs e)
        {
            // Запоминаем текущий выбранный диалог
            Dialog selectedDialog = DialogsList.SelectedItem as Dialog;
            string currentPeerId = selectedDialog?.PeerId;

            // Обновляем список диалогов
            await LoadDialogs();

            // Восстанавливаем выбор диалога
            if (!string.IsNullOrEmpty(currentPeerId))
            {
                var dialogToSelect = Dialogs.FirstOrDefault(d => d.PeerId == currentPeerId);
                if (dialogToSelect != null)
                {
                    DialogsList.SelectedItem = dialogToSelect;
                    DialogsList.ScrollIntoView(dialogToSelect);
                }
            }

            // Обновляем историю сообщений, если диалог выбран
            if (DialogsList.SelectedItem is Dialog currentDialog)
            {
                await LoadHistory(currentDialog.PeerId);
            }
        }
        private string GetCachedName(string userId)
        {
            return _nameCache.TryGetValue(userId, out string name) ? name : null;
        }
        private readonly Dictionary<string, string> _nameCache = new Dictionary<string, string>();
        private async Task<List<Dialog>> GetDialogs(string token)
        {
            using (HttpClient client = new HttpClient())
            {
                var response = await client.GetAsync(
                    $"https://api.vk.com/method/messages.getConversations?" +
                    $"access_token={token}&v=5.199&extended=1&count=20");

                string content = await response.Content.ReadAsStringAsync();
                JObject json = JObject.Parse(content);

                if (json["error"] != null)
                    throw new Exception(json["error"]["error_msg"].ToString());

                var dialogs = new List<Dialog>();
                var profiles = new Dictionary<long, string>();
                var groups = new Dictionary<long, string>();

                // Очищаем кэш перед обновлением
                _nameCache.Clear();

                // 1. Обработка пользовательских профилей
                if (json["response"]["profiles"] != null)
                {
                    foreach (var profile in json["response"]["profiles"])
                    {
                        long id = profile["id"].Value<long>();
                        string firstName = profile["first_name"].ToString();
                        string lastName = profile["last_name"].ToString();
                        string fullName = $"{firstName} {lastName}";

                        profiles[id] = fullName;
                        _nameCache[id.ToString()] = fullName; // Сохраняем в кэш
                    }
                }

                // 2. Обработка групп и сообществ
                if (json["response"]["groups"] != null)
                {
                    foreach (var group in json["response"]["groups"])
                    {
                        long id = group["id"].Value<long>();
                        string name = group["name"].ToString();

                        groups[id] = name;
                        _nameCache[$"-{id}"] = name; // Ключ для групп: "-group_id"
                    }
                }

                // 3. Обработка диалогов
                foreach (var item in json["response"]["items"])
                {
                    var conversation = item["conversation"];
                    var peer = conversation["peer"];
                    long peerId = peer["id"].Value<long>();
                    string type = peer["type"].ToString();

                    string title = "";
                    string preview = item["last_message"]["text"].ToString();

                    // Форматирование даты
                    DateTime date = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                        .AddSeconds(item["last_message"]["date"].Value<long>())
                        .ToLocalTime();
                    string dateStr = date.ToString("g");

                    // Определение названия диалога
                    if (type == "user")
                    {
                        long userId = peerId;
                        title = profiles.TryGetValue(userId, out string name) ?
                            name : $"Пользователь {userId}";
                    }
                    else if (type == "chat")
                    {
                        title = conversation["chat_settings"]["title"].ToString();
                    }
                    else if (type == "group")
                    {
                        long groupId = Math.Abs(peerId);
                        title = groups.TryGetValue(groupId, out string name) ?
                            name : $"Сообщество {groupId}";
                    }

                    // Сохраняем название диалога в кэш по peerId
                    _nameCache[peerId.ToString()] = title;

                    dialogs.Add(new Dialog
                    {
                        PeerId = peerId.ToString(),
                        Title = title,
                        PreviewText = preview.Length > 50 ? preview.Substring(0, 50) + "..." : preview,
                        LastMessageDate = dateStr
                    });
                }

                return dialogs;
            }
        }
        private async void DialogsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DialogsList.SelectedItem is Dialog selectedDialog)
            {
                _selectedDialog = selectedDialog; // Сохраняем выбранный диалог
                _currentPeerName = selectedDialog.Title;
                await LoadHistory(selectedDialog.PeerId);
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

                    var attachment = new Attachment // Используется класс из отдельного файла
                    {
                        FilePath = filePath,
                        Type = GetAttachmentType(filePath),
                        FileName = fileInfo.Name // Устанавливаем имя файла
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
            if (DialogsList.SelectedItem == null)
            {
                MessageBox.Show("Выберите диалог!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string peerId = ((Dialog)DialogsList.SelectedItem).PeerId;
            string message = MessageInputBox.Text;

            
            string token = _token;            

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
                await SendMessage(token, peerId, message, attachmentsString);

                // Обновляем историю сообщений
                await LoadHistory(peerId);

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
            if (e.Key == Key.Enter)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    // Разрешаем перенос строки при Shift+Enter
                    return;
                }

                // Отправляем сообщение и блокируем стандартную обработку
                SendButton_Click(sender, e);
                e.Handled = true;
            }
        }
        

    }
}