using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SvoyaGame.Models;

/// <summary>
/// Строка игрока на табло для зала.
/// </summary>
public class BoardPlayer : INotifyPropertyChanged
{
	private int _score;
	private bool _isOnline;
	private int _rank;

	public string Name { get; }

	public BoardPlayer(string name) => Name = name;

	public int Score
	{
		get => _score;
		set { if (_score == value) return; _score = value; OnPropertyChanged(); }
	}

	public bool IsOnline
	{
		get => _isOnline;
		set { if (_isOnline == value) return; _isOnline = value; OnPropertyChanged(); }
	}

	private bool _isFirst;

	/// <summary>Первый нажавший кнопку в текущем раунде.</summary>
	public bool IsFirst
	{
		get => _isFirst;
		set
		{
			if (_isFirst == value)
				return;
			_isFirst = value;
			OnPropertyChanged();
		}
	}

	/// <summary>Место в текущем рейтинге (1 — лидер).</summary>
	public int Rank
	{
		get => _rank;
		set
		{
			if (_rank == value)
				return;
			_rank = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(RankLabel));
		}
	}

	public string RankLabel => Rank > 0 ? $"{Rank}." : "";

	public event PropertyChangedEventHandler? PropertyChanged;

	private void OnPropertyChanged([CallerMemberName] string? name = null)
		=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}