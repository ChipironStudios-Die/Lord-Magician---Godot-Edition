class_name GameData
extends RefCounted
## Datos portados del MainActivity.kt original. Los mapas se han conservado celda a celda.
## 0 es suelo; el resto son paredes/elementos sólidos con su variante visual.

enum EnemyType { MELEE, RANGED, TANK, BOSS, SENTINEL }
enum ProjectileOwner { PLAYER, ENEMY }
enum MissionType { ELIMINATE, ESCAPE, COLLECT }
enum Difficulty { EASY, NORMAL, HARD }
enum GraphicsQuality { PERFORMANCE, STANDARD, HIGH_DEFINITION }
enum GamePhase { MAIN_MENU, PLAYING, LEVEL_CLEAR, SHOP, GAME_OVER, FINISHED, SETTINGS, PAUSED, MULTIPLAYER_MENU }


class WeaponDef:
    var name: String
    var damage: float
    var mana_cost: float
    var cooldown: float
    var cost: int

    func _init(p_name: String, p_damage: float, p_mana_cost: float, p_cooldown: float, p_cost: int) -> void:
        name = p_name
        damage = p_damage
        mana_cost = p_mana_cost
        cooldown = p_cooldown
        cost = p_cost


class ArmorDef:
    var name: String
    var defense: float
    var cost: int

    func _init(p_name: String, p_defense: float, p_cost: int) -> void:
        name = p_name
        defense = p_defense
        cost = p_cost


class AccessoryDef:
    var name: String
    var attack_bonus: float
    var cost: int

    func _init(p_name: String, p_attack_bonus: float, p_cost: int) -> void:
        name = p_name
        attack_bonus = p_attack_bonus
        cost = p_cost


class EnemySpawn:
    var position: Vector2
    var hp: float
    var type: int
    var speed: float
    var exp_reward: int
    var gold_min: int
    var gold_max: int

    func _init(p_position: Vector2, p_hp: float, p_type: int, p_speed: float = 1.2, p_exp_reward: int = 20, p_gold_min: int = 5, p_gold_max: int = 15) -> void:
        position = p_position
        hp = p_hp
        type = p_type
        speed = p_speed
        exp_reward = p_exp_reward
        gold_min = p_gold_min
        gold_max = p_gold_max


class LightDef:
    var position: Vector2
    var color: Color
    var energy: float
    var light_range: float
    var height: float

    func _init(p_position: Vector2, p_color: Color, p_energy: float = 1.8, p_range: float = 9.0, p_height: float = 0.85) -> void:
        position = p_position
        color = p_color
        energy = p_energy
        light_range = p_range
        height = p_height


class LevelDef:
    var name: String
    var map: Array
    var start: Vector2
    var spawns: Array
    var lights: Array
    var mission: int
    var target_count: int

    func _init(p_name: String, p_map: Array, p_start: Vector2, p_spawns: Array, p_lights: Array, p_mission: int = MissionType.ELIMINATE, p_target_count: int = 0) -> void:
        name = p_name
        map = p_map
        start = p_start
        spawns = p_spawns
        lights = p_lights
        mission = p_mission
        target_count = p_target_count


static var weapons: Array[WeaponDef] = []
static var armors: Array[ArmorDef] = []
static var accessories: Array[AccessoryDef] = []
static var levels: Array[LevelDef] = []


