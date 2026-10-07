using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace SvoyaGame.Models;

/// <summary>
/// Раунд игры. Обычный раунд — цены + список тем с вопросами по цене.
/// Финал — isFinal: true + список FinalTheme (одна тема = один вопрос).
/// Коллекции — ObservableCollection, чтобы UI-списки (ListBox) обновлялись автоматически.
/// </summary>
public class Round
{
	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	/// <summary>Массив цен для обычного раунда. Для финала — пустой.</summary>
	[JsonPropertyName("prices")]
	public int[] Prices { get; set; } = Array.Empty<int>();

	[JsonPropertyName("isFinal")]
	public bool IsFinal { get; set; }

	/// <summary>Темы обычного раунда.</summary>
	[JsonPropertyName("themes")]
	public ObservableCollection<Theme> Themes { get; set; } = new();

	/// <summary>Темы финала (используется при IsFinal == true).</summary>
	[JsonPropertyName("finalThemes")]
	public ObservableCollection<FinalTheme> FinalThemes { get; set; } = new();

	/// <summary>Глубокая копия.</summary>
	public Round Clone()
	{
		var copy = new Round
		{
			Name = Name,
			Prices = (int[])Prices.Clone(),
			IsFinal = IsFinal
		};

		foreach (var t in Themes)
		{
			var themeCopy = new Theme { Name = t.Name };
			foreach (var kv in t.Questions)
			{
				themeCopy.Questions[kv.Key] = new QuestionItem
				{
					Question = kv.Value.Question is null ? null : new ContentBlock
					{
						Text = kv.Value.Question.Text,
						Image = kv.Value.Question.Image,
						Audio = kv.Value.Question.Audio
					},
					Answer = kv.Value.Answer is null ? null : new ContentBlock
					{
						Text = kv.Value.Answer.Text,
						Image = kv.Value.Answer.Image,
						Audio = kv.Value.Answer.Audio
					}
				};
			}
			copy.Themes.Add(themeCopy);
		}

		foreach (var ft in FinalThemes)
			copy.FinalThemes.Add(ft.Clone());

		return copy;
	}
}