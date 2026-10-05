using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SvoyaGame.Models;

namespace SvoyaGame.Services;

public static class QuestionsLoader
{
	private static readonly JsonSerializerOptions ReadOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true
	};

	private static readonly JsonSerializerOptions WriteOptions = new()
	{
		WriteIndented = true,
		// Чтобы русские буквы не превращались в \u0420\u0430...
		Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	/// <summary>Путь к questions.json рядом с .exe.</summary>
	public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "questions.json");

	/// <summary>Папка с медиа рядом с .exe.</summary>
	public static string MediaFolder => Path.Combine(AppContext.BaseDirectory, "media");

	// ------------------------------------------------------------
	// ЗАГРУЗКА
	// ------------------------------------------------------------
	public static List<Theme> Load(string? path = null)
	{
		path ??= DefaultPath;

		if (!File.Exists(path))
			throw new FileNotFoundException($"Файл вопросов не найден: {path}");

		var json = File.ReadAllText(path);
		var root = JsonSerializer.Deserialize<QuestionsRoot>(json, ReadOptions);

		if (root?.Themes is null || root.Themes.Count == 0)
			throw new InvalidDataException("questions.json не содержит тем.");

		return root.Themes;
	}

	public static List<Theme>? TryLoad(out string? error, string? path = null)
	{
		try
		{
			error = null;
			return Load(path);
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return null;
		}
	}

	// ------------------------------------------------------------
	// СОХРАНЕНИЕ (с автобэкапом)
	// ------------------------------------------------------------
	public static void Save(List<Theme> themes, string? path = null)
	{
		path ??= DefaultPath;

		// Автобэкап предыдущего файла
		if (File.Exists(path))
		{
			var dir = Path.GetDirectoryName(path) ?? ".";
			var backup = Path.Combine(dir,
				Path.GetFileNameWithoutExtension(path) + ".backup" + Path.GetExtension(path));
			try
			{ File.Copy(path, backup, overwrite: true); }
			catch { /* не критично */ }
		}

		var root = new QuestionsRoot { Themes = themes };
		var json = JsonSerializer.Serialize(root, WriteOptions);

		// Пишем через временный файл — на случай сбоя не побьём оригинал
		var tmp = path + ".tmp";
		File.WriteAllText(tmp, json);
		File.Move(tmp, path, overwrite: true);
	}

	// ------------------------------------------------------------
	// РЕЗОЛВ ПУТИ К МЕДИА
	// ------------------------------------------------------------
	/// <summary>
	/// Превращает "media/foo.png" из JSON в абсолютный путь рядом с .exe.
	/// Абсолютные пути возвращает как есть. Пустое/отсутствующее — null.
	/// </summary>
	public static string? ResolveMediaPath(string? relativePath)
	{
		if (string.IsNullOrWhiteSpace(relativePath))
			return null;

		var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
		return Path.IsPathRooted(normalized)
			? normalized
			: Path.Combine(AppContext.BaseDirectory, normalized);
	}
}