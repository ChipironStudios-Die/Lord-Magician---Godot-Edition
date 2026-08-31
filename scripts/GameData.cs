using Godot;
using System;
using System.Linq;

namespace LordMagician;

public enum EnemyType { Melee, Ranged, Tank, Boss, Sentinel }
public enum ProjectileOwner { Player, Enemy }
public enum MissionType { Eliminate, Escape, Collect }
public enum Difficulty { Easy, Normal, Hard }
public enum GraphicsQuality { Performance, Standard, HighDefinition }
public enum GamePhase { MainMenu, Playing, LevelClear, Shop, GameOver, Finished, Settings, Paused, MultiplayerMenu }

public readonly record struct WeaponDef(string Name, float Damage, float ManaCost, float Cooldown, int Cost);
public readonly record struct ArmorDef(string Name, float Defense, int Cost);
public readonly record struct AccessoryDef(string Name, float AttackBonus, int Cost);
public readonly record struct EnemySpawn(Vector2 Position, float Hp, EnemyType Type, float Speed = 1.2f, int ExpReward = 20, int GoldMin = 5, int GoldMax = 15);
public readonly record struct LightDef(Vector2 Position, Color Color, float Energy = 1.8f, float Range = 9f, float Height = 0.85f);

public sealed class LevelDef
{
	public string Name { get; }
	public int[][] Map { get; }
	public Vector2 Start { get; }
	public EnemySpawn[] Spawns { get; }
	public LightDef[] Lights { get; }
	public MissionType Mission { get; }
	public int TargetCount { get; }

	public LevelDef(string name, int[][] map, Vector2 start, EnemySpawn[] spawns, LightDef[] lights, MissionType mission = MissionType.Eliminate, int targetCount = 0)
	{
		Name = name;
		Map = map;
		Start = start;
		Spawns = spawns;
		Lights = lights;
		Mission = mission;
		TargetCount = targetCount;
	}
}

/// <summary>
/// Datos portados del MainActivity.kt original. Los mapas se han conservado celda a celda.
/// 0 es suelo; el resto son paredes/elementos sólidos con su variante visual.
/// </summary>
public static class GameData
{
	public static readonly WeaponDef[] Weapons =
	{
		new("Vara de Aprendiz", 25f, 20f, 0.35f, 0),
		new("Daga Arcana Arrojadiza", 32f, 14f, 0.20f, 45),
		new("Bastón de Fuego", 45f, 22f, 0.32f, 90),
		new("Cetro de Hielo", 55f, 24f, 0.30f, 150),
		new("Cetro Arcano", 65f, 25f, 0.28f, 220),
		new("Vara del Trueno", 80f, 28f, 0.26f, 300),
		new("Báculo del Lord Mago", 100f, 32f, 0.22f, 400),
		new("Reliquia del Vacío", 130f, 35f, 0.20f, 550),
		new("Apocalipsis", 180f, 45f, 0.18f, 800)
	};

	public static readonly ArmorDef[] Armors =
	{
		new("Túnica de Tela", 0.00f, 0),
		new("Chaleco de Cuero", 0.08f, 35),
		new("Chaleco Reforzado", 0.15f, 75),
		new("Cota de Malla Arcana", 0.22f, 130),
		new("Armadura Arcana", 0.30f, 200),
		new("Placas Rúnicas", 0.38f, 280),
		new("Égida del Lord Mago", 0.48f, 370),
		new("Coraza del Infinito", 0.58f, 500),
		new("Manto Estelar", 0.65f, 700)
	};

	public static readonly AccessoryDef[] Accessories =
	{
		new("Ninguno", 0.00f, 0),
		new("Anillo de Poder", 0.10f, 50),
		new("Amuleto de Furia", 0.20f, 110),
		new("Guantelete Arcano", 0.30f, 190),
		new("Anillo del Archimago", 0.45f, 300),
		new("Esfera Cósmica", 0.65f, 500)
	};

