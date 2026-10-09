using System.Collections.Generic;

namespace SvoyaGame.Models;

/// <summary>
/// Состояние партии на стороне WPF.
/// Сыгранные ячейки, лидер раунда, состояние финальных тем.
/// </summary>
public class GameState
{
	public int Version { get; set; } = 1;
	public string RoomNumber { get; set; } = "";
	public List<string> UsedCells { get; set; } = new();
	public string? CurrentLeader { get; set; }
	public List<string> FinalRemovedThemes { get; set; } = new();
	public bool FinalActive { get; set; }
	public List<string> CatInBagCells { get; set; } = new();
}