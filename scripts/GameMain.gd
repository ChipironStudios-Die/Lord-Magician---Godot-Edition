extends Node2D
## Port funcional de MainActivity.kt a GDScript (equivale a GameMain.cs).
## Godot gestiona ventana, audio e input; las paredes y los sprites son una escena 3D
## real dentro de un SubViewport y el HUD/menús se dibujan en 2D por encima.

const GamePhase = GameData.GamePhase
const GraphicsQuality = GameData.GraphicsQuality
const Difficulty = GameData.Difficulty
const EnemyType = GameData.EnemyType
const ProjectileOwner = GameData.ProjectileOwner
const MissionType = GameData.MissionType

const FIELD_OF_VIEW := 1.57
const WALL_MAX_DISTANCE := 18.0
const PLAYER_WALL_COLLISION_RADIUS := 0.22
const ENEMY_PROJECTILE_PLAYER_HIT_RADIUS := 0.40
const ITEM_PICKUP_RADIUS := 0.50
const MULTIPLAYER_PORT := 8910
const EYE_HEIGHT_3D := 0.5
const ENEMY_BASE_WORLD_HEIGHT := 1.0 # altura en unidades de mundo para enemy_scale = 1 (igual que asume _enemy_collision_radius)

# Textura procedural de ladrillos (ver _create_wall_textures)
const WALL_TEXTURE_SIZE := 256
const BRICK_ROWS := 8
const BRICK_COLS := 8
const BRICK_ROW_HEIGHT := 32 # WALL_TEXTURE_SIZE / BRICK_ROWS
const BRICK_COL_WIDTH := 32 # WALL_TEXTURE_SIZE / BRICK_COLS
const BRICK_HALF_WIDTH := 16 # desfase de las hiladas impares (medio ladrillo)
const MORTAR_PX := 3

enum ItemType { POTION_RED, POTION_BLUE, SCROLL }
enum ShopEntryKind { POTION, WEAPON, ARMOR, ACCESSORY, SHIELD }
enum UiAction {
    NONE,
    START, OPEN_SETTINGS, OPEN_MULTIPLAYER,
    SET_DIFFICULTY_EASY, SET_DIFFICULTY_NORMAL, SET_DIFFICULTY_HARD,
    SET_QUALITY_PERFORMANCE, SET_QUALITY_STANDARD, SET_QUALITY_HIGH_DEF,
    SET_SENSITIVITY, SET_MUSIC_VOLUME, SET_FX_VOLUME, SETTINGS_BACK,
    LEVEL_SHOP, LEVEL_CONTINUE, SHOP_CONTINUE,
    MP_HOST, MP_JOIN, MP_BACK, MP_FOCUS_IP_FIELD,
    RETRY, GAME_OVER_MENU, RESUME, PAUSE_QUIT, VICTORY_MENU,
}


# =====================================================================
# TIPOS AUXILIARES (en C# eran clases/records privados al final del archivo)
# =====================================================================

class UiHit:
    var action: int
    var rect: Rect2

    func _init(p_action: int, p_rect: Rect2) -> void:
        action = p_action
        rect = p_rect


class ShopEntry:
    var label: String
    var cost: int
    var kind: int
    var index: int

    func _init(p_label: String, p_cost: int, p_kind: int, p_index: int) -> void:
        label = p_label
        cost = p_cost
        kind = p_kind
        index = p_index


## Fila de la tienda: o bien una cabecera de categoría (header) o bien un objeto a la venta (entry).
class ShopRow:
    var header: String = ""
    var entry: ShopEntry = null

    func _init(p_header: String, p_entry: ShopEntry) -> void:
        header = p_header
        entry = p_entry


## Estado replicado de un jugador remoto: solo lo que hace falta para dibujarlo
## en el mundo y en el minimapa. Nada de esto se valida en el host todavía.
class RemotePlayerState:
    var position := Vector2.ZERO
    var angle := 0.0
    var health := 100.0
    var max_health := 100.0
    var player_name := "Jugador"


class PlayerState:
    var position := Vector2.ZERO
    var angle := 0.0
    var health := 100.0
    var max_health := 100.0
    var mana := 100.0
    var max_mana := 100.0
    var shoot_cooldown := 0.0
    var level := 1
    var experience := 0.0
    var exp_to_next := 100.0
    var gold := 0
    var equipped_weapon := 0
    var equipped_armor := 0
    var equipped_accessory := 0
    var owned_weapons: Array[int] = [0]
    var owned_armors: Array[int] = [0]
    var owned_accessories: Array[int] = [0]
    var owns_shield := false
    var shield_time_remaining := 0.0
    var shield_cooldown_remaining := 0.0
    var screen_shake := 0.0
    var items_collected := 0

    var weapon_damage: float:
        get:
            return GameData.weapons[equipped_weapon].damage * (1.0 + 0.05 * (level - 1)) * (1.0 + GameData.accessories[equipped_accessory].attack_bonus)
    var weapon_mana_cost: float:
        get:
            return GameData.weapons[equipped_weapon].mana_cost
    var weapon_cooldown: float:
        get:
            return GameData.weapons[equipped_weapon].cooldown
    var armor_defense: float:
        get:
            return GameData.armors[equipped_armor].defense


class Enemy:
    var position: Vector2
    var hp: float
    var max_hp: float
    var type: int
    var speed: float
    var exp_reward: int
    var gold_min: int
    var gold_max: int
    var attack_cooldown := 0.0
    var hit_flash := 0.0
    var rewarded := false
    var animation_frame := 0
    var animation_timer := 0.0
    var player_visible := false
    var alive: bool:
        get:
            return hp > 0.0

    func _init(p_position: Vector2, p_hp: float, p_type: int, p_speed: float, p_exp_reward: int, p_gold_min: int, p_gold_max: int) -> void:
        position = p_position
        hp = p_hp
        max_hp = p_hp
        type = p_type
        speed = p_speed
        exp_reward = p_exp_reward
        gold_min = p_gold_min
        gold_max = p_gold_max


class Projectile:
    var position: Vector2
    var angle: float
    var speed: float
    var damage: float
    var owner_kind: int
    var color: Color

    func _init(p_position: Vector2, p_angle: float, p_speed: float, p_damage: float, p_owner_kind: int, p_color: Color) -> void:
        position = p_position
        angle = p_angle
        speed = p_speed
        damage = p_damage
        owner_kind = p_owner_kind
        color = p_color


class WorldItem:
    var position: Vector2
    var type: int
    var color: Color

    func _init(p_position: Vector2, p_type: int, p_color: Color) -> void:
        position = p_position
        type = p_type
        color = p_color


class Particle:
    var position: Vector2
    var velocity: Vector2
    var life: float
    var max_life: float
    var color: Color

    func _init(p_position: Vector2, p_velocity: Vector2, p_life: float, p_max_life: float, p_color: Color) -> void:
        position = p_position
        velocity = p_velocity
        life = p_life
        max_life = p_max_life
        color = p_color


## Fuente única de verdad para el layout de los controles táctiles: el dibujado
## (_draw_touch_controls) y la detección de toques (_handle_playing_press, _update_touch_joystick)
## usan exactamente los mismos valores, escalados con el alto real de pantalla en vez
## de píxeles fijos pensados para el lienzo de diseño de 1280x720.
class TouchLayout:
    var ui_scale: float
    var joystick_center: Vector2
    var joystick_radius: float
    var joystick_knob_radius: float
    var shoot_center: Vector2
    var shoot_radius: float
    var shield_center: Vector2
    var shield_radius: float
    var pause_center: Vector2
    var pause_radius: float
    var joystick_zone_width: float
    var joystick_zone_height: float

    func _init(size: Vector2) -> void:
        ui_scale = clampf(size.y / 720.0, 0.85, 2.5)
        var joystick_margin_x := 260.0 * ui_scale
        var joystick_margin_bottom := 220.0 * ui_scale
        var shoot_margin := 112.0 * ui_scale
        var pause_margin := 56.0 * ui_scale
        joystick_center = Vector2(joystick_margin_x, size.y - joystick_margin_bottom)
        joystick_radius = 130.0 * ui_scale
        joystick_knob_radius = 54.0 * ui_scale
        shoot_center = Vector2(size.x - shoot_margin, size.y - shoot_margin)
        shoot_radius = 84.0 * ui_scale
        # El botón de escudo se apoya arriba a la izquierda del de disparo, a una distancia
        # proporcional a ambos radios, para que no se solapen a ningún tamaño de pantalla.
        shield_center = shoot_center + Vector2(-(shoot_radius + 46.0 * ui_scale), -(shoot_radius + 46.0 * ui_scale))
        shield_radius = 52.0 * ui_scale
        pause_center = Vector2(size.x - pause_margin, pause_margin)
        pause_radius = 36.0 * ui_scale
        # Zona de agarre del joystick: se calcula a partir de su propio centro/radio
        # (con margen extra), así siempre encaja aunque se mueva o cambie de tamaño.
        joystick_zone_width = joystick_center.x + joystick_radius + 60.0 * ui_scale
        joystick_zone_height = (size.y - joystick_center.y) + joystick_radius + 60.0 * ui_scale


# =====================================================================
# ESTADO
# =====================================================================

var _player := PlayerState.new()

# --- Multijugador (co-op LAN/online por IP directa, vía ENet) ---
var _peer: ENetMultiplayerPeer = null
var _multiplayer_active := false
var _network_status := ""
var _join_ip_text := "127.0.0.1"
var _join_field_active := false
var _network_send_timer := 0.0
var _remote_players := {} # id de peer (int) -> RemotePlayerState

var _enemies: Array[Enemy] = []
var _projectiles: Array[Projectile] = []
var _items: Array[WorldItem] = []
var _particles: Array[Particle] = []
var _ui_hits: Array[UiHit] = []
var _wall_textures := {} # código de pared (int) -> Texture2D
var _sounds := {} # nombre (String) -> AudioStream

var _font: Font
var _logo: Texture2D
var _staff: Texture2D
var _red_enemy: Texture2D
var _green_wizard: Texture2D
var _blue_tank: Texture2D
var _boss: Texture2D
var _sentinel: Texture2D
var _red_potion: Texture2D
var _blue_potion: Texture2D
var _music_player: AudioStreamPlayer
var _current_music_name := ""

var _phase: int = GamePhase.MAIN_MENU
var _difficulty: int = Difficulty.NORMAL
var _graphics_quality: int = GraphicsQuality.STANDARD
var _level_index := 0
var _menu_index := 0
var _shop_scroll := 0.0
var _shop_focus_index := 0
var _shop_pointer_active := false
var _shop_pointer_touch := -1
var _shop_pointer_last_pos := Vector2.ZERO
var _shop_pointer_total_drag := 0.0
var _music_volume := 0.5
var _fx_volume := 0.8
var _look_sensitivity := 1.0
var _frame_tick := 0.0
var _touch_move := Vector2.ZERO
var _touch_move_active := false
var _touch_shooting := false
var _mouse_shooting := false
var _joystick_touch := -1
var _look_touch := -1
var _last_look_position := Vector2.ZERO
var _dragging_slider: int = UiAction.NONE
var _dragging_slider_touch_index := -1
var _debug_overlay_enabled := false

var _wall_colors := {
    1: Color.html("5d4037"),
    2: Color.html("2e7d32"),
    3: Color.html("4527a0"),
    4: Color.html("c62828"),
    5: Color.html("ffd54f"),
    6: Color.html("616161"),
    7: Color.html("424242"),
}


func _ready() -> void:
    _font = ThemeDB.fallback_font
    _logo = _load_texture("res://assets/sprites/game_logo.png")
    _staff = _load_texture("res://assets/sprites/player_staff.png")
    _red_enemy = _load_texture("res://assets/sprites/red_enemy_spritesheet.png")
    _green_wizard = _load_texture("res://assets/sprites/green_wizard_spritesheet.png")
    _blue_tank = _load_texture("res://assets/sprites/blue_tank_spritesheet.png")
    _boss = _load_texture("res://assets/sprites/boss_spritesheet.png")
    _sentinel = _load_texture("res://assets/sprites/sentinel_spritesheet.png")
    _red_potion = _load_texture("res://assets/sprites/red_potion.png")
    _blue_potion = _load_texture("res://assets/sprites/blue_potion.png")

    _load_audio()
    _create_wall_textures()
    _setup_3d_scaffold()
    _play_music("bgm_menu")
    queue_redraw()


# =====================================================================
# GRÁFICOS 3D (sustituye al raycaster para el pintado de paredes/suelo)
# ---------------------------------------------------------------------
# El resto del juego (posición del jugador, colisiones, IA, HUD, tienda,
# multijugador...) sigue funcionando exactamente igual, en el mismo
# espacio 2D de rejilla de siempre. Aquí solo se traduce esa rejilla a
# una escena 3D real que Godot renderiza por hardware: X de la rejilla
# sigue siendo X en 3D, e Y de la rejilla pasa a ser Z en 3D (el mundo
# 3D queda "tumbado" sobre el plano XZ, con Y como la altura).
# =====================================================================
var _viewport_3d: SubViewport
var _camera_3d: Camera3D
var _level_geometry_3d: Node3D
var _sprites_layer_3d: Node3D
var _glow_texture_3d: ImageTexture
var _solid_texture_3d: ImageTexture
var _sprite_nodes_3d := {} # Enemy / Projectile / WorldItem / RemotePlayerState -> Node3D


func _setup_3d_scaffold() -> void:
    _viewport_3d = SubViewport.new()
    _viewport_3d.size = Vector2i(1280, 720)
    _viewport_3d.render_target_update_mode = SubViewport.UPDATE_ALWAYS
    _viewport_3d.own_world_3d = true
    add_child(_viewport_3d)

    var world_environment := WorldEnvironment.new()
    var environment := Environment.new()
    environment.background_mode = Environment.BG_COLOR
    environment.background_color = Color.html("0a0810")
    environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
    environment.ambient_light_color = Color.html("3a3540")
    environment.ambient_light_energy = 0.35
    world_environment.environment = environment
    _viewport_3d.add_child(world_environment)

    # FOV horizontal (KEEP_WIDTH) para que coincida con el mismo campo de
    # visión que ya usaba el raycaster (FIELD_OF_VIEW), sea cual sea la
    # proporción de pantalla — el raycaster también medía el FOV en horizontal.
    _camera_3d = Camera3D.new()
    _camera_3d.fov = rad_to_deg(FIELD_OF_VIEW)
    _camera_3d.keep_aspect = Camera3D.KEEP_WIDTH
    _viewport_3d.add_child(_camera_3d)
    _camera_3d.current = true

    _level_geometry_3d = Node3D.new()
    _viewport_3d.add_child(_level_geometry_3d)

    # Enemigos/objetos/proyectiles/jugadores remotos viven aparte de la
    # geometría estática del nivel: se crean y destruyen todo el rato
    # durante la partida, no solo al cargar nivel.
    _sprites_layer_3d = Node3D.new()
    _viewport_3d.add_child(_sprites_layer_3d)

    _glow_texture_3d = _create_glow_texture()
    _solid_texture_3d = _create_solid_texture()


