using System.Windows;

namespace WeCreatives_EOI
{
    public partial class CloseWindow : Window
    {
        public string? EnteredPassword { get; private set; }

        public CloseWindow()
        {
            InitializeComponent();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            EnteredPassword = passwordBox.Password;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}

