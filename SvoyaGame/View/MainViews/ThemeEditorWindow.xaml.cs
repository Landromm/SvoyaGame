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
	private static readonly int[] Prices = { 100, 200, 400, 700, 1000 };

	private readonly ObservableCollection<Theme> _themes;
	private readonly ContentRowControls[] _questionRows = new ContentRowControls[Prices.Length];
	private readonly ContentRowControls[] _answerRows = new ContentRowControls[Prices.Length];

	private readonly MediaPlayer _previewPlayer = new();

	private Theme? _selectedTheme;
	private bool _loadingUi;
	private bool _isDirty;

	public ThemeEditorWindow()
	{
		InitializeComponent();

		var loaded = QuestionsLoader.TryLoad(out var err) ?? new List<Theme>();
		if (err != null)
		{
			MessageBox.Show($"Не удалось загрузить questions.json:\n{err}",
				"Ошибка загрузки", MessageBoxButton.OK, MessageBoxImage.Warning);
		}

		_themes = new ObservableCollection<Theme>(loaded);
		ThemesList.ItemsSource = _themes;

		BuildQuestionRows();

		if (_themes.Count > 0)
			ThemesList.SelectedIndex = 0;
	}

	// =====================================================================
	// ВСПОМОГАТЕЛЬНЫЙ КЛАСС: контролы одной строки "вопрос" или "ответ"
	// =====================================================================
	private class ContentRowControls
	{
		public StackPanel Panel = null!;
		public TextBox Text = null!;
		public TextBox ImagePath = null!;
		public TextBox AudioPath = null!;
		public Image Preview = null!;
	}

	// =====================================================================
	// ПОСТРОЕНИЕ 5 БЛОКОВ "ВОПРОС / ОТВЕТ"
	// =====================================================================
	private void BuildQuestionRows()
	{
		QuestionsPanel.Children.Clear();

		for (int i = 0; i < Prices.Length; i++)
		{
			int price = Prices[i];

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

			// Колонка цены
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

			// Колонка вопроса
			_questionRows[i] = BuildContentRow("ВОПРОС");
			Grid.SetColumn(_questionRows[i].Panel, 1);
			grid.Children.Add(_questionRows[i].Panel);

			// Колонка ответа
			_answerRows[i] = BuildContentRow("ОТВЕТ");
			Grid.SetColumn(_answerRows[i].Panel, 2);
			grid.Children.Add(_answerRows[i].Panel);

			outer.Child = grid;
			QuestionsPanel.Children.Add(outer);
		}
	}

	/// <summary>
	/// Строит колонку «ВОПРОС» или «ОТВЕТ»: текст, картинка + превью, звук + прослушать.
	/// </summary>
	private ContentRowControls BuildContentRow(string label)
	{
		var ctrl = new ContentRowControls();
		var panel = new StackPanel { Margin = new Thickness(6, 0, 6, 0) };
		ctrl.Panel = panel;

		// Заголовок
		panel.Children.Add(new TextBlock
		{
			Text = label,
			Foreground = (Brush)FindResource("TextMuted"),
			FontSize = 11,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0, 0, 0, 4)
		});

		// Текст
		ctrl.Text = new TextBox
		{
			Style = (Style)FindResource("DarkTextBox"),
			MinHeight = 60,
			MaxHeight = 140
		};
		panel.Children.Add(ctrl.Text);

		// ---------------- Картинка ----------------
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

		// Превью картинки
		ctrl.Preview = new Image
		{
			MaxHeight = 60,
			Margin = new Thickness(0, 4, 0, 0),
			Stretch = Stretch.Uniform,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
		};
		panel.Children.Add(ctrl.Preview);

		// ---------------- Звук ----------------
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

		// ---------------- Обработчики ----------------
		btnPickImg.Click += (_, _) => PickImage(ctrl);
		btnClearImg.Click += (_, _) => ClearImage(ctrl);
		btnPickAud.Click += (_, _) => PickAudio(ctrl);
		btnPlayAud.Click += (_, _) => PlayAudio(ctrl);
		btnClearAud.Click += (_, _) => ClearAudio(ctrl);

		ctrl.Text.TextChanged += (_, _) => { if (!_loadingUi) _isDirty = true; };
		ctrl.ImagePath.TextChanged += (_, _) => { if (!_loadingUi) _isDirty = true; };
		ctrl.AudioPath.TextChanged += (_, _) => { if (!_loadingUi) _isDirty = true; };

		return ctrl;
	}

	// =====================================================================
	// ОБРАБОТЧИКИ МЕДИА
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

	// =====================================================================
	// СИНХРОНИЗАЦИЯ UI ↔ МОДЕЛЬ
	// =====================================================================
	private void ThemesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		SaveUiToTheme(_selectedTheme);
		_selectedTheme = ThemesList.SelectedItem as Theme;
		LoadThemeToUi(_selectedTheme);
	}

	private void LoadThemeToUi(Theme? theme)
	{
		_loadingUi = true;
		try
		{
			if (theme == null)
			{
				TxtThemeName.Text = "";
				for (int i = 0; i < Prices.Length; i++)
				{
					ResetRow(_questionRows[i]);
					ResetRow(_answerRows[i]);
				}
				return;
			}

			TxtThemeName.Text = theme.Name;

			for (int i = 0; i < Prices.Length; i++)
			{
				var q = theme.GetQuestion(Prices[i]);
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
	}

	private void SaveUiToTheme(Theme? theme)
	{
		if (theme == null)
			return;

		for (int i = 0; i < Prices.Length; i++)
		{
			var key = Prices[i].ToString();
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

	private void TxtThemeName_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (_loadingUi || _selectedTheme == null)
			return;

		_selectedTheme.Name = TxtThemeName.Text;

		int idx = ThemesList.SelectedIndex;
		ThemesList.Items.Refresh();
		ThemesList.SelectedIndex = idx;
		_isDirty = true;
	}

	// =====================================================================
	// ДОБАВИТЬ / УДАЛИТЬ ТЕМУ
	// =====================================================================
	private void BtnAddTheme_Click(object sender, RoutedEventArgs e)
	{
		SaveUiToTheme(_selectedTheme);

		var t = new Theme { Name = "Новая тема", Questions = new Dictionary<string, QuestionItem>() };
		foreach (var p in Prices)
		{
			t.Questions[p.ToString()] = new QuestionItem
			{
				Question = new ContentBlock(),
				Answer = new ContentBlock()
			};
		}

		_themes.Add(t);
		ThemesList.SelectedItem = t;
		_isDirty = true;
	}

	private void BtnRemoveTheme_Click(object sender, RoutedEventArgs e)
	{
		if (_themes.Count <= 1)
		{
			MessageBox.Show("Нельзя удалить последнюю тему.",
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
		_themes.Remove(_selectedTheme);

		_selectedTheme = null;
		ThemesList.SelectedIndex = Math.Max(0, idx - 1);
		_isDirty = true;
	}

	// =====================================================================
	// СОХРАНИТЬ / ОТМЕНА / MEDIA
	// =====================================================================
	private void BtnSave_Click(object sender, RoutedEventArgs e)
	{
		SaveUiToTheme(_selectedTheme);

		// Валидация имён тем
		if (_themes.Any(t => string.IsNullOrWhiteSpace(t.Name)))
		{
			MessageBox.Show("У каждой темы должно быть название.",
				"Редактор", MessageBoxButton.OK, MessageBoxImage.Warning);
			return;
		}

		// Валидация вопросов/ответов: хотя бы одно из трёх полей
		var error = FindEmptyContentError();
		if (error != null)
		{
			MessageBox.Show(error,
				"Проверьте содержимое", MessageBoxButton.OK, MessageBoxImage.Warning);
			return;
		}

		try
		{
			QuestionsLoader.Save(_themes.ToList());
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

	/// <summary>
	/// Проверяет, что у каждого вопроса и ответа заполнено хотя бы одно поле.
	/// Возвращает текст ошибки или null.
	/// </summary>
	private string? FindEmptyContentError()
	{
		foreach (var theme in _themes)
		{
			foreach (var price in Prices)
			{
				var item = theme.GetQuestion(price);
				if (item == null)
					continue;

				if (item.Question == null || item.Question.IsEmpty)
					return $"Тема «{theme.Name}», вопрос {price}: заполните хотя бы текст, картинку или звук.";

				if (item.Answer == null || item.Answer.IsEmpty)
					return $"Тема «{theme.Name}», ответ на вопрос {price}: заполните хотя бы одно поле.";
			}
		}
		return null;
	}

	private void BtnCancel_Click(object sender, RoutedEventArgs e)
	{
		// Диалог покажет OnClosing, если есть несохранённые изменения
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
	// ОЧИСТКА РЕСУРСОВ
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
}