static func _create_solid_texture() -> ImageTexture:
    var image := Image.create_empty(4, 4, false, Image.FORMAT_RGBA8)
    image.fill(Color.WHITE)
    return ImageTexture.create_from_image(image)


## Textura radial blanca (centro opaco, borde transparente) para proyectiles
## y el marcador de jugadores remotos — se tiñe con modulate según el color
## que ya tenía cada uno en el sistema 2D anterior.
static func _create_glow_texture() -> ImageTexture:
    const SIZE := 64
    var image := Image.create_empty(SIZE, SIZE, false, Image.FORMAT_RGBA8)
    var center := Vector2(SIZE * 0.5, SIZE * 0.5)
    for y in SIZE:
        for x in SIZE:
            var t := clampf(Vector2(x, y).distance_to(center) / (SIZE * 0.5), 0.0, 1.0)
            var alpha := pow(1.0 - t, 2.2)
            image.set_pixel(x, y, Color(1.0, 1.0, 1.0, alpha))
    return ImageTexture.create_from_image(image)


## Reconstruye la geometría 3D del nivel a partir de la misma rejilla (Array de filas)
## que ya usa el resto del juego. Una caja de 1x1x1 por celda de pared, agrupando por
## código para reutilizar malla y material entre todas las celdas del mismo tipo.
## El suelo es un conjunto de baldosas que cubre el mapa (sin techo).
func _generate_3d_level_geometry(map: Array, lights: Array) -> void:
    if _level_geometry_3d == null:
        return
    for child in _level_geometry_3d.get_children():
        child.queue_free()

    var rows := map.size()
    var cols: int = map[0].size()

    var wall_kinds := {} # código -> [BoxMesh, StandardMaterial3D]
    for y in rows:
        for x in cols:
            var code: int = map[y][x]
            if code <= 0:
                continue
            if not wall_kinds.has(code):
                var wall_material := StandardMaterial3D.new()
                wall_material.albedo_color = _wall_color(code)
                wall_material.texture_filter = BaseMaterial3D.TEXTURE_FILTER_NEAREST_WITH_MIPMAPS
                if _wall_textures.has(code):
                    wall_material.albedo_texture = _wall_textures[code]
                var box_mesh := BoxMesh.new()
                box_mesh.size = Vector3.ONE
                wall_kinds[code] = [box_mesh, wall_material]
            var kind: Array = wall_kinds[code]
            var box := MeshInstance3D.new()
            box.mesh = kind[0]
            box.material_override = kind[1]
            box.position = Vector3(x + 0.5, 0.5, y + 0.5)
            _level_geometry_3d.add_child(box)

    # Suelo en baldosas pequeñas (no un único plano gigante): el
    # renderer "Mobile" de Godot (el que se usa para exportar a Android)
    # solo tiene en cuenta hasta 8 luces por cada malla — un plano que
    # cubriera todo el nivel podría empezar a parpadear en cuanto un nivel
    # tenga más de 8 antorchas. Con baldosas pequeñas, cada una solo ve
    # las antorchas cercanas, así que no hay tope real por nivel.
    const FLOOR_TILE_SIZE := 2
    var floor_material := StandardMaterial3D.new()
    floor_material.albedo_color = Color.html("2b2118")
    var ty := 0
    while ty < rows:
        var tx := 0
        while tx < cols:
            var tile_width := float(mini(FLOOR_TILE_SIZE, cols - tx))
            var tile_depth := float(mini(FLOOR_TILE_SIZE, rows - ty))
            var tile_mesh := PlaneMesh.new()
            tile_mesh.size = Vector2(tile_width, tile_depth)
            var floor_tile := MeshInstance3D.new()
            floor_tile.mesh = tile_mesh
            floor_tile.material_override = floor_material
            floor_tile.position = Vector3(tx + tile_width * 0.5, 0.0, ty + tile_depth * 0.5)
            _level_geometry_3d.add_child(floor_tile)
            tx += FLOOR_TILE_SIZE
        ty += FLOOR_TILE_SIZE

    # Antorchas/focos de cada nivel, colocados por coordenadas en GameData
    # (LevelDef.lights) igual que los enemigos — ver GameData.gd.
    for light: GameData.LightDef in lights:
        var torch := OmniLight3D.new()
        torch.position = Vector3(light.position.x, light.height, light.position.y)
        torch.light_color = light.color
        torch.light_energy = light.energy
        torch.omni_range = light.light_range
        torch.omni_attenuation = 1.4
        _level_geometry_3d.add_child(torch)


## Coloca la cámara 3D en la misma posición/ángulo 2D que ya lleva el jugador.
## La X e Y de la rejilla son X y Z en 3D; el ángulo 2D (0 = mirando hacia +X)
## se traduce al giro en Y de la cámara.
func _update_3d_camera(size: Vector2) -> void:
    if _camera_3d == null or _viewport_3d == null:
        return
    var target_size := Vector2i(int(size.x), int(size.y))
    if _viewport_3d.size != target_size:
        _viewport_3d.size = target_size

    _camera_3d.position = Vector3(_player.position.x, EYE_HEIGHT_3D, _player.position.y)
    # Ángulo 2D θ (dirección = cos θ, sin θ) ⇒ giro en Y de la cámara = -(θ + 90°),
    # para que la cámara mire en esa misma dirección (X_2D→X_3D, Y_2D→Z_3D).
    _camera_3d.rotation = Vector3(0.0, -(_player.angle + PI * 0.5), 0.0)


## Crea/actualiza/destruye los Sprite3D de enemigos, objetos, proyectiles y
## jugadores remotos, comparando qué sigue "vivo" en las listas de siempre
## (_enemies, _items, _projectiles, _remote_players) contra lo que ya había
## en _sprite_nodes_3d.
func _update_3d_sprites() -> void:
    if _sprites_layer_3d == null:
        return
    var active := {} # conjunto de claves vivas

    for enemy in _enemies:
        if not enemy.alive:
            continue
        active[enemy] = true
        var body_height := ENEMY_BASE_WORLD_HEIGHT * _enemy_scale(enemy.type)
        var root := _get_or_create_enemy_node(enemy, body_height)
        root.position = Vector3(enemy.position.x, body_height * 0.5, enemy.position.y)
        _update_enemy_node(root, enemy, body_height)
        # La detección vuelve a mirar si hay pared de por medio en línea recta,
        # ahora que las paredes/sprites son 3D de verdad.
        enemy.player_visible = _has_line_of_sight_3d(_player.position, enemy.position)

    for projectile in _projectiles:
        active[projectile] = true
        var glow := _get_or_create_glow_node(projectile, projectile.color, 0.5)
        glow.position = Vector3(projectile.position.x, EYE_HEIGHT_3D, projectile.position.y)

    for item in _items:
        active[item] = true
        var item_node := _get_or_create_item_node(item)
        item_node.position = Vector3(item.position.x, 0.35, item.position.y)

    if _multiplayer_active:
        for remote: RemotePlayerState in _remote_players.values():
            active[remote] = true
            var remote_root := _get_or_create_remote_player_node(remote)
            remote_root.position = Vector3(remote.position.x, EYE_HEIGHT_3D, remote.position.y)
            var label := remote_root.get_node_or_null("Label") as Label3D
            if label != null:
                label.text = "%s (%d/%d)" % [remote.player_name, ceili(remote.health), ceili(remote.max_health)]

    var stale: Array = []
    for key in _sprite_nodes_3d.keys():
        if not active.has(key):
            stale.append(key)
    for key in stale:
        _sprite_nodes_3d[key].queue_free()
        _sprite_nodes_3d.erase(key)


func _has_line_of_sight_3d(from: Vector2, to: Vector2) -> bool:
    var map: Array = GameData.levels[_level_index].map
    var delta := to - from
    var distance := delta.length()
    if distance < 0.001:
        return true
    var step := delta / distance * 0.25
    var point := from
    var steps := ceili(distance / 0.25)
    for i in range(1, steps):
        point += step
        if _is_wall(point, map):
            return false
    return true


func _get_or_create_enemy_node(enemy: Enemy, body_height: float) -> Node3D:
    if _sprite_nodes_3d.has(enemy):
        return _sprite_nodes_3d[enemy]

    var root := Node3D.new()
    var texture := _enemy_texture(enemy.type)
    if texture != null:
        var body := Sprite3D.new()
        body.name = "Body"
        body.texture = texture
        body.hframes = 5
        body.vframes = 1
        body.pixel_size = body_height / texture.get_height()
        body.billboard = BaseMaterial3D.BILLBOARD_FIXED_Y
        body.alpha_cut = SpriteBase3D.ALPHA_CUT_DISCARD
        body.texture_filter = BaseMaterial3D.TEXTURE_FILTER_NEAREST
        root.add_child(body)
    _add_health_bar_children(root)
    _sprites_layer_3d.add_child(root)
    _sprite_nodes_3d[enemy] = root
    return root


func _update_enemy_node(root: Node3D, enemy: Enemy, body_height: float) -> void:
    var body := root.get_node_or_null("Body") as Sprite3D
    if body != null:
        body.frame = enemy.animation_frame
        body.modulate = Color(1.0, 0.55, 0.55) if enemy.hit_flash > 0.0 else Color.WHITE
    _update_health_bar_children(root, body_height * 0.5 + 0.18, enemy.hp / enemy.max_hp if enemy.max_hp > 0.0 else 0.0)


## Barra de vida flotante: dos Sprite3D con una textura sólida de 4x4,
## estirados con scale a modo de rectángulo (fondo oscuro + relleno rojo).
func _add_health_bar_children(root: Node3D) -> void:
    var bg := Sprite3D.new()
    bg.name = "HealthBg"
    bg.texture = _solid_texture_3d
    bg.pixel_size = 0.25
    bg.billboard = BaseMaterial3D.BILLBOARD_FIXED_Y
    bg.modulate = Color(0.0, 0.0, 0.0, 0.6)
    bg.shaded = false
    root.add_child(bg)

    var fill := Sprite3D.new()
    fill.name = "HealthFill"
    fill.texture = _solid_texture_3d
    fill.pixel_size = 0.25
    fill.billboard = BaseMaterial3D.BILLBOARD_FIXED_Y
    fill.modulate = Color.html("ef5350")
    fill.shaded = false
    root.add_child(fill)


static func _update_health_bar_children(root: Node3D, bar_y: float, health_t: float, bar_width: float = 0.7, bar_height: float = 0.08) -> void:
    health_t = clampf(health_t, 0.0, 1.0)
    var bg := root.get_node_or_null("HealthBg") as Sprite3D
    if bg != null:
        bg.scale = Vector3(bar_width, bar_height, 1.0)
        bg.position = Vector3(0.0, bar_y, 0.0)
    var fill := root.get_node_or_null("HealthFill") as Sprite3D
    if fill != null:
        var filled_width := maxf(0.001, bar_width * health_t)
        fill.scale = Vector3(filled_width, bar_height, 1.0)
        fill.position = Vector3(0.0, bar_y, 0.001)
        # El relleno se ancla al borde izquierdo del fondo con offset (espacio
        # local del sprite, que SÍ rota con el Billboard) en vez de position
        # (espacio del padre, que no rota). Con position solo quedaba alineado
        # desde el ángulo con el que se probó y se descolocaba al rodear al
        # enemigo. offset va en píxeles, así que se compensa por pixel_size y
        # por el propio scale (que también lo escala).
        var world_shift := -(bar_width - filled_width) * 0.5
        fill.offset = Vector2(world_shift / (fill.pixel_size * filled_width), 0.0)


func _get_or_create_item_node(item: WorldItem) -> Node3D:
    if _sprite_nodes_3d.has(item):
        return _sprite_nodes_3d[item]

    var texture: Texture2D = null
    match item.type:
        ItemType.POTION_RED:
            texture = _red_potion
        ItemType.POTION_BLUE:
            texture = _blue_potion
    var sprite := Sprite3D.new()
    sprite.billboard = BaseMaterial3D.BILLBOARD_FIXED_Y
    sprite.alpha_cut = SpriteBase3D.ALPHA_CUT_DISCARD
    sprite.texture_filter = BaseMaterial3D.TEXTURE_FILTER_NEAREST
    if texture != null:
        sprite.texture = texture
        sprite.pixel_size = 0.55 / texture.get_height()
    else:
        # Sin textura propia (p.ej. pergaminos de misión): círculo de
        # resplandor teñido con el color que ya trae el objeto.
        sprite.texture = _glow_texture_3d
        sprite.modulate = item.color
        sprite.pixel_size = 0.4 / 64.0
        sprite.alpha_cut = SpriteBase3D.ALPHA_CUT_DISABLED
    _sprites_layer_3d.add_child(sprite)
    _sprite_nodes_3d[item] = sprite
    return sprite


func _get_or_create_glow_node(source: Object, color: Color, world_size: float) -> Sprite3D:
    if _sprite_nodes_3d.has(source):
        return _sprite_nodes_3d[source]
    var sprite := Sprite3D.new()
    sprite.texture = _glow_texture_3d
    sprite.billboard = BaseMaterial3D.BILLBOARD_ENABLED
    sprite.modulate = color
    sprite.pixel_size = world_size / 64.0
    sprite.shaded = false
    _sprites_layer_3d.add_child(sprite)
    _sprite_nodes_3d[source] = sprite
    return sprite


func _get_or_create_remote_player_node(remote: RemotePlayerState) -> Node3D:
    if _sprite_nodes_3d.has(remote):
        return _sprite_nodes_3d[remote]
    var root := Node3D.new()
    var body := Sprite3D.new()
    body.name = "Body"
    body.texture = _glow_texture_3d
    body.billboard = BaseMaterial3D.BILLBOARD_ENABLED
    body.modulate = Color.html("29b6f6")
    body.pixel_size = 0.9 / 64.0
    body.shaded = false
    root.add_child(body)
    var label := Label3D.new()
    label.name = "Label"
    label.billboard = BaseMaterial3D.BILLBOARD_ENABLED
    label.font_size = 32
    label.pixel_size = 0.012
    label.position = Vector3(0.0, 0.6, 0.0)
    label.modulate = Color.WHITE
    root.add_child(label)
    _sprites_layer_3d.add_child(root)
    _sprite_nodes_3d[remote] = root
    return root


func _process(delta: float) -> void:
    if _phase == GamePhase.PLAYING:
        _update_game(minf(delta, 0.05))

    queue_redraw()


