using SvoyaGame.Models;
using SvoyaGame.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;

namespace SvoyaGame.View.MainViews;

public partial class GameBoardWindow : Window
{
	private enum BoardMode { None, Round, Final }

	private readonly ObservableCollection<BoardPlayer> _boardPlayers = new();
	private readonly MediaPlayer _mediaQuestion = new();
	private readonly MediaPlayer _mediaAnswer = new();

	// Плитки текущего режима: "theme|price" для раунда, "final|themeName" для финала
	private readonly Dictionary<string, Border> _tiles = new();

	// Состояние подложки (что показывается, когда вопрос свёрнут)
	private BoardMode _underlyingMode = BoardMode.None;
	private int _currentRoundIndex = -1;

	// Для раундов:
	private Theme?[] _currentThemes = Array.Empty<Theme?>();
	private int[] _currentPrices = Array.Empty<int>();

	public GameBoardWindow()
	{
		InitializeComponent();
		PlayersList.ItemsSource = _boardPlayers;
		//MoveToSecondaryMonitorIfExists();
	}

	/// <summary>Показывает сейчас окно вопроса?</summary>
	public bool IsQuestionVisible => QuestionView.Visibility == Visibility.Visible;

	// =====================================================================
	// РЕЖИМ РАУНДА
	// =====================================================================
	public void ShowRound(int roundIndex, Round round, HashSet<string> usedCells)
	{
		_underlyingMode = BoardMode.Round;
		_currentRoundIndex = roundIndex;

		var prices = round.Prices ?? Array.Empty<int>();
		var themes = round.Themes.ToArray();

		_currentPrices = prices;
		_currentThemes = new Theme?[Math.Max(themes.Length, 1)];
		for (int i = 0; i < themes.Length; i++)
			_currentThemes[i] = themes[i];

		BoardHost.Children.Clear();
		_tiles.Clear();

		int rowCount = Math.Max(1, themes.Length);
		int colCount = 1 + prices.Length;

		var grid = new UniformGrid
		{
			Rows = rowCount,
			Columns = colCount
		};

		for (int r = 0; r < rowCount; r++)
		{
			var theme = r < themes.Length ? themes[r] : null;

			// Колонка темы
			grid.Children.Add(new Border
			{
				Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x1E, 0x3A)),
				CornerRadius = new CornerRadius(6),
				Margin = new Thickness(3),
				Child = new TextBlock
				{
					Text = theme?.Name ?? "—",
					Foreground = (Brush)FindResource("AccentGold"),
					FontWeight = FontWeights.Bold,
					FontSize = 28,
					TextWrapping = TextWrapping.Wrap,
					TextAlignment = TextAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center,
					HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
					Margin = new Thickness(8)
				}
			});

