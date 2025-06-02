using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace VKMessenger
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var mainWindow = new Window
            {
                Title = "VK Messenger",
                Width = 1000,
                Height = 700,
                MinWidth = 800,
                MinHeight = 600,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Icon = new BitmapImage(new Uri("C:\\Users\\azkar\\source\\repos\\VKMessenger\\VK.com-logo.ico"))
            };

            mainWindow.Content = new LoginPage();
            mainWindow.Show();
        }
    }
}