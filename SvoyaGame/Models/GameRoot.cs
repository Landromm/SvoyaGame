using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SvoyaGame.Models;

/// <summary>
/// Корневой объект questions.json в новом формате.
/// Поле LegacyThemes оставлено только для чтения старого формата.
/// </summary>
public class GameRoot
{
	[JsonPropertyName("rounds")]
	public List<Round> Rounds { get; set; } = new();

	/// <summary>Для чтения старого формата {"themes": [...]} — при сохранении не пишется.</summary>
	[JsonPropertyName("themes")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<Theme>? LegacyThemes { get; set; }
}