	public static readonly LevelDef[] Levels =
	{
		new("Nivel 1 - Las Criptas", Decode(Map1), new Vector2(2.5f, 1.5f), new[]
		{
			new EnemySpawn(new Vector2(5.5f, 3.5f), 60f, EnemyType.Melee),
			new EnemySpawn(new Vector2(10.5f, 5.5f), 40f, EnemyType.Ranged),
			new EnemySpawn(new Vector2(3.5f, 10.5f), 60f, EnemyType.Melee),
			new EnemySpawn(new Vector2(13.5f, 13.5f), 40f, EnemyType.Ranged),
			new EnemySpawn(new Vector2(7.5f, 7.5f), 100f, EnemyType.Melee, 1.6f, 40, 15, 25)
		}, new[]
		{
			new LightDef(new Vector2(1.5f, 1.5f), Color.FromHtml("ffb066")),
			new LightDef(new Vector2(1.5f, 9.5f), Color.FromHtml("ffb066")),
			new LightDef(new Vector2(8.5f, 1.5f), Color.FromHtml("ffb066")),
			new LightDef(new Vector2(5.5f, 3.5f), Color.FromHtml("ffb066")),
			new LightDef(new Vector2(10.5f, 5.5f), Color.FromHtml("ffb066")),
			new LightDef(new Vector2(3.5f, 10.5f), Color.FromHtml("ffb066")),
			new LightDef(new Vector2(9.5f, 11.5f), Color.FromHtml("ffb066")),
			new LightDef(new Vector2(13.5f, 13.5f), Color.FromHtml("ffb066")),
			new LightDef(new Vector2(7.5f, 7.5f), Color.FromHtml("ffb066")),
			new LightDef(new Vector2(14.5f, 5.5f), Color.FromHtml("ffb066"), 2f, 12f)
		}),
		new("Nivel 2 - El Bosque de Piedra", Decode(Map2), new Vector2(2.5f, 1.5f), new[]
		{
			new EnemySpawn(new Vector2(13.5f, 1.5f), 90f, EnemyType.Melee, 1.3f, 30),
			new EnemySpawn(new Vector2(1.5f, 13.5f), 90f, EnemyType.Melee, 1.3f, 30),
			new EnemySpawn(new Vector2(13.5f, 14.5f), 60f, EnemyType.Ranged, 1.2f, 35),
			new EnemySpawn(new Vector2(8f, 4.5f), 150f, EnemyType.Tank, 0.8f, 55, 20, 35)
		}, new[]
		{
			new LightDef(new Vector2(1.5f, 1.5f), Color.FromHtml("ffb066")),
			new LightDef(new Vector2(14.5f, 1.5f), Color.FromHtml("ffb066")),
			new LightDef(new Vector2(1.5f, 14.5f), Color.FromHtml("ffb066")),
			new LightDef(new Vector2(14.5f, 14.5f), Color.FromHtml("ffb066")),
			new LightDef(new Vector2(8f, 4.5f), Color.FromHtml("ffb066")),
			new LightDef(new Vector2(4f, 8f), Color.FromHtml("ffb066")),
			new LightDef(new Vector2(8f, 11.5f), Color.FromHtml("ffb066")),
			new LightDef(new Vector2(12f, 8f), Color.FromHtml("ffb066")),
		}),
		new("Nivel 3 - El Núcleo Arcano", Decode(Map3), new Vector2(1.5f, 1.5f), new[]
		{
			new EnemySpawn(new Vector2(14.5f, 1.5f), 120f, EnemyType.Melee, 1.4f, 40),
			new EnemySpawn(new Vector2(1.5f, 14.5f), 90f, EnemyType.Ranged, 1.2f, 45),
			new EnemySpawn(new Vector2(14.5f, 14.5f), 120f, EnemyType.Tank, 0.9f, 50),
			new EnemySpawn(new Vector2(8f, 8f), 260f, EnemyType.Melee, 2.0f, 120, 50, 80)
		}, new[]
		{
			new LightDef(new Vector2(1.5f, 1.5f), Color.FromHtml("b388ff")),
			new LightDef(new Vector2(1.5f, 8f), Color.FromHtml("b388ff")),
			new LightDef(new Vector2(14.5f, 1.5f), Color.FromHtml("b388ff")),
			new LightDef(new Vector2(8f, 1.5f), Color.FromHtml("b388ff")),
			new LightDef(new Vector2(1.5f, 14.5f), Color.FromHtml("b388ff")),
			new LightDef(new Vector2(8f, 14.5f), Color.FromHtml("b388ff")),
			new LightDef(new Vector2(14.5f, 14.5f), Color.FromHtml("b388ff")),
			new LightDef(new Vector2(14.5f, 8f), Color.FromHtml("b388ff")),
			new LightDef(new Vector2(8f, 8f), Color.FromHtml("b388ff"), 2.4f, 12f)
		}),
		new("Nivel 4 - Catacumbas", Decode(Map4), new Vector2(8f, 8f), new[]
		{
			new EnemySpawn(new Vector2(2.5f, 2.5f), 150f, EnemyType.Tank, 0.9f),
			new EnemySpawn(new Vector2(13.5f, 2.5f), 100f, EnemyType.Ranged, 1.0f),
			new EnemySpawn(new Vector2(2.5f, 13.5f), 120f, EnemyType.Melee, 1.5f),
			new EnemySpawn(new Vector2(13.5f, 13.5f), 100f, EnemyType.Ranged, 1.0f),
			new EnemySpawn(new Vector2(8f, 13.5f), 400f, EnemyType.Tank, 0.8f, 150, 80, 150)
		}, new[]
		{
			new LightDef(new Vector2(8f, 8f), Color.FromHtml("ffb066"), 1.6f, 8f),
			new LightDef(new Vector2(2.5f, 2.5f), Color.FromHtml("ffb066")),
			new LightDef(new Vector2(13.5f, 2.5f), Color.FromHtml("ffb066")),
			new LightDef(new Vector2(2.5f, 13.5f), Color.FromHtml("ffb066")),
			new LightDef(new Vector2(13.5f, 13.5f), Color.FromHtml("ffb066"))
		}),
		new("Nivel 5 - El Trono Sangriento", Decode(Map5), new Vector2(8f, 14.5f), new[]
		{
			new EnemySpawn(new Vector2(8f, 6f), 4500f, EnemyType.Boss, 1.0f, 1000, 500, 1000)
		}, new[]
		{
			new LightDef(new Vector2(8f, 14.5f), Color.FromHtml("ff5555")),
			new LightDef(new Vector2(8f, 6f), Color.FromHtml("ff5555"), 2f, 8f),
			new LightDef(new Vector2(4.5f, 4.5f), Color.FromHtml("ff5555")),
			new LightDef(new Vector2(11.5f, 4.5f), Color.FromHtml("ff5555")),
			new LightDef(new Vector2(4.5f, 11.5f), Color.FromHtml("ff5555")),
			new LightDef(new Vector2(11.5f, 11.5f), Color.FromHtml("ff5555"))
		}),
		new("Nivel 6 - La Biblioteca", Decode(Map6), new Vector2(1.5f, 4.5f), new[]
		{
			new EnemySpawn(new Vector2(8f, 4.5f), 100f, EnemyType.Melee),
			new EnemySpawn(new Vector2(14.5f, 4.5f), 100f, EnemyType.Melee),
			new EnemySpawn(new Vector2(14.5f, 14.5f), 100f, EnemyType.Ranged)
		}, new[]
		{
			new LightDef(new Vector2(1.5f, 4.5f), Color.FromHtml("ffd27a")),
			new LightDef(new Vector2(8f, 4.5f), Color.FromHtml("ffd27a")),
			new LightDef(new Vector2(14.5f, 4.5f), Color.FromHtml("ffd27a")),
			new LightDef(new Vector2(14.5f, 14.5f), Color.FromHtml("ffd27a")),
			new LightDef(new Vector2(7.5f, 7.5f), Color.FromHtml("ffd27a"))
		}, MissionType.Collect, 3),
		new("Nivel 7 - El Puente", Decode(Map7), new Vector2(1.5f, 1.5f), new[]
		{
			new EnemySpawn(new Vector2(8f, 2f), 150f, EnemyType.Tank),
			new EnemySpawn(new Vector2(3.5f, 7.5f), 150f, EnemyType.Tank),
			new EnemySpawn(new Vector2(11.5f, 7.5f), 150f, EnemyType.Tank)
		}, new[]
		{
			new LightDef(new Vector2(1.5f, 1.5f), Color.FromHtml("ffb066")),
			new LightDef(new Vector2(8f, 2f), Color.FromHtml("ffb066")),
			new LightDef(new Vector2(3.5f, 7.5f), Color.FromHtml("ffb066")),
			new LightDef(new Vector2(11.5f, 7.5f), Color.FromHtml("ffb066")),
			new LightDef(new Vector2(8.5f, 13.5f), Color.FromHtml("ffb066"))
		}, MissionType.Collect, 3),
		new("Nivel 8 - El Corazón del Vacío", Decode(Map8), new Vector2(7.5f, 7.5f), new[]
		{
			new EnemySpawn(new Vector2(8f, 1.5f), 7500f, EnemyType.Sentinel, 0.5f, 5000, 2000, 5000)
		}, new[]
		{
			new LightDef(new Vector2(7.5f, 7.5f), Color.FromHtml("9d4edd"), 1.8f, 8f),
			new LightDef(new Vector2(8f, 1.5f), Color.FromHtml("9d4edd"), 1.8f, 7f),
			new LightDef(new Vector2(1.5f, 2.5f), Color.FromHtml("9d4edd")),
			new LightDef(new Vector2(14.5f, 2.5f), Color.FromHtml("9d4edd")),
			new LightDef(new Vector2(1.5f, 13.5f), Color.FromHtml("9d4edd")),
			new LightDef(new Vector2(14.5f, 13.5f), Color.FromHtml("9d4edd"))
		})
	};

