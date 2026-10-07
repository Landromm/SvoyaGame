using System.Text.Json.Serialization;

namespace SvoyaGame.Models;

/// <summary>
/// Тема финала: одно название + один вопрос и один ответ.
/// </summary>
public class FinalTheme
{
	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("question")]
	public ContentBlock? Question { get; set; }

	[JsonPropertyName("answer")]
	public ContentBlock? Answer { get; set; }

	public bool HasQuestion => Question != null && !Question.IsEmpty;
	public bool HasAnswer => Answer != null && !Answer.IsEmpty;

	public FinalTheme Clone() => new()
	{
		Name = Name,
		Question = Question is null ? null : new ContentBlock
		{
			Text = Question.Text,
			Image = Question.Image,
			Audio = Question.Audio
		},
		Answer = Answer is null ? null : new ContentBlock
		{
			Text = Answer.Text,
			Image = Answer.Image,
			Audio = Answer.Audio
		}
	};
}