			// Колонки цен
			for (int c = 0; c < prices.Length; c++)
			{
				int price = prices[c];
				bool hasQuestion = theme?.GetQuestion(price) != null;

				var tile = new Border
				{
					Background = (Brush)FindResource("BgPanel"),
					BorderBrush = (Brush)FindResource("AccentGold"),
					BorderThickness = new Thickness(1),
					CornerRadius = new CornerRadius(10),
					Margin = new Thickness(6),
					Child = new TextBlock
					{
						Text = hasQuestion ? price.ToString() : "",
						Foreground = hasQuestion
							? (Brush)FindResource("AccentGold")
							: new SolidColorBrush(Color.FromRgb(0x2C, 0x4A, 0x75)),
						FontWeight = FontWeights.Bold,
						FontSize = 36,
						HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
						VerticalAlignment = VerticalAlignment.Center
					}
				};

				if (!hasQuestion)
				{
					tile.Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x1E, 0x3A));
					tile.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0x4A, 0x75));
				}

				if (theme != null)
					_tiles[$"{theme.Name}|{price}"] = tile;

				grid.Children.Add(tile);
			}
		}

		BoardHost.Children.Add(grid);

		// Восстанавливаем затемнение сыгранных ячеек
		foreach (var key in usedCells)
		{
			var parts = key.Split('|');
			if (parts.Length == 3 && parts[0] == roundIndex.ToString()
				&& int.TryParse(parts[2], out var p))
			{
				MarkCellUsedInternal(parts[1], p);
			}
		}

		// Показываем сетку
		QuestionView.Visibility = Visibility.Collapsed;
		GridView.Visibility = Visibility.Visible;
		AnswerPanel.Visibility = Visibility.Collapsed;
	}

	// =====================================================================
	// РЕЖИМ ФИНАЛА
	// =====================================================================
	public void ShowFinal(int roundIndex, List<FinalTheme> activeThemes, HashSet<string> usedCells)
	{
		_underlyingMode = BoardMode.Final;
		_currentRoundIndex = roundIndex;

		BoardHost.Children.Clear();
		_tiles.Clear();

		var wrap = new WrapPanel
		{
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Orientation = System.Windows.Controls.Orientation.Horizontal
		};

		foreach (var theme in activeThemes)
		{
			string usedKey = $"final|{roundIndex}|{theme.Name}";
			bool isPlayed = usedCells.Contains(usedKey);

			var tile = new Border
			{
				Background = (Brush)FindResource("BgPanel"),
				BorderBrush = (Brush)FindResource("AccentGold"),
				BorderThickness = new Thickness(2),
				CornerRadius = new CornerRadius(12),
				Padding = new Thickness(28, 20, 28, 20),
				Margin = new Thickness(10),
				MinWidth = 200,
				Child = new TextBlock
				{
					Text = theme.Name,
					Foreground = (Brush)FindResource("AccentGold"),
					FontWeight = FontWeights.Bold,
					FontSize = 22,
					TextAlignment = TextAlignment.Center,
					HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center,
					TextWrapping = TextWrapping.Wrap
				}
			};

			if (isPlayed)
			{
				tile.Background = new SolidColorBrush(Color.FromRgb(0x08, 0x14, 0x28));
				tile.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x30, 0x50));

				if (tile.Child is TextBlock tb)
				{
					tb.Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x4A, 0x75));
					tb.TextDecorations = TextDecorations.Strikethrough;
				}
			}

			_tiles[$"final|{theme.Name}"] = tile;
			wrap.Children.Add(tile);
		}

		var scroll = new ScrollViewer
		{
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			Content = wrap
		};
		BoardHost.Children.Add(scroll);

		QuestionView.Visibility = Visibility.Collapsed;
		GridView.Visibility = Visibility.Visible;
		AnswerPanel.Visibility = Visibility.Collapsed;
	}

	// =====================================================================
	// ПОКАЗ ВОПРОСА
	// =====================================================================
	public void ShowQuestion(string themeName, int price, ContentBlock question, ContentBlock answer, bool isFinal = false)
	{
		if (isFinal)
		{
			HeaderTheme.Text = "ФИНАЛ";
			HeaderSeparator.Text = " — ";
			HeaderPrice.Text = themeName;
		}
		else
		{
			HeaderTheme.Text = themeName;
			HeaderSeparator.Text = " • ";
			HeaderPrice.Text = price.ToString();
		}

		FillBlock(ImgQuestion, QuestionText, AudioBtnQuestion, _mediaQuestion, question);
		FillBlock(ImgAnswer, AnswerText, AudioBtnAnswer, _mediaAnswer, answer);

		AnswerPanel.Visibility = Visibility.Collapsed;
		QuestionView.Visibility = Visibility.Visible;
		GridView.Visibility = Visibility.Collapsed;
	}

	/// <summary>Вернуться к подложке (сетке или финальному списку).</summary>
	public void ShowGrid()
	{
		_mediaQuestion.Stop();
		_mediaAnswer.Stop();

		QuestionView.Visibility = Visibility.Collapsed;
		GridView.Visibility = Visibility.Visible;
		AnswerPanel.Visibility = Visibility.Collapsed;
	}

	public void ShowAnswer()
	{
		if (QuestionView.Visibility == Visibility.Visible)
			AnswerPanel.Visibility = Visibility.Visible;
	}

	// =====================================================================
	// ЗАТЕМНЕНИЕ СЫГРАННЫХ
	// =====================================================================
	public void MarkCellUsed(string themeName, int price)
	{
		MarkCellUsedInternal(themeName, price);
	}

	public void MarkFinalThemePlayed(string themeName)
	{
		string key = $"final|{themeName}";
		if (!_tiles.TryGetValue(key, out var tile))
			return;

		tile.Background = new SolidColorBrush(Color.FromRgb(0x08, 0x14, 0x28));
		tile.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x30, 0x50));

		if (tile.Child is TextBlock tb)
		{
			tb.Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x4A, 0x75));
			tb.TextDecorations = TextDecorations.Strikethrough;
		}
	}

	private void MarkCellUsedInternal(string themeName, int price)
	{
		string key = $"{themeName}|{price}";
		if (!_tiles.TryGetValue(key, out var tile))
			return;

		tile.Background = new SolidColorBrush(Color.FromRgb(0x08, 0x14, 0x28));
		tile.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x30, 0x50));

		if (tile.Child is TextBlock tb)
		{
			tb.Text = "✓";
			tb.Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x4A, 0x75));
			tb.FontSize = 22;
		}
	}

	// =====================================================================
	// ПАНЕЛЬ ОБНОВЛЕНИЯ СПИСКА ИГРОКОВ
	// =====================================================================
	public void UpdatePlayers(IEnumerable<(string Name, int Score, bool Online)> players)
	{
		var incoming = players.ToList();

		for (int i = _boardPlayers.Count - 1; i >= 0; i--)
		{
			if (incoming.All(p => p.Name != _boardPlayers[i].Name))
				_boardPlayers.RemoveAt(i);
		}

		foreach (var p in incoming)
		{
			var existing = _boardPlayers.FirstOrDefault(x => x.Name == p.Name);
			if (existing == null)
			{
				_boardPlayers.Add(new BoardPlayer(p.Name)
				{
					Score = p.Score,
					IsOnline = p.Online
				});
			}
			else
			{
				existing.Score = p.Score;
				existing.IsOnline = p.Online;
			}
		}

		var sorted = _boardPlayers
			.OrderByDescending(x => x.Score)
			.ThenBy(x => x.Name, StringComparer.CurrentCulture)
			.ToList();

		for (int i = 0; i < sorted.Count; i++)
		{
			sorted[i].Rank = i + 1;
			int currentIndex = _boardPlayers.IndexOf(sorted[i]);
			if (currentIndex != i)
				_boardPlayers.Move(currentIndex, i);
		}

		bool empty = _boardPlayers.Count == 0;
		NoPlayersText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
		PlayersScroll.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
	}

	/// <summary>
	/// Подсвечивает игрока, который первым нажал кнопку.
	/// Передайте null, чтобы снять подсветку.
	/// </summary>
	public void HighlightFirst(string? playerName)
	{
		foreach (var p in _boardPlayers)
			p.IsFirst = !string.IsNullOrEmpty(playerName) && p.Name == playerName;
	}

	// =====================================================================
	// МЕДИА-БЛОК
	// =====================================================================
	private void FillBlock(
		System.Windows.Controls.Image imgCtrl,
		System.Windows.Controls.TextBlock txtCtrl,
		System.Windows.Controls.Button audioBtn,
		MediaPlayer media,
		ContentBlock? block)
	{
		// --- Картинка ---
		bool imageOk = false;
		if (block?.HasImage == true)
		{
			var path = Services.QuestionsLoader.ResolveMediaPath(block.Image);
			if (path != null && File.Exists(path))
			{
				try
				{
					var bmp = new BitmapImage();
					bmp.BeginInit();
					bmp.CacheOption = BitmapCacheOption.OnLoad;
					bmp.UriSource = new Uri(path, UriKind.Absolute);
					bmp.EndInit();
					imgCtrl.Source = bmp;
					imgCtrl.Visibility = Visibility.Visible;
					imageOk = true;
				}
				catch
				{
					imgCtrl.Source = null;
					imgCtrl.Visibility = Visibility.Collapsed;
				}
			}
			else
			{
				imgCtrl.Source = null;
				imgCtrl.Visibility = Visibility.Collapsed;
			}
		}
		else
		{
			imgCtrl.Source = null;
			imgCtrl.Visibility = Visibility.Collapsed;
		}

		// --- Текст ---
		if (block?.HasText == true)
		{
			txtCtrl.Text = block.Text!;
			txtCtrl.Foreground = (Brush)FindResource("TextMain");
			txtCtrl.Visibility = Visibility.Visible;
		}
		else if (block?.HasImage == true && !imageOk)
		{
			txtCtrl.Text = "[изображение не найдено]";
			txtCtrl.Foreground = new SolidColorBrush(Color.FromRgb(0x9F, 0xB3, 0xC8));
			txtCtrl.Visibility = Visibility.Visible;
		}
		else
		{
			txtCtrl.Text = "";
			txtCtrl.Visibility = Visibility.Collapsed;
		}

		// --- Звук ---
		media.Stop();
		media.Close();

		if (block?.HasAudio == true)
		{
			var audioPath = Services.QuestionsLoader.ResolveMediaPath(block.Audio);
			if (audioPath != null && File.Exists(audioPath))
			{
				try
				{
					media.Open(new Uri(audioPath, UriKind.Absolute));
					audioBtn.Visibility = Visibility.Visible;
					return;
				}
				catch { /* ignore */ }
			}
		}
		audioBtn.Visibility = Visibility.Collapsed;
	}

	private void AudioButton_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not Button btn)
			return;

		MediaPlayer? player = null;
		if (btn == AudioBtnQuestion)
			player = _mediaQuestion;
		else if (btn == AudioBtnAnswer)
			player = _mediaAnswer;

		if (player is null)
			return;

		player.Position = TimeSpan.Zero;
		player.Play();
	}

	// =====================================================================
	// ХОТКЕИ
	// =====================================================================
	/// <summary>Событие хоткея для пересылки в MainWindow.</summary>
	public event Action<System.Windows.Input.Key>? HotkeyPressed;

	protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
	{
		// Пропускаем автоповтор
		if (e.IsRepeat)
		{ base.OnPreviewKeyDown(e); return; }

		// Пропускаем с модификаторами
		var mods = System.Windows.Input.Keyboard.Modifiers;
		if ((mods & (System.Windows.Input.ModifierKeys.Control
				   | System.Windows.Input.ModifierKeys.Alt)) != 0)
		{
			base.OnPreviewKeyDown(e);
			return;
		}

		// Не перехватываем ввод в текстовых полях (на табло их сейчас нет, но на будущее)
		var focused = System.Windows.Input.Keyboard.FocusedElement;
		if (focused is System.Windows.Controls.TextBox
			|| focused is System.Windows.Controls.PasswordBox)
		{
			base.OnPreviewKeyDown(e);
			return;
		}

		if (e.Key == System.Windows.Input.Key.Space
			|| e.Key == System.Windows.Input.Key.Q
			|| e.Key == System.Windows.Input.Key.R)
		{
			HotkeyPressed?.Invoke(e.Key);
			e.Handled = true;
			return;
		}

		base.OnPreviewKeyDown(e);
	}

	protected override void OnClosed(EventArgs e)
	{
		_mediaQuestion.Close();
		_mediaAnswer.Close();
		base.OnClosed(e);
	}
}