func _input(event: InputEvent) -> void:
    var key := event as InputEventKey
    if key != null and key.pressed and not key.echo:
        if _phase == GamePhase.PLAYING and key.keycode == KEY_ENTER:
            _toggle_debug_overlay()
            return

        if _phase == GamePhase.PLAYING:
            if key.keycode == KEY_ESCAPE or key.keycode == KEY_P:
                _set_phase(GamePhase.PAUSED)
            if key.keycode == KEY_E:
                _activate_shield()
        else:
            if key.keycode == KEY_UP or key.keycode == KEY_W:
                _move_menu(-1)
            if key.keycode == KEY_DOWN or key.keycode == KEY_S:
                _move_menu(1)
            if key.keycode == KEY_ENTER or key.keycode == KEY_SPACE:
                _confirm_menu()
            if key.keycode == KEY_ESCAPE or key.keycode == KEY_BACKSPACE:
                _cancel_menu()
            if key.keycode == KEY_LEFT or key.keycode == KEY_A:
                if _phase == GamePhase.SETTINGS:
                    _adjust_focused_setting(-1)
            if key.keycode == KEY_RIGHT or key.keycode == KEY_D:
                if _phase == GamePhase.SETTINGS:
                    _adjust_focused_setting(1)

    var joy_button := event as InputEventJoypadButton
    if joy_button != null and joy_button.pressed:
        if _phase == GamePhase.PLAYING and joy_button.button_index == JOY_BUTTON_Y:
            _toggle_debug_overlay()
            return

        if _phase == GamePhase.PLAYING:
            if joy_button.button_index == JOY_BUTTON_START or joy_button.button_index == JOY_BUTTON_BACK:
                _set_phase(GamePhase.PAUSED)
            if joy_button.button_index == JOY_BUTTON_LEFT_SHOULDER:
                _activate_shield()
        else:
            if joy_button.button_index == JOY_BUTTON_DPAD_UP:
                _move_menu(-1)
            if joy_button.button_index == JOY_BUTTON_DPAD_DOWN:
                _move_menu(1)
            if joy_button.button_index == JOY_BUTTON_DPAD_LEFT:
                if _phase == GamePhase.SETTINGS:
                    _adjust_focused_setting(-1)
            if joy_button.button_index == JOY_BUTTON_DPAD_RIGHT:
                if _phase == GamePhase.SETTINGS:
                    _adjust_focused_setting(1)
            if joy_button.button_index == JOY_BUTTON_A or joy_button.button_index == JOY_BUTTON_X:
                _confirm_menu()
            if joy_button.button_index == JOY_BUTTON_B or joy_button.button_index == JOY_BUTTON_Y or joy_button.button_index == JOY_BUTTON_BACK:
                _cancel_menu()

    var mouse_button := event as InputEventMouseButton
    if mouse_button != null and mouse_button.button_index == MOUSE_BUTTON_LEFT:
        if _phase == GamePhase.PLAYING:
            _mouse_shooting = mouse_button.pressed
            if mouse_button.pressed:
                _handle_playing_press(mouse_button.position, -2)
        elif _phase == GamePhase.SHOP:
            if mouse_button.pressed:
                _handle_shop_pointer_down(mouse_button.position, -2)
            else:
                _handle_shop_pointer_up(mouse_button.position)
        elif mouse_button.pressed:
            _handle_ui_press(mouse_button.position)
        else:
            _dragging_slider = UiAction.NONE

    var wheel := event as InputEventMouseButton
    if _phase == GamePhase.SHOP and wheel != null and wheel.pressed and (wheel.button_index == MOUSE_BUTTON_WHEEL_UP or wheel.button_index == MOUSE_BUTTON_WHEEL_DOWN):
        _shop_scroll += -60.0 if wheel.button_index == MOUSE_BUTTON_WHEEL_UP else 60.0

    var mouse_motion := event as InputEventMouseMotion
    if mouse_motion != null:
        if _phase == GamePhase.PLAYING:
            _player.angle += mouse_motion.relative.x * 0.005 * _look_sensitivity
        elif _phase == GamePhase.SHOP and Input.is_mouse_button_pressed(MOUSE_BUTTON_LEFT):
            _handle_shop_pointer_move(mouse_motion.position)
        elif _dragging_slider != UiAction.NONE and Input.is_mouse_button_pressed(MOUSE_BUTTON_LEFT):
            var hit := _find_ui_hit(_dragging_slider)
            if hit != null:
                _apply_slider_value(_dragging_slider, hit.rect, mouse_motion.position.x)

    var screen_touch := event as InputEventScreenTouch
    if screen_touch != null:
        if _phase == GamePhase.SHOP:
            if screen_touch.pressed:
                _handle_shop_pointer_down(screen_touch.position, screen_touch.index)
            elif screen_touch.index == _shop_pointer_touch:
                _handle_shop_pointer_up(screen_touch.position)
        elif _phase != GamePhase.PLAYING:
            if screen_touch.pressed:
                _handle_ui_press(screen_touch.position)
                _dragging_slider_touch_index = screen_touch.index if _dragging_slider != UiAction.NONE else -1
            elif screen_touch.index == _dragging_slider_touch_index:
                _dragging_slider = UiAction.NONE
                _dragging_slider_touch_index = -1
        elif screen_touch.pressed:
            _handle_playing_press(screen_touch.position, screen_touch.index)
        else:
            if screen_touch.index == _joystick_touch:
                _joystick_touch = -1
                _touch_move_active = false
                _touch_move = Vector2.ZERO
            if screen_touch.index == _look_touch:
                _look_touch = -1
            _touch_shooting = false

    var screen_drag := event as InputEventScreenDrag
    if screen_drag != null:
        if _phase == GamePhase.PLAYING:
            if screen_drag.index == _joystick_touch:
                _update_touch_joystick(screen_drag.position)
            elif screen_drag.index == _look_touch:
                _player.angle += (screen_drag.position.x - _last_look_position.x) * 0.005 * _look_sensitivity
                _last_look_position = screen_drag.position
        elif _phase == GamePhase.SHOP and screen_drag.index == _shop_pointer_touch:
            _handle_shop_pointer_move(screen_drag.position)
        elif screen_drag.index == _dragging_slider_touch_index and _dragging_slider != UiAction.NONE:
            var slider_hit := _find_ui_hit(_dragging_slider)
            if slider_hit != null:
                _apply_slider_value(_dragging_slider, slider_hit.rect, screen_drag.position.x)


func _draw() -> void:
    _ui_hits.clear()
    var size := get_viewport_rect().size

    if _phase == GamePhase.PLAYING:
        _draw_game(size)
        _draw_hud(size)
        _draw_crosshair(size)
        if _player.shield_time_remaining > 0.0:
            var pulse := 0.06 + 0.03 * sin(_frame_tick * 0.15)
            draw_rect(Rect2(Vector2.ZERO, size), Color(0.25, 0.7, 1.0, pulse))
            draw_rect(Rect2(Vector2.ZERO, size), Color(0.6, 0.9, 1.0, 0.5), false, 6.0)
        if not _has_gamepad() and DisplayServer.is_touchscreen_available():
            _draw_touch_controls(size)
        if _debug_overlay_enabled:
            _draw_debug_overlay(size)
        return

    _draw_menu_background(size)
    match _phase:
        GamePhase.MAIN_MENU: _draw_main_menu(size)
        GamePhase.SETTINGS: _draw_settings(size)
        GamePhase.LEVEL_CLEAR: _draw_level_clear(size)
        GamePhase.SHOP: _draw_shop(size)
        GamePhase.GAME_OVER: _draw_game_over(size)
        GamePhase.PAUSED: _draw_pause(size)
        GamePhase.FINISHED: _draw_victory(size)
        GamePhase.MULTIPLAYER_MENU: _draw_multiplayer_menu(size)


# En una partida multijugador, solo el anfitrión simula a los enemigos (IA,
# movimiento, ataques) y difunde su estado; los clientes solo lo reciben y
# lo dibujan. En un solo jugador esto siempre es true (nada cambia).
func _is_host_authority() -> bool:
    return not _multiplayer_active or (multiplayer.has_multiplayer_peer() and multiplayer.is_server())


func _update_game(dt: float) -> void:
    var level: GameData.LevelDef = GameData.levels[_level_index]
    var map: Array = level.map
    _frame_tick += 1.0

    if _multiplayer_active:
        _network_send_timer -= dt
        if _network_send_timer <= 0.0:
            _network_send_timer = 0.1
            _broadcast_local_player_state()
            if _is_host_authority():
                _broadcast_enemy_snapshot()

    for i in range(_particles.size() - 1, -1, -1):
        var particle := _particles[i]
        particle.position += particle.velocity * dt
        particle.velocity *= clampf(1.0 - 2.0 * dt, 0.0, 1.0)
        particle.life -= dt
        if particle.life <= 0.0:
            _particles.remove_at(i)

    if _player.screen_shake > 0.0:
        _player.screen_shake -= dt * 5.0

    if _player.shield_time_remaining > 0.0:
        _player.shield_time_remaining = maxf(0.0, _player.shield_time_remaining - dt)
    elif _player.shield_cooldown_remaining > 0.0:
        _player.shield_cooldown_remaining = maxf(0.0, _player.shield_cooldown_remaining - dt)

    var left_stick := _apply_deadzone(Vector2(Input.get_joy_axis(0, JOY_AXIS_LEFT_X), Input.get_joy_axis(0, JOY_AXIS_LEFT_Y)))
    var right_stick := _apply_deadzone(Vector2(Input.get_joy_axis(0, JOY_AXIS_RIGHT_X), Input.get_joy_axis(0, JOY_AXIS_RIGHT_Y)))
    var keyboard_forward := (1.0 if Input.is_key_pressed(KEY_W) else 0.0) - (1.0 if Input.is_key_pressed(KEY_S) else 0.0)
    var keyboard_strafe := (1.0 if Input.is_key_pressed(KEY_D) else 0.0) - (1.0 if Input.is_key_pressed(KEY_A) else 0.0)
    var forward := clampf(keyboard_forward + _touch_move.y - left_stick.y, -1.0, 1.0)
    var strafe := clampf(keyboard_strafe + _touch_move.x + left_stick.x, -1.0, 1.0)
    _player.angle += right_stick.x * 3.0 * dt * _look_sensitivity

    var direction := Vector2(cos(_player.angle), sin(_player.angle))
    var side := Vector2(-direction.y, direction.x)
    var movement := (direction * forward + side * strafe) * 2.6 * dt
    _player.position = _move_with_walls(_player.position, movement, PLAYER_WALL_COLLISION_RADIUS, map)

    for i in range(_items.size() - 1, -1, -1):
        var item := _items[i]
        if _player.position.distance_squared_to(item.position) >= ITEM_PICKUP_RADIUS * ITEM_PICKUP_RADIUS:
            continue

        # Conserva el comportamiento original: cualquier objeto suma al contador de recogidos.
        _player.items_collected += 1
        if item.type == ItemType.POTION_RED:
            _player.health = minf(_player.max_health, _player.health + 40.0)
        if item.type == ItemType.POTION_BLUE:
            _player.mana = minf(_player.max_mana, _player.mana + 40.0)
        _spawn_explosion(item.position, item.color, 15)
        _items.remove_at(i)

    if level.mission == MissionType.ESCAPE and map[floori(_player.position.y)][floori(_player.position.x)] == 5:
        _set_phase(GamePhase.LEVEL_CLEAR)
        return

    _player.shoot_cooldown -= dt
    var shooting := _touch_shooting or _mouse_shooting or Input.is_key_pressed(KEY_SPACE) \
        or Input.get_joy_axis(0, JOY_AXIS_TRIGGER_RIGHT) > 0.1 or Input.is_joy_button_pressed(0, JOY_BUTTON_RIGHT_SHOULDER)
    if shooting and _player.shoot_cooldown <= 0.0 and _player.mana >= _player.weapon_mana_cost:
        _player.mana -= _player.weapon_mana_cost
        _player.shoot_cooldown = _player.weapon_cooldown
        _projectiles.append(Projectile.new(_player.position, _player.angle, 7.0, _player.weapon_damage, ProjectileOwner.PLAYER, Color.YELLOW))
        _play_sfx("snd_shoot")

    var mana_multiplier := 0.7 if _difficulty == Difficulty.HARD else 1.0
    _player.mana = minf(_player.max_mana, _player.mana + 12.0 * dt * mana_multiplier)
    _update_projectiles(dt, map)
    if _is_host_authority():
        _update_enemies(dt, map)
    _update_3d_sprites()

    if _player.health <= 0.0:
        _player.health = 0.0
        _set_phase(GamePhase.GAME_OVER)
        return

    var mission_cleared := false
    match level.mission:
        MissionType.ELIMINATE:
            mission_cleared = true
            for enemy in _enemies:
                if enemy.alive:
                    mission_cleared = false
                    break
        MissionType.COLLECT:
            mission_cleared = _player.items_collected >= level.target_count
    if mission_cleared:
        _set_phase(GamePhase.LEVEL_CLEAR)


func _update_projectiles(dt: float, map: Array) -> void:
    for p in range(_projectiles.size() - 1, -1, -1):
        var projectile := _projectiles[p]
        projectile.position += Vector2(cos(projectile.angle), sin(projectile.angle)) * projectile.speed * dt
        var dead := false

        if _is_wall(projectile.position, map):
            dead = true
            _spawn_explosion(projectile.position, projectile.color, 5)
        elif projectile.owner_kind == ProjectileOwner.PLAYER:
            for enemy in _enemies:
                if not enemy.alive:
                    continue
                var hit_radius := _enemy_collision_radius(enemy)
                if enemy.position.distance_squared_to(projectile.position) >= hit_radius * hit_radius:
                    continue
                enemy.hp -= projectile.damage
                enemy.hit_flash = 0.15
                dead = true
                _spawn_explosion(projectile.position, projectile.color, 8)
                _play_sfx(_enemy_sound(enemy.type))
                if _multiplayer_active and not _is_host_authority():
                    rpc_request_enemy_hit.rpc_id(1, _enemies.find(enemy), projectile.damage)
                if enemy.hp <= 0.0 and not enemy.rewarded:
                    enemy.rewarded = true
                    _grant_reward(enemy)
                    _spawn_explosion(enemy.position, _enemy_color(enemy.type), 20)
                break
        elif _player.position.distance_squared_to(projectile.position) < ENEMY_PROJECTILE_PLAYER_HIT_RADIUS * ENEMY_PROJECTILE_PLAYER_HIT_RADIUS:
            _damage_player(projectile.damage * (1.0 - _player.armor_defense), 0.3)
            dead = true
            _spawn_explosion(projectile.position, projectile.color, 8)

        if dead:
            _projectiles.remove_at(p)


