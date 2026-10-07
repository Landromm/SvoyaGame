using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SvoyaGame.Models;
using SvoyaGame.Services;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;

namespace SvoyaGame.View.MainViews;

public partial class ThemeEditorWindow : Window
{
	private static readonly int[] DefaultPrices = { 100, 200, 400, 700, 1000 };

	private readonly ObservableCollection<Round> _rounds;
	private readonly List<ContentRowControls> _questionRows = new();
	private readonly List<ContentRowControls> _answerRows = new();

	private readonly MediaPlayer _previewPlayer = new();

	private Round? _selectedRound;
	private Theme? _selectedTheme;
	private int[] _currentPrices = Array.Empty<int>();
	private bool _loadingUi;
	private bool _isDirty;

	public ThemeEditorWindow()
	{
		InitializeComponent();

		var loaded = QuestionsLoader.TryLoadRounds(out var err) ?? new List<Round>();
		if (err != null)
		{
			MessageBox.Show($"Не удалось загрузить questions.json:\n{err}",
				"Ошибка загрузки", MessageBoxButton.OK, MessageBoxImage.Warning);
		}

		_rounds = new ObservableCollection<Round>(loaded);
		RoundsList.ItemsSource = _rounds;

		if (_rounds.Count > 0)
			RoundsList.SelectedIndex = 0;
	}

	// =====================================================================
	// КОНТРОЛЫ ОДНОЙ СТРОКИ (вопрос/ответ)
	// =====================================================================
	private class ContentRowControls
	{
		public StackPanel Panel = null!;
		public TextBox Text = null!;
		public TextBox ImagePath = null!;
		public TextBox AudioPath = null!;
		public System.Windows.Controls.Image Preview = null!;
	}

	// =====================================================================
	// УТИЛИТЫ
	// =====================================================================
	private static int[] ParsePrices(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return Array.Empty<int>();

		var parts = text.Split(new[] { ',', ';', ' ', '\t' },
			StringSplitOptions.RemoveEmptyEntries);

		var result = new List<int>();
		foreach (var p in parts)
		{
			if (int.TryParse(p, out var v) && v > 0)
				result.Add(v);
		}
		return result.ToArray();
	}

	private static Theme CreateEmptyTheme(string name, int[] prices)
	{
		var theme = new Theme { Name = name };
		foreach (var p in prices)
		{
			theme.Questions[p.ToString()] = new QuestionItem
			{
				Question = new ContentBlock(),
				Answer = new ContentBlock()
			};
		}
		return theme;
	}

