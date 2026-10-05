using System;
using System.IO;

namespace SvoyaGame.Services;

public static class MediaManager
{
	/// <summary>Папка media/ рядом с .exe.</summary>
	public static string MediaFolder => QuestionsLoader.MediaFolder;

	/// <summary>
	/// Копирует выбранный пользователем файл в media/ и возвращает относительный путь
	/// в формате JSON ("media/foo.png"). Если файл уже лежит в media/ — возвращает как есть.
	/// Если в media/ уже есть файл с таким именем, но другой — добавляет _1, _2 и т.д.
	/// </summary>
	public static string? CopyToMedia(string sourcePath)
	{
		if (string.IsNullOrWhiteSpace(sourcePath))
			return null;
		if (!File.Exists(sourcePath))
			return null;

		Directory.CreateDirectory(MediaFolder);

		var fileName = Path.GetFileName(sourcePath);
		var targetPath = Path.Combine(MediaFolder, fileName);

		// Файл уже в media/ — не копируем
		if (Path.GetFullPath(sourcePath)
			.Equals(Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
		{
			return $"media/{fileName}";
		}

		// Если такой файл уже есть и совпадает по размеру — считаем «тем же»
		if (File.Exists(targetPath))
		{
			var src = new FileInfo(sourcePath);
			var dst = new FileInfo(targetPath);
			if (src.Length == dst.Length)
				return $"media/{fileName}";

			// Иначе — генерируем уникальное имя
			var nameNoExt = Path.GetFileNameWithoutExtension(sourcePath);
			var ext = Path.GetExtension(sourcePath);
			int n = 1;
			do
			{
				fileName = $"{nameNoExt}_{n}{ext}";
				targetPath = Path.Combine(MediaFolder, fileName);
				n++;
			} while (File.Exists(targetPath));
		}

		File.Copy(sourcePath, targetPath);
		return $"media/{fileName}";
	}
}