func _update_enemies(dt: float, map: Array) -> void:
    var damage_multiplier := 1.0
    var cooldown_multiplier := 1.0
    match _difficulty:
        Difficulty.EASY:
            damage_multiplier = 0.5
            cooldown_multiplier = 1.6
        Difficulty.HARD:
            damage_multiplier = 4.0
            cooldown_multiplier = 0.75
    var melee_damage := (5.0 + _level_index * 2.0) * damage_multiplier
    var ranged_damage := (10.0 + _level_index * 3.0) * damage_multiplier

    for enemy in _enemies:
        if not enemy.alive:
            continue
        enemy.hit_flash -= dt
        enemy.attack_cooldown -= dt
        enemy.animation_timer += dt
        if enemy.animation_timer >= 0.12:
            enemy.animation_timer = 0.0
            enemy.animation_frame = (enemy.animation_frame + 1) % 5

        var to_player := _player.position - enemy.position
        var distance_squared := to_player.length_squared()
        if distance_squared <= 0.00001:
            continue
        var distance := sqrt(distance_squared)
        var direction := to_player / distance
        # El enemigo "ve" al jugador si hay línea recta despejada entre los dos
        # (comprobado en _update_3d_sprites vía _has_line_of_sight_3d).
        var can_see := enemy.player_visible
        if not can_see and enemy.type != EnemyType.BOSS and enemy.type != EnemyType.SENTINEL:
            continue

        match enemy.type:
            EnemyType.MELEE, EnemyType.TANK:
                var is_tank := enemy.type == EnemyType.TANK
                var stop_distance := 1.2 if is_tank else 0.9
                if distance > stop_distance:
                    enemy.position = _move_with_walls(enemy.position, direction * enemy.speed * dt, _enemy_collision_radius(enemy), map)
                elif enemy.attack_cooldown <= 0.0:
                    var damage := (melee_damage * 1.5 if is_tank else melee_damage) * (1.0 - _player.armor_defense)
                    _damage_player(damage, 0.4 if is_tank else 0.3)
                    enemy.attack_cooldown = (1.5 if is_tank else 1.1) * cooldown_multiplier

            EnemyType.RANGED:
                if distance > 7.0:
                    enemy.position = _move_with_walls(enemy.position, direction * enemy.speed * 0.6 * dt, _enemy_collision_radius(enemy), map)
                if distance < 10.0 and enemy.attack_cooldown <= 0.0:
                    _projectiles.append(Projectile.new(enemy.position, atan2(to_player.y, to_player.x), 5.0, ranged_damage, ProjectileOwner.ENEMY, Color.GREEN))
                    enemy.attack_cooldown = 1.6 * cooldown_multiplier

            EnemyType.BOSS:
                var boss_direction := direction + Vector2(sin(_frame_tick * 0.05), cos(_frame_tick * 0.05)) * 0.5
                if distance > 4.0:
                    enemy.position = _move_with_walls(enemy.position, boss_direction * enemy.speed * dt, _enemy_collision_radius(enemy), map)
                if enemy.attack_cooldown <= 0.0:
                    var boss_angle := atan2(to_player.y, to_player.x)
                    for i in range(-1, 2):
                        _projectiles.append(Projectile.new(enemy.position, boss_angle + i * 0.2, 5.0, ranged_damage * 1.2, ProjectileOwner.ENEMY, Color.RED))
                    enemy.attack_cooldown = 1.5 * cooldown_multiplier

            EnemyType.SENTINEL:
                enemy.position = _move_with_walls(enemy.position, direction * enemy.speed * dt, _enemy_collision_radius(enemy), map)
                if enemy.attack_cooldown <= 0.0:
                    var sentinel_angle := atan2(to_player.y, to_player.x)
                    for i in 8:
                        _projectiles.append(Projectile.new(enemy.position, sentinel_angle + i * PI / 4.0, 6.0, ranged_damage * 1.5, ProjectileOwner.ENEMY, Color.html("ff5722")))
                    enemy.attack_cooldown = 2.0 * cooldown_multiplier


func _load_level(index: int) -> void:
    _level_index = index
    _touch_move = Vector2.ZERO
    _touch_move_active = false
    _touch_shooting = false
    _mouse_shooting = false
    var level: GameData.LevelDef = GameData.levels[index]
    _generate_3d_level_geometry(level.map, level.lights)
    for node: Node3D in _sprite_nodes_3d.values():
        node.queue_free()
    _sprite_nodes_3d.clear()
    _player.position = level.start
    _player.angle = 0.0
    _player.max_health = 200.0 if _difficulty == Difficulty.EASY else 120.0
    _player.health = _player.max_health
    _player.mana = _player.max_mana
    _player.items_collected = 0
    _enemies.clear()
    _projectiles.clear()
    _particles.clear()
    _items.clear()

    var hp_multiplier := 0.8
    var speed_multiplier := 1.0
    match _difficulty:
        Difficulty.EASY:
            hp_multiplier = 0.5
            speed_multiplier = 0.8
        Difficulty.HARD:
            hp_multiplier = 3.5
            speed_multiplier = 1.45
    for spawn: GameData.EnemySpawn in level.spawns:
        _enemies.append(Enemy.new(spawn.position, spawn.hp * hp_multiplier, spawn.type, spawn.speed * speed_multiplier, spawn.exp_reward, spawn.gold_min, spawn.gold_max))

    if index >= 3:
        for i in 2:
            _items.append(WorldItem.new(_random_open_cell(level.map), ItemType.POTION_RED, Color.RED))
            _items.append(WorldItem.new(_random_open_cell(level.map), ItemType.POTION_BLUE, Color.BLUE))
    if level.mission == MissionType.COLLECT:
        for i in level.target_count:
            _items.append(WorldItem.new(_random_open_cell(level.map), ItemType.SCROLL, Color.MAGENTA))

    _set_phase(GamePhase.PLAYING)


func _random_open_cell(map: Array) -> Vector2:
    for attempt in 128:
        var point := Vector2(1.0 + randf() * 14.0, 1.0 + randf() * 14.0)
        if not _is_wall(point, map):
            return point
    return _player.position


func _grant_reward(enemy: Enemy) -> void:
    _player.experience += enemy.exp_reward
    _player.gold += randi_range(enemy.gold_min, enemy.gold_max)
    while _player.experience >= _player.exp_to_next:
        _player.experience -= _player.exp_to_next
        _player.level += 1
        _player.max_health += 20.0
        _player.max_mana += 10.0
        _player.exp_to_next *= 1.4
        _player.health = _player.max_health
        _player.mana = _player.max_mana


## Activa el Escudo Arcano si se posee, no está ya activo y no está en recarga.
## 7.5s de inmunidad total al daño, seguidos de una recarga antes de poder reactivarlo.
func _activate_shield() -> void:
    if _phase != GamePhase.PLAYING or not _player.owns_shield:
        return
    if _player.shield_time_remaining > 0.0 or _player.shield_cooldown_remaining > 0.0:
        return
    _player.shield_time_remaining = 7.5
    _player.shield_cooldown_remaining = 15.0
    _play_sfx("snd_shoot")


func _damage_player(damage: float, shake: float) -> void:
    if _player.shield_time_remaining > 0.0:
        return
    _player.health -= damage
    _player.screen_shake = shake
    _play_sfx("snd_player_hit")


func _is_wall(pos: Vector2, map: Array) -> bool:
    var x := floori(pos.x)
    var y := floori(pos.y)
    return y < 0 or y >= map.size() or x < 0 or x >= map[0].size() or map[y][x] != 0


## Devuelve la posición resultante de mover `pos` por `movement` deslizando contra las paredes
## (en C# esto era un `ref Vector2`; en GDScript los Vector2 se pasan por valor).
func _move_with_walls(pos: Vector2, movement: Vector2, radius: float, map: Array) -> Vector2:
    var x_candidate := Vector2(pos.x + movement.x, pos.y)
    if not _circle_touches_wall(x_candidate, radius, map):
        pos.x = x_candidate.x
    var y_candidate := Vector2(pos.x, pos.y + movement.y)
    if not _circle_touches_wall(y_candidate, radius, map):
        pos.y = y_candidate.y
    return pos


## Comprueba el círculo completo contra todas las celdas sólidas cercanas. Las
## pruebas anteriores solo tanteaban un punto en X/Y, por lo que un sprite ancho
## podía introducir brazos o bordes en una esquina de pared.
static func _circle_touches_wall(center: Vector2, radius: float, map: Array) -> bool:
    var min_x := floori(center.x - radius)
    var max_x := floori(center.x + radius)
    var min_y := floori(center.y - radius)
    var max_y := floori(center.y + radius)
    var radius_squared := radius * radius

    for y in range(min_y, max_y + 1):
        for x in range(min_x, max_x + 1):
            if y >= 0 and y < map.size() and x >= 0 and x < map[0].size() and map[y][x] == 0:
                continue
            var nearest_x := clampf(center.x, x, x + 1.0)
            var nearest_y := clampf(center.y, y, y + 1.0)
            var offset_x := center.x - nearest_x
            var offset_y := center.y - nearest_y
            if offset_x * offset_x + offset_y * offset_y < radius_squared:
                return true

    return false


func _spawn_explosion(pos: Vector2, color: Color, count: int) -> void:
    for i in count:
        var angle := randf() * TAU
        var speed := randf() * 3.0 + 1.0
        var life := 0.4 + randf() * 0.4
        _particles.append(Particle.new(pos, Vector2(cos(angle), sin(angle)) * speed, life, life, color))


func _draw_game(size: Vector2) -> void:
    _update_3d_camera(size)
    if _viewport_3d != null:
        draw_texture_rect(_viewport_3d.get_texture(), Rect2(Vector2.ZERO, size), false)

    var screen_distance := (size.x * 0.5) / tan(FIELD_OF_VIEW * 0.5)
    var direction := Vector2(cos(_player.angle), sin(_player.angle))
    var plane := Vector2(-sin(_player.angle), cos(_player.angle)) * tan(FIELD_OF_VIEW * 0.5)
    var shake := Vector2.ZERO
    if _player.screen_shake > 0.0:
        shake = Vector2((randf() - 0.5) * _player.screen_shake * 100.0, (randf() - 0.5) * _player.screen_shake * 100.0)

    _draw_weapon(size, shake)

    # Las paredes y los sprites los pinta la escena 3D de arriba; en 2D solo quedan las
    # partículas, que necesitan saber si una pared las tapa (ver _draw_particle).
    if not _particles.is_empty():
        var map: Array = GameData.levels[_level_index].map
        for particle in _particles:
            _draw_particle(particle, size, screen_distance, direction, plane, shake, map)

    # Viñeta suave para conservar el contraste oscuro del original.
    var edge := size.x * 0.025
    draw_rect(Rect2(0, 0, edge, size.y), Color(0, 0, 0, 0.32))
    draw_rect(Rect2(size.x - edge, 0, edge, size.y), Color(0, 0, 0, 0.32))


## Distancia perpendicular hasta la primera pared que cruza un rayo (DDA sobre la rejilla), igual
## que el raycaster original: el rayo "direction + plane * camera_x" avanza 1 unidad por unidad de
## profundidad, así que su parámetro ya es la distancia perpendicular a la cámara.
static func _cast_wall_distance(origin: Vector2, ray_dir: Vector2, map: Array) -> float:
    var map_rows := map.size()
    var map_cols: int = map[0].size()
    var map_x := floori(origin.x)
    var map_y := floori(origin.y)
    var delta_x := 3.4e38 if absf(ray_dir.x) < 0.00001 else absf(1.0 / ray_dir.x)
    var delta_y := 3.4e38 if absf(ray_dir.y) < 0.00001 else absf(1.0 / ray_dir.y)
    var step_x := -1 if ray_dir.x < 0.0 else 1
    var step_y := -1 if ray_dir.y < 0.0 else 1
    var side_x := (origin.x - map_x) * delta_x if ray_dir.x < 0.0 else (map_x + 1.0 - origin.x) * delta_x
    var side_y := (origin.y - map_y) * delta_y if ray_dir.y < 0.0 else (map_y + 1.0 - origin.y) * delta_y
    var hit_side := 0

    for step in 40:
        if side_x < side_y:
            side_x += delta_x
            map_x += step_x
            hit_side = 0
        else:
            side_y += delta_y
            map_y += step_y
            hit_side = 1
        if map_y < 0 or map_y >= map_rows or map_x < 0 or map_x >= map_cols or map[map_y][map_x] > 0:
            break

    return maxf(0.001, side_x - delta_x if hit_side == 0 else side_y - delta_y)


func _draw_particle(particle: Particle, size: Vector2, screen_distance: float, direction: Vector2, plane: Vector2, shake: Vector2, map: Array) -> void:
    var delta := particle.position - _player.position
    var inverse_determinant := 1.0 / (plane.x * direction.y - direction.x * plane.y)
    var transform_y := inverse_determinant * (-plane.y * delta.x + plane.x * delta.y)
    if transform_y <= 0.1:
        return
    var transform_x := inverse_determinant * (direction.y * delta.x - direction.x * delta.y)
    var screen_x := size.x * 0.5 * (1.0 + transform_x / transform_y)
    # En vez de rellenar un z-buffer con un rayo por columna de pantalla en cada fotograma (lo que
    # en GDScript costaría varios milisegundos), se lanza un único rayo por la columna de cada
    # partícula: mismo resultado visual con una fracción del trabajo.
    var camera_x := clampf(transform_x / transform_y, -1.0, 1.0)
    var wall_distance := _cast_wall_distance(_player.position, direction + plane * camera_x, map)
    if transform_y >= wall_distance + 0.1:
        return
    var life := clampf(particle.life / particle.max_life, 0.0, 1.0)
    var radius := absf(screen_distance / transform_y) * 0.06 * life + 1.0
    _draw_glow(particle.color, Vector2(screen_x + shake.x, size.y * 0.5 + shake.y), radius * 2.0, life)


func _draw_weapon(size: Vector2, shake: Vector2) -> void:
    if _staff == null:
        return
    var height := size.y * 0.85
    var width := height * _staff.get_width() / _staff.get_height()
    var bob_x := sin(_frame_tick * 0.05) * 20.0
    var bob_y := absf(cos(_frame_tick * 0.05)) * 15.0
    draw_texture_rect(_staff, Rect2(size.x * 0.85 - width * 0.5 + bob_x + shake.x, size.y - height + bob_y + shake.y, width, height), false)


func _draw_glow(color: Color, center: Vector2, diameter: float, alpha: float = 1.0) -> void:
    var radius := maxf(1.0, diameter * 0.5)
    draw_circle(center, radius, _alpha(color, 0.15 * alpha))
    draw_circle(center, radius * 0.6, _alpha(color, 0.35 * alpha))
    draw_circle(center, radius * 0.35, _alpha(color, alpha))


func _draw_hud(size: Vector2) -> void:
    var level: GameData.LevelDef = GameData.levels[_level_index]
    draw_rect(Rect2(14, 14, 330, 54), Color(0.0, 0.0, 0.0, 0.48))
    _draw_text(level.name, Vector2(26, 36), 16, Color.WHITE)
    _draw_text("Oro: %d   Nv.%d" % [_player.gold, _player.level], Vector2(26, 58), 15, Color.GOLD)

    var bar_x := 356.0
    _draw_hud_bar("EXP", _player.experience / _player.exp_to_next, Color.GOLD, Vector2(bar_x, 24))
    _draw_hud_bar("VIDA", _player.health / _player.max_health, Color.html("ef5350"), Vector2(bar_x, 46))
    _draw_hud_bar("MANA", _player.mana / _player.max_mana, Color.html("42a5f5"), Vector2(bar_x, 68))

    if _player.owns_shield:
        var shield_text: String
        var shield_color: Color
        if _player.shield_time_remaining > 0.0:
            shield_text = "ESCUDO ACTIVO (%.1fs)" % _player.shield_time_remaining
            shield_color = Color.html("64c8ff")
        elif _player.shield_cooldown_remaining > 0.0:
            shield_text = "Escudo en recarga (%.0fs)" % _player.shield_cooldown_remaining
            shield_color = Color(1.0, 1.0, 1.0, 0.5)
        else:
            shield_text = "Escudo listo (E)"
            shield_color = Color.html("bfe6ff")
        _draw_text(shield_text, Vector2(bar_x, 90), 14, shield_color)

    _draw_mini_map(Rect2(16, 82, 128, 128))
    if level.mission == MissionType.COLLECT:
        _draw_text("Pergaminos: %d/%d" % [_player.items_collected, level.target_count], Vector2(16, 228), 14, Color.VIOLET)


