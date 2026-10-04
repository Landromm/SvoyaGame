using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using SvoyaGame.Models;

namespace SvoyaGame.Services
{
    /// <summary>
    /// Загрузка вопросов из questions.json, лежащего рядом с .exe.
    /// </summary>
    public static class QuestionsLoader
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        /// <summary>
        /// Читает questions.json. Если путь не задан — берёт файл рядом с исполняемым файлом.
        /// </summary>
        public static List<Theme> Load(string? path = null)
        {
            path ??= Path.Combine(AppContext.BaseDirectory, "questions.json");

            if (!File.Exists(path))
                throw new FileNotFoundException($"Файл вопросов не найден: {path}");

            var json = File.ReadAllText(path);
            var root = JsonSerializer.Deserialize<QuestionsRoot>(json, Options);

            if (root?.Themes is null || root.Themes.Count == 0)
                throw new InvalidDataException("questions.json не содержит тем.");

            return root.Themes;
        }

        /// <summary>
        /// Попытка загрузить без исключения — вернёт null при ошибке.
        /// </summary>
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
    }
}
