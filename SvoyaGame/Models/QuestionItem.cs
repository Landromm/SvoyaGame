using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace SvoyaGame.Models
{
    /// <summary>
    /// Один вопрос с ответом.
    /// </summary>
    public class QuestionItem
    {
        [JsonPropertyName("question")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("answer")]
        public string Answer { get; set; } = string.Empty;
    }
}