func _draw_hud_bar(label: String, value: float, color: Color, pos: Vector2) -> void:
    const WIDTH := 190.0
    _draw_text(label, pos + Vector2(0, 13), 13, Color.WHITE)
    var frame := Rect2(pos.x + 44.0, pos.y, WIDTH, 16.0)
    draw_rect(frame, Color(0.0, 0.0, 0.0, 0.62))
    draw_rect(Rect2(frame.position + Vector2(2, 2), Vector2((WIDTH - 4.0) * clampf(value, 0.0, 1.0), 12.0)), color)


func _draw_mini_map(rect: Rect2) -> void:
    var map: Array = GameData.levels[_level_index].map
    draw_rect(rect, Color(0.0, 0.0, 0.0, 0.66))
    var cell := Vector2(rect.size.x / map[0].size(), rect.size.y / map.size())
    for y in map.size():
        for x in map[y].size():
            if map[y][x] != 0:
                draw_rect(Rect2(rect.position + Vector2(x * cell.x, y * cell.y), cell), Color.GRAY)

    for enemy in _enemies:
        if enemy.alive:
            draw_circle(rect.position + Vector2(enemy.position.x * cell.x, enemy.position.y * cell.y), 2.0, Color.RED)
    for item in _items:
        draw_circle(rect.position + Vector2(item.position.x * cell.x, item.position.y * cell.y), 1.5, item.color)
    if _multiplayer_active:
        for remote: RemotePlayerState in _remote_players.values():
            draw_circle(rect.position + Vector2(remote.position.x * cell.x, remote.position.y * cell.y), 2.4, Color.html("29b6f6"))

    var player_point := rect.position + Vector2(_player.position.x * cell.x, _player.position.y * cell.y)
    var look := Vector2(cos(_player.angle), sin(_player.angle))
    var triangle := PackedVector2Array([
        player_point + look * 6.0,
        player_point + look.rotated(2.4) * 5.0,
        player_point + look.rotated(-2.4) * 5.0,
    ])
    draw_colored_polygon(triangle, Color.CYAN)


## Superposición de depuración: representa el mismo espacio de coordenadas que
## usan las comprobaciones de colisión, para que los radios no sean estimaciones
## visuales de los sprites sino los valores que utiliza realmente la simulación.
func _draw_debug_overlay(size: Vector2) -> void:
    var map: Array = GameData.levels[_level_index].map
    const MARGIN := 14.0
    const PADDING := 8.0
    const HEADER_HEIGHT := 24.0
    const FOOTER_HEIGHT := 18.0
    var panel_size := clampf(minf(size.x * 0.34, size.y * 0.62), 240.0, 420.0)
    var panel := Rect2(size.x - panel_size - MARGIN, MARGIN, panel_size, panel_size)
    draw_rect(panel, Color(0.015, 0.025, 0.05, 0.92))
    draw_rect(panel, Color.html("22d3ee"), false, 2.0)
    _draw_text("DEBUG · ENTER / Y para ocultar", panel.position + Vector2(10.0, 17.0), 13, Color.html("bff7ff"))

    var map_size := minf(panel.size.x - PADDING * 2.0, panel.size.y - HEADER_HEIGHT - FOOTER_HEIGHT - PADDING * 3.0)
    var map_rect := Rect2(panel.position.x + (panel.size.x - map_size) * 0.5, panel.position.y + HEADER_HEIGHT + PADDING, map_size, map_size)
    draw_rect(map_rect, Color(0.02, 0.04, 0.08, 1.0))
    var cell_size := Vector2(map_rect.size.x / map[0].size(), map_rect.size.y / map.size())
    var wall_fill := Color(1.0, 0.34, 0.08, 0.30)
    var wall_outline := Color.html("ff8a3d")

    for y in map.size():
        for x in map[y].size():
            if map[y][x] == 0:
                continue
            var collision_cell := Rect2(map_rect.position + Vector2(x * cell_size.x, y * cell_size.y), cell_size)
            draw_rect(collision_cell, wall_fill)
            draw_rect(collision_cell, wall_outline, false, 1.0)

    var pixels_per_unit := minf(cell_size.x, cell_size.y)
    var movement_color := Color.html("22d3ee")
    var hit_color := Color.html("f472ff")
    var pickup_color := Color.html("86efac")

    for item in _items:
        var point := _debug_map_point(map_rect, cell_size, item.position)
        _draw_debug_radius(point, ITEM_PICKUP_RADIUS * pixels_per_unit, pickup_color)
        draw_circle(point, maxf(2.5, pixels_per_unit * 0.14), item.color)

    for projectile in _projectiles:
        var point := _debug_map_point(map_rect, cell_size, projectile.position)
        var projectile_direction := Vector2(cos(projectile.angle), sin(projectile.angle))
        draw_line(point, point + projectile_direction * pixels_per_unit * 0.75, projectile.color, 1.5)
        draw_circle(point, maxf(2.0, pixels_per_unit * 0.1), projectile.color)

    for remote: RemotePlayerState in _remote_players.values():
        var point := _debug_map_point(map_rect, cell_size, remote.position)
        _draw_debug_radius(point, PLAYER_WALL_COLLISION_RADIUS * pixels_per_unit, Color.html("60a5fa"))
        draw_circle(point, maxf(3.0, pixels_per_unit * 0.16), Color.html("60a5fa"))

    for enemy in _enemies:
        if not enemy.alive:
            continue
        var point := _debug_map_point(map_rect, cell_size, enemy.position)
        var enemy_color := _enemy_color(enemy.type)
        var collision_radius := _enemy_collision_radius(enemy)
        _draw_debug_radius(point, collision_radius * pixels_per_unit, hit_color)
        draw_circle(point, collision_radius * pixels_per_unit, wall_outline, false, 0.75)
        draw_circle(point, maxf(3.0, pixels_per_unit * 0.16), enemy_color)
        _draw_text("%s %.0f h:%.2f" % [_debug_enemy_label(enemy.type), enemy.hp, collision_radius], point + Vector2(4.0, -4.0), 11, enemy_color)

    var player_point := _debug_map_point(map_rect, cell_size, _player.position)
    _draw_debug_radius(player_point, ENEMY_PROJECTILE_PLAYER_HIT_RADIUS * pixels_per_unit, hit_color)
    _draw_debug_radius(player_point, PLAYER_WALL_COLLISION_RADIUS * pixels_per_unit, movement_color)
    var look := Vector2(cos(_player.angle), sin(_player.angle))
    var fov_length := pixels_per_unit * 3.25
    draw_line(player_point, player_point + look.rotated(-FIELD_OF_VIEW * 0.5) * fov_length, movement_color, 1.5)
    draw_line(player_point, player_point + look.rotated(FIELD_OF_VIEW * 0.5) * fov_length, movement_color, 1.5)
    draw_colored_polygon(PackedVector2Array([player_point + look * 5.0, player_point + look.rotated(2.4) * 4.0, player_point + look.rotated(-2.4) * 4.0]), movement_color)

    draw_rect(map_rect, Color.html("c4f1ff"), false, 1.5)
    _draw_text("Cian: jugador · naranja: muro · violeta: impacto · verde: recoger", Vector2(panel.position.x + 10.0, panel.end.y - 6.0), 10, Color(1.0, 1.0, 1.0, 0.82))


func _toggle_debug_overlay() -> void:
    _debug_overlay_enabled = not _debug_overlay_enabled
    queue_redraw()


static func _debug_map_point(map_rect: Rect2, cell_size: Vector2, world_position: Vector2) -> Vector2:
    return map_rect.position + Vector2(world_position.x * cell_size.x, world_position.y * cell_size.y)


func _draw_debug_radius(center: Vector2, radius: float, color: Color) -> void:
    draw_circle(center, radius, _alpha(color, 0.10))
    draw_circle(center, radius, color, false, 1.25)


static func _debug_enemy_label(type: int) -> String:
    match type:
        EnemyType.MELEE: return "M"
        EnemyType.RANGED: return "R"
        EnemyType.TANK: return "T"
        EnemyType.BOSS: return "B"
    return "S"


func _draw_crosshair(size: Vector2) -> void:
    var center := size * 0.5
    var color := Color(1.0, 1.0, 1.0, 0.6)
    draw_line(center + Vector2(-12, 0), center + Vector2(12, 0), color, 2.0)
    draw_line(center + Vector2(0, -12), center + Vector2(0, 12), color, 2.0)
    draw_circle(center, 2.0, color)


func _draw_touch_controls(size: Vector2) -> void:
    var layout := TouchLayout.new(size)
    draw_circle(layout.joystick_center, layout.joystick_radius, Color(1.0, 1.0, 1.0, 0.10))
    var knob_offset := Vector2(_touch_move.x, -_touch_move.y) * (layout.joystick_radius - layout.joystick_knob_radius)
    draw_circle(layout.joystick_center + knob_offset, layout.joystick_knob_radius, Color(0.0, 0.9, 1.0, 0.48))

    draw_circle(layout.shoot_center, layout.shoot_radius, Color(0.49, 0.3, 1.0, 0.42))
    var shoot_font_size := roundi(13 * layout.ui_scale)
    _draw_text("DISPARAR", Vector2(layout.shoot_center.x - 40.0 * layout.ui_scale, layout.shoot_center.y + 5.0 * layout.ui_scale), shoot_font_size, Color.WHITE)

    if _player.owns_shield:
        var shield_fill: Color
        if _player.shield_time_remaining > 0.0:
            shield_fill = Color(0.25, 0.7, 1.0, 0.55)
        elif _player.shield_cooldown_remaining > 0.0:
            shield_fill = Color(0.4, 0.4, 0.4, 0.35)
        else:
            shield_fill = Color(0.25, 0.7, 1.0, 0.30)
        draw_circle(layout.shield_center, layout.shield_radius, shield_fill)
        draw_circle(layout.shield_center, layout.shield_radius, Color(1.0, 1.0, 1.0, 0.5), false, 2.0)
        var shield_font_size := roundi(11 * layout.ui_scale)
        var shield_label: String
        if _player.shield_time_remaining > 0.0:
            shield_label = "%.1fs" % _player.shield_time_remaining
        elif _player.shield_cooldown_remaining > 0.0:
            shield_label = "%.0fs" % _player.shield_cooldown_remaining
        else:
            shield_label = "ESCUDO"
        _draw_text(shield_label, Vector2(layout.shield_center.x - 24.0 * layout.ui_scale, layout.shield_center.y + 4.0 * layout.ui_scale), shield_font_size, Color.WHITE)

    draw_circle(layout.pause_center, layout.pause_radius, Color(0.0, 0.0, 0.0, 0.48))
    var pause_font_size := roundi(18 * layout.ui_scale)
    _draw_text("II", Vector2(layout.pause_center.x - 7.0 * layout.ui_scale, layout.pause_center.y + 7.0 * layout.ui_scale), pause_font_size, Color.WHITE)


func _draw_menu_background(size: Vector2) -> void:
    _draw_vertical_gradient(Rect2(Vector2.ZERO, size), Color.html("0d0714"), Color.html("25112f"))
    for x in 10:
        var alpha := 0.015 + (x % 3) * 0.008
        draw_rect(Rect2(x * size.x / 10.0, 0, 1.0, size.y), Color(0.5, 0.3, 1.0, alpha))


func _draw_main_menu(size: Vector2) -> void:
    if _logo != null:
        var width := minf(size.x * 0.56, 670.0)
        var height := width * _logo.get_height() / _logo.get_width()
        draw_texture_rect(_logo, Rect2(size.x * 0.5 - width * 0.5, 62.0, width, height), false)
    else:
        _draw_centered_text("LORD MAGICIAN", size.y * 0.28, 48, Color.GOLD)

    var y := size.y * 0.58
    _draw_button(Rect2(size.x * 0.5 - 145.0, y, 290.0, 54.0), "COMENZAR", UiAction.START, _menu_index == 0)
    _draw_button(Rect2(size.x * 0.5 - 145.0, y + 70.0, 290.0, 54.0), "MULTIJUGADOR", UiAction.OPEN_MULTIPLAYER, _menu_index == 1, false)
    _draw_button(Rect2(size.x * 0.5 - 145.0, y + 140.0, 290.0, 54.0), "AJUSTES", UiAction.OPEN_SETTINGS, _menu_index == 2)
    _draw_centered_text("WASD/mando para moverte · Ratón/táctil para mirar · Espacio/R2 para disparar", size.y - 32.0, 14, Color(1.0, 1.0, 1.0, 0.65))


func _draw_multiplayer_menu(size: Vector2) -> void:
    _draw_centered_text("MULTIJUGADOR", 90.0, 34, Color.GOLD)
    _draw_centered_text("Cooperativo — hasta 8 jugadores en la misma partida", 126.0, 14, Color(1.0, 1.0, 1.0, 0.65))
    if not _network_status.is_empty():
        _draw_centered_text(_network_status, 152.0, 15, Color.html("f2c94c"))

    var x := size.x * 0.5 - 170.0
    _draw_button(Rect2(x, 190.0, 340.0, 52.0), "ALOJAR PARTIDA", UiAction.MP_HOST, _menu_index == 0, not _multiplayer_active)

    _draw_text("IP a la que unirse:", Vector2(x, 268.0), 15, Color(1.0, 1.0, 1.0, 0.75))
    var ip_field := Rect2(x, 278.0, 340.0, 46.0)
    draw_rect(ip_field, Color(1.0, 1.0, 1.0, 0.08))
    draw_rect(ip_field, Color.WHITE if _join_field_active else Color(1.0, 1.0, 1.0, 0.3), false, 2.0)
    draw_string(_font, Vector2(ip_field.position.x + 14.0, ip_field.position.y + 30.0), _join_ip_text + ("_" if _join_field_active else ""), HORIZONTAL_ALIGNMENT_LEFT, ip_field.size.x - 28.0, 18, Color.WHITE)
    _ui_hits.append(UiHit.new(UiAction.MP_FOCUS_IP_FIELD, ip_field))

    _draw_button(Rect2(x, 340.0, 340.0, 52.0), "UNIRSE", UiAction.MP_JOIN, _menu_index == 1, not _multiplayer_active)
    _draw_button(Rect2(x, 408.0, 340.0, 48.0), "DESCONECTAR Y VOLVER" if _multiplayer_active else "VOLVER", UiAction.MP_BACK, _menu_index == 2)

    if _multiplayer_active:
        _draw_centered_text("Jugadores conectados: %d" % (_remote_players.size() + 1), 478.0, 15, Color.WHITE)
        if multiplayer.has_multiplayer_peer() and multiplayer.is_server():
            _draw_centered_text("Cuando estéis todos, pulsa COMENZAR en el menú principal de cualquier jugador para que el anfitrión inicie la partida.", 502.0, 13, Color(1.0, 1.0, 1.0, 0.6))


