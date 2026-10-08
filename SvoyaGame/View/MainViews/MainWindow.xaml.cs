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
	private readonly HashSet<string> _usedCells = new(); // "Тема|Цена"
	private readonly MediaPlayer _adminAnswerMedia = new();
	private readonly MediaPlayer _adminQuestionMedia = new();
	private string? _currentRoom;
    private GameBoardWindow? _boardWindow;

    // Хранит ссылки на кнопки ячеек: ключ = "тема|цена"
    private readonly Dictionary<string, System.Windows.Controls.Button> _cellButtons = new();

    public MainWindow()
    {
        InitializeComponent();

		// ---- Загрузка вопросов ----
		_rounds = QuestionsLoader.TryLoadRounds(out var loadError) ?? new List<Round>();
		if (loadError != null)
		{
			MessageBox.Show($"Не удалось загрузить questions.json:\n{loadError}",
				"Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
		}

		GridPlayers.ItemsSource = _players;
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
            });
            return Task.CompletedTask;
        });

        _client.On("onWinnerDetected", response =>
        {
            Dispatcher.Invoke(() =>
            {
                BtnReset.IsEnabled = true;
            });
            return Task.CompletedTask;
        });

        _client.On("onScoresUpdated", response =>
        {
            var list = response.GetValue<List<System.Text.Json.JsonElement>>(0);
            Dispatcher.Invoke(() => ApplyScores(list));
            return Task.CompletedTask;
        });

        _client.On("onGameReset", response =>
        {
            Dispatcher.Invoke(() =>
            {
                UpdateTop3(new List<string>());
                BtnReset.IsEnabled = false;
                // Дополнительно: разблокировать нажатия в таблице игроков,
                // если ты решил их блокировать после ответа (см. п.4 ниже)
            });
            return Task.CompletedTask;
        });
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
						FontSize = 13,
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
					var btn = new Button
					{
						Content = price.ToString(),
						Style = (Style)FindResource("GreenButton"),
						Margin = new Thickness(3),
						FontSize = 16,
						IsEnabled = theme != null && theme.GetQuestion(price) != null,
						Tag = new CellTag
						{
							Round = round,
							RoundIndex = ri,
							Theme = theme,
							Price = price
						}
					};
					btn.Click += CellButton_Click;

					if (theme != null)
						_cellButtons[$"{ri}|{theme.Name}|{price}"] = btn;

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
		btnResetFinal.Click += (_, _) =>
		{
			if (_finalRound is null)
				return;

			_activeFinalThemes = _finalRound.FinalThemes.ToList();

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

		ShowBoardWindow();

		// Если табло ещё не в режиме финала — переведём его туда (без затемнения: MarkFinalThemePlayed всё сделает)
		_boardWindow!.ShowFinal(roundIndex, _activeFinalThemes, _usedCells);

		// Показываем вопрос
		_boardWindow!.ShowQuestion(
			theme.Name,
			0,
			theme.Question ?? new ContentBlock(),
			theme.Answer ?? new ContentBlock(),
			isFinal: true);

		// Панель ответа админа
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
		btn.IsEnabled = false;

			ShowBoardWindow();
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

			_boardWindow.Closed += (_, _) =>
			{
				_boardWindow = null;
				BtnShowAnswer.IsEnabled = false;
				BtnHideQuestion.IsEnabled = false;
			};

			_boardWindow.Show();
			SyncBoardToCurrentTab();
			RefreshBoardPlayers();
		}
		else
		{
			_boardWindow.Activate();
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
        // Удаляем тех, кого больше нет (на практике сервер их не удаляет)
        // Добавляем новых
        foreach (var name in names)
        {
            if (_players.All(p => p.Name != name))
                _players.Add(new PlayerRow(name) { IsOnline = true });
        }
		RefreshBoardPlayers();
	}

    private void ApplyScores(List<System.Text.Json.JsonElement> list)
    {
        foreach (var el in list)
        {
            if (el.ValueKind != System.Text.Json.JsonValueKind.Object) continue;

            string name = el.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
            int score = el.TryGetProperty("score", out var s) && s.TryGetInt32(out var si) ? si : 0;
            bool online = el.TryGetProperty("online", out var o) && o.ValueKind == System.Text.Json.JsonValueKind.True;

            if (string.IsNullOrEmpty(name)) continue;

            var row = _players.FirstOrDefault(p => p.Name == name);
            if (row is null)
            {
                row = new PlayerRow(name);
                _players.Add(row);
            }
            row.Score = score;
            row.IsOnline = online;
        }
		RefreshBoardPlayers();
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
        if (string.IsNullOrEmpty(_currentRoom)) return;

        try
        {
            StatusText.Text = "Подключение...";
            BtnCreateRoom.IsEnabled = false;

            if (!_client.Connected)
                await _client.ConnectAsync();

            await _client.EmitAsync("createRoom", new object[] { _currentRoom });

            StatusText.Text = $"Статус: Активна комната №{_currentRoom}";
            TxtRoomNumber.IsEnabled = false;
            BtnReset.IsEnabled = true;
        }
        catch (Exception ex)
        {
            BtnCreateRoom.IsEnabled = true;
            StatusText.Text = "Ошибка подключения!";
            MessageBox.Show($"Ошибка: {ex.Message}");
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
		_adminAnswerMedia.Close();
		_adminQuestionMedia.Close();
		base.OnClosed(e);
	}

	private void RoundsTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!ReferenceEquals(e.OriginalSource, RoundsTabControl))
			return;

		// Если на табло открыт вопрос — не трогаем ни панель ответа, ни кнопки.
		// Админ должен иметь возможность дочитать ответ и свернуть вопрос.
		if (_boardWindow is { IsLoaded: true } && _boardWindow.IsQuestionVisible)
			return;

		// Иначе — сброс UI и синхронизация табло с новой вкладкой
		AdminAnswerPanel.Visibility = Visibility.Collapsed;
		_adminAnswerMedia.Stop();
		_adminQuestionMedia.Stop();
		BtnShowAnswer.IsEnabled = false;
		BtnHideQuestion.IsEnabled = false;

		if (_boardWindow is { IsLoaded: true })
		{
			SyncBoardToCurrentTab();
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