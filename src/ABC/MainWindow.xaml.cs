using System.Windows;
using ABC.ViewModels;

namespace ABC;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