func _draw_settings(size: Vector2) -> void:
    _draw_centered_text("AJUSTES", 70.0, 38, Color.WHITE)

    const PANEL_WIDTH := 640.0
    var left := size.x * 0.5 - PANEL_WIDTH * 0.5
    const GAP := 12.0
    var option_width := (PANEL_WIDTH - GAP * 2.0) / 3.0
    var y := 118.0

    _draw_centered_text("DIFICULTAD", y, 17, Color.html("22d3ee"))
    y += 22.0
    var difficulty_row := Rect2(left, y, PANEL_WIDTH, 54.0)
    _draw_settings_option(Rect2(left, y, option_width, 54.0), "EASY", UiAction.SET_DIFFICULTY_EASY, _difficulty == Difficulty.EASY)
    _draw_settings_option(Rect2(left + option_width + GAP, y, option_width, 54.0), "NORMAL", UiAction.SET_DIFFICULTY_NORMAL, _difficulty == Difficulty.NORMAL)
    _draw_settings_option(Rect2(left + (option_width + GAP) * 2.0, y, option_width, 54.0), "HARD", UiAction.SET_DIFFICULTY_HARD, _difficulty == Difficulty.HARD)
    if _menu_index == 0:
        _draw_pill_outline(difficulty_row.grow(6.0), Color.html("22d3ee"), 2.0)
    y += 54.0 + 34.0

    _draw_centered_text("GRÁFICOS", y, 17, Color.html("f2c94c"))
    y += 22.0
    var quality_row := Rect2(left, y, PANEL_WIDTH, 54.0)
    _draw_settings_option(Rect2(left, y, option_width, 54.0), "Rendimiento", UiAction.SET_QUALITY_PERFORMANCE, _graphics_quality == GraphicsQuality.PERFORMANCE)
    _draw_settings_option(Rect2(left + option_width + GAP, y, option_width, 54.0), "Estándar", UiAction.SET_QUALITY_STANDARD, _graphics_quality == GraphicsQuality.STANDARD)
    _draw_settings_option(Rect2(left + (option_width + GAP) * 2.0, y, option_width, 54.0), "Alta Definición", UiAction.SET_QUALITY_HIGH_DEF, _graphics_quality == GraphicsQuality.HIGH_DEFINITION)
    if _menu_index == 1:
        _draw_pill_outline(quality_row.grow(6.0), Color.html("22d3ee"), 2.0)
    y += 54.0 + 40.0

    const SLIDER_HEIGHT := 26.0
    var sensitivity_fill := clampf((_look_sensitivity - 0.4) / (2.5 - 0.4), 0.0, 1.0)
    _draw_centered_text("SENSIBILIDAD (%d%%)" % roundi(_look_sensitivity * 100.0), y, 16, Color.WHITE)
    y += 20.0
    _draw_settings_slider(Rect2(left, y, PANEL_WIDTH, SLIDER_HEIGHT), sensitivity_fill, UiAction.SET_SENSITIVITY, _menu_index == 2)
    y += SLIDER_HEIGHT + 34.0

    _draw_centered_text("VOLUMEN MÚSICA (%d%%)" % roundi(_music_volume * 100.0), y, 16, Color.WHITE)
    y += 20.0
    _draw_settings_slider(Rect2(left, y, PANEL_WIDTH, SLIDER_HEIGHT), _music_volume, UiAction.SET_MUSIC_VOLUME, _menu_index == 3)
    y += SLIDER_HEIGHT + 34.0

    _draw_centered_text("VOLUMEN EFECTOS (%d%%)" % roundi(_fx_volume * 100.0), y, 16, Color.WHITE)
    y += 20.0
    _draw_settings_slider(Rect2(left, y, PANEL_WIDTH, SLIDER_HEIGHT), _fx_volume, UiAction.SET_FX_VOLUME, _menu_index == 4)
    y += SLIDER_HEIGHT + 44.0

    _draw_settings_option(_centered_rect(size, y, 300.0, 54.0), "VOLVER AL TÍTULO", UiAction.SETTINGS_BACK, _menu_index == 5)


## Rectángulo con extremos totalmente redondeados (forma de píldora), relleno.
func _draw_pill_rect(rect: Rect2, color: Color) -> void:
    var radius := minf(rect.size.x, rect.size.y) * 0.5
    if rect.size.x <= rect.size.y + 0.01:
        draw_circle(rect.get_center(), radius, color)
        return
    draw_rect(Rect2(rect.position.x + radius, rect.position.y, rect.size.x - radius * 2.0, rect.size.y), color)
    draw_circle(Vector2(rect.position.x + radius, rect.position.y + radius), radius, color)
    draw_circle(Vector2(rect.position.x + rect.size.x - radius, rect.position.y + radius), radius, color)


## Contorno (sin relleno) de una píldora, usado como indicador de foco de teclado/mando.
func _draw_pill_outline(rect: Rect2, color: Color, width: float) -> void:
    var radius := minf(rect.size.x, rect.size.y) * 0.5
    draw_circle(Vector2(rect.position.x + radius, rect.position.y + radius), radius, color, false, width)
    draw_circle(Vector2(rect.position.x + rect.size.x - radius, rect.position.y + radius), radius, color, false, width)
    draw_rect(Rect2(rect.position.x + radius, rect.position.y, rect.size.x - radius * 2.0, rect.size.y), color, false, width)


## Botón tipo píldora para Ajustes: relleno morado + contorno blanco cuando "selected" es la opción activa.
func _draw_settings_option(rect: Rect2, label: String, action: int, selected: bool) -> void:
    if selected:
        _draw_pill_rect(rect.grow(2.0), Color.WHITE)
        _draw_pill_rect(rect, Color.html("4a2e97"))
    else:
        _draw_pill_rect(rect, Color.html("525252"))
    draw_string(_font, Vector2(rect.position.x, rect.position.y + rect.size.y * 0.63), label, HORIZONTAL_ALIGNMENT_CENTER, rect.size.x, 16, Color.WHITE)
    _ui_hits.append(UiHit.new(action, rect))


## Barra deslizante tipo píldora: tramo relleno (azul) / vacío (lavanda), divisor y pomo.
func _draw_settings_slider(rect: Rect2, fill_t: float, action: int, focused: bool) -> void:
    fill_t = clampf(fill_t, 0.0, 1.0)
    _draw_pill_rect(rect, Color.html("dce0f9"))
    var fill_width := rect.size.x * fill_t
    if fill_width > 1.0:
        draw_rect(Rect2(rect.position.x, rect.position.y, fill_width, rect.size.y), Color.html("435d9a"))
    draw_rect(Rect2(rect.position.x + fill_width - 1.5, rect.position.y - 6.0, 3.0, rect.size.y + 12.0), Color.html("7b4bff"))
    draw_circle(Vector2(rect.position.x + fill_width, rect.position.y + rect.size.y * 0.5), rect.size.y * 0.5 + 4.0, Color.html("334155"))
    if focused:
        _draw_pill_outline(rect.grow(6.0), Color.html("22d3ee"), 2.0)
    _ui_hits.append(UiHit.new(action, rect))


func _draw_level_clear(size: Vector2) -> void:
    _draw_centered_text("¡%s superado!" % GameData.levels[_level_index].name, size.y * 0.34, 32, Color.GOLD)
    _draw_button(_centered_rect(size, size.y * 0.48, 250.0, 52.0), "TIENDA", UiAction.LEVEL_SHOP, _menu_index == 0)
    _draw_button(_centered_rect(size, size.y * 0.58, 250.0, 52.0), "CONTINUAR", UiAction.LEVEL_CONTINUE, _menu_index == 1)


func _draw_shop(size: Vector2) -> void:
    _draw_centered_text("TIENDA", 50.0, 34, Color.GOLD)
    _draw_centered_text("Oro: %d" % _player.gold, 80.0, 18, Color.WHITE)

    var rows := _build_shop_rows()
    var tops := _shop_row_tops(rows)
    var total_height := tops[-1] + _shop_row_height(rows[-1]) if tops.size() > 0 else 0.0

    const VIEWPORT_TOP := 116.0
    var viewport_bottom := size.y - 78.0
    var viewport_height := maxf(0.0, viewport_bottom - VIEWPORT_TOP)
    _shop_scroll = clampf(_shop_scroll, 0.0, maxf(0.0, total_height - viewport_height))

    const PANEL_WIDTH := 620.0
    var left := size.x * 0.5 - PANEL_WIDTH * 0.5

    var item_index := -1
    for i in rows.size():
        var row := rows[i]
        var row_height := _shop_row_height(row)
        var y: float = VIEWPORT_TOP + tops[i] - _shop_scroll
        if row.entry != null:
            item_index += 1
        if y + row_height < VIEWPORT_TOP or y > viewport_bottom:
            continue

        if row.entry == null:
            _draw_text(row.header, Vector2(left, y + row_height * 0.72), 17, Color.html("a78bfa"))
            continue

        _draw_shop_row(Rect2(left, y, PANEL_WIDTH, row_height - 10.0), row.entry, item_index == _shop_focus_index)

    var continue_rect := _centered_rect(size, size.y - 62.0, 330.0, 48.0)
    _draw_pill_rect(continue_rect, Color.html("4527a0"))
    draw_string(_font, Vector2(continue_rect.position.x, continue_rect.position.y + continue_rect.size.y * 0.65), "SIGUIENTE NIVEL", HORIZONTAL_ALIGNMENT_CENTER, continue_rect.size.x, 16, Color.WHITE)
    _ui_hits.append(UiHit.new(UiAction.SHOP_CONTINUE, continue_rect))


func _draw_shop_row(rect: Rect2, entry: ShopEntry, focused: bool) -> void:
    var equipped := false
    var owned := false
    match entry.kind:
        ShopEntryKind.WEAPON:
            equipped = _player.equipped_weapon == entry.index
            owned = _player.owned_weapons.has(entry.index)
        ShopEntryKind.ARMOR:
            equipped = _player.equipped_armor == entry.index
            owned = _player.owned_armors.has(entry.index)
        ShopEntryKind.ACCESSORY:
            equipped = _player.equipped_accessory == entry.index
            owned = _player.owned_accessories.has(entry.index)
        ShopEntryKind.SHIELD:
            equipped = _player.owns_shield

    var fill: Color
    if entry.kind == ShopEntryKind.POTION:
        fill = Color.html("4a2e97")
    elif equipped:
        fill = Color.html("6d3ef0")
    elif owned:
        fill = Color.html("4a2e97")
    else:
        fill = Color.html("454545")

    if focused:
        _draw_pill_rect(rect.grow(2.0), Color.WHITE)
        _draw_pill_rect(rect, fill)
    else:
        _draw_pill_rect(rect, fill)

    var label: String
    if entry.kind == ShopEntryKind.POTION or entry.kind == ShopEntryKind.SHIELD:
        label = entry.label
    else:
        label = "%s (%s)" % [entry.label, _shop_detail(entry)]
    draw_string(_font, Vector2(rect.position.x + 20.0, rect.position.y + rect.size.y * 0.63), label, HORIZONTAL_ALIGNMENT_LEFT, rect.size.x - 130.0, 17, Color.WHITE)
    draw_string(_font, Vector2(rect.end.x - 110.0, rect.position.y + rect.size.y * 0.63), "%d O" % entry.cost, HORIZONTAL_ALIGNMENT_RIGHT, 90.0, 17, Color.GOLD)


func _build_shop_rows() -> Array[ShopRow]:
    var entries := _get_shop_entries()
    var rows: Array[ShopRow] = []
    var last_kind := -1
    for entry in entries:
        if entry.kind != last_kind:
            var header: String
            match entry.kind:
                ShopEntryKind.POTION: header = "POCIONES"
                ShopEntryKind.WEAPON: header = "ARMAS"
                ShopEntryKind.ARMOR: header = "ARMADURAS"
                ShopEntryKind.SHIELD: header = "ESCUDO"
                _: header = "ACCESORIOS"
            rows.append(ShopRow.new(header, null))
            last_kind = entry.kind
        rows.append(ShopRow.new("", entry))
    return rows


static func _shop_row_height(row: ShopRow) -> float:
    return 74.0 if row.entry != null else 42.0


static func _shop_row_tops(rows: Array[ShopRow]) -> Array[float]:
    var tops: Array[float] = []
    var y := 0.0
    for row in rows:
        tops.append(y)
        y += _shop_row_height(row)
    return tops


## Fila de ítem bajo un punto de pantalla (en coordenadas de lienzo), o null si cae
## sobre una cabecera de categoría o fuera de la lista. Usa el mismo layout que _draw_shop.
func _shop_entry_at(pos: Vector2, size: Vector2) -> ShopEntry:
    var rows := _build_shop_rows()
    var tops := _shop_row_tops(rows)
    const VIEWPORT_TOP := 116.0
    var viewport_bottom := size.y - 78.0
    const PANEL_WIDTH := 620.0
    var left := size.x * 0.5 - PANEL_WIDTH * 0.5
    if pos.x < left or pos.x > left + PANEL_WIDTH:
        return null

    for i in rows.size():
        var row_height := _shop_row_height(rows[i])
        var y: float = VIEWPORT_TOP + tops[i] - _shop_scroll
        if y + row_height < VIEWPORT_TOP or y > viewport_bottom:
            continue
        if pos.y >= y and pos.y < y + row_height:
            return rows[i].entry
    return null


func _move_shop_focus(direction: int) -> void:
    var item_count := 0
    for row in _build_shop_rows():
        if row.entry != null:
            item_count += 1
    if item_count == 0:
        return
    _shop_focus_index = posmod(_shop_focus_index + direction, item_count)
    _scroll_shop_to_focus()


func _scroll_shop_to_focus() -> void:
    var rows := _build_shop_rows()
    var tops := _shop_row_tops(rows)
    var viewport_height := maxf(0.0, (get_viewport_rect().size.y - 78.0) - 116.0)
    var item_index := -1
    for i in rows.size():
        if rows[i].entry == null:
            continue
        item_index += 1
        if item_index != _shop_focus_index:
            continue
        var row_top := tops[i]
        var row_bottom := row_top + _shop_row_height(rows[i])
        if row_top < _shop_scroll:
            _shop_scroll = row_top
        elif row_bottom > _shop_scroll + viewport_height:
            _shop_scroll = row_bottom - viewport_height
        return


func _confirm_shop_focus() -> void:
    var entries: Array[ShopEntry] = []
    for row in _build_shop_rows():
        if row.entry != null:
            entries.append(row.entry)
    if _shop_focus_index >= 0 and _shop_focus_index < entries.size():
        _buy_shop_entry(entries[_shop_focus_index])


func _draw_game_over(size: Vector2) -> void:
    _draw_centered_text("HAS MUERTO", size.y * 0.32, 44, Color.RED)
    _draw_centered_text("En %s" % GameData.levels[_level_index].name, size.y * 0.38, 18, Color(1.0, 1.0, 1.0, 0.66))
    _draw_button(_centered_rect(size, size.y * 0.48, 280.0, 52.0), "REINTENTAR NIVEL", UiAction.RETRY, _menu_index == 0)
    _draw_button(_centered_rect(size, size.y * 0.58, 280.0, 52.0), "VOLVER AL TÍTULO", UiAction.GAME_OVER_MENU, _menu_index == 1)


