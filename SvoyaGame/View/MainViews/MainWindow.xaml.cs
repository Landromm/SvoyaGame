using SocketIOClient;
using SvoyaGame.Models;
using SvoyaGame.Services;
using SvoyaGame.View.MainViews;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using Panel = System.Windows.Controls.Panel;

namespace SvoyaGame.View.MainViews;

public partial class MainWindow : Window
{
    private static int[] Prices = { 100, 200, 400, 700, 1000 };

    private readonly SocketIOClient.SocketIO _client;
    private readonly ObservableCollection<PlayerRow> _players = new();
	private List<Round> _rounds = new();
	private List<FinalTheme> _activeFinalThemes = new();
	private Panel? _finalThemesPanel;
	private Round? _finalRound;
	private bool _finalActive;
	private readonly ObservableCollection<FinalAnswerView> _finalAnswers = new();
	private readonly HashSet<string> _finalBetsApplied = new();
	private readonly HashSet<string> _usedCells = new(); // "Тема|Цена"
	private readonly MediaPlayer _adminAnswerMedia = new();
	private readonly MediaPlayer _adminQuestionMedia = new();
	private string? _currentRoom;
	private GameState? _savedState;
	private string? _currentLeader;
	private GameBoardWindow? _boardWindow;
	private Button? _finalEndButton;

	// Хранит ссылки на кнопки ячеек: ключ = "тема|цена"
	private readonly Dictionary<string, System.Windows.Controls.Button> _cellButtons = new();

