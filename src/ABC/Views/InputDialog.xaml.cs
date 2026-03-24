using System.Windows;

namespace ABC.Views;

public partial class InputDialog : Window
{
    public string InputText => InputTextBox.Text;

    public InputDialog()
    {
        InitializeComponent();
        InputTextBox.Focus();
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(InputTextBox.Text))
        {
            DialogResult = true;
        }
    }
}