static func _static_init() -> void:
    weapons = [
        WeaponDef.new("Vara de Aprendiz", 25.0, 20.0, 0.35, 0),
        WeaponDef.new("Daga Arcana Arrojadiza", 32.0, 14.0, 0.20, 45),
        WeaponDef.new("Bastón de Fuego", 45.0, 22.0, 0.32, 90),
        WeaponDef.new("Cetro de Hielo", 55.0, 24.0, 0.30, 150),
        WeaponDef.new("Cetro Arcano", 65.0, 25.0, 0.28, 220),
        WeaponDef.new("Vara del Trueno", 80.0, 28.0, 0.26, 300),
        WeaponDef.new("Báculo del Lord Mago", 100.0, 32.0, 0.22, 400),
        WeaponDef.new("Reliquia del Vacío", 130.0, 35.0, 0.20, 550),
        WeaponDef.new("Apocalipsis", 180.0, 45.0, 0.18, 800),
    ]

    armors = [
        ArmorDef.new("Túnica de Tela", 0.00, 0),
        ArmorDef.new("Chaleco de Cuero", 0.08, 35),
        ArmorDef.new("Chaleco Reforzado", 0.15, 75),
        ArmorDef.new("Cota de Malla Arcana", 0.22, 130),
        ArmorDef.new("Armadura Arcana", 0.30, 200),
        ArmorDef.new("Placas Rúnicas", 0.38, 280),
        ArmorDef.new("Égida del Lord Mago", 0.48, 370),
        ArmorDef.new("Coraza del Infinito", 0.58, 500),
        ArmorDef.new("Manto Estelar", 0.65, 700),
    ]

    accessories = [
        AccessoryDef.new("Ninguno", 0.00, 0),
        AccessoryDef.new("Anillo de Poder", 0.10, 50),
        AccessoryDef.new("Amuleto de Furia", 0.20, 110),
        AccessoryDef.new("Guantelete Arcano", 0.30, 190),
        AccessoryDef.new("Anillo del Archimago", 0.45, 300),
        AccessoryDef.new("Esfera Cósmica", 0.65, 500),
    ]

    var torch := Color.html("ffb066")
    var arcane := Color.html("b388ff")
    var blood := Color.html("ff5555")
    var library := Color.html("ffd27a")
    var void_purple := Color.html("9d4edd")

    levels = [
        LevelDef.new("Nivel 1 - Las Criptas", _decode(MAP_1), Vector2(2.5, 1.5), [
            EnemySpawn.new(Vector2(5.5, 3.5), 60.0, EnemyType.MELEE),
            EnemySpawn.new(Vector2(10.5, 5.5), 40.0, EnemyType.RANGED),
            EnemySpawn.new(Vector2(3.5, 10.5), 60.0, EnemyType.MELEE),
            EnemySpawn.new(Vector2(13.5, 13.5), 40.0, EnemyType.RANGED),
            EnemySpawn.new(Vector2(7.5, 7.5), 100.0, EnemyType.MELEE, 1.6, 40, 15, 25),
        ], [
            LightDef.new(Vector2(1.5, 1.5), torch),
            LightDef.new(Vector2(1.5, 9.5), torch),
            LightDef.new(Vector2(8.5, 1.5), torch),
            LightDef.new(Vector2(5.5, 3.5), torch),
            LightDef.new(Vector2(10.5, 5.5), torch),
            LightDef.new(Vector2(3.5, 10.5), torch),
            LightDef.new(Vector2(9.5, 11.5), torch),
            LightDef.new(Vector2(13.5, 13.5), torch),
            LightDef.new(Vector2(7.5, 7.5), torch),
            LightDef.new(Vector2(14.5, 5.5), torch, 2.0, 12.0),
        ]),
        LevelDef.new("Nivel 2 - El Bosque de Piedra", _decode(MAP_2), Vector2(2.5, 1.5), [
            EnemySpawn.new(Vector2(13.5, 1.5), 90.0, EnemyType.MELEE, 1.3, 30),
            EnemySpawn.new(Vector2(1.5, 13.5), 90.0, EnemyType.MELEE, 1.3, 30),
            EnemySpawn.new(Vector2(13.5, 14.5), 60.0, EnemyType.RANGED, 1.2, 35),
            EnemySpawn.new(Vector2(8.0, 4.5), 150.0, EnemyType.TANK, 0.8, 55, 20, 35),
        ], [
            LightDef.new(Vector2(1.5, 1.5), torch),
            LightDef.new(Vector2(14.5, 1.5), torch),
            LightDef.new(Vector2(1.5, 14.5), torch),
            LightDef.new(Vector2(14.5, 14.5), torch),
            LightDef.new(Vector2(8.0, 4.5), torch),
            LightDef.new(Vector2(4.0, 8.0), torch),
            LightDef.new(Vector2(8.0, 11.5), torch),
            LightDef.new(Vector2(12.0, 8.0), torch),
        ]),
        LevelDef.new("Nivel 3 - El Núcleo Arcano", _decode(MAP_3), Vector2(1.5, 1.5), [
            EnemySpawn.new(Vector2(14.5, 1.5), 120.0, EnemyType.MELEE, 1.4, 40),
            EnemySpawn.new(Vector2(1.5, 14.5), 90.0, EnemyType.RANGED, 1.2, 45),
            EnemySpawn.new(Vector2(14.5, 14.5), 120.0, EnemyType.TANK, 0.9, 50),
            EnemySpawn.new(Vector2(8.0, 8.0), 260.0, EnemyType.MELEE, 2.0, 120, 50, 80),
        ], [
            LightDef.new(Vector2(1.5, 1.5), arcane),
            LightDef.new(Vector2(1.5, 8.0), arcane),
            LightDef.new(Vector2(14.5, 1.5), arcane),
            LightDef.new(Vector2(8.0, 1.5), arcane),
            LightDef.new(Vector2(1.5, 14.5), arcane),
            LightDef.new(Vector2(8.0, 14.5), arcane),
            LightDef.new(Vector2(14.5, 14.5), arcane),
            LightDef.new(Vector2(14.5, 8.0), arcane),
            LightDef.new(Vector2(8.0, 8.0), arcane, 2.4, 12.0),
        ]),
        LevelDef.new("Nivel 4 - Catacumbas", _decode(MAP_4), Vector2(8.0, 8.0), [
            EnemySpawn.new(Vector2(2.5, 2.5), 150.0, EnemyType.TANK, 0.9),
            EnemySpawn.new(Vector2(13.5, 2.5), 100.0, EnemyType.RANGED, 1.0),
            EnemySpawn.new(Vector2(2.5, 13.5), 120.0, EnemyType.MELEE, 1.5),
            EnemySpawn.new(Vector2(13.5, 13.5), 100.0, EnemyType.RANGED, 1.0),
            EnemySpawn.new(Vector2(8.0, 13.5), 400.0, EnemyType.TANK, 0.8, 150, 80, 150),
        ], [
            LightDef.new(Vector2(8.0, 8.0), torch, 2.4, 12.0),
            LightDef.new(Vector2(2.0, 2.0), torch),
            LightDef.new(Vector2(2.0, 8.0), torch),
            LightDef.new(Vector2(14.0, 2.0), torch),
            LightDef.new(Vector2(8.0, 2.0), torch),
            LightDef.new(Vector2(2.0, 14.0), torch),
            LightDef.new(Vector2(8.0, 14.0), torch),
            LightDef.new(Vector2(14.0, 14.0), torch),
            LightDef.new(Vector2(14.0, 8.0), torch),
        ]),
        LevelDef.new("Nivel 5 - El Trono Sangriento", _decode(MAP_5), Vector2(8.0, 14.5), [
            EnemySpawn.new(Vector2(8.0, 6.0), 4500.0, EnemyType.BOSS, 1.0, 1000, 500, 1000),
        ], [
            LightDef.new(Vector2(8.0, 14.5), blood),
            LightDef.new(Vector2(8.0, 6.0), blood, 2.0, 8.0),
            LightDef.new(Vector2(4.5, 4.5), blood),
            LightDef.new(Vector2(11.5, 4.5), blood),
            LightDef.new(Vector2(4.5, 11.5), blood),
            LightDef.new(Vector2(11.5, 11.5), blood),
        ]),
        LevelDef.new("Nivel 6 - La Biblioteca", _decode(MAP_6), Vector2(1.5, 4.5), [
            EnemySpawn.new(Vector2(8.0, 4.5), 100.0, EnemyType.MELEE),
            EnemySpawn.new(Vector2(14.5, 4.5), 100.0, EnemyType.MELEE),
            EnemySpawn.new(Vector2(14.5, 14.5), 100.0, EnemyType.RANGED),
        ], [
            LightDef.new(Vector2(1.5, 4.5), library),
            LightDef.new(Vector2(8.0, 4.5), library),
            LightDef.new(Vector2(14.5, 4.5), library),
            LightDef.new(Vector2(14.5, 14.5), library),
            LightDef.new(Vector2(7.5, 7.5), library),
        ], MissionType.COLLECT, 3),
        LevelDef.new("Nivel 7 - El Puente", _decode(MAP_7), Vector2(1.5, 1.5), [
            EnemySpawn.new(Vector2(8.0, 2.0), 150.0, EnemyType.TANK),
            EnemySpawn.new(Vector2(3.5, 7.5), 150.0, EnemyType.TANK),
            EnemySpawn.new(Vector2(11.5, 7.5), 150.0, EnemyType.TANK),
        ], [
            LightDef.new(Vector2(1.5, 1.5), torch),
            LightDef.new(Vector2(8.0, 2.0), torch),
            LightDef.new(Vector2(3.5, 7.5), torch),
            LightDef.new(Vector2(11.5, 7.5), torch),
            LightDef.new(Vector2(8.5, 13.5), torch),
        ], MissionType.COLLECT, 3),
        LevelDef.new("Nivel 8 - El Corazón del Vacío", _decode(MAP_8), Vector2(7.5, 7.5), [
            EnemySpawn.new(Vector2(8.0, 1.5), 7500.0, EnemyType.SENTINEL, 0.5, 5000, 2000, 5000),
        ], [
            LightDef.new(Vector2(7.5, 7.5), void_purple, 1.8, 8.0),
            LightDef.new(Vector2(8.0, 1.5), void_purple, 1.8, 7.0),
            LightDef.new(Vector2(1.5, 2.5), void_purple),
            LightDef.new(Vector2(14.5, 2.5), void_purple),
            LightDef.new(Vector2(1.5, 13.5), void_purple),
            LightDef.new(Vector2(14.5, 13.5), void_purple),
        ]),
    ]


## Convierte el mapa de texto en filas de enteros (cada carácter es un código de celda).
static func _decode(map_text: String) -> Array:
    var rows: Array = []
    for line in map_text.strip_edges().split("\n", false):
        var row := PackedInt32Array()
        for ch in line.strip_edges():
            row.append(ch.unicode_at(0) - 48)
        rows.append(row)
    return rows


const MAP_1 := """
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
"""

const MAP_2 := """
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
"""

const MAP_3 := """
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
"""

const MAP_4 := """
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
"""

const MAP_5 := """
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
"""

const MAP_6 := """
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
"""

const MAP_7 := """
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
"""

const MAP_8 := """
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
"""
