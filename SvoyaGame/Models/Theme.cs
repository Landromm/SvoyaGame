using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace SvoyaGame.Models
{
    /// <summary>
    /// Тема игры: название + словарь вопросов по цене ("100", "200", ...).
    /// </summary>
    public class Theme
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("questions")]
        public Dictionary<string, QuestionItem> Questions { get; set; } = new();

        /// <summary>
        /// Удобный доступ к вопросу по цене. Возвращает null, если вопрос не найден.
        /// </summary>
        public QuestionItem? GetQuestion(int price)
            => Questions.TryGetValue(price.ToString(), out var q) ? q : null;
    }
}
