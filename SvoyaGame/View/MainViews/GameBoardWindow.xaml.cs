using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using SvoyaGame.Models;
using Color = System.Windows.Media.Color;

namespace SvoyaGame.View.MainViews;

/// <summary>
/// Окно табло, транслируемое на второй монитор для зрителей.
/// Два режима: сетка тем/цен и показ вопроса.
/// </summary>
public partial class GameBoardWindow : Window
{
    private readonly List<Theme> _themes;
    private readonly int[] _prices;
    private readonly Dictionary<string, Border> _tiles = new();


    public GameBoardWindow(List<Theme> themes, int[] prices)
    {
        InitializeComponent();
        _themes = themes;
        _prices = prices;

        BuildBoard();
        //MoveToSecondaryMonitorIfExists();
    }

    // =====================================================================
    // ПОСТРОЕНИЕ СЕТКИ 5×6 (только визуал)
    // =====================================================================
    // Ключ "Тема|Цена" -> Border ячейки, чтобы можно было её затемнить

    private void BuildBoard()
    {
        BoardGrid.Children.Clear();
        _tiles.Clear();

        int rows = 5;
        int cols = 1 + _prices.Length;
        BoardGrid.Rows = rows;
        BoardGrid.Columns = cols;

        for (int r = 0; r < rows; r++)
        {
            var theme = r < _themes.Count ? _themes[r] : null;

            // Колонка темы — не участвует в блокировке, просто добавляем
            var themeTile = new Border { Style = (Style)FindResource("TileStyle") };
            themeTile.Child = new TextBlock
            {
                Text = theme?.Name ?? "—",
                Style = (Style)FindResource("TileTextStyle"),
                FontSize = 18
            };
            BoardGrid.Children.Add(themeTile);

            for (int c = 0; c < _prices.Length; c++)
            {
                int price = _prices[c];
                bool hasQuestion = theme?.GetQuestion(price) != null;

                var priceTile = new Border { Style = (Style)FindResource("TileStyle") };

                if (!hasQuestion)
                {
                    priceTile.Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x1E, 0x3A));
                    priceTile.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0x4A, 0x75));
                }

                var text = new TextBlock
                {
                    Text = hasQuestion ? price.ToString() : "",
                    Style = (Style)FindResource("TileTextStyle"),
                    FontSize = 26
                };
                priceTile.Child = text;
                BoardGrid.Children.Add(priceTile);

                if (theme != null)
                    _tiles[$"{theme.Name}|{price}"] = priceTile;
            }
        }
    }

    /// <summary>
    /// Помечает ячейку сыгранной: приглушает фон и убирает цифру.
    /// </summary>
    public void MarkCellUsed(string themeName, int price)
    {
        string key = $"{themeName}|{price}";
        if (!_tiles.TryGetValue(key, out var tile)) return;

        // Приглушаем фон и текст
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
    // ПЕРЕКЛЮЧЕНИЕ РЕЖИМОВ
    // =====================================================================
    /// <summary>
    /// Показать вопрос крупным планом. Ответ — скрыт до вызова ShowAnswer().
    /// </summary>
    public void ShowQuestion(string themeName, int price, string question, string answer)
    {
        HeaderTheme.Text = themeName;
        HeaderPrice.Text = price.ToString();
        QuestionText.Text = question;
        AnswerText.Text = answer;
        AnswerPanel.Visibility = Visibility.Collapsed;

        QuestionView.Visibility = Visibility.Visible;
        GridView.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Показать ответ на экране (вызывается админом отдельной кнопкой).
    /// </summary>
    public void ShowAnswer()
    {
        if (QuestionView.Visibility == Visibility.Visible)
            AnswerPanel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Вернуть табло к показу игрового поля.
    /// </summary>
    public void ShowGrid()
    {
        QuestionView.Visibility = Visibility.Collapsed;
        GridView.Visibility = Visibility.Visible;
        AnswerPanel.Visibility = Visibility.Collapsed;
    }

    // =====================================================================
    // ВТОРОЙ МОНИТОР
    // =====================================================================
    /// <summary>
    /// Если в системе есть второй монитор — разворачиваем табло на нём
    /// на весь экран. Иначе — открываем в оконном режиме по центру.
    /// </summary>
    private void MoveToSecondaryMonitorIfExists()
    {
        // Полное имя — чтобы не путать с System.Windows.Forms.Screen
        var screens = System.Windows.Forms.Screen.AllScreens;

        if (screens.Length < 2)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Width = 1100;
            Height = 680;
            return;
        }

        var secondary = screens[1];
        var bounds = secondary.Bounds;

        var source = PresentationSource.FromVisual(this);
        double dpiX = 1.0, dpiY = 1.0;
        if (source?.CompositionTarget != null)
        {
            var m = source.CompositionTarget.TransformToDevice;
            dpiX = m.M11;
            dpiY = m.M22;
        }

        Left = bounds.Left / dpiX;
        Top = bounds.Top / dpiY;
        Width = bounds.Width / dpiX;
        Height = bounds.Height / dpiY;

        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
    }

    // =====================================================================
    // ХОТКЕИ
    // =====================================================================
    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        // ESC — вернуть сетку (удобно, если табло активно)
        if (e.Key == System.Windows.Input.Key.Escape)
            ShowGrid();

        base.OnKeyDown(e);
    }
}