	private static int[][] Decode(string map) => map.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries)
		.Select(row => row.Trim().Select(cell => cell - '0').ToArray()).ToArray();

	private const string Map1 = """
1111111111111111
1000000000100001
1011011101111001
1010000100001001
1010110111011001
1000100001000001
1010101101011101
1010001000000101
1011101011110101
1000100010010001
1110111010111101
1000001000000101
1011101111110101
1000100000010001
1100111111011101
1111111111111111
""";

	private const string Map2 = """
1111111111111111
1000000110000001
1022220110222201
1020000000000201
1020222002220201
1000200000020001
1102202222022011
1100002002000011
1100002002000011
1102202222022011
1000200000020001
1020222002220201
1020000000000201
1022220110222201
1000000110000001
1111111111111111
""";

	private const string Map3 = """
1111111111111111
1000000000000001
1033000330003301
1033000330003301
1000033333300001
1000030000300001
1033330000333301
1000000000000001
1000000000000001
1033330000333301
1000030000300001
1000033333300001
1033000330003301
1033000330003301
1000000000000001
1111111111111111
""";

	private const string Map4 = """
6666666666666666
6000000000000006
6000000000000006
6006666006666006
6006000000006006
6006000000006006
6000000000000006
6000000000000006
6000000000000006
6000000000000006
6006000000006006
6006000000006006
6006666006666006
6000000000000006
6000000000000006
6666666666666666
""";

	private const string Map5 = """
1111111111111111
1000000000000001
1040000000000401
1000000440000001
1000000440000001
1040000000000401
1000000000000001
1000000000000001
1000000000000001
1000000000000001
1040000000000401
1000000440000001
1000000440000001
1040000000000401
1000000000000001
1111111111111111
""";

	private const string Map6 = """
1111111111111111
1000010000100001
1033010330103301
1033010330103301
1000000000000001
1101110110111011
1000010000100001
1033010330103301
1033000000003301
1000010330100001
1101110110111011
1000010000100001
1033010330103301
1033010330103301
1000000000000001
1111111111111111
""";

	private const string Map7 = """
1111111111111111
1000000000000001
1000000000000001
1444444004444441
1444444004444441
1000000000000001
1000000000000001
1440044444400441
1440044444400441
1000000000000001
1000000000000001
1444444004444441
1444444004444441
1000000000000051
1000000000000001
1111111111111111
""";

	private const string Map8 = """
4444444004444444
4000004004000004
4033304004033304
4030300000030304
4033304004033304
4000004004000004
4440444004440444
0000000000000000
0000000000000000
4440444004440444
4000004004000004
4033304004033304
4030300000030304
4033304004033304
4000004004000004
4444444004444444
""";
}
