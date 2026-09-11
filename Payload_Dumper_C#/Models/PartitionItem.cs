using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Payload_Dumper_C_.Models;

public sealed class PartitionItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string Name { get; init; }
    public required long SizeBytes { get; init; }
    public required string SizeReadable { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

