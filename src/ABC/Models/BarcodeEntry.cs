using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ABC.Models;

public class BarcodeEntry : INotifyPropertyChanged
{
    private bool _isDuplicate;
    private bool _isSelected;
    private int _sequenceNumber;

    public string Barcode { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string CodeType { get; set; } = string.Empty;
    public string ScannerId { get; set; } = string.Empty;

    public int SequenceNumber
    {
        get => _sequenceNumber;
        set
        {
            if (_sequenceNumber == value) return;
            _sequenceNumber = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SequenceNumber)));
        }
    }

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

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