	// =====================================================================
	// РАУНДЫ
	// =====================================================================
	private void RoundsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_loadingUi)
			return;

		SaveUiToRound(_selectedRound);
		_selectedRound = RoundsList.SelectedItem as Round;
		LoadRoundToUi(_selectedRound);
	}

	private void LoadRoundToUi(Round? round)
	{
		_loadingUi = true;
		try
		{
			_selectedTheme = null;
			ThemesList.ItemsSource = null;
			TxtThemeName.Text = "";
			TxtRoundName.Text = "";
			TxtPrices.Text = "";
			BuildQuestionRowsForTheme(null);

			if (round == null)
			{
				TxtRoundName.IsEnabled = false;
				TxtPrices.IsEnabled = false;
				TxtThemeName.IsEnabled = false;
				ThemesList.IsEnabled = false;
				return;
			}

			TxtRoundName.Text = round.Name;
			TxtRoundName.IsEnabled = true;

			if (round.IsFinal)
			{
				// Финал — редактирование тем появится на этапе C
				TxtPrices.Text = "";
				TxtPrices.IsEnabled = false;
				TxtThemeName.IsEnabled = false;
				ThemesList.IsEnabled = false;
				_currentPrices = Array.Empty<int>();
				// Заглушка в области вопросов
				QuestionsPanel.Children.Clear();
				QuestionsPanel.Children.Add(new TextBlock
				{
					Text = "Редактирование финала появится на следующем этапе.\n" +
						   "Темы финала и их вопросы пока сохраняются из исходного файла без изменений.",
					Foreground = (Brush)FindResource("TextMuted"),
					FontSize = 14,
					TextWrapping = TextWrapping.Wrap,
					Margin = new Thickness(8)
				});
				return;
			}

			TxtPrices.Text = string.Join(", ", round.Prices);
			TxtPrices.IsEnabled = true;
			TxtThemeName.IsEnabled = true;
			ThemesList.IsEnabled = true;
			_currentPrices = round.Prices;
			ThemesList.ItemsSource = round.Themes;

			if (round.Themes.Count > 0)
				ThemesList.SelectedIndex = 0;
			else
			{
				_selectedTheme = null;
				TxtThemeName.Text = "";
				BuildQuestionRowsForTheme(null);
			}
		}
		finally
		{
			_loadingUi = false;
		}
	}

	private void SaveUiToRound(Round? round)
	{
		if (round == null || _loadingUi)
			return;

		round.Name = TxtRoundName.Text;

		if (!round.IsFinal)
		{
			// Сохраняем данные текущей темы
			SaveUiToTheme(_selectedTheme);

			// Цены применим через LostFocus, но на случай — ещё раз при сохранении
			var parsed = ParsePrices(TxtPrices.Text);
			if (parsed.Length > 0)
				round.Prices = parsed;
		}
	}

	private void TxtRoundName_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (_loadingUi || _selectedRound == null)
			return;
		_selectedRound.Name = TxtRoundName.Text;
		_isDirty = true;

		int idx = RoundsList.SelectedIndex;
		RoundsList.Items.Refresh();
		RoundsList.SelectedIndex = idx;
	}

	private void TxtPrices_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (_loadingUi || _selectedRound == null || _selectedRound.IsFinal)
			return;
		_isDirty = true;
	}

	private void TxtPrices_LostFocus(object sender, RoutedEventArgs e)
	{
		if (_loadingUi || _selectedRound == null || _selectedRound.IsFinal)
			return;

		var parsed = ParsePrices(TxtPrices.Text);
		if (parsed.Length == 0)
			return;
		if (parsed.SequenceEqual(_selectedRound.Prices))
			return;

		// Сохраняем данные текущей темы перед перестройкой
		SaveUiToTheme(_selectedTheme);

		_currentPrices = parsed;
		_selectedRound.Prices = parsed;

		// Нормализуем поле (убираем мусор)
		_loadingUi = true;
		TxtPrices.Text = string.Join(", ", parsed);
		_loadingUi = false;

		BuildQuestionRowsForTheme(_selectedTheme);
	}

	// =====================================================================
	// ТЕМЫ
	// =====================================================================
	private void ThemesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_loadingUi)
			return;

		SaveUiToTheme(_selectedTheme);
		_selectedTheme = ThemesList.SelectedItem as Theme;

		_loadingUi = true;
		TxtThemeName.Text = _selectedTheme?.Name ?? "";
		_loadingUi = false;

		BuildQuestionRowsForTheme(_selectedTheme);
	}

	private void TxtThemeName_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (_loadingUi || _selectedTheme == null)
			return;
		_selectedTheme.Name = TxtThemeName.Text;
		_isDirty = true;

		int idx = ThemesList.SelectedIndex;
		ThemesList.Items.Refresh();
		ThemesList.SelectedIndex = idx;
	}

	// =====================================================================
	// ПОСТРОЕНИЕ СТРОК ВОПРОСОВ ПО ТЕКУЩИМ ЦЕНАМ
	// =====================================================================
	private void BuildQuestionRowsForTheme(Theme? theme)
	{
		QuestionsPanel.Children.Clear();
		_questionRows.Clear();
		_answerRows.Clear();

		if (_selectedRound?.IsFinal == true)
			return;
		if (_currentPrices.Length == 0)
			return;

		for (int i = 0; i < _currentPrices.Length; i++)
		{
			int price = _currentPrices[i];

			var outer = new Border
			{
				Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x1E, 0x3A)),
				CornerRadius = new CornerRadius(8),
				Padding = new Thickness(12),
				Margin = new Thickness(0, 0, 0, 10)
			};

			var grid = new Grid();
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

			var priceText = new TextBlock
			{
				Text = price.ToString(),
				Foreground = (Brush)FindResource("AccentGold"),
				FontSize = 20,
				FontWeight = FontWeights.Bold,
				VerticalAlignment = VerticalAlignment.Center,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			};
			Grid.SetColumn(priceText, 0);
			grid.Children.Add(priceText);

			var qRow = BuildContentRow("ВОПРОС");
			Grid.SetColumn(qRow.Panel, 1);
			grid.Children.Add(qRow.Panel);
			_questionRows.Add(qRow);

			var aRow = BuildContentRow("ОТВЕТ");
			Grid.SetColumn(aRow.Panel, 2);
			grid.Children.Add(aRow.Panel);
			_answerRows.Add(aRow);

			outer.Child = grid;
			QuestionsPanel.Children.Add(outer);
		}

		LoadThemeToUi(theme);
	}

	private ContentRowControls BuildContentRow(string label)
	{
		var ctrl = new ContentRowControls();
		var panel = new StackPanel { Margin = new Thickness(6, 0, 6, 0) };
		ctrl.Panel = panel;

		panel.Children.Add(new TextBlock
		{
			Text = label,
			Foreground = (Brush)FindResource("TextMuted"),
			FontSize = 11,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0, 0, 0, 4)
		});

		ctrl.Text = new TextBox
		{
			Style = (Style)FindResource("DarkTextBox"),
			MinHeight = 60,
			MaxHeight = 140
		};
		panel.Children.Add(ctrl.Text);

		// Картинка
		panel.Children.Add(new TextBlock
		{
			Text = "Картинка",
			Foreground = (Brush)FindResource("TextMuted"),
			FontSize = 11,
			Margin = new Thickness(0, 8, 0, 2)
		});

		var imgGrid = new Grid();
		imgGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		imgGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		imgGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

		ctrl.ImagePath = new TextBox
		{
			Style = (Style)FindResource("DarkTextBox"),
			IsReadOnly = true,
			FontSize = 11,
			AcceptsReturn = false,
			TextWrapping = TextWrapping.NoWrap
		};
		Grid.SetColumn(ctrl.ImagePath, 0);
		imgGrid.Children.Add(ctrl.ImagePath);

		var btnPickImg = new Button
		{
			Content = "📷",
			Style = (Style)FindResource("GoldButton"),
			Width = 40,
			Margin = new Thickness(4, 0, 0, 0),
			ToolTip = "Выбрать картинку"
		};
		Grid.SetColumn(btnPickImg, 1);
		imgGrid.Children.Add(btnPickImg);

		var btnClearImg = new Button
		{
			Content = "✕",
			Style = (Style)FindResource("RedButton"),
			Width = 34,
			Margin = new Thickness(4, 0, 0, 0),
			ToolTip = "Убрать картинку"
		};
		Grid.SetColumn(btnClearImg, 2);
		imgGrid.Children.Add(btnClearImg);

		panel.Children.Add(imgGrid);

		ctrl.Preview = new Image
		{
			MaxHeight = 60,
			Margin = new Thickness(0, 4, 0, 0),
			Stretch = Stretch.Uniform,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
		};
		panel.Children.Add(ctrl.Preview);

		// Звук
		panel.Children.Add(new TextBlock
		{
			Text = "Звук",
			Foreground = (Brush)FindResource("TextMuted"),
			FontSize = 11,
			Margin = new Thickness(0, 8, 0, 2)
		});

		var audGrid = new Grid();
		audGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		audGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		audGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		audGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

		ctrl.AudioPath = new TextBox
		{
			Style = (Style)FindResource("DarkTextBox"),
			IsReadOnly = true,
			FontSize = 11,
			AcceptsReturn = false,
			TextWrapping = TextWrapping.NoWrap
		};
		Grid.SetColumn(ctrl.AudioPath, 0);
		audGrid.Children.Add(ctrl.AudioPath);

		var btnPickAud = new Button
		{
			Content = "🔊",
			Style = (Style)FindResource("GoldButton"),
			Width = 40,
			Margin = new Thickness(4, 0, 0, 0),
			ToolTip = "Выбрать звук"
		};
		Grid.SetColumn(btnPickAud, 1);
		audGrid.Children.Add(btnPickAud);

		var btnPlayAud = new Button
		{
			Content = "▶",
			Style = (Style)FindResource("GreenButton"),
			Width = 34,
			Margin = new Thickness(4, 0, 0, 0),
			ToolTip = "Прослушать"
		};
		Grid.SetColumn(btnPlayAud, 2);
		audGrid.Children.Add(btnPlayAud);

		var btnClearAud = new Button
		{
			Content = "✕",
			Style = (Style)FindResource("RedButton"),
			Width = 34,
			Margin = new Thickness(4, 0, 0, 0),
			ToolTip = "Убрать звук"
		};
		Grid.SetColumn(btnClearAud, 3);
		audGrid.Children.Add(btnClearAud);

		panel.Children.Add(audGrid);

		// Обработчики
		btnPickImg.Click += (_, _) => { PickImage(ctrl); _isDirty = true; };
		btnClearImg.Click += (_, _) => { ClearImage(ctrl); _isDirty = true; };
		btnPickAud.Click += (_, _) => { PickAudio(ctrl); _isDirty = true; };
		btnPlayAud.Click += (_, _) => PlayAudio(ctrl);
		btnClearAud.Click += (_, _) => { ClearAudio(ctrl); _isDirty = true; };

		ctrl.Text.TextChanged += (_, _) => { if (!_loadingUi) _isDirty = true; };
		ctrl.ImagePath.TextChanged += (_, _) => { if (!_loadingUi) _isDirty = true; };
		ctrl.AudioPath.TextChanged += (_, _) => { if (!_loadingUi) _isDirty = true; };

		return ctrl;
	}

	// =====================================================================
	// СИНХРОНИЗАЦИЯ UI ↔ МОДЕЛЬ (вопросы темы)
	// =====================================================================
	private void LoadThemeToUi(Theme? theme)
	{
		_loadingUi = true;
		try
		{
			if (theme == null || _currentPrices.Length == 0)
			{
				foreach (var row in _questionRows)
					ResetRow(row);
				foreach (var row in _answerRows)
					ResetRow(row);
				return;
			}

			for (int i = 0; i < _currentPrices.Length && i < _questionRows.Count; i++)
			{
				var q = theme.GetQuestion(_currentPrices[i]);
				LoadRow(_questionRows[i], q?.Question);
				LoadRow(_answerRows[i], q?.Answer);
			}
		}
		finally
		{
			_loadingUi = false;
		}
	}

	private static void ResetRow(ContentRowControls row)
	{
		row.Text.Text = "";
		row.ImagePath.Text = "";
		row.AudioPath.Text = "";
		row.Preview.Source = null;
	}

	private void LoadRow(ContentRowControls row, ContentBlock? block)
	{
		row.Text.Text = block?.Text ?? "";
		row.ImagePath.Text = block?.Image ?? "";
		row.AudioPath.Text = block?.Audio ?? "";
		RefreshPreview(row);
		HighlightMissing(row);
	}

	private void SaveUiToTheme(Theme? theme)
	{
		if (theme == null || _selectedRound?.IsFinal == true)
			return;
		if (_currentPrices.Length == 0)
			return;

		for (int i = 0; i < _currentPrices.Length && i < _questionRows.Count; i++)
		{
			var key = _currentPrices[i].ToString();
			if (!theme.Questions.TryGetValue(key, out var item))
			{
				item = new QuestionItem();
				theme.Questions[key] = item;
			}
			item.Question ??= new ContentBlock();
			item.Answer ??= new ContentBlock();

			ApplyRow(_questionRows[i], item.Question);
			ApplyRow(_answerRows[i], item.Answer);
		}
	}

	private static void ApplyRow(ContentRowControls row, ContentBlock block)
	{
		block.Text = string.IsNullOrWhiteSpace(row.Text.Text) ? null : row.Text.Text;
		block.Image = string.IsNullOrWhiteSpace(row.ImagePath.Text) ? null : row.ImagePath.Text;
		block.Audio = string.IsNullOrWhiteSpace(row.AudioPath.Text) ? null : row.AudioPath.Text;
	}

	// =====================================================================
	// МЕДИА
	// =====================================================================
	private void PickImage(ContentRowControls ctrl)
	{
		var dlg = new Microsoft.Win32.OpenFileDialog
		{
			Filter = "Изображения|*.png;*.jpg;*.jpeg;*.gif;*.bmp|Все файлы|*.*",
			Title = "Выберите картинку"
		};
		if (dlg.ShowDialog() != true)
			return;

		var relative = MediaManager.CopyToMedia(dlg.FileName);
		if (relative == null)
		{
			MessageBox.Show("Не удалось скопировать файл в папку media/.",
				"Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
			return;
		}

		ctrl.ImagePath.Text = relative;
		RefreshPreview(ctrl);
		HighlightMissing(ctrl);
	}

	private void ClearImage(ContentRowControls ctrl)
	{
		ctrl.ImagePath.Text = "";
		ctrl.Preview.Source = null;
	}

	private void RefreshPreview(ContentRowControls ctrl)
	{
		var absolute = QuestionsLoader.ResolveMediaPath(ctrl.ImagePath.Text);
		if (absolute != null && File.Exists(absolute))
		{
			try
			{
				var bmp = new BitmapImage();
				bmp.BeginInit();
				bmp.CacheOption = BitmapCacheOption.OnLoad;
				bmp.UriSource = new Uri(absolute, UriKind.Absolute);
				bmp.EndInit();
				ctrl.Preview.Source = bmp;
				return;
			}
			catch { /* ignore */ }
		}
		ctrl.Preview.Source = null;
	}

	private void PickAudio(ContentRowControls ctrl)
	{
		var dlg = new Microsoft.Win32.OpenFileDialog
		{
			Filter = "Аудио|*.mp3;*.wav;*.ogg|Все файлы|*.*",
			Title = "Выберите звук"
		};
		if (dlg.ShowDialog() != true)
			return;

		var relative = MediaManager.CopyToMedia(dlg.FileName);
		if (relative == null)
		{
			MessageBox.Show("Не удалось скопировать файл в папку media/.",
				"Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
			return;
		}

		ctrl.AudioPath.Text = relative;
		HighlightMissing(ctrl);
	}

	private void ClearAudio(ContentRowControls ctrl)
	{
		_previewPlayer.Stop();
		ctrl.AudioPath.Text = "";
	}

	private void PlayAudio(ContentRowControls ctrl)
	{
		var absolute = QuestionsLoader.ResolveMediaPath(ctrl.AudioPath.Text);
		if (absolute == null || !File.Exists(absolute))
			return;

		try
		{
			_previewPlayer.Stop();
			_previewPlayer.Close();
			_previewPlayer.Open(new Uri(absolute, UriKind.Absolute));
			_previewPlayer.Play();
		}
		catch { /* ignore */ }
	}

	private void HighlightMissing(ContentRowControls row)
	{
		var imgAbs = QuestionsLoader.ResolveMediaPath(row.ImagePath.Text);
		row.ImagePath.Foreground = (imgAbs != null && File.Exists(imgAbs))
			? (Brush)FindResource("TextMain")
			: (Brush)FindResource("AccentRed");

		var audAbs = QuestionsLoader.ResolveMediaPath(row.AudioPath.Text);
		row.AudioPath.Foreground = (audAbs != null && File.Exists(audAbs))
			? (Brush)FindResource("TextMain")
			: (Brush)FindResource("AccentRed");

		if (string.IsNullOrWhiteSpace(row.ImagePath.Text))
			row.ImagePath.Foreground = (Brush)FindResource("TextMain");
		if (string.IsNullOrWhiteSpace(row.AudioPath.Text))
			row.AudioPath.Foreground = (Brush)FindResource("TextMain");
	}

	// =====================================================================
	// ДОБАВИТЬ / УДАЛИТЬ РАУНД
	// =====================================================================
	private void BtnAddRound_Click(object sender, RoutedEventArgs e)
	{
		SaveUiToRound(_selectedRound);

		// Обычные раунды вставляем ПЕРЕД финалом, если он есть
		int insertIndex = _rounds.Count;
		for (int i = 0; i < _rounds.Count; i++)
		{
			if (_rounds[i].IsFinal)
			{ insertIndex = i; break; }
		}

		var newRound = new Round
		{
			Name = $"Раунд {_rounds.Count(r => !r.IsFinal) + 1}",
			Prices = (int[])DefaultPrices.Clone(),
			IsFinal = false
		};
		newRound.Themes.Add(CreateEmptyTheme("Новая тема", newRound.Prices));

		_rounds.Insert(insertIndex, newRound);
		RoundsList.SelectedItem = newRound;
		_isDirty = true;
	}

	private void BtnRemoveRound_Click(object sender, RoutedEventArgs e)
	{
		if (_selectedRound == null)
			return;

		if (_selectedRound.IsFinal)
		{
			MessageBox.Show("Финал удалить нельзя.",
				"Редактор", MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}

		var nonFinal = _rounds.Count(r => !r.IsFinal);
		if (nonFinal <= 1)
		{
			MessageBox.Show("Нельзя удалить последний игровой раунд.",
				"Редактор", MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}

		var confirm = MessageBox.Show(
			$"Удалить раунд «{_selectedRound.Name}»?",
			"Подтверждение",
			MessageBoxButton.YesNo, MessageBoxImage.Question);
		if (confirm != MessageBoxResult.Yes)
			return;

		int idx = RoundsList.SelectedIndex;
		_rounds.Remove(_selectedRound);
		_selectedRound = null;
		_selectedTheme = null;
		_isDirty = true;
		RoundsList.SelectedIndex = Math.Max(0, idx - 1);
	}

	// =====================================================================
	// ДОБАВИТЬ / УДАЛИТЬ ТЕМУ
	// =====================================================================
	private void BtnAddTheme_Click(object sender, RoutedEventArgs e)
	{
		if (_selectedRound == null || _selectedRound.IsFinal)
			return;

		SaveUiToTheme(_selectedTheme);

		var theme = CreateEmptyTheme("Новая тема", _selectedRound.Prices);
		_selectedRound.Themes.Add(theme);

		// Гарантированное обновление ListBox:
		// перепривязываем источник на актуальную ObservableCollection раунда.
		ThemesList.ItemsSource = null;
		ThemesList.ItemsSource = _selectedRound.Themes;

		ThemesList.SelectedItem = theme;
		_isDirty = true;
	}

	private void BtnRemoveTheme_Click(object sender, RoutedEventArgs e)
	{
		if (_selectedRound == null || _selectedRound.IsFinal)
			return;
		if (_selectedRound.Themes.Count <= 1)
		{
			MessageBox.Show("Нельзя удалить последнюю тему в раунде.",
				"Редактор", MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		if (_selectedTheme == null)
			return;

		var confirm = MessageBox.Show(
			$"Удалить тему «{_selectedTheme.Name}»?",
			"Подтверждение",
			MessageBoxButton.YesNo, MessageBoxImage.Question);
		if (confirm != MessageBoxResult.Yes)
			return;

		int idx = ThemesList.SelectedIndex;
		_selectedRound.Themes.Remove(_selectedTheme);
		ThemesList.ItemsSource = null;
		ThemesList.ItemsSource = _selectedRound.Themes;
		_selectedTheme = null;
		_isDirty = true;
		ThemesList.SelectedIndex = Math.Max(0, idx - 1);
	}

	// =====================================================================
	// СОХРАНИТЬ / ОТМЕНА / MEDIA
	// =====================================================================
	private void BtnSave_Click(object sender, RoutedEventArgs e)
	{
		SaveUiToRound(_selectedRound);

		// ---- Валидация ----
		foreach (var r in _rounds)
		{
			if (string.IsNullOrWhiteSpace(r.Name))
			{
				MessageBox.Show("У каждого раунда должно быть имя.",
					"Редактор", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			if (r.IsFinal)
				continue;

			if (r.Prices == null || r.Prices.Length == 0)
			{
				MessageBox.Show($"Раунд «{r.Name}»: укажите хотя бы одну цену.",
					"Редактор", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			if (r.Prices.Distinct().Count() != r.Prices.Length)
			{
				MessageBox.Show($"Раунд «{r.Name}»: цены не должны повторяться.",
					"Редактор", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			if (r.Themes.Any(t => string.IsNullOrWhiteSpace(t.Name)))
			{
				MessageBox.Show($"Раунд «{r.Name}»: у всех тем должны быть имена.",
					"Редактор", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}
		}

		// ---- Уборка устаревших ключей в вопросах ----
		foreach (var r in _rounds)
		{
			if (r.IsFinal)
				continue;
			var keys = r.Prices.Select(p => p.ToString()).ToHashSet();
			foreach (var theme in r.Themes)
			{
				var toRemove = theme.Questions.Keys.Where(k => !keys.Contains(k)).ToList();
				foreach (var k in toRemove)
					theme.Questions.Remove(k);
			}
		}

		try
		{
			QuestionsLoader.SaveRounds(_rounds.ToList());
			_isDirty = false;
			DialogResult = true;
			Close();
		}
		catch (Exception ex)
		{
			MessageBox.Show($"Не удалось сохранить вопросы:\n{ex.Message}",
				"Ошибка сохранения", MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}

	private void BtnCancel_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void BtnOpenMedia_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			Directory.CreateDirectory(QuestionsLoader.MediaFolder);
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
			{
				FileName = QuestionsLoader.MediaFolder,
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			MessageBox.Show($"Не удалось открыть папку media:\n{ex.Message}",
				"Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	// =====================================================================
	// ЗАЩИТА ОТ ПОТЕРИ ИЗМЕНЕНИЙ
	// =====================================================================
	protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
	{
		if (_isDirty)
		{
			var answer = MessageBox.Show(
				"Есть несохранённые изменения. Закрыть редактор без сохранения?",
				"Подтверждение",
				MessageBoxButton.YesNo, MessageBoxImage.Question);

			if (answer != MessageBoxResult.Yes)
			{
				e.Cancel = true;
				return;
			}
		}
		base.OnClosing(e);
	}

	protected override void OnClosed(EventArgs e)
	{
		_previewPlayer.Close();
		base.OnClosed(e);
	}
}