using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace VKMessenger
{
    public partial class LoginPage : Page
    {
        public LoginPage()
        {
            InitializeComponent();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            // Открываем ссылку в браузере
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string token = TokenBox.Password.Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                StatusText.Text = "Введите токен!";
                return;
            }

            // Проверим токен, запросив информацию о текущем пользователе
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    var response = await client.GetAsync(
                        $"https://api.vk.com/method/users.get?access_token={token}&v=5.199");

                    string content = await response.Content.ReadAsStringAsync();
                    JObject json = JObject.Parse(content);

                    if (json["error"] != null)
                    {
                        StatusText.Text = "Ошибка: " + json["error"]["error_msg"].ToString();
                        return;
                    }
                }

                
                var mainPage = new MainPage(token);

                // Получаем текущее окно
                var window = Window.GetWindow(this);

                // Заменяем контент окна
                window.Content = mainPage;
                if (this.Parent is Window parentWindow)
                {
                    parentWindow.Content = mainPage;
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка: " + ex.Message;
            }
        }
    }
}