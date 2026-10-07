using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SvoyaGame.Models;

namespace SvoyaGame.Services;

public static class QuestionsLoader
{
	/// <summary>Цены по умолчанию для миграции старого формата.</summary>
	public static readonly int[] DefaultPrices = { 100, 200, 400, 700, 1000 };

	private static readonly JsonSerializerOptions ReadOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true
	};

	private static readonly JsonSerializerOptions WriteOptions = new()
	{
		WriteIndented = true,
		Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "questions.json");
	public static string MediaFolder => Path.Combine(AppContext.BaseDirectory, "media");

	// =====================================================================
	// ЗАГРУЗКА РАУНДОВ
	// =====================================================================
	/// <summary>Читает questions.json и возвращает список раундов (включая финал).</summary>
	public static List<Round> LoadRounds(string? path = null)
	{
		path ??= DefaultPath;

		if (!File.Exists(path))
			throw new FileNotFoundException($"Файл вопросов не найден: {path}");

		var json = File.ReadAllText(path);
		var root = JsonSerializer.Deserialize<GameRoot>(json, ReadOptions);

		if (root is null)
			throw new InvalidDataException("questions.json пуст или повреждён.");

		// ---- Новый формат ----
		if (root.Rounds is { Count: > 0 })
		{
			// Подстраховка: у обычных раундов цены не должны быть пустыми
			foreach (var r in root.Rounds)
			{
				if (!r.IsFinal && (r.Prices == null || r.Prices.Length == 0))
					r.Prices = (int[])DefaultPrices.Clone();
			}
			return root.Rounds;
		}

		if (root.LegacyThemes is { Count: > 0 })
		{
			return new List<Round>
	{
		new Round
		{
			Name = "Раунд 1",
			Prices = (int[])DefaultPrices.Clone(),
			IsFinal = false,
			Themes = new System.Collections.ObjectModel.ObservableCollection<Theme>(root.LegacyThemes)
		}
	};
		}

		throw new InvalidDataException(
			"questions.json не содержит ни \"rounds\", ни \"themes\".");
	}

	public static List<Round>? TryLoadRounds(out string? error, string? path = null)
	{
		try
		{ error = null; return LoadRounds(path); }
		catch (Exception ex) { error = ex.Message; return null; }
	}

	// =====================================================================
	// СОХРАНЕНИЕ РАУНДОВ (с автобэкапом)
	// =====================================================================
	public static void SaveRounds(List<Round> rounds, string? path = null)
	{
		path ??= DefaultPath;

		if (File.Exists(path))
		{
			var dir = Path.GetDirectoryName(path) ?? ".";
			var backup = Path.Combine(dir,
				Path.GetFileNameWithoutExtension(path) + ".backup" + Path.GetExtension(path));
			try
			{ File.Copy(path, backup, overwrite: true); }
			catch { /* ignore */ }
		}

		var root = new GameRoot { Rounds = rounds };
		var json = JsonSerializer.Serialize(root, WriteOptions);

		var tmp = path + ".tmp";
		File.WriteAllText(tmp, json);
		File.Move(tmp, path, overwrite: true);
	}

	// =====================================================================
	// СТАРЫЕ МЕТОДЫ — ОБЁРТКИ ДЛЯ ОБРАТНОЙ СОВМЕСТИМОСТИ
	// (используются MainWindow и ThemeEditorWindow до этапа D)
	// =====================================================================
	/// <summary>
	/// Возвращает темы первого НЕ финального раунда.
	/// Нужно только для старого кода; будет удалено на этапе D.
	/// </summary>
	public static List<Theme> Load(string? path = null)
	{
		var rounds = LoadRounds(path);
		var first = rounds.FirstOrDefault(r => !r.IsFinal) ?? rounds.FirstOrDefault();
		if (first is null || first.Themes.Count == 0)
			throw new InvalidDataException("В questions.json нет ни одной темы в первом раунде.");
		return first.Themes.ToList();
	}

	public static List<Theme>? TryLoad(out string? error, string? path = null)
	{
		try
		{ error = null; return Load(path); }
		catch (Exception ex) { error = ex.Message; return null; }
	}

	/// <summary>
	/// Сохраняет темы как один раунд с ценами по умолчанию.
	/// </summary>
	public static void Save(List<Theme> themes, string? path = null)
	{
		var round = new Round
		{
			Name = "Раунд 1",
			Prices = (int[])DefaultPrices.Clone(),
			IsFinal = false,
			Themes = new System.Collections.ObjectModel.ObservableCollection<Theme>(themes)
		};
		SaveRounds(new List<Round> { round }, path);
	}

	// =====================================================================
	// РЕЗОЛВ ПУТИ К МЕДИА
	// =====================================================================
	public static string? ResolveMediaPath(string? relativePath)
	{
		if (string.IsNullOrWhiteSpace(relativePath))
			return null;

		var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
		return Path.IsPathRooted(normalized)
			? normalized
			: Path.Combine(AppContext.BaseDirectory, normalized);
	}

	// =====================================================================
	// ВАЛИДАЦИЯ МЕДИА
	// =====================================================================
	/// <summary>Старый метод — валидирует темы одного раунда.</summary>
	public static List<string> ValidateMedia(List<Theme> themes)
	{
		var problems = new List<string>();
		foreach (var theme in themes)
		{
			foreach (var kv in theme.Questions)
			{
				var q = kv.Value;
				if (q?.Question?.HasImage == true)
					CheckMediaFile(q.Question.Image, theme.Name, kv.Key, "вопрос", "картинка", problems);
				if (q?.Question?.HasAudio == true)
					CheckMediaFile(q.Question.Audio, theme.Name, kv.Key, "вопрос", "звук", problems);
				if (q?.Answer?.HasImage == true)
					CheckMediaFile(q.Answer.Image, theme.Name, kv.Key, "ответ", "картинка", problems);
				if (q?.Answer?.HasAudio == true)
					CheckMediaFile(q.Answer.Audio, theme.Name, kv.Key, "ответ", "звук", problems);
			}
		}
		return problems;
	}

	/// <summary>Новый метод — валидирует все раунды, включая финал.</summary>
	public static List<string> ValidateMedia(List<Round> rounds)
	{
		var problems = new List<string>();

		foreach (var round in rounds)
		{
			if (round.IsFinal)
			{
				foreach (var ft in round.FinalThemes)
				{
					if (ft.Question?.HasImage == true)
						CheckMediaFile(ft.Question.Image, $"{round.Name} / {ft.Name}", "-", "вопрос", "картинка", problems);
					if (ft.Question?.HasAudio == true)
						CheckMediaFile(ft.Question.Audio, $"{round.Name} / {ft.Name}", "-", "вопрос", "звук", problems);
					if (ft.Answer?.HasImage == true)
						CheckMediaFile(ft.Answer.Image, $"{round.Name} / {ft.Name}", "-", "ответ", "картинка", problems);
					if (ft.Answer?.HasAudio == true)
						CheckMediaFile(ft.Answer.Audio, $"{round.Name} / {ft.Name}", "-", "ответ", "звук", problems);
				}
			}
			else
			{
				foreach (var theme in round.Themes)
				{
					foreach (var kv in theme.Questions)
					{
						var q = kv.Value;
						if (q?.Question?.HasImage == true)
							CheckMediaFile(q.Question.Image, $"{round.Name} / {theme.Name}", kv.Key, "вопрос", "картинка", problems);
						if (q?.Question?.HasAudio == true)
							CheckMediaFile(q.Question.Audio, $"{round.Name} / {theme.Name}", kv.Key, "вопрос", "звук", problems);
						if (q?.Answer?.HasImage == true)
							CheckMediaFile(q.Answer.Image, $"{round.Name} / {theme.Name}", kv.Key, "ответ", "картинка", problems);
						if (q?.Answer?.HasAudio == true)
							CheckMediaFile(q.Answer.Audio, $"{round.Name} / {theme.Name}", kv.Key, "ответ", "звук", problems);
					}
				}
			}
		}

		return problems;
	}

	private static void CheckMediaFile(
		string? relPath, string context, string price, string where, string what,
		List<string> problems)
	{
		var abs = ResolveMediaPath(relPath);
		if (abs == null || !File.Exists(abs))
			problems.Add($"{context} • {price} • {where}: {what} не найден ({relPath})");
	}
}