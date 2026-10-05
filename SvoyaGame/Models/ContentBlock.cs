using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SvoyaGame.Models;

/// <summary>
/// Универсальный блок контента: текст, картинка, звук — все поля опциональны.
/// Читается и как обычная строка (старый формат), и как объект (новый формат).
/// </summary>
[JsonConverter(typeof(ContentBlockConverter))]
public class ContentBlock
{
	public string? Text { get; set; }
	public string? Image { get; set; }
	public string? Audio { get; set; }

	public bool IsEmpty => !HasText && !HasImage && !HasAudio;
	public bool HasText => !string.IsNullOrWhiteSpace(Text);
	public bool HasImage => !string.IsNullOrWhiteSpace(Image);
	public bool HasAudio => !string.IsNullOrWhiteSpace(Audio);

	public static ContentBlock FromText(string text) => new() { Text = text };
}

/// <summary>
/// Позволяет читать ContentBlock из двух форматов:
///   "question": "просто строка"                      — старый
///   "question": { "text": "...", "image": "...", "audio": "..." } — новый
/// При записи всегда сохраняет в новом (объектном) формате.
/// </summary>
public class ContentBlockConverter : JsonConverter<ContentBlock>
{
	public override ContentBlock? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		switch (reader.TokenType)
		{
			case JsonTokenType.Null:
				return null;

			// Старый формат: просто строка
			case JsonTokenType.String:
				return new ContentBlock { Text = reader.GetString() };

			// Новый формат: объект
			case JsonTokenType.StartObject:
				var block = new ContentBlock();
				while (reader.Read())
				{
					if (reader.TokenType == JsonTokenType.EndObject)
						break;
					if (reader.TokenType != JsonTokenType.PropertyName)
						continue;

					var prop = reader.GetString();
					reader.Read();
					var value = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();

					switch (prop?.ToLowerInvariant())
					{
						case "text":
							block.Text = value;
							break;
						case "image":
							block.Image = value;
							break;
						case "audio":
							block.Audio = value;
							break;
					}
				}
				return block;

			default:
				throw new JsonException($"Неожиданный токен для ContentBlock: {reader.TokenType}");
		}
	}

	public override void Write(Utf8JsonWriter writer, ContentBlock value, JsonSerializerOptions options)
	{
		if (value is null)
		{
			writer.WriteNullValue();
			return;
		}

		writer.WriteStartObject();
		writer.WriteString("text", value.Text);
		writer.WriteString("image", value.Image);
		writer.WriteString("audio", value.Audio);
		writer.WriteEndObject();
	}
}