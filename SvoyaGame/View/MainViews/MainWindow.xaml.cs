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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;

namespace SvoyaGame.View.MainViews;

public partial class MainWindow : Window
{
    private static int[] Prices = { 100, 200, 400, 700, 1000 };

    private readonly SocketIOClient.SocketIO _client;
    private readonly ObservableCollection<PlayerRow> _players = new();
	private List<Theme> _themes;
	private readonly HashSet<string> _usedCells = new(); // "Тема|Цена"
	private readonly MediaPlayer _adminAnswerMedia = new();
	private string? _currentRoom;
    private GameBoardWindow? _boardWindow;

    // Хранит ссылки на кнопки ячеек: ключ = "тема|цена"
    private readonly Dictionary<string, System.Windows.Controls.Button> _cellButtons = new();

    public MainWindow()
    {
        InitializeComponent();

        // ---- Загрузка вопросов ----
        _themes = QuestionsLoader.TryLoad(out var loadError) ?? new List<Theme>();


		//// ВРЕМЕННО — для проверки парсера
  //      var sb = new System.Text.StringBuilder();
		//sb.AppendLine($"Тем: {_themes.Count}");
		//foreach (var t in _themes)
		//{
		//	sb.AppendLine($"— {t.Name}");
		//	foreach (var price in new[] { 100, 200, 400, 700, 1000 })
		//	{
		//		var q = t.GetQuestion(price);
		//		if (q == null)
		//		{ sb.AppendLine($"   {price}: <нет>"); continue; }

		//		var qText = q.Question?.Text ?? "<нет текста>";
		//		var qImg = q.Question?.Image ?? "-";
		//		var qAud = q.Question?.Audio ?? "-";
		//		var aText = q.Answer?.Text ?? "<нет текста>";
		//		sb.AppendLine($"   {price}: Q=\"{qText}\" img={qImg} aud={qAud} | A=\"{aText}\"");
		//	}
		//}
		//MessageBox.Show(sb.ToString(), "Содержимое questions.json");

		if (loadError != null)
        {
            MessageBox.Show($"Не удалось загрузить questions.json:\n{loadError}",
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // ---- Привязки ----
        GridPlayers.ItemsSource = _players;

        // ---- Построение игрового поля ----
        BuildGameGrid();

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
    private void BuildGameGrid()
    {
        GameGrid.Children.Clear();
        _cellButtons.Clear();

        int rowCount = Math.Max(1, _themes.Count);   // до 5 тем
        int colCount = 1 + Prices.Length;            // тема + 5 цен

        GameGrid.Rows = Math.Max(rowCount, 1);
        GameGrid.Columns = colCount;

        for (int r = 0; r < rowCount; r++)
        {
            var theme = r < _themes.Count ? _themes[r] : null;

            // --- Колонка с темой ---
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
            GameGrid.Children.Add(themeBox);

            // --- Колонки с ценами ---
            for (int c = 0; c < Prices.Length; c++)
            {
                int price = Prices[c];
                var btn = new Button
                {
                    Content = price.ToString(),
                    Style = (Style)FindResource("GreenButton"),
                    Margin = new Thickness(3),
                    FontSize = 16,
                    IsEnabled = theme != null && theme.GetQuestion(price) != null,
                    Tag = (Theme: theme, Price: price)
                };
                btn.Click += CellButton_Click;

                if (theme != null)
                    _cellButtons[$"{theme.Name}|{price}"] = btn;

                GameGrid.Children.Add(btn);
            }
        }
    }

    // =====================================================================
    // ОБРАБОТКА КЛИКА ПО ЯЧЕЙКЕ
    // =====================================================================
    private void CellButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not ValueTuple<Theme?, int> tag) return;
        if (tag.Item1 is null) return;

        var theme = tag.Item1;
        int price = tag.Item2;
        var q = theme.GetQuestion(price);
        if (q is null) return;

        // Блокируем кнопку, чтобы нельзя было нажать повторно
        btn.IsEnabled = false;
        _usedCells.Add($"{theme.Name}|{price}");

        ShowBoardWindow();
		_boardWindow!.ShowQuestion(
			theme.Name,
			price,
			q.Question ?? new ContentBlock(),
			q.Answer ?? new ContentBlock());

		ShowAdminAnswer(theme.Name, price, q.Answer);
		_boardWindow!.MarkCellUsed(theme.Name, price);

        // Подставляем цену во все строки таблицы игроков
        foreach (var player in _players)
            player.BetInput = price.ToString();

        // Активируем кнопки управления табло
        BtnShowAnswer.IsEnabled = true;
        BtnHideQuestion.IsEnabled = true;
    }



    // =====================================================================
    // ОКНО ТАБЛО
    // =====================================================================
    private void BtnOpenBoard_Click(object sender, RoutedEventArgs e) => ShowBoardWindow();

    private void ShowBoardWindow()
    {
        if (_boardWindow is null || !_boardWindow.IsLoaded)
        {
            _boardWindow = new GameBoardWindow(_themes, Prices);

            // Восстанавливаем состояние сыгранных ячеек
            foreach (var key in _usedCells)
            {
                var parts = key.Split('|');
                if (parts.Length == 2 && int.TryParse(parts[1], out var p))
                    _boardWindow.MarkCellUsed(parts[0], p);
            }

            _boardWindow.Closed += (_, _) =>
            {
                _boardWindow = null;
                BtnShowAnswer.IsEnabled = false;
                BtnHideQuestion.IsEnabled = false;
            };
            _boardWindow.Show();
        }
        else
        {
            _boardWindow.Activate();
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

        // Обновляем статусы (пока просто все online; после onScoresUpdated уточнится)
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
	}

	private void ShowAdminAnswer(string themeName, int price, ContentBlock? answer)
	{
		AdminQuestionMeta.Text = $"{themeName} • {price}";

		// Текст
		if (answer?.HasText == true)
		{
			AdminAnswerText.Text = answer.Text!;
			AdminAnswerText.Visibility = Visibility.Visible;
		}
		else
		{
			AdminAnswerText.Text = "";
			AdminAnswerText.Visibility = Visibility.Collapsed;
		}

		// Картинка
		if (answer?.HasImage == true)
		{
			var path = Services.QuestionsLoader.ResolveMediaPath(answer.Image);
			if (path != null && File.Exists(path))
			{
				try
				{
					var bmp = new BitmapImage();
					bmp.BeginInit();
					bmp.CacheOption = BitmapCacheOption.OnLoad;
					bmp.UriSource = new Uri(path, UriKind.Absolute);
					bmp.EndInit();
					AdminAnswerImage.Source = bmp;
					AdminAnswerImage.Visibility = Visibility.Visible;
				}
				catch
				{
					AdminAnswerImage.Source = null;
					AdminAnswerImage.Visibility = Visibility.Collapsed;
				}
			}
			else
			{
				AdminAnswerImage.Source = null;
				AdminAnswerImage.Visibility = Visibility.Collapsed;
			}
		}
		else
		{
			AdminAnswerImage.Source = null;
			AdminAnswerImage.Visibility = Visibility.Collapsed;
		}

		// --- Звук ---
		_adminAnswerMedia.Stop();
		_adminAnswerMedia.Close();

		if (answer?.HasAudio == true)
		{
			var audioPath = Services.QuestionsLoader.ResolveMediaPath(answer.Audio);
			if (audioPath != null && File.Exists(audioPath))
			{
				try
				{
					_adminAnswerMedia.Open(new Uri(audioPath, UriKind.Absolute));
					AdminAnswerAudioBtn.Visibility = Visibility.Visible;
				}
				catch
				{
					AdminAnswerAudioBtn.Visibility = Visibility.Collapsed;
				}
			}
			else
			{
				AdminAnswerAudioBtn.Visibility = Visibility.Collapsed;
			}
		}
		else
		{
			AdminAnswerAudioBtn.Visibility = Visibility.Collapsed;
		}

		AdminAnswerPanel.Visibility = Visibility.Visible;
	}

	private void AdminAnswerAudio_Click(object sender, RoutedEventArgs e)
	{
		if (_adminAnswerMedia.Source is null)
			return;
		_adminAnswerMedia.Position = TimeSpan.Zero;
		_adminAnswerMedia.Play();
	}
	private void BtnEditThemes_Click(object sender, RoutedEventArgs e)
	{
		// Предупреждение — редактирование сбрасывает текущее поле
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

		// Перезагрузка из файла
		var reloaded = QuestionsLoader.TryLoad(out var err);
		if (reloaded == null)
		{
			MessageBox.Show($"Не удалось перезагрузить вопросы:\n{err}",
				"Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
			return;
		}

		_themes = reloaded;

		// Пересборка поля и сброс состояния
		BuildGameGrid();

		// Табло держит старый список — пересоздадим его, если открыто
		if (_boardWindow != null && _boardWindow.IsLoaded)
		{
			_boardWindow.Close();
			_boardWindow = null;
		}

		// Скрыть панель ответа для админа, если была открыта
		AdminAnswerPanel.Visibility = Visibility.Collapsed;
		BtnShowAnswer.IsEnabled = false;
		BtnHideQuestion.IsEnabled = false;
	}

	protected override void OnClosed(EventArgs e)
	{
		_adminAnswerMedia.Close();
		base.OnClosed(e);
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