    public MainWindow()
    {
        InitializeComponent();
		_savedState = GameStateService.Load();

		// ---- Загрузка вопросов ----
		_rounds = QuestionsLoader.TryLoadRounds(out var loadError) ?? new List<Round>();
		if (loadError != null)
		{
			MessageBox.Show($"Не удалось загрузить questions.json:\n{loadError}",
				"Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
		}

		GridPlayers.ItemsSource = _players;
		FinalAnswersList.ItemsSource = _finalAnswers;
		BuildRoundsUi();
		CheckMediaOrWarn();

		// ---- Socket.IO ----
		_client = new SocketIOClient.SocketIO(new Uri("http://localhost:5000"));

        _client.OnConnected += (_, _) =>
            Dispatcher.Invoke(() => StatusText.Text = "Статус: Подключен. Создайте комнату.");

        _client.OnDisconnected += (_, _) => Dispatcher.Invoke(() =>
        {
            StatusText.Text = "Статус: Отключен от сервера";
            BtnCreateRoom.IsEnabled = true;
            TxtRoomNumber.IsEnabled = true;
        });

        _client.On("onPlayersListUpdated", response =>
        {
            var players = response.GetValue<List<string>>(0);
            Dispatcher.Invoke(() => SyncPlayers(players));
            return Task.CompletedTask;
        });

		_client.On("onTopClicksUpdated", response =>
		{
			var top = response.GetValue<List<string>>(0);
			Dispatcher.Invoke(() =>
			{
				UpdateTop3(top);
				BtnReset.IsEnabled = top.Count > 0;

				_currentLeader = top.Count > 0 ? top[0] : null;
				_boardWindow?.HighlightFirst(_currentLeader);
			});
			return Task.CompletedTask;
		});

		_client.On("onWinnerDetected", response =>
		{
			var winnerName = response.GetValue<string>(0);
			Dispatcher.Invoke(() =>
			{
				BtnReset.IsEnabled = true;
				_currentLeader = winnerName;
				SaveGameState();
				_boardWindow?.HighlightFirst(_currentLeader);
			});
			return Task.CompletedTask;
		});

		//_client.On("onScoresUpdated", response =>
		//      {
		//          var list = response.GetValue<List<System.Text.Json.JsonElement>>(0);
		//          Dispatcher.Invoke(() => ApplyScores(list));
		//          return Task.CompletedTask;
		//      });

		_client.On("onScoresUpdated", response =>
		{
			var list = response.GetValue<List<System.Text.Json.JsonElement>>(0);

			// ВРЕМЕННО: печатаем в окно вывода
			System.Diagnostics.Debug.WriteLine("=== onScoresUpdated ===");
			foreach (var el in list)
				System.Diagnostics.Debug.WriteLine(el.GetRawText());

			Dispatcher.Invoke(() => ApplyScores(list));
			return Task.CompletedTask;
		});

		_client.On("onFinalAnswersUpdated", response =>
		{
			var list = response.GetValue<List<System.Text.Json.JsonElement>>(0);
			Dispatcher.Invoke(() => UpdateFinalAnswers(list));
			return Task.CompletedTask;
		});

		_client.On("onGameReset", response =>
		{
			Dispatcher.Invoke(() =>
			{
				UpdateTop3(new List<string>());
				BtnReset.IsEnabled = false;

				// Снимаем подсветку
				_currentLeader = null;
				SaveGameState();
				_boardWindow?.HighlightFirst(null);
			});
			return Task.CompletedTask;
		});

		_client.On("onGameFullReset", response =>
		{
			Dispatcher.Invoke(() =>
			{
				// Сбрасываем всё финальное состояние
				_finalActive = false;
				_finalBetsApplied.Clear();
				_finalAnswers.Clear();
				FinalAnswersPanel.Visibility = Visibility.Collapsed;
				if (_finalEndButton != null)
					_finalEndButton.Visibility = Visibility.Collapsed;

				// Сыгранные ячейки — очистить
				_usedCells.Clear();

				// Лидер — снять подсветку
				_currentLeader = null;
				_boardWindow?.HighlightFirst(null);

				// ТОП-3 очистить
				UpdateTop3(new List<string>());

				// Кнопка сброса раунда выключить — сбрасывать нечего
				BtnReset.IsEnabled = false;

				// Панель ответа админа — скрыть
				AdminAnswerPanel.Visibility = Visibility.Collapsed;
				BtnShowAnswer.IsEnabled = false;
				BtnHideQuestion.IsEnabled = false;
				_adminAnswerMedia.Stop();
				_adminQuestionMedia.Stop();

				// Вернуть все финальные темы
				if (_finalRound != null)
				{
					_activeFinalThemes = _finalRound.FinalThemes.ToList();
					RenderFinalPanel();
				}

				// Перестроить игровое поле — все ячейки снова активны
				BuildRoundsUi();

				// Обновить табло
				if (_boardWindow is { IsLoaded: true })
				{
					SyncBoardToCurrentTab();
				}
				// Стереть сохранённое состояние
				GameStateService.Clear();
				_savedState = null;
			});


			return Task.CompletedTask;
		});
	}

	/// <summary>
	/// Сохраняет текущее состояние партии в game_state.json.
	/// Вызывается после каждого изменения (сыгранная ячейка, лидер, финал).
	/// </summary>
	private void SaveGameState()
	{
		if (string.IsNullOrEmpty(_currentRoom))
			return;

		var removedThemes = new List<string>();
		if (_finalRound != null)
		{
			var activeNames = new HashSet<string>(_activeFinalThemes.Select(t => t.Name));
			removedThemes = _finalRound.FinalThemes
				.Where(t => !activeNames.Contains(t.Name))
				.Select(t => t.Name)
				.ToList();
		}

		var state = new GameState
		{
			RoomNumber = _currentRoom,
			UsedCells = _usedCells.ToList(),
			CurrentLeader = _currentLeader,
			FinalRemovedThemes = removedThemes,
			FinalActive = _finalActive
		};

		GameStateService.Save(state);
		_savedState = state;
	}

	// =====================================================================
	// ПОСТРОЕНИЕ ИГРОВОГО ПОЛЯ 5×6
	// =====================================================================
	private void BuildRoundsUi()
	{
		RoundsTabControl.Items.Clear();
		_cellButtons.Clear();

		for (int ri = 0; ri < _rounds.Count; ri++)
		{
			var round = _rounds[ri];

			var tab = new TabItem
			{
				Header = round.Name,
				Style = (Style)FindResource("DarkTabItem")
			};

			// ----- ФИНАЛ -----
			if (round.IsFinal)
			{
				tab.Content = BuildFinalTabContent(round, ri);
				RoundsTabControl.Items.Add(tab);
				continue;
			}

			// ----- Обычный раунд -----
			var prices = round.Prices;
			int rowCount = Math.Max(1, round.Themes.Count);
			int colCount = 1 + prices.Length;

			var grid = new UniformGrid
			{
				Rows = rowCount,
				Columns = colCount,
				Margin = new Thickness(0, 10, 0, 0)
			};

			for (int r = 0; r < rowCount; r++)
			{
				var theme = r < round.Themes.Count ? round.Themes[r] : null;

				// --- Колонка темы ---
				var themeBox = new Border
				{
					Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x1E, 0x3A)),
					CornerRadius = new CornerRadius(6),
					Margin = new Thickness(3),
					Child = new TextBlock
					{
						Text = theme?.Name ?? "—",
						Foreground = (Brush)FindResource("AccentGold"),
						FontWeight = FontWeights.Bold,
						FontSize = 16,
						TextWrapping = TextWrapping.Wrap,
						TextAlignment = TextAlignment.Center,
						VerticalAlignment = VerticalAlignment.Center,
						HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
						Margin = new Thickness(8)
					}
				};
				grid.Children.Add(themeBox);

				// --- Колонки с ценами ---
				for (int c = 0; c < prices.Length; c++)
				{
					int price = prices[c];

					bool hasQuestion = theme != null && theme.GetQuestion(price) != null;

					// Ключ, совпадающий с форматом в _usedCells
					string cellKey = theme != null ? $"{ri}|{theme.Name}|{price}" : "";
					bool isUsed = theme != null && _usedCells.Contains(cellKey);

					var btn = new Button
					{
						Content = isUsed ? "✓" : price.ToString(),
						Style = (Style)FindResource("GreenButton"),
						Margin = new Thickness(3),
						FontSize = 24,
						IsEnabled = hasQuestion && !isUsed,
						Tag = new CellTag
						{
							Round = round,
							RoundIndex = ri,
							Theme = theme,
							Price = price
						}
					};

					// Сыгранная ячейка — визуально приглушена
					if (isUsed)
					{
						btn.FontWeight = FontWeights.Bold;
					}

					btn.Click += CellButton_Click;

					if (theme != null)
						_cellButtons[cellKey] = btn;

					grid.Children.Add(btn);
				}
			}

			tab.Content = grid;
			RoundsTabControl.Items.Add(tab);
		}

