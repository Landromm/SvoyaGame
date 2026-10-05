using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SvoyaGame.Models;
using SvoyaGame.Services;
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;

namespace SvoyaGame.View.MainViews;

public partial class ThemeEditorWindow : Window
{
	private static readonly int[] Prices = { 100, 200, 400, 700, 1000 };

	private readonly ObservableCollection<Theme> _themes;
	private readonly TextBox[] _questionBoxes = new TextBox[Prices.Length];
	private readonly TextBox[] _answerBoxes = new TextBox[Prices.Length];

	private Theme? _selectedTheme;
	private bool _loadingUi;

	public ThemeEditorWindow()
	{
		InitializeComponent();

		// Свежая загрузка из файла — работаем с копией
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
	// ПОСТРОЕНИЕ 5 БЛОКОВ ВОПРОС/ОТВЕТ (программно, чтобы не дублировать XAML)
	// =====================================================================
	private void BuildQuestionRows()
	{
		QuestionsPanel.Children.Clear();

		for (int i = 0; i < Prices.Length; i++)
		{
			int price = Prices[i];

			// Внешний блок
			var outer = new Border
			{
				Background = new System.Windows.Media.SolidColorBrush(
					System.Windows.Media.Color.FromRgb(0x0B, 0x1E, 0x3A)),
				CornerRadius = new CornerRadius(8),
				Padding = new Thickness(12),
				Margin = new Thickness(0, 0, 0, 10)
			};

			var grid = new Grid();
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });   // цена
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // вопрос
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // ответ

			// Цена
			var priceText = new TextBlock
			{
				Text = price.ToString(),
				Foreground = (System.Windows.Media.Brush)FindResource("AccentGold"),
				FontSize = 20,
				FontWeight = FontWeights.Bold,
				VerticalAlignment = VerticalAlignment.Center,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			};
			Grid.SetColumn(priceText, 0);
			grid.Children.Add(priceText);

			// Панель вопроса
			var qPanel = new StackPanel { Margin = new Thickness(6, 0, 6, 0) };
			qPanel.Children.Add(new TextBlock
			{
				Text = "ВОПРОС",
				Foreground = (System.Windows.Media.Brush)FindResource("TextMuted"),
				FontSize = 11,
				FontWeight = FontWeights.Bold,
				Margin = new Thickness(0, 0, 0, 4)
			});
			var qBox = new TextBox
			{
				Style = (Style)FindResource("DarkTextBox"),
				MinHeight = 60,
				MaxHeight = 160
			};
			qPanel.Children.Add(qBox);
			Grid.SetColumn(qPanel, 1);
			grid.Children.Add(qPanel);

			// Панель ответа
			var aPanel = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
			aPanel.Children.Add(new TextBlock
			{
				Text = "ОТВЕТ",
				Foreground = (System.Windows.Media.Brush)FindResource("TextMuted"),
				FontSize = 11,
				FontWeight = FontWeights.Bold,
				Margin = new Thickness(0, 0, 0, 4)
			});
			var aBox = new TextBox
			{
				Style = (Style)FindResource("DarkTextBox"),
				MinHeight = 60,
				MaxHeight = 160
			};
			aPanel.Children.Add(aBox);
			Grid.SetColumn(aPanel, 2);
			grid.Children.Add(aPanel);

			outer.Child = grid;
			QuestionsPanel.Children.Add(outer);

			_questionBoxes[i] = qBox;
			_answerBoxes[i] = aBox;
		}
	}

	// =====================================================================
	// СМЕНА ВЫБРАННОЙ ТЕМЫ
	// =====================================================================
	private void ThemesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		// 1) Сохраняем данные UI в ПРЕДЫДУЩУЮ тему
		SaveUiToTheme(_selectedTheme);

		// 2) Забираем новую тему
		_selectedTheme = ThemesList.SelectedItem as Theme;

		// 3) Загружаем её в UI
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
					_questionBoxes[i].Text = "";
					_answerBoxes[i].Text = "";
				}
				return;
			}

			TxtThemeName.Text = theme.Name;

			for (int i = 0; i < Prices.Length; i++)
			{
				var q = theme.GetQuestion(Prices[i]);
				_questionBoxes[i].Text = q?.Question?.Text ?? "";
				_answerBoxes[i].Text = q?.Answer?.Text ?? "";
			}
		}
		finally
		{
			_loadingUi = false;
		}
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

			item.Question.Text = _questionBoxes[i].Text;
			item.Answer.Text = _answerBoxes[i].Text;
		}
	}

	private void TxtThemeName_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (_loadingUi || _selectedTheme == null)
			return;

		_selectedTheme.Name = TxtThemeName.Text;

		// Обновляем отображаемое имя в списке
		int idx = ThemesList.SelectedIndex;
		ThemesList.Items.Refresh();
		ThemesList.SelectedIndex = idx;
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

		// Сброс, чтобы не сохранять в удалённую тему
		_selectedTheme = null;
		ThemesList.SelectedIndex = Math.Max(0, idx - 1);
	}

	// =====================================================================
	// СОХРАНИТЬ / ОТМЕНА
	// =====================================================================
	private void BtnSave_Click(object sender, RoutedEventArgs e)
	{
		SaveUiToTheme(_selectedTheme);

		if (_themes.Any(t => string.IsNullOrWhiteSpace(t.Name)))
		{
			MessageBox.Show("У каждой темы должно быть название.",
				"Редактор", MessageBoxButton.OK, MessageBoxImage.Warning);
			return;
		}

		try
		{
			QuestionsLoader.Save(_themes.ToList());
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
		var confirm = MessageBox.Show(
			"Отменить изменения и выйти без сохранения?",
			"Подтверждение",
			MessageBoxButton.YesNo, MessageBoxImage.Question);
		if (confirm != MessageBoxResult.Yes)
			return;

		DialogResult = false;
		Close();
	}
}
