using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using SvoyaGame.Models;

namespace SvoyaGame.Services;

public static class PackService
{
	private const string JsonFileName = "questions.json";
	private const string MediaFolderName = "media";

	// =====================================================================
	// ЭКСПОРТ
	// =====================================================================
	/// <summary>
	/// Упаковывает раунды и все связанные медиафайлы в ZIP-пак.
	/// </summary>
	public static void ExportPack(List<Round> rounds, string zipPath)
	{
		var tempDir = Path.Combine(Path.GetTempPath(),
			"svoyagame_pack_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tempDir);

		try
		{
			// 1) JSON
			var jsonPath = Path.Combine(tempDir, JsonFileName);
			var options = new JsonSerializerOptions
			{
				WriteIndented = true,
				Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
			};
			var root = new GameRoot { Rounds = rounds };
			File.WriteAllText(jsonPath, JsonSerializer.Serialize(root, options));

			// 2) Медиа
			var mediaDir = Path.Combine(tempDir, MediaFolderName);
			Directory.CreateDirectory(mediaDir);

			var mediaSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			CollectMediaPaths(rounds, mediaSet);

			foreach (var rel in mediaSet)
			{
				var abs = QuestionsLoader.ResolveMediaPath(rel);
				if (abs == null || !File.Exists(abs))
					continue;

				var fileName = Path.GetFileName(abs);
				File.Copy(abs, Path.Combine(mediaDir, fileName), overwrite: true);
			}

			// 3) ZIP
			if (File.Exists(zipPath))
				File.Delete(zipPath);
			ZipFile.CreateFromDirectory(tempDir, zipPath);
		}
		finally
		{
			try
			{ Directory.Delete(tempDir, recursive: true); }
			catch { }
		}
	}

	// =====================================================================
	// ИМПОРТ
	// =====================================================================
	public class ImportResult
	{
		public List<Round> Rounds = new();
		public int MediaCopied;
		public int MediaMissing;
	}

	/// <summary>
	/// Распаковывает пак, копирует медиафайлы в media/ и возвращает раунды.
	/// </summary>
	public static ImportResult ImportPack(string zipPath)
	{
		var result = new ImportResult();

		var tempDir = Path.Combine(Path.GetTempPath(),
			"svoyagame_pack_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tempDir);

		try
		{
			ZipFile.ExtractToDirectory(zipPath, tempDir);

			// 1) JSON
			var jsonPath = Path.Combine(tempDir, JsonFileName);
			if (!File.Exists(jsonPath))
				throw new FileNotFoundException("В архиве нет questions.json.");

			var options = new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true,
				ReadCommentHandling = JsonCommentHandling.Skip,
				AllowTrailingCommas = true
			};
			var json = File.ReadAllText(jsonPath);
			var root = JsonSerializer.Deserialize<GameRoot>(json, options);

			if (root?.Rounds == null || root.Rounds.Count == 0)
				throw new InvalidDataException("questions.json не содержит раундов.");

			// 2) Медиа
			var mediaSrcDir = Path.Combine(tempDir, MediaFolderName);
			Directory.CreateDirectory(QuestionsLoader.MediaFolder);

			var mediaSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			CollectMediaPaths(root.Rounds, mediaSet);

			foreach (var rel in mediaSet)
			{
				var fileName = Path.GetFileName(rel.Replace('/', Path.DirectorySeparatorChar));
				var src = Path.Combine(mediaSrcDir, fileName);
				var dst = Path.Combine(QuestionsLoader.MediaFolder, fileName);

				if (File.Exists(src))
				{
					File.Copy(src, dst, overwrite: true);
					result.MediaCopied++;
				}
				else
				{
					result.MediaMissing++;
				}
			}

			result.Rounds = root.Rounds;
			return result;
		}
		finally
		{
			try
			{ Directory.Delete(tempDir, recursive: true); }
			catch { }
		}
	}

	// =====================================================================
	// ВСПОМОГАТЕЛЬНОЕ
	// =====================================================================
	private static void CollectMediaPaths(List<Round> rounds, HashSet<string> set)
	{
		foreach (var round in rounds)
		{
			if (round.IsFinal)
			{
				foreach (var ft in round.FinalThemes)
				{
					AddMedia(set, ft.Question?.Image);
					AddMedia(set, ft.Question?.Audio);
					AddMedia(set, ft.Answer?.Image);
					AddMedia(set, ft.Answer?.Audio);
				}
			}
			else
			{
				foreach (var theme in round.Themes)
				{
					foreach (var kv in theme.Questions)
					{
						var q = kv.Value;
						AddMedia(set, q.Question?.Image);
						AddMedia(set, q.Question?.Audio);
						AddMedia(set, q.Answer?.Image);
						AddMedia(set, q.Answer?.Audio);
					}
				}
			}
		}
	}

	private static void AddMedia(HashSet<string> set, string? rel)
	{
		if (!string.IsNullOrWhiteSpace(rel))
			set.Add(rel);
	}
}