		if (RoundsTabControl.Items.Count > 0)
			RoundsTabControl.SelectedIndex = 0;
	}
	// =====================================================================
	// ФИНАЛ
	// =====================================================================
	private UIElement BuildFinalTabContent(Round round, int roundIndex)
	{
		_finalRound = round;
		_activeFinalThemes = round.FinalThemes.ToList();

		var root = new Grid { Margin = new Thickness(0, 10, 0, 0) };
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

		// Верхняя панель — кнопка сброса
		var toolbar = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
			Margin = new Thickness(0, 0, 0, 12)
		};

		var btnResetFinal = new Button
		{
			Content = "↻ СБРОСИТЬ ФИНАЛ",
			Style = (Style)FindResource("BlueButton"),
			Padding = new Thickness(16, 8, 16, 8),
			FontSize = 13
		};
		var btnEndFinal = new Button
		{
			Content = "✓ ЗАВЕРШИТЬ ФИНАЛ",
			Style = (Style)FindResource("GreenButton"),
			Padding = new Thickness(16, 8, 16, 8),
			FontSize = 13,
			Margin = new Thickness(10, 0, 10, 0),
			Visibility = Visibility.Collapsed  // покажем при старте финала
		};
		btnEndFinal.Click += BtnEndFinal_Click;
		toolbar.Children.Add(btnEndFinal);
		_finalEndButton = btnEndFinal;
		
		btnResetFinal.Click += (_, _) =>
		{
			if (_finalActive && !string.IsNullOrEmpty(_currentRoom) && _client.Connected)
			{
				_ = _client.EmitAsync("endFinal", new object[]
				{
					new { roomNumber = _currentRoom }
				});
			}
			_finalActive = false;
			_finalBetsApplied.Clear();
			_finalAnswers.Clear();
			FinalAnswersPanel.Visibility = Visibility.Collapsed;
			if (_finalEndButton != null)
				_finalEndButton.Visibility = Visibility.Collapsed;

			if (_finalRound is null)
				return;

			_activeFinalThemes = _finalRound.FinalThemes.ToList();
			SaveGameState();

			var toRemove = _usedCells.Where(k => k.StartsWith("final|")).ToList();
			foreach (var k in toRemove)
				_usedCells.Remove(k);

			RenderFinalPanel();

			AdminAnswerPanel.Visibility = Visibility.Collapsed;
			BtnShowAnswer.IsEnabled = false;
			BtnHideQuestion.IsEnabled = false;

			// Если табло в режиме финала и без вопроса — обновляем список
			if (_boardWindow is { IsLoaded: true } && !_boardWindow.IsQuestionVisible)
			{
				int roundIndex = _rounds.IndexOf(_finalRound);
				_boardWindow.ShowFinal(roundIndex, _activeFinalThemes, _usedCells);
			}
		};

		toolbar.Children.Add(btnResetFinal);

		Grid.SetRow(toolbar, 0);
		root.Children.Add(toolbar);

		// Область отрисовки тем
		var host = new Grid();
		_finalThemesPanel = host;
		Grid.SetRow(host, 1);
		root.Children.Add(host);

		RenderFinalPanel();
		return root;
	}

	private void RenderFinalPanel()
	{
		if (_finalThemesPanel is null || _finalRound is null)
			return;

		_finalThemesPanel.Children.Clear();
		int count = _activeFinalThemes.Count;

		// --- Нет тем ---
		if (count == 0)
		{
			_finalThemesPanel.Children.Add(new TextBlock
			{
				Text = "Все темы удалены.\nНажмите «Сбросить финал», чтобы вернуть список.",
				Foreground = (Brush)FindResource("TextMuted"),
				FontSize = 16,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				TextAlignment = TextAlignment.Center,
				TextWrapping = TextWrapping.Wrap
			});
			return;
		}

		// --- Одна тема — большая кнопка ---
		if (count == 1)
		{
			var theme = _activeFinalThemes[0];
			int roundIndex = _rounds.IndexOf(_finalRound);
			string key = $"final|{roundIndex}|{theme.Name}";
			bool used = _usedCells.Contains(key);

			var bigBtn = new Button
			{
				Content = $"ИГРАТЬ\n\n{theme.Name}",
				Style = (Style)FindResource("GoldButton"),
				FontSize = 30,
				FontWeight = FontWeights.Bold,
				Padding = new Thickness(60, 40, 60, 40),
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				IsEnabled = !used,
				Tag = theme
			};
			bigBtn.Click += FinalThemePlay_Click;

			_finalThemesPanel.Children.Add(bigBtn);
			// Осталась одна тема → запускаем финальную фазу у игроков
			StartFinalPhase();
			return;
		}

		// --- Много тем — карточки с ✕ ---
		var wrap = new WrapPanel
		{
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Top,
			Orientation = System.Windows.Controls.Orientation.Vertical
		};

		foreach (var theme in _activeFinalThemes)
			wrap.Children.Add(BuildFinalThemeCard(theme));

		_finalThemesPanel.Children.Add(new ScrollViewer
		{
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			Content = wrap
		});
	}

	private void StartFinalPhase()
	{
		if (_finalActive)
			return;
		if (string.IsNullOrEmpty(_currentRoom) || !_client.Connected)
			return;

		_finalActive = true;
		SaveGameState();
		_finalBetsApplied.Clear();
		_finalAnswers.Clear();

		_ = _client.EmitAsync("startFinal", new object[]
		{
		new { roomNumber = _currentRoom }
		});

		FinalAnswersPanel.Visibility = Visibility.Visible;
		FinalAnswersEmpty.Visibility = Visibility.Visible;
		FinalAnswersScroll.Visibility = Visibility.Collapsed;

		if (_finalEndButton != null)
			_finalEndButton.Visibility = Visibility.Visible;
	}

	private Border BuildFinalThemeCard(FinalTheme theme)
	{
		var border = new Border
		{
			Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x1E, 0x3A)),
			CornerRadius = new CornerRadius(10),
			Padding = new Thickness(18, 14, 12, 14),
			Margin = new Thickness(6),
			MinWidth = 180
		};

		var grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

		var nameText = new TextBlock
		{
			Text = theme.Name,
			Foreground = (Brush)FindResource("AccentGold"),
			FontSize = 20,
			FontWeight = FontWeights.Bold,
			TextWrapping = TextWrapping.Wrap,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0, 0, 10, 0)
		};
		Grid.SetColumn(nameText, 0);
		grid.Children.Add(nameText);

		var btnRemove = new Button
		{
			Content = "✕",
			Style = (Style)FindResource("RedButton"),
			Width = 34,
			Height = 34,
			Padding = new Thickness(0),
			FontSize = 16,
			ToolTip = "Убрать тему из розыгрыша",
			Tag = theme
		};
		btnRemove.Click += FinalThemeRemove_Click;
		Grid.SetColumn(btnRemove, 1);
		grid.Children.Add(btnRemove);

		border.Child = grid;
		return border;
	}

	private void FinalThemeRemove_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not Button btn || btn.Tag is not FinalTheme theme)
			return;

		_activeFinalThemes.Remove(theme);
		SaveGameState();
		RenderFinalPanel();

		// Обновляем табло, если оно в режиме финала
		if (_boardWindow is { IsLoaded: true } && !_boardWindow.IsQuestionVisible)
		{
			int roundIndex = _rounds.IndexOf(_finalRound!);
			_boardWindow.ShowFinal(roundIndex, _activeFinalThemes, _usedCells);
		}
	}

	private void FinalThemePlay_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not Button btn || btn.Tag is not FinalTheme theme)
			return;
		if (_finalRound is null)
			return;

		int roundIndex = _rounds.IndexOf(_finalRound);
		string key = $"final|{roundIndex}|{theme.Name}";

		if (_usedCells.Contains(key))
			return;
		_usedCells.Add(key);
		SaveGameState();
		// Переводим игроков на фазу ответов
		if (!string.IsNullOrEmpty(_currentRoom) && _client.Connected)
		{
			_ = _client.EmitAsync("openFinalAnswers", new object[]
			{
			new { roomNumber = _currentRoom }
			});
		}

		ShowBoardWindow();
		this.Activate();

		_boardWindow!.ShowFinal(roundIndex, _activeFinalThemes, _usedCells);
		_boardWindow!.ShowQuestion(
			theme.Name,
			0,
			theme.Question ?? new ContentBlock(),
			theme.Answer ?? new ContentBlock(),
			isFinal: true);

		ShowAdminCard(theme.Name, 0, theme.Question, theme.Answer, isFinal: true);

		BtnShowAnswer.IsEnabled = true;
		BtnHideQuestion.IsEnabled = true;

		btn.IsEnabled = false;
	}
	// =====================================================================
	// ОБРАБОТКА КЛИКА ПО ЯЧЕЙКЕ
	// =====================================================================
	private void CellButton_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not Button btn)
			return;
		if (btn.Tag is not CellTag tag)
			return;
		if (tag.Theme is null)
			return;

		var theme = tag.Theme;
		int price = tag.Price;

		var q = theme.GetQuestion(price);
		if (q is null)
			return;

		string usedKey = $"{tag.RoundIndex}|{theme.Name}|{price}";
		_usedCells.Add(usedKey);
		SaveGameState();
		btn.IsEnabled = false;

		ShowBoardWindow();
		// Гарантируем, что фокус клавиатуры остаётся в главном окне,
		// чтобы горячие клавиши срабатывали сразу
		this.Activate();

		_boardWindow!.ShowQuestion(
			theme.Name,
			price,
			q.Question ?? new ContentBlock(),
			q.Answer ?? new ContentBlock());
		_boardWindow!.MarkCellUsed(theme.Name, price);

		// Цена во все строки игроков
		foreach (var p in _players)
			p.BetInput = price.ToString();

		// Панель ответа админа
		ShowAdminCard(theme.Name, price, q.Question, q.Answer);

		BtnShowAnswer.IsEnabled = true;
		BtnHideQuestion.IsEnabled = true;
	}

	// =====================================================================
	// ОКНО ТАБЛО
	// =====================================================================
	private void BtnOpenBoard_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			ShowBoardWindow();
		}
		catch (Exception ex)
		{
			MessageBox.Show(
				$"Не удалось открыть табло:\n\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}",
				"Ошибка",
				MessageBoxButton.OK,
				MessageBoxImage.Error);
		}
	}

	private void ShowBoardWindow()
	{
		if (_boardWindow is null || !_boardWindow.IsLoaded)
		{
			_boardWindow = new GameBoardWindow();

			_boardWindow.HotkeyPressed += key =>
				Dispatcher.Invoke(() => HandleHotkey(key));

			_boardWindow.Closed += (_, _) =>
			{
				_boardWindow = null;
				BtnShowAnswer.IsEnabled = false;
				BtnHideQuestion.IsEnabled = false;
			};

			_boardWindow.ShowActivated = false;
			_boardWindow.Show();

			SyncBoardToCurrentTab();
			RefreshBoardPlayers();
			_boardWindow.HighlightFirst(_currentLeader);
		}
		else
		{
			// Намеренно НЕ активируем табло — иначе фокус клавиатуры уходит туда,
			// и хоткеи главного окна перестают срабатывать.
			// Если окно свёрнуто — разворачиваем без активации.
			if (_boardWindow.WindowState == WindowState.Minimized)
				_boardWindow.WindowState = WindowState.Normal;
		}
	}

	/// <summary>
	/// Синхронизирует табло с текущей активной вкладкой админа.
	/// Вызывается при открытии табло и при смене вкладки.
	/// </summary>
	private void SyncBoardToCurrentTab()
	{
		if (_boardWindow is null || !_boardWindow.IsLoaded)
			return;

		int idx = RoundsTabControl.SelectedIndex;
		if (idx < 0 || idx >= _rounds.Count)
			return;

		var round = _rounds[idx];

		if (round.IsFinal)
		{
			_boardWindow.ShowFinal(idx, _activeFinalThemes, _usedCells);
		}
		else
		{
			_boardWindow.ShowRound(idx, round, _usedCells);
		}
	}
	// =====================================================================
	// РАБОТА С ИГРОКАМИ
	// =====================================================================
	private void SyncPlayers(List<string> names)
	{
		// 1) Удаляем тех, кого больше нет на сервере
		var incoming = new HashSet<string>(names);
		for (int i = _players.Count - 1; i >= 0; i--)
		{
			if (!incoming.Contains(_players[i].Name))
				_players.RemoveAt(i);
		}

		// 2) Добавляем новых
		foreach (var name in names)
		{
			if (_players.All(p => p.Name != name))
				_players.Add(new PlayerRow(name) { IsOnline = true });
		}
	}

	private void ApplyScores(List<System.Text.Json.JsonElement> list)
	{
		// 1) Собираем пришедшие имена
		var incoming = new HashSet<string>();
		foreach (var el in list)
		{
			if (el.ValueKind == System.Text.Json.JsonValueKind.Object
				&& el.TryGetProperty("name", out var n))
			{
				var nm = n.GetString();
				if (!string.IsNullOrEmpty(nm))
					incoming.Add(nm);
			}
		}

		// 2) Удаляем из таблицы тех, кого нет на сервере
		for (int i = _players.Count - 1; i >= 0; i--)
		{
			if (!incoming.Contains(_players[i].Name))
				_players.RemoveAt(i);
		}

		// 3) Обновляем / добавляем
		foreach (var el in list)
		{
			if (el.ValueKind != System.Text.Json.JsonValueKind.Object)
				continue;

			string name = el.TryGetProperty("name", out var n2) ? (n2.GetString() ?? "") : "";
			if (string.IsNullOrEmpty(name))
				continue;

			int score = el.TryGetProperty("score", out var s) && s.TryGetInt32(out var si) ? si : 0;
			bool online = el.TryGetProperty("online", out var o)
						  && o.ValueKind == System.Text.Json.JsonValueKind.True;

			var row = _players.FirstOrDefault(p => p.Name == name);
			if (row is null)
			{
				row = new PlayerRow(name);
				_players.Add(row);
			}
			row.Score = score;
			row.IsOnline = online;
		}
	}

	private void UpdateTop3(List<string> top)
    {
        Top1Text.Text = "1. " + (top.Count > 0 ? top[0] : "—");
        Top2Text.Text = "2. " + (top.Count > 1 ? top[1] : "—");
        Top3Text.Text = "3. " + (top.Count > 2 ? top[2] : "—");
    }

    // =====================================================================
    // КНОПКИ +/− ОЧКОВ
    // =====================================================================
    private async void BtnPlusScore_Click(object sender, RoutedEventArgs e)
        => await ChangeScoreAsync(sender, +1);

    private async void BtnMinusScore_Click(object sender, RoutedEventArgs e)
        => await ChangeScoreAsync(sender, -1);

    private async Task ChangeScoreAsync(object sender, int sign)
    {
        if (sender is not Button btn || btn.Tag is not PlayerRow player) return;
        if (_currentRoom is null || !_client.Connected) return;

        if (!int.TryParse(player.BetInput, out var amount) || amount < 0)
        {
            MessageBox.Show("Введите корректное число очков.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await _client.EmitAsync("updateScore", new object[] { new
        {
            roomNumber = _currentRoom,
            playerName = player.Name,
            delta = amount * sign
        }});
    }

	// =====================================================================
	// СОЗДАНИЕ КОМНАТЫ / СБРОС
	// =====================================================================
	private async void BtnCreateRoom_Click(object sender, RoutedEventArgs e)
	{
		_currentRoom = TxtRoomNumber.Text.Trim();
		if (string.IsNullOrEmpty(_currentRoom))
			return;

		try
		{
			StatusText.Text = "Подключение...";
			BtnCreateRoom.IsEnabled = false;

			if (!_client.Connected)
				await _client.ConnectAsync();

			await _client.EmitAsync("createRoom", new object[] { _currentRoom });

			StatusText.Text = $"Статус: Активна комната №{_currentRoom}";
			TxtRoomNumber.IsEnabled = false;

			// Восстановление сохранённой партии
			if (_savedState != null && _savedState.RoomNumber == _currentRoom)
			{
				RestoreGameState(_savedState);
				StatusText.Text = $"Статус: Комната №{_currentRoom} (восстановлена)";
			}
		}
		catch (Exception ex)
		{
			BtnCreateRoom.IsEnabled = true;
			StatusText.Text = "Ошибка подключения!";
			MessageBox.Show($"Ошибка: {ex.Message}");
		}
	}

	/// <summary>
	/// Восстанавливает состояние партии из game_state.json.
	/// Вызывается сразу после успешного createRoom, если номер комнаты совпадает.
	/// </summary>
	private void RestoreGameState(GameState state)
	{
		// 1) Сыгранные ячейки
		_usedCells.Clear();
		foreach (var key in state.UsedCells)
			_usedCells.Add(key);

		// 2) Лидер и финальные флаги — до перестроения UI
		_currentLeader = state.CurrentLeader;
		_finalActive = state.FinalActive;

		// 3) Перестраиваем вкладки и игровое поле
		BuildRoundsUi();

		// 4) BuildRoundsUi сбрасывает _activeFinalThemes — восстанавливаем нужное подмножество
		if (_finalRound != null && state.FinalRemovedThemes.Count > 0)
		{
			var removedSet = new HashSet<string>(state.FinalRemovedThemes);
			_activeFinalThemes = _finalRound.FinalThemes
				.Where(t => !removedSet.Contains(t.Name))
				.ToList();
			RenderFinalPanel();
		}

		// 5) Панель ответов финала — если финал был активен
		if (_finalActive)
		{
			FinalAnswersPanel.Visibility = Visibility.Visible;
			if (_finalEndButton != null)
				_finalEndButton.Visibility = Visibility.Visible;
		}

		// 6) Обновить табло, если открыто
		if (_boardWindow is { IsLoaded: true })
		{
			SyncBoardToCurrentTab();
			_boardWindow.HighlightFirst(_currentLeader);
		}
	}

	private async void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        if (!_client.Connected || _currentRoom is null) return;

        BtnReset.IsEnabled = false;
        try
        {
            await _client.EmitAsync("resetGame", new object[] { _currentRoom });
        }
        catch
        {
            // Что-то пошло не так — вернём кнопку
            BtnReset.IsEnabled = true;
        }
    }

    // =====================================================================
    // УПРАВЛЕНИЕ ТАБЛО
    // =====================================================================
    private void BtnShowAnswer_Click(object sender, RoutedEventArgs e)
    {
        _boardWindow?.ShowAnswer();
        BtnShowAnswer.IsEnabled = false;
    }

	private void BtnHideQuestion_Click(object sender, RoutedEventArgs e)
	{
		_boardWindow?.ShowGrid();
		BtnShowAnswer.IsEnabled = false;
		BtnHideQuestion.IsEnabled = false;

		AdminAnswerPanel.Visibility = Visibility.Collapsed;
		_adminAnswerMedia.Stop();
		_adminQuestionMedia.Stop();

		// Синхронизируем табло с текущей активной вкладкой админа —
		// за время показа вопроса админ мог переключить раунд.
		if (_boardWindow is { IsLoaded: true })
		{
			SyncBoardToCurrentTab();
		}
	}

	// =====================================================================
	// КАРТОЧКА ВОПРОСА ДЛЯ АДМИНА
	// =====================================================================
	private void ShowAdminCard(string themeName, int price, ContentBlock? question, ContentBlock? answer, bool isFinal = false)
	{
		AdminQuestionMeta.Text = isFinal
			? $"ФИНАЛ — {themeName}"
			: $"{themeName} • {price}";

		FillAdminBlock(AdminQuestionImage, AdminQuestionText,
					   AdminQuestionAudioBtn, _adminQuestionMedia, question);

		FillAdminBlock(AdminAnswerImage, AdminAnswerText,
					   AdminAnswerAudioBtn, _adminAnswerMedia, answer);

		AdminAnswerPanel.Visibility = Visibility.Visible;
	}

	private void FillAdminBlock(
		System.Windows.Controls.Image imgCtrl,
		System.Windows.Controls.TextBlock txtCtrl,
		System.Windows.Controls.Button audioBtn,
		MediaPlayer media,
		ContentBlock? block)
	{
		// --- Картинка ---
		if (block?.HasImage == true)
		{
			var path = QuestionsLoader.ResolveMediaPath(block.Image);
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
			var audioPath = QuestionsLoader.ResolveMediaPath(block.Audio);
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

	private void AdminAnswerAudio_Click(object sender, RoutedEventArgs e)
	{
		if (_adminAnswerMedia.Source is null)
			return;
		_adminAnswerMedia.Position = TimeSpan.Zero;
		_adminAnswerMedia.Play();
	}

	private void AdminQuestionAudio_Click(object sender, RoutedEventArgs e)
	{
		if (_adminQuestionMedia.Source is null)
			return;
		_adminQuestionMedia.Position = TimeSpan.Zero;
		_adminQuestionMedia.Play();
	}

	private void BtnEditThemes_Click(object sender, RoutedEventArgs e)
	{
		var confirm = MessageBox.Show(
			"Изменения тем вступят в силу сразу и сбросят текущее игровое поле.\nПродолжить?",
			"Редактор вопросов",
			MessageBoxButton.YesNo, MessageBoxImage.Question);
		if (confirm != MessageBoxResult.Yes)
			return;

		var editor = new ThemeEditorWindow { Owner = this };
		var result = editor.ShowDialog();

		if (result != true)
			return;

		var reloaded = QuestionsLoader.TryLoadRounds(out var err);
		if (reloaded == null)
		{
			MessageBox.Show($"Не удалось перезагрузить вопросы:\n{err}",
				"Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
			return;
		}

		_rounds = reloaded;

		// Сбрасываем состояние поля
		_usedCells.Clear();
		_finalActive = false;
		_finalBetsApplied.Clear();
		_finalAnswers.Clear();
		FinalAnswersPanel.Visibility = Visibility.Collapsed;
		_finalEndButton = null;

		BuildRoundsUi();

		if (_boardWindow != null && _boardWindow.IsLoaded)
		{
			_boardWindow.Close();
			_boardWindow = null;
		}

		AdminAnswerPanel.Visibility = Visibility.Collapsed;
		BtnShowAnswer.IsEnabled = false;
		BtnHideQuestion.IsEnabled = false;

		CheckMediaOrWarn();
	}

	/// <summary>
	/// Проверяет вопросы на отсутствующие медиа-файлы.
	/// Показывает предупреждение, если что-то не найдено.
	/// </summary>
	private void CheckMediaOrWarn()
	{
		var problems = QuestionsLoader.ValidateMedia(_rounds);
		if (problems.Count == 0)
			return;

		// Ограничим вывод, чтобы окно не растянулось на 100 строк
		const int maxShow = 12;
		var shown = problems.Take(maxShow).ToList();
		var text = new System.Text.StringBuilder();
		text.AppendLine($"Не найдено медиа-файлов: {problems.Count}");
		text.AppendLine();
		foreach (var p in shown)
			text.AppendLine("• " + p);
		if (problems.Count > maxShow)
			text.AppendLine($"…и ещё {problems.Count - maxShow}.");

		MessageBox.Show(text.ToString(),
			"Отсутствующие файлы",
			MessageBoxButton.OK, MessageBoxImage.Warning);
	}

	private void HandleHotkey(System.Windows.Input.Key key)
	{
		switch (key)
		{
			case System.Windows.Input.Key.Space:
				if (_boardWindow is { IsLoaded: true, IsQuestionVisible: true })
					BtnShowAnswer_Click(this, new RoutedEventArgs());
				break;

			case System.Windows.Input.Key.Q:
				if (_boardWindow is { IsLoaded: true, IsQuestionVisible: true })
					BtnHideQuestion_Click(this, new RoutedEventArgs());
				break;

			case System.Windows.Input.Key.R:
				if (!string.IsNullOrEmpty(_currentRoom) && _client.Connected)
					BtnReset_Click(this, new RoutedEventArgs());
				break;
		}
	}

	/// <summary>
	/// Отправляет на табло актуальный список игроков с очками.
	/// </summary>
	private void RefreshBoardPlayers()
	{
		if (_boardWindow is null || !_boardWindow.IsLoaded)
			return;

		var data = _players
			.Select(p => (p.Name, p.Score, p.IsOnline))
			.ToList();

		_boardWindow.UpdatePlayers(data);
	}

	protected override void OnClosed(EventArgs e)
	{
		SaveGameState();   // сохранить перед выходом
		_adminAnswerMedia.Close();
		_adminQuestionMedia.Close();
		base.OnClosed(e);
	}

	protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
	{
		// Не мешаем вводу в текстовых полях
		var focused = System.Windows.Input.Keyboard.FocusedElement;
		if (focused is System.Windows.Controls.TextBox || focused is System.Windows.Controls.PasswordBox)
		{
			base.OnPreviewKeyDown(e);
			return;
		}

		// Автоповтор
		if (e.IsRepeat)
		{
			base.OnPreviewKeyDown(e);
			return;
		}

		// Модификаторы
		var mods = System.Windows.Input.Keyboard.Modifiers;
		if ((mods & (System.Windows.Input.ModifierKeys.Control
				   | System.Windows.Input.ModifierKeys.Alt)) != 0)
		{
			base.OnPreviewKeyDown(e);
			return;
		}

		if (e.Key == System.Windows.Input.Key.Space
			|| e.Key == System.Windows.Input.Key.Q
			|| e.Key == System.Windows.Input.Key.R)
		{
			HandleHotkey(e.Key);
			e.Handled = true;
		}

		base.OnPreviewKeyDown(e);
	}

	private void RoundsTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!ReferenceEquals(e.OriginalSource, RoundsTabControl))
			return;

		// Если на табло открыт вопрос — не трогаем ни панель ответа, ни кнопки
		if (_boardWindow is { IsLoaded: true } && _boardWindow.IsQuestionVisible)
			return;

		AdminAnswerPanel.Visibility = Visibility.Collapsed;
		BtnShowAnswer.IsEnabled = false;
		BtnHideQuestion.IsEnabled = false;

		_adminAnswerMedia.Stop();
		_adminQuestionMedia.Stop();

		if (_boardWindow is { IsLoaded: true })
		{
			SyncBoardToCurrentTab();
		}
	}

	private void UpdateFinalAnswers(List<System.Text.Json.JsonElement> list)
	{
		_finalAnswers.Clear();

		foreach (var el in list)
		{
			if (el.ValueKind != System.Text.Json.JsonValueKind.Object)
				continue;

			string name = el.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
			if (string.IsNullOrEmpty(name))
				continue;

			int bet = el.TryGetProperty("bet", out var b) && b.TryGetInt32(out var bi) ? bi : 0;
			string answer = el.TryGetProperty("answer", out var a) ? (a.GetString() ?? "") : "";
			bool betSubmitted = el.TryGetProperty("betSubmitted", out var bs)
								&& bs.ValueKind == System.Text.Json.JsonValueKind.True;
			bool answerSubmitted = el.TryGetProperty("answerSubmitted", out var asb)
								   && asb.ValueKind == System.Text.Json.JsonValueKind.True;

			_finalAnswers.Add(new FinalAnswerView
			{
				Name = name,
				Bet = bet,
				Answer = answer,
				BetSubmitted = betSubmitted,
				AnswerSubmitted = answerSubmitted
			});

			// Автоподстановка ставки в BetInput — один раз
			if (betSubmitted && _finalBetsApplied.Add(name))
			{
				var row = _players.FirstOrDefault(p => p.Name == name);
				if (row != null && bet > 0)
					row.BetInput = bet.ToString();
			}
		}

		bool any = _finalAnswers.Count > 0;
		FinalAnswersScroll.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
		FinalAnswersEmpty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
	}

	private async void BtnEndFinal_Click(object sender, RoutedEventArgs e)
	{
		if (string.IsNullOrEmpty(_currentRoom) || !_client.Connected)
			return;

		await _client.EmitAsync("endFinal", new object[]
		{
		new { roomNumber = _currentRoom }
		});

		_finalActive = false;
		SaveGameState();
		_finalBetsApplied.Clear();
		_finalAnswers.Clear();

		FinalAnswersPanel.Visibility = Visibility.Collapsed;

		if (_finalEndButton != null)
			_finalEndButton.Visibility = Visibility.Collapsed;
	}

	private async void BtnResetAll_Click(object sender, RoutedEventArgs e)
	{
		if (string.IsNullOrEmpty(_currentRoom) || !_client.Connected)
		{
			MessageBox.Show("Сначала создайте комнату.",
				"Сброс игры", MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}

		var confirm = MessageBox.Show(
			"Полный сброс игры:\n" +
			"• очки всех игроков обнулятся;\n" +
			"• все сыгранные вопросы станут доступны снова;\n" +
			"• финал вернётся к исходному списку тем;\n" +
			"• ТОП-3 и подсветка лидера сбросятся.\n\n" +
			"Продолжить?",
			"Подтверждение",
			MessageBoxButton.YesNo,
			MessageBoxImage.Warning);

		if (confirm != MessageBoxResult.Yes)
			return;

		try
		{
			await _client.EmitAsync("resetAllGame", new object[]
			{
			new { roomNumber = _currentRoom }
			});
		}
		catch (Exception ex)
		{
			MessageBox.Show($"Не удалось выполнить сброс:\n{ex.Message}",
				"Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}
}

/// <summary>
/// DTO, приходящий с сервера в onScoresUpdated.
/// </summary>
public class ScorePayload
{
    public string Name { get; set; } = string.Empty;
    public int Score { get; set; }
    public bool Online { get; set; }
}

/// <summary>
/// Информация, вложенная в Tag кнопки-ячейки игрового поля.
/// </summary>
internal class CellTag
{
	public Round Round = null!;
	public int RoundIndex;
	public Theme? Theme;
	public int Price;
}

public class FinalAnswerView
{
	public string Name { get; set; } = "";
	public int Bet { get; set; }
	public string Answer { get; set; } = "";
	public bool BetSubmitted { get; set; }
	public bool AnswerSubmitted { get; set; }

	public string BetLabel => BetSubmitted ? (Bet > 0 ? $"+{Bet}" : "1") : "—";
	public string AnswerLabel => AnswerSubmitted ? Answer : "(ожидается ответ)";
}