func _draw_pause(size: Vector2) -> void:
    _draw_centered_text("PAUSA", size.y * 0.34, 44, Color.WHITE)
    _draw_button(_centered_rect(size, size.y * 0.48, 250.0, 52.0), "REANUDAR", UiAction.RESUME, _menu_index == 0)
    _draw_button(_centered_rect(size, size.y * 0.58, 250.0, 52.0), "SALIR AL MENÚ", UiAction.PAUSE_QUIT, _menu_index == 1)


func _draw_victory(size: Vector2) -> void:
    _draw_centered_text("¡VICTORIA!", size.y * 0.38, 48, Color.GOLD)
    _draw_centered_text("El Corazón del Vacío ha sido derrotado.", size.y * 0.44, 18, Color.WHITE)
    _draw_button(_centered_rect(size, size.y * 0.54, 290.0, 52.0), "VOLVER AL MENÚ", UiAction.VICTORY_MENU, true)


func _draw_button(rect: Rect2, label: String, action: int, selected: bool = false, enabled: bool = true) -> void:
    var fill := (Color.html("7c4dff") if selected else Color.html("4527a0")) if enabled else Color.html("3f3f46")
    draw_rect(rect, fill)
    draw_rect(rect, Color.WHITE if selected else Color(1.0, 1.0, 1.0, 0.25), false, 2.0 if selected else 1.0)
    draw_string(_font, Vector2(rect.position.x, rect.position.y + rect.size.y * 0.65), label, HORIZONTAL_ALIGNMENT_CENTER, rect.size.x, 17, Color.WHITE if enabled else Color(1.0, 1.0, 1.0, 0.45))
    if enabled:
        _ui_hits.append(UiHit.new(action, rect))


func _draw_vertical_gradient(rect: Rect2, top: Color, bottom: Color) -> void:
    const STEPS := 32
    for i in STEPS:
        var t := i / float(STEPS - 1)
        draw_rect(Rect2(rect.position.x, rect.position.y + rect.size.y * t, rect.size.x, rect.size.y / STEPS + 1.0), top.lerp(bottom, t))


func _draw_text(text: String, baseline: Vector2, font_size: int, color: Color) -> void:
    draw_string(_font, baseline, text, HORIZONTAL_ALIGNMENT_LEFT, -1.0, font_size, color)


func _draw_centered_text(text: String, baseline_y: float, font_size: int, color: Color) -> void:
    var size := get_viewport_rect().size
    draw_string(_font, Vector2(0.0, baseline_y), text, HORIZONTAL_ALIGNMENT_CENTER, size.x, font_size, color)


static func _centered_rect(size: Vector2, y: float, width: float, height: float) -> Rect2:
    return Rect2(size.x * 0.5 - width * 0.5, y, width, height)


func _handle_playing_press(pos: Vector2, touch_index: int) -> void:
    var size := get_viewport_rect().size
    var layout := TouchLayout.new(size)
    if pos.distance_to(layout.pause_center) < layout.pause_radius + 12.0 * layout.ui_scale:
        _set_phase(GamePhase.PAUSED)
        return
    if pos.distance_to(layout.shoot_center) < layout.shoot_radius + 12.0 * layout.ui_scale:
        _touch_shooting = true
        return
    if _player.owns_shield and pos.distance_to(layout.shield_center) < layout.shield_radius + 12.0 * layout.ui_scale:
        _activate_shield()
        return
    if pos.x < layout.joystick_zone_width and pos.y > size.y - layout.joystick_zone_height:
        _joystick_touch = touch_index
        _touch_move_active = true
        _update_touch_joystick(pos)
        return
    _look_touch = touch_index
    _last_look_position = pos


func _update_touch_joystick(pos: Vector2) -> void:
    var layout := TouchLayout.new(get_viewport_rect().size)
    var offset := pos - layout.joystick_center
    if offset.length() > layout.joystick_radius:
        offset = offset.normalized() * layout.joystick_radius
    _touch_move = Vector2(offset.x / layout.joystick_radius, -offset.y / layout.joystick_radius)


func _handle_shop_pointer_down(pos: Vector2, touch_index: int) -> void:
    if _handle_ui_press(pos):
        return # ya lo consumió "SIGUIENTE NIVEL"
    _shop_pointer_active = true
    _shop_pointer_touch = touch_index
    _shop_pointer_last_pos = pos
    _shop_pointer_total_drag = 0.0


func _handle_shop_pointer_move(pos: Vector2) -> void:
    if not _shop_pointer_active:
        return
    _shop_scroll -= pos.y - _shop_pointer_last_pos.y
    _shop_pointer_total_drag += (pos - _shop_pointer_last_pos).length()
    _shop_pointer_last_pos = pos


func _handle_shop_pointer_up(pos: Vector2) -> void:
    if not _shop_pointer_active:
        return
    _shop_pointer_active = false
    _shop_pointer_touch = -1
    if _shop_pointer_total_drag < 12.0:
        var entry := _shop_entry_at(pos, get_viewport_rect().size)
        if entry != null:
            _buy_shop_entry(entry)


func _handle_ui_press(pos: Vector2) -> bool:
    for i in range(_ui_hits.size() - 1, -1, -1):
        if _ui_hits[i].rect.has_point(pos):
            var action := _ui_hits[i].action
            if _is_slider_action(action):
                _dragging_slider = action
                _apply_slider_value(action, _ui_hits[i].rect, pos.x)
            else:
                _handle_ui_action(action)
            return true
    return false


static func _is_slider_action(action: int) -> bool:
    return action == UiAction.SET_SENSITIVITY or action == UiAction.SET_MUSIC_VOLUME or action == UiAction.SET_FX_VOLUME


func _apply_slider_value(action: int, rect: Rect2, pointer_x: float) -> void:
    var t := clampf((pointer_x - rect.position.x) / rect.size.x, 0.0, 1.0) if rect.size.x > 0.0 else 0.0
    match action:
        UiAction.SET_SENSITIVITY:
            _look_sensitivity = lerpf(0.4, 2.5, t)
        UiAction.SET_MUSIC_VOLUME:
            _music_volume = t
            _update_music_volume()
        UiAction.SET_FX_VOLUME:
            _fx_volume = t


func _find_ui_hit(action: int) -> UiHit:
    for i in range(_ui_hits.size() - 1, -1, -1):
        if _ui_hits[i].action == action:
            return _ui_hits[i]
    return null


## Ajusta con Izquierda/Derecha (teclado o mando) la fila de Ajustes actualmente enfocada
## por teclado/mando (_menu_index), en vez de arrastrar como con el ratón/dedo.
func _adjust_focused_setting(direction: int) -> void:
    match _menu_index:
        0:
            _difficulty = posmod(_difficulty + direction, 3)
        1:
            _graphics_quality = posmod(_graphics_quality + direction, 3)
        2:
            _look_sensitivity = clampf(_look_sensitivity + direction * 0.1, 0.4, 2.5)
        3:
            _music_volume = clampf(_music_volume + direction * 0.05, 0.0, 1.0)
            _update_music_volume()
        4:
            _fx_volume = clampf(_fx_volume + direction * 0.05, 0.0, 1.0)


func _move_menu(direction: int) -> void:
    if _phase == GamePhase.SHOP:
        _move_shop_focus(direction)
        return
    var count := 1
    match _phase:
        GamePhase.MAIN_MENU: count = 3
        GamePhase.SETTINGS: count = 6
        GamePhase.LEVEL_CLEAR: count = 2
        GamePhase.GAME_OVER: count = 2
        GamePhase.PAUSED: count = 2
        GamePhase.MULTIPLAYER_MENU: count = 3
    _menu_index = posmod(_menu_index + direction, count)


func _confirm_menu() -> void:
    if _phase == GamePhase.SHOP:
        _confirm_shop_focus()
        return
    var action: int = UiAction.NONE
    match _phase:
        GamePhase.MAIN_MENU:
            match _menu_index:
                0: action = UiAction.START
                1: action = UiAction.NONE
                _: action = UiAction.OPEN_SETTINGS
        GamePhase.SETTINGS:
            action = UiAction.SETTINGS_BACK if _menu_index == 5 else UiAction.NONE
        GamePhase.LEVEL_CLEAR:
            action = UiAction.LEVEL_SHOP if _menu_index == 0 else UiAction.LEVEL_CONTINUE
        GamePhase.GAME_OVER:
            action = UiAction.RETRY if _menu_index == 0 else UiAction.GAME_OVER_MENU
        GamePhase.PAUSED:
            action = UiAction.RESUME if _menu_index == 0 else UiAction.PAUSE_QUIT
        GamePhase.FINISHED:
            action = UiAction.VICTORY_MENU
        GamePhase.MULTIPLAYER_MENU:
            match _menu_index:
                0: action = UiAction.MP_HOST
                1: action = UiAction.MP_JOIN
                _: action = UiAction.MP_BACK
    _handle_ui_action(action)


func _cancel_menu() -> void:
    match _phase:
        GamePhase.SETTINGS:
            _set_phase(GamePhase.MAIN_MENU)
        GamePhase.PAUSED:
            _set_phase(GamePhase.PLAYING)
        GamePhase.SHOP:
            _advance_level()
        GamePhase.LEVEL_CLEAR:
            _advance_level()


func _handle_ui_action(action: int) -> void:
    match action:
        UiAction.START:
            _load_level(0)
            if _multiplayer_active and multiplayer.has_multiplayer_peer() and multiplayer.is_server():
                rpc_start_level.rpc(0)
        UiAction.OPEN_SETTINGS:
            _set_phase(GamePhase.SETTINGS)
        UiAction.OPEN_MULTIPLAYER:
            _network_status = ""
            _set_phase(GamePhase.MULTIPLAYER_MENU)
        UiAction.MP_HOST:
            _start_multiplayer_host()
        UiAction.MP_JOIN:
            _start_multiplayer_client(_join_ip_text)
        UiAction.MP_BACK:
            _stop_multiplayer()
            _set_phase(GamePhase.MAIN_MENU)
        UiAction.MP_FOCUS_IP_FIELD:
            _join_field_active = true
        UiAction.SETTINGS_BACK:
            _set_phase(GamePhase.MAIN_MENU)
        UiAction.SET_DIFFICULTY_EASY:
            _difficulty = Difficulty.EASY
        UiAction.SET_DIFFICULTY_NORMAL:
            _difficulty = Difficulty.NORMAL
        UiAction.SET_DIFFICULTY_HARD:
            _difficulty = Difficulty.HARD
        UiAction.SET_QUALITY_PERFORMANCE:
            _graphics_quality = GraphicsQuality.PERFORMANCE
        UiAction.SET_QUALITY_STANDARD:
            _graphics_quality = GraphicsQuality.STANDARD
        UiAction.SET_QUALITY_HIGH_DEF:
            _graphics_quality = GraphicsQuality.HIGH_DEFINITION
        UiAction.LEVEL_SHOP:
            _set_phase(GamePhase.SHOP)
        UiAction.LEVEL_CONTINUE:
            _advance_level()
        UiAction.SHOP_CONTINUE:
            _advance_level()
        UiAction.RETRY:
            _load_level(_level_index)
        UiAction.GAME_OVER_MENU:
            _set_phase(GamePhase.MAIN_MENU)
        UiAction.RESUME:
            _set_phase(GamePhase.PLAYING)
        UiAction.PAUSE_QUIT:
            _set_phase(GamePhase.MAIN_MENU)
        UiAction.VICTORY_MENU:
            _set_phase(GamePhase.MAIN_MENU)


func _advance_level() -> void:
    if _level_index + 1 < GameData.levels.size():
        _load_level(_level_index + 1)
    else:
        _set_phase(GamePhase.FINISHED)


func _get_shop_entries() -> Array[ShopEntry]:
    var entries: Array[ShopEntry] = [
        ShopEntry.new("Poción de Vida", 10, ShopEntryKind.POTION, 0),
        ShopEntry.new("Maná Máximo", 8, ShopEntryKind.POTION, 1),
        ShopEntry.new("Escudo Arcano", 220, ShopEntryKind.SHIELD, 0),
    ]
    for i in range(1, GameData.weapons.size()):
        entries.append(ShopEntry.new(GameData.weapons[i].name, GameData.weapons[i].cost, ShopEntryKind.WEAPON, i))
    for i in range(1, GameData.armors.size()):
        entries.append(ShopEntry.new(GameData.armors[i].name, GameData.armors[i].cost, ShopEntryKind.ARMOR, i))
    for i in range(1, GameData.accessories.size()):
        entries.append(ShopEntry.new(GameData.accessories[i].name, GameData.accessories[i].cost, ShopEntryKind.ACCESSORY, i))
    return entries


func _shop_detail(entry: ShopEntry) -> String:
    match entry.kind:
        ShopEntryKind.POTION:
            return "+30 puntos de vida" if entry.index == 0 else "+15 de maná máximo (permanente)"
        ShopEntryKind.WEAPON:
            var weapon := GameData.weapons[entry.index]
            return "Daño: %.0f · Maná: %.0f · CD: %.2fs" % [weapon.damage, weapon.mana_cost, weapon.cooldown]
        ShopEntryKind.ARMOR:
            return "Defensa: %.0f%%" % (GameData.armors[entry.index].defense * 100.0)
        ShopEntryKind.SHIELD:
            return "Actívalo en partida (tecla E / LB / botón táctil): inmunidad total 7.5s, con recarga"
    return "Ataque: +%.0f%%" % (GameData.accessories[entry.index].attack_bonus * 100.0)


func _can_buy(entry: ShopEntry) -> bool:
    if entry.kind == ShopEntryKind.POTION:
        return _player.gold >= entry.cost
    if entry.kind == ShopEntryKind.SHIELD:
        return _player.owns_shield or _player.gold >= entry.cost
    var owned: bool
    match entry.kind:
        ShopEntryKind.WEAPON: owned = _player.owned_weapons.has(entry.index)
        ShopEntryKind.ARMOR: owned = _player.owned_armors.has(entry.index)
        _: owned = _player.owned_accessories.has(entry.index)
    return owned or _player.gold >= entry.cost


func _buy_shop_entry(entry: ShopEntry) -> void:
    if not _can_buy(entry):
        return

    match entry.kind:
        ShopEntryKind.POTION:
            _player.gold -= entry.cost
            if entry.index == 0:
                _player.health = minf(_player.max_health, _player.health + 30.0)
            else:
                _player.max_mana += 15.0
                _player.mana = _player.max_mana
        ShopEntryKind.SHIELD:
            if not _player.owns_shield:
                _player.gold -= entry.cost
                _player.owns_shield = true
        ShopEntryKind.WEAPON:
            if not _player.owned_weapons.has(entry.index):
                _player.gold -= entry.cost
                _player.owned_weapons.append(entry.index)
            _player.equipped_weapon = entry.index
        ShopEntryKind.ARMOR:
            if not _player.owned_armors.has(entry.index):
                _player.gold -= entry.cost
                _player.owned_armors.append(entry.index)
            _player.equipped_armor = entry.index
        ShopEntryKind.ACCESSORY:
            if not _player.owned_accessories.has(entry.index):
                _player.gold -= entry.cost
                _player.owned_accessories.append(entry.index)
            _player.equipped_accessory = entry.index


