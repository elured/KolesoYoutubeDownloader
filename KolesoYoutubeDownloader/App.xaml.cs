using KolesoYoutubeDownloader.Services;
using KolesoYoutubeDownloader.ViewModels;
using KolesoYoutubeDownloader.Views;
using Microsoft.Extensions.DependencyInjection;
using System.Configuration;
using System.Data;
using System.Windows;

namespace KolesoYoutubeDownloader
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public IServiceProvider ServiceProvider { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. Создаем коллекцию сервисов
            var lServiceCollection = new ServiceCollection();
            ConfigureServices(lServiceCollection);

            // 2. Строим провайдер (наш DI-контейнер)
            ServiceProvider = lServiceCollection.BuildServiceProvider();

            // 3. Запрашиваем главное окно. Контейнер САМ соберет для него MainViewModel и все нужные сервисы!
            var lMainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            System.Net.WebRequest.DefaultWebProxy.Credentials = System.Net.CredentialCache.DefaultCredentials;
            lMainWindow.Show();
        }

        private void ConfigureServices(IServiceCollection pServices)
        {
            // --- Регистрируем сервисы (бизнес-логика) ---
            // AddSingleton означает, что сервис создастся 1 раз и будет жить всё время работы приложения
            pServices.AddSingleton<IYouTubeDownloaderService, YouTubeDownloaderService>();
            pServices.AddSingleton<IDialogService, DialogService>();

            // --- Регистрируем ViewModels ---
            // AddTransient означает, что каждый раз будет создаваться новый экземпляр
            pServices.AddTransient<MainViewModel>();

            // --- Регистрируем Views ---
            pServices.AddTransient<MainWindow>();
        }
    }

}
