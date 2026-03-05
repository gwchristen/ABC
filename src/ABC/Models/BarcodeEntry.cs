using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ABC.Models;

public class BarcodeEntry : INotifyPropertyChanged
{
    private bool _isDuplicate;

    public string Barcode { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string CodeType { get; set; } = string.Empty;
    public string ScannerId { get; set; } = string.Empty;
    public int SequenceNumber { get; set; }

    public bool IsDuplicate
    {
        get => _isDuplicate;
        set
        {
            if (_isDuplicate == value) return;
            _isDuplicate = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDuplicate)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
