using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Bogus;
using System;

namespace OrderProducerAvalonia
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void NativeMenuItem_Click_1(object? sender, System.EventArgs e)
        {
            Environment.Exit(0);
        }

        private void NativeMenuItem_Click_Open(object? sender, EventArgs e)
        {
            MainWindow wOpen = new();
            wOpen.Show();
        }
    }
}