func _set_phase(phase: int) -> void:
    _phase = phase
    _menu_index = 0
    _touch_shooting = false
    _mouse_shooting = false
    _touch_move = Vector2.ZERO
    if phase == GamePhase.SHOP:
        _shop_scroll = 0.0
        _shop_focus_index = 0
        _shop_pointer_active = false
    if phase == GamePhase.MAIN_MENU:
        _play_music("bgm_menu")
    if phase == GamePhase.PLAYING and _level_index + 1 == 5:
        _play_music("bgm_boss5_game")
    if phase == GamePhase.PLAYING and _level_index + 1 == 8:
        _play_music("bgm_boss8_game")
    if phase == GamePhase.PLAYING and (_level_index + 1 != 5 and _level_index + 1 != 8):
        _play_music("bgm_regular_game")

    # En PC, el ratón no se usa para apuntar (el disparo sigue _player.angle),
    # así que se oculta durante la partida para no tapar la mira. Se usa
    # "Captured" en vez de "Hidden" para que además quede anclado a la
    # ventana: así el arrastre con el botón derecho sigue generando
    # movimiento aunque el cursor "quiera" salirse de la vista de juego.
    # En menús se vuelve a mostrar para poder pulsar los botones.
    Input.mouse_mode = Input.MOUSE_MODE_CAPTURED if phase == GamePhase.PLAYING else Input.MOUSE_MODE_VISIBLE


# =====================================================================
# MULTIJUGADOR
# ---------------------------------------------------------------------
# Cooperativo por IP directa usando el ENetMultiplayerPeer de Godot (todos
# exploran/pelean en el mismo nivel). El anfitrión también juega como un
# peer más. AVISO: de momento la posición/salud de cada jugador remoto es
# la que ESE jugador dice tener — no hay validación en el host, así que no
# es a prueba de trampas. Los enemigos, objetos y la tienda siguen siendo
# locales a cada cliente (no están sincronizados todavía).
# =====================================================================

func _start_multiplayer_host() -> void:
    if _multiplayer_active:
        return
    _peer = ENetMultiplayerPeer.new()
    var err := _peer.create_server(MULTIPLAYER_PORT, 8)
    if err != OK:
        _network_status = "No se pudo alojar (error: %s)." % error_string(err)
        _peer = null
        return
    multiplayer.multiplayer_peer = _peer
    _multiplayer_active = true
    _network_status = "Partida alojada. Esperando jugadores..."
    multiplayer.peer_connected.connect(_on_peer_connected)
    multiplayer.peer_disconnected.connect(_on_peer_disconnected)


func _start_multiplayer_client(ip: String) -> void:
    if _multiplayer_active:
        return
    _peer = ENetMultiplayerPeer.new()
    var err := _peer.create_client(ip, MULTIPLAYER_PORT)
    if err != OK:
        _network_status = "No se pudo conectar (error: %s)." % error_string(err)
        _peer = null
        return
    multiplayer.multiplayer_peer = _peer
    _multiplayer_active = true
    _network_status = "Conectando..."
    multiplayer.connected_to_server.connect(_on_connected_to_server)
    multiplayer.connection_failed.connect(_on_connection_failed)
    multiplayer.peer_connected.connect(_on_peer_connected)
    multiplayer.peer_disconnected.connect(_on_peer_disconnected)


func _stop_multiplayer() -> void:
    if _peer != null:
        _disconnect_signal(multiplayer.peer_connected, _on_peer_connected)
        _disconnect_signal(multiplayer.peer_disconnected, _on_peer_disconnected)
        _disconnect_signal(multiplayer.connected_to_server, _on_connected_to_server)
        _disconnect_signal(multiplayer.connection_failed, _on_connection_failed)
        _peer.close()
        multiplayer.multiplayer_peer = null
        _peer = null
    _multiplayer_active = false
    _remote_players.clear()


## En C# desuscribirse con `-=` es inocuo aunque no estuviera suscrito; en GDScript hay que comprobarlo.
static func _disconnect_signal(sig: Signal, callable: Callable) -> void:
    if sig.is_connected(callable):
        sig.disconnect(callable)


func _on_connected_to_server() -> void:
    _network_status = "Conectado. Vuelve al menú principal y pulsa COMENZAR."


func _on_connection_failed() -> void:
    _network_status = "No se pudo conectar — revisa la IP y el puerto (%d)." % MULTIPLAYER_PORT
    _multiplayer_active = false
    _peer = null


func _on_peer_connected(id: int) -> void:
    var remote := RemotePlayerState.new()
    remote.player_name = "Jugador %d" % id
    _remote_players[id] = remote


func _on_peer_disconnected(id: int) -> void:
    _remote_players.erase(id)


## Se llama periódicamente desde _update_game mientras hay partida multijugador
## activa, para difundir la posición/ángulo/vida propios a los demás peers.
func _broadcast_local_player_state() -> void:
    if not _multiplayer_active or not multiplayer.has_multiplayer_peer():
        return
    rpc_receive_player_state.rpc(_player.position.x, _player.position.y, _player.angle, _player.health, _player.max_health)


@rpc("authority", "call_remote", "reliable")
func rpc_start_level(level_index: int) -> void:
    _load_level(level_index)


@rpc("any_peer", "call_remote", "unreliable")
func rpc_receive_player_state(x: float, y: float, angle: float, health: float, max_health: float) -> void:
    var sender := multiplayer.get_remote_sender_id()
    var remote: RemotePlayerState = _remote_players.get(sender)
    if remote == null:
        remote = RemotePlayerState.new()
        remote.player_name = "Jugador %d" % sender
        _remote_players[sender] = remote
    remote.position = Vector2(x, y)
    remote.angle = angle
    remote.health = health
    remote.max_health = max_health


## El anfitrión manda un único RPC con todos los enemigos vivos, en vez de uno
## por enemigo: (índice, x, y, hp, frameDeAnimación) por cada uno, todo seguido
## en un solo array de floats.
func _broadcast_enemy_snapshot() -> void:
    if not multiplayer.has_multiplayer_peer():
        return
    var data := PackedFloat32Array()
    data.resize(_enemies.size() * 5)
    for i in _enemies.size():
        var enemy := _enemies[i]
        var b := i * 5
        data[b] = i
        data[b + 1] = enemy.position.x
        data[b + 2] = enemy.position.y
        data[b + 3] = enemy.hp
        data[b + 4] = enemy.animation_frame
    rpc_sync_enemies.rpc(data)


@rpc("authority", "call_remote", "unreliable")
func rpc_sync_enemies(data: PackedFloat32Array) -> void:
    # Los clientes no simulan enemigos: solo pintan lo último que dijo el anfitrión.
    var b := 0
    while b + 4 < data.size():
        var index := int(data[b])
        if index >= 0 and index < _enemies.size():
            var enemy := _enemies[index]
            enemy.position = Vector2(data[b + 1], data[b + 2])
            enemy.hp = data[b + 3]
            enemy.animation_frame = int(data[b + 4])
        b += 5


## Un cliente que golpea a un enemigo ya se lo aplica de forma local (para
## feedback instantáneo, igual que en un jugador), y además avisa al
## anfitrión para que su copia — la que se difunde a todos — también lo
## refleje. El anfitrión NO concede recompensa aquí (ya se la llevó quien
## disparó); solo mantiene su simulación al día.
@rpc("any_peer", "call_remote", "reliable")
func rpc_request_enemy_hit(enemy_index: int, damage: float) -> void:
    if not _is_host_authority() or enemy_index < 0 or enemy_index >= _enemies.size():
        return
    var enemy := _enemies[enemy_index]
    if not enemy.alive:
        return
    enemy.hp -= damage
    enemy.hit_flash = 0.15


# =====================================================================
# AUDIO
# =====================================================================

func _load_audio() -> void:
    for sound_name in ["bgm_menu", "bgm_regular_game", "bgm_boss5_game", "bgm_boss8_game", "bgm_finalboss_game", "snd_shoot", "snd_player_hit", "snd_enemy_red", "snd_enemy_green", "snd_enemy_blue", "snd_enemy_boss", "snd_enemy_sentinel"]:
        var path := "res://assets/audio/%s.ogg" % sound_name
        if not ResourceLoader.exists(path):
            continue
        var stream := load(path) as AudioStream
        if stream == null:
            continue
        # La música (bgm_*) debe repetirse sin fin; los efectos de sonido (snd_*) no.
        if sound_name.begins_with("bgm_") and stream is AudioStreamOggVorbis:
            stream.loop = true
        _sounds[sound_name] = stream
    _music_player = AudioStreamPlayer.new()
    add_child(_music_player)


func _play_music(track: String) -> void:
    if _music_player == null or not _sounds.has(track):
        return
    if _current_music_name == track and _music_player.playing:
        return # ya está sonando: no reiniciar desde el principio
    _current_music_name = track
    _music_player.stop()
    _music_player.stream = _sounds[track]
    _music_player.volume_db = linear_to_db(maxf(0.001, _music_volume))
    _music_player.play()


func _update_music_volume() -> void:
    if _music_player != null:
        _music_player.volume_db = linear_to_db(maxf(0.001, _music_volume))


func _play_sfx(sound_name: String) -> void:
    if not _sounds.has(sound_name):
        return
    var player := AudioStreamPlayer.new()
    player.stream = _sounds[sound_name]
    player.volume_db = linear_to_db(maxf(0.001, _fx_volume))
    add_child(player)
    player.finished.connect(player.queue_free)
    player.play()


# =====================================================================
# TEXTURAS PROCEDURALES
# =====================================================================

## Genera una textura de ladrillos por tipo de pared (8x8 ladrillos a hiladas alternas,
## con juntas de mortero y una variación de tono determinista por ladrillo).
##
## En C# se escribía píxel a píxel (65.536 SetPixel por textura). En GDScript eso tardaría
## varios segundos al arrancar (sobre todo en móvil), así que aquí se rellena el mortero
## y después cada ladrillo con fill_rect: el resultado es exactamente el mismo.
func _create_wall_textures() -> void:
    for code in _wall_colors:
        var base_color: Color = _wall_colors[code]
        var image := Image.create_empty(WALL_TEXTURE_SIZE, WALL_TEXTURE_SIZE, false, Image.FORMAT_RGBA8)
        image.fill(base_color.lerp(Color.BLACK, 0.45)) # mortero

        for row in BRICK_ROWS:
            var y0 := row * BRICK_ROW_HEIGHT
            # Ladrillos a hiladas alternas (running bond), como un muro real.
            var row_offset := 0 if row % 2 == 0 else BRICK_HALF_WIDTH
            for col in BRICK_COLS:
                # Variación de tono por ladrillo determinista (no ruido por píxel/rectángulo al azar).
                var brick_seed: int = (row * 928371 + col * 12871 + code * 37) & 0x7fffffff
                var variation := float(brick_seed % 21 - 10) / 110.0 # -0.09..0.09
                var brick := base_color.lerp(Color.WHITE, variation) if variation >= 0.0 else base_color.lerp(Color.BLACK, -variation)
                var highlight := brick.lerp(Color.WHITE, 0.06) # realce suave arriba de cada ladrillo
                # Tramo del ladrillo en coordenadas desplazadas: [col*32 + mortero, col*32 + 31].
                var x_start := col * BRICK_COL_WIDTH + MORTAR_PX - row_offset
                var x_end := col * BRICK_COL_WIDTH + BRICK_COL_WIDTH - 1 - row_offset
                if x_start < 0:
                    # El ladrillo cruza el borde izquierdo: continúa por el borde derecho de la textura.
                    _fill_brick(image, x_start + WALL_TEXTURE_SIZE, WALL_TEXTURE_SIZE - 1, y0, brick, highlight)
                    x_start = 0
                _fill_brick(image, x_start, x_end, y0, brick, highlight)

        image.generate_mipmaps()
        _wall_textures[code] = ImageTexture.create_from_image(image)


## Pinta un ladrillo entre x_start..x_end (inclusive): 2 filas de realce y el resto de cuerpo,
## dejando mortero arriba (3 px) y abajo (2 px) dentro de la hilada.
static func _fill_brick(image: Image, x_start: int, x_end: int, y0: int, brick: Color, highlight: Color) -> void:
    var width := x_end - x_start + 1
    image.fill_rect(Rect2i(x_start, y0 + MORTAR_PX, width, 2), highlight)
    image.fill_rect(Rect2i(x_start, y0 + MORTAR_PX + 2, width, BRICK_ROW_HEIGHT - MORTAR_PX - 2 - 2), brick)


# =====================================================================
# UTILIDADES
# =====================================================================

static func _load_texture(path: String) -> Texture2D:
    if not ResourceLoader.exists(path):
        return null
    return load(path) as Texture2D


static func _alpha(color: Color, alpha: float) -> Color:
    return Color(color.r, color.g, color.b, alpha)


func _wall_color(code: int) -> Color:
    return _wall_colors.get(code, Color.GRAY)


static func _enemy_color(type: int) -> Color:
    match type:
        EnemyType.MELEE: return Color.html("b5452e")
        EnemyType.RANGED: return Color.html("4ea34e")
        EnemyType.TANK: return Color.html("455a64")
        EnemyType.BOSS: return Color.html("6a1b9a")
    return Color.BLACK


static func _enemy_scale(type: int) -> float:
    match type:
        EnemyType.TANK: return 1.5
        EnemyType.BOSS: return 2.5
        EnemyType.SENTINEL: return 2.8
    return 1.2


func _enemy_texture(type: int) -> Texture2D:
    match type:
        EnemyType.MELEE: return _red_enemy
        EnemyType.RANGED: return _green_wizard
        EnemyType.TANK: return _blue_tank
        EnemyType.BOSS: return _boss
    return _sentinel


## La escala en el mundo coincide con _update_3d_sprites: cada spritesheet
## contiene cinco fotogramas horizontales. El radio es la mitad del ancho
## visible del fotograma ya escalado, de modo que brazos y alas no
## atraviesan las paredes y los proyectiles impactan donde se ve al enemigo.
func _enemy_collision_radius(enemy: Enemy) -> float:
    var texture := _enemy_texture(enemy.type)
    if texture == null:
        return _enemy_scale(enemy.type) * 0.3
    var frame_width := texture.get_width() / 5.0
    var visible_world_width := _enemy_scale(enemy.type) * frame_width / texture.get_height()
    return visible_world_width * 0.5


static func _enemy_sound(type: int) -> String:
    match type:
        EnemyType.MELEE: return "snd_enemy_red"
        EnemyType.RANGED: return "snd_enemy_green"
        EnemyType.TANK: return "snd_enemy_blue"
        EnemyType.BOSS: return "snd_enemy_boss"
    return "snd_enemy_sentinel"


static func _has_gamepad() -> bool:
    return Input.get_connected_joypads().size() > 0


## Filtra el drift de los sticks analógicos: por debajo del umbral se considera reposo (0,0)
## y por encima se reescala suavemente para no perder rango de movimiento.
static func _apply_deadzone(stick: Vector2, deadzone: float = 0.2) -> Vector2:
    var length := stick.length()
    if length < deadzone:
        return Vector2.ZERO
    var rescaled := minf(1.0, (length - deadzone) / (1.0 - deadzone))
    return stick.normalized() * rescaled
