using System;
using System.ComponentModel;
using System.Windows;
using WeCreatives_EOI.Classes;

namespace WeCreatives_EOI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Closing += MainWindow_Closing;

            try
            {
                var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                string versionStr = ver != null ? $"v{ver.Major}.{ver.Minor}.{Math.Max(0, ver.Build)}" : "v1.0.1";
                Title = $"WeCreatives EOI - {versionStr}";
                if (TxtAppVersion != null)
                {
                    TxtAppVersion.Text = versionStr;
                }
            }
            catch { }
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            CloseWindow passwordDialog = new CloseWindow();
            passwordDialog.Owner = this;

            if (passwordDialog.ShowDialog() == true)
            {
                if (CheckPassword(passwordDialog.EnteredPassword))
                {
                    if (DataContext is IDisposable disposableVm)
                    {
                        disposableVm.Dispose();
                    }
                }
                else
                {
                    MessageBox.Show("Incorrect Password. Application exit cancelled.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                    e.Cancel = true;
                }
            }
            else
            {
                e.Cancel = true;
            }
        }

        private bool CheckPassword(string? enteredPassword)
        {
            const string correctPassword = "1122";
            return string.Equals(enteredPassword, correctPassword, StringComparison.Ordinal);
        }
    }
}

