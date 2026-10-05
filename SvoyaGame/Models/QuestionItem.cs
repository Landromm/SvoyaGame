using System.Text.Json.Serialization;

namespace SvoyaGame.Models;

/// <summary>
/// Вопрос и ответ. Оба — ContentBlock (текст + опционально картинка/звук).
/// </summary>
public class QuestionItem
{
	[JsonPropertyName("question")]
	public ContentBlock? Question { get; set; }

	[JsonPropertyName("answer")]
	public ContentBlock? Answer { get; set; }

	public bool HasQuestion => Question != null && !Question.IsEmpty;
	public bool HasAnswer => Answer != null && !Answer.IsEmpty;
}