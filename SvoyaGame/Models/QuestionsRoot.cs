using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace SvoyaGame.Models
{
    /// <summary>
    /// Корневой объект questions.json.
    /// </summary>
    public class QuestionsRoot
    {
        [JsonPropertyName("themes")]
        public List<Theme> Themes { get; set; } = new();
    }
}
