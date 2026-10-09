using System;
using System.IO;
using System.Text.Json;
using System.Text.Encodings.Web;
using SvoyaGame.Models;

namespace SvoyaGame.Services;

public static class GameStateService
{
	public static string StatePath =>
		Path.Combine(AppContext.BaseDirectory, "game_state.json");

	private static readonly JsonSerializerOptions Options = new()
	{
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	public static GameState? Load()
	{
		try
		{
			if (!File.Exists(StatePath))
				return null;
			var json = File.ReadAllText(StatePath);
			return JsonSerializer.Deserialize<GameState>(json, Options);
		}
		catch
		{
			return null;
		}
	}

	public static void Save(GameState state)
	{
		try
		{
			var json = JsonSerializer.Serialize(state, Options);
			File.WriteAllText(StatePath, json);
		}
		catch { /* не критично */ }
	}

	public static void Clear()
	{
		try
		{
			if (File.Exists(StatePath))
				File.Delete(StatePath);
		}
		catch { /* не критично */ }
	}
}