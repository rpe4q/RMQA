using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;

namespace OrderConsServA
{
    public partial class App : Application
    {
        private Window _MainWindow;
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                _MainWindow = new MainWindow();
                desktop.MainWindow = _MainWindow;
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void NativeMenuItem_Click_1(object? sender, System.EventArgs e)
        {
            Environment.Exit(0);
        }

        private void NativeMenuItem_Click_HO(object? sender, EventArgs e)
        {
            if (_MainWindow.IsVisible)
            {
                _MainWindow.Hide();
            }
            else
            {
                if (_MainWindow.WindowState == WindowState.Minimized)
                {
                    _MainWindow.WindowState = WindowState.Normal;
                }
                _MainWindow.Show();
                _MainWindow.Activate();
            }
        }
    }
}