using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SvoyaGame.Models;

public class PlayerRow : INotifyPropertyChanged
{
    private bool _isOnline;
    private int _score;
    private string _betInput = "100";

    public string Name { get; }

    public PlayerRow(string name)
    {
        Name = name;
    }

    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            if (_isOnline == value) return;
            _isOnline = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>Текстовая подпись статуса (для Tooltip).</summary>
    public string StatusText => IsOnline ? "В сети" : "Оффлайн";

    public int Score
    {
        get => _score;
        set
        {
            if (_score == value) return;
            _score = value;
            OnPropertyChanged();
        }
    }

    public string BetInput
    {
        get => _betInput;
        set
        {
            if (_betInput == value) return;
            _betInput = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}