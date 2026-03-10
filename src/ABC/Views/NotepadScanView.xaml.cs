using System.Windows.Controls;
using System.Windows.Input;
using ABC.ViewModels;

namespace ABC.Views;

public partial class NotepadScanView : UserControl
{
    public NotepadScanView()
    {
        InitializeComponent();
    }

    private void ScanInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Return)
            return;

        if (DataContext is NotepadScanViewModel vm)
        {
            vm.AddBarcode(vm.ScanInput);
            vm.ScanInput = string.Empty;
        }

        ScanInputBox.Focus();
        e.Handled = true;
    }
}
