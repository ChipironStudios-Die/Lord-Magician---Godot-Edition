#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LordMagician;

/// <summary>
/// Port funcional de MainActivity.kt. Godot gestiona ventana, audio e input;
/// el raycaster se mantiene para preservar la estética del juego original.
/// </summary>
public partial class GameMain : Node2D
{
	private const float FieldOfView = 1.57f;
	private const float WallMaxDistance = 18f;
	private const float PlayerWallCollisionRadius = 0.22f;
	private const float EnemyProjectilePlayerHitRadius = 0.40f;
	private const float ItemPickupRadius = 0.50f;

	private readonly Random _random = new();
	private readonly PlayerState _player = new();

	// --- Multijugador (co-op LAN/online por IP directa, vía ENet) ---
	private const int MultiplayerPort = 8910;
	private ENetMultiplayerPeer? _peer;
	private bool _multiplayerActive;
	private string _networkStatus = "";
	private string _joinIpText = "127.0.0.1";
	private bool _joinFieldActive;
	private float _networkSendTimer;
	private readonly Dictionary<long, RemotePlayerState> _remotePlayers = new();

	// En una partida multijugador, solo el anfitrión simula a los enemigos (IA,
	// movimiento, ataques) y difunde su estado; los clientes solo lo reciben y
	// lo dibujan. En un solo jugador esto siempre es true (nada cambia).
	private bool IsHostAuthority => !_multiplayerActive || (Multiplayer.HasMultiplayerPeer() && Multiplayer.IsServer());
	private readonly List<Enemy> _enemies = new();
	private readonly List<Projectile> _projectiles = new();
	private readonly List<WorldItem> _items = new();
	private readonly List<Particle> _particles = new();
	private readonly List<UiHit> _uiHits = new();
	private readonly Dictionary<int, Texture2D> _wallTextures = new();
	private readonly Dictionary<string, AudioStream> _sounds = new();

	private Font _font = null!;
	private Texture2D? _logo;
	private Texture2D? _staff;
	private Texture2D? _redEnemy;
	private Texture2D? _greenWizard;
	private Texture2D? _blueTank;
	private Texture2D? _boss;
	private Texture2D? _sentinel;
	private Texture2D? _redPotion;
	private Texture2D? _bluePotion;
	private AudioStreamPlayer? _musicPlayer;
	private string _currentMusicName = "";

	private GamePhase _phase = GamePhase.MainMenu;
	private Difficulty _difficulty = Difficulty.Normal;
	private GraphicsQuality _graphicsQuality = GraphicsQuality.Standard;
	private int _levelIndex;
	private int _menuIndex;
	private float _shopScroll;
	private int _shopFocusIndex;
	private bool _shopPointerActive;
	private int _shopPointerTouch = -1;
	private Vector2 _shopPointerLastPos;
	private float _shopPointerTotalDrag;
	private float _musicVolume = 0.5f;
	private float _fxVolume = 0.8f;
	private float _lookSensitivity = 1f;
	private float _frameTick;
	private Vector2 _touchMove;
	private bool _touchMoveActive;
	private bool _touchShooting;
	private bool _mouseShooting;
	private int _joystickTouch = -1;
	private int _lookTouch = -1;
	private Vector2 _lastLookPosition;
	private UiAction _draggingSlider = UiAction.None;
	private int _draggingSliderTouchIndex = -1;
	private float[] _zBuffer = Array.Empty<float>();
	private float[] _wallTopBuffer = Array.Empty<float>();
	private bool _debugOverlayEnabled;

	private static readonly Dictionary<int, Color> WallColors = new()
	{
		[1] = Color.FromHtml("5d4037"),
		[2] = Color.FromHtml("2e7d32"),
		[3] = Color.FromHtml("4527a0"),
		[4] = Color.FromHtml("c62828"),
		[5] = Color.FromHtml("ffd54f"),
		[6] = Color.FromHtml("616161"),
		[7] = Color.FromHtml("424242")
	};

	public override void _Ready()
	{
		_font = ThemeDB.FallbackFont;
		_logo = LoadTexture("res://assets/sprites/game_logo.png");
		_staff = LoadTexture("res://assets/sprites/player_staff.png");
		_redEnemy = LoadTexture("res://assets/sprites/red_enemy_spritesheet.png");
		_greenWizard = LoadTexture("res://assets/sprites/green_wizard_spritesheet.png");
		_blueTank = LoadTexture("res://assets/sprites/blue_tank_spritesheet.png");
		_boss = LoadTexture("res://assets/sprites/boss_spritesheet.png");
		_sentinel = LoadTexture("res://assets/sprites/sentinel_spritesheet.png");
		_redPotion = LoadTexture("res://assets/sprites/red_potion.png");
		_bluePotion = LoadTexture("res://assets/sprites/blue_potion.png");

		LoadAudio();
		CreateWallTextures();
		PlayMusic("bgm_menu");
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		if (_phase == GamePhase.Playing)
			UpdateGame(Mathf.Min((float)delta, 0.05f));

		QueueRedraw();
	}

	public override void _Input(InputEvent inputEvent)
	{
		if (inputEvent is InputEventKey key && key.Pressed && !key.Echo)
		{
			if (_phase == GamePhase.Playing && key.Keycode == Key.Enter)
			{
				ToggleDebugOverlay();
				return;
			}

			if (_phase == GamePhase.Playing)
			{
				if (key.Keycode is Key.Escape or Key.P)
					SetPhase(GamePhase.Paused);
				if (key.Keycode == Key.E) ActivateShield();
			}
			else
			{
				if (key.Keycode is Key.Up or Key.W) MoveMenu(-1);
				if (key.Keycode is Key.Down or Key.S) MoveMenu(1);
				if (key.Keycode is Key.Enter or Key.Space) ConfirmMenu();
				if (key.Keycode is Key.Escape or Key.Backspace) CancelMenu();
				if (key.Keycode is Key.Left or Key.A) { if (_phase == GamePhase.Settings) AdjustFocusedSetting(-1); }
				if (key.Keycode is Key.Right or Key.D) { if (_phase == GamePhase.Settings) AdjustFocusedSetting(1); }
			}
		}

		if (inputEvent is InputEventJoypadButton joyButton && joyButton.Pressed)
		{
			if (_phase == GamePhase.Playing && joyButton.ButtonIndex == JoyButton.Y)
			{
				ToggleDebugOverlay();
				return;
			}

			if (_phase == GamePhase.Playing)
			{
				if (joyButton.ButtonIndex is JoyButton.Start or JoyButton.Back)
					SetPhase(GamePhase.Paused);
				if (joyButton.ButtonIndex == JoyButton.LeftShoulder) ActivateShield();
			}
			else
			{
				if (joyButton.ButtonIndex == JoyButton.DpadUp) MoveMenu(-1);
				if (joyButton.ButtonIndex == JoyButton.DpadDown) MoveMenu(1);
				if (joyButton.ButtonIndex == JoyButton.DpadLeft) { if (_phase == GamePhase.Settings) AdjustFocusedSetting(-1); }
				if (joyButton.ButtonIndex == JoyButton.DpadRight) { if (_phase == GamePhase.Settings) AdjustFocusedSetting(1); }
				if (joyButton.ButtonIndex is JoyButton.A or JoyButton.X) ConfirmMenu();
				if (joyButton.ButtonIndex is JoyButton.B or JoyButton.Y or JoyButton.Back) CancelMenu();
			}
		}

		if (inputEvent is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
		{
			if (_phase == GamePhase.Playing)
			{
				_mouseShooting = mouseButton.Pressed;
				if (mouseButton.Pressed) HandlePlayingPress(mouseButton.Position, -2);
			}
			else if (_phase == GamePhase.Shop)
			{
				if (mouseButton.Pressed) HandleShopPointerDown(mouseButton.Position, -2);
				else HandleShopPointerUp(mouseButton.Position);
			}
			else if (mouseButton.Pressed)
			{
				HandleUiPress(mouseButton.Position);
			}
			else
			{
				_draggingSlider = UiAction.None;
			}
		}

		if (_phase == GamePhase.Shop && inputEvent is InputEventMouseButton wheel && wheel.Pressed && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
			_shopScroll += wheel.ButtonIndex == MouseButton.WheelUp ? -60f : 60f;

		if (inputEvent is InputEventMouseMotion mouseMotion)
		{
			if (_phase == GamePhase.Playing)
			{
				_player.Angle += mouseMotion.Relative.X * 0.005f * _lookSensitivity;
			}
			else if (_phase == GamePhase.Shop && Input.IsMouseButtonPressed(MouseButton.Left))
			{
				HandleShopPointerMove(mouseMotion.Position);
			}
			else if (_draggingSlider != UiAction.None && Input.IsMouseButtonPressed(MouseButton.Left))
			{
				UiHit? hit = FindUiHit(_draggingSlider);
				if (hit.HasValue) ApplySliderValue(_draggingSlider, hit.Value.Rect, mouseMotion.Position.X);
			}
		}

		if (inputEvent is InputEventScreenTouch screenTouch)
		{
			if (_phase == GamePhase.Shop)
			{
				if (screenTouch.Pressed) HandleShopPointerDown(screenTouch.Position, screenTouch.Index);
				else if (screenTouch.Index == _shopPointerTouch) HandleShopPointerUp(screenTouch.Position);
			}
			else if (_phase != GamePhase.Playing)
			{
				if (screenTouch.Pressed)
				{
					HandleUiPress(screenTouch.Position);
					_draggingSliderTouchIndex = _draggingSlider != UiAction.None ? screenTouch.Index : -1;
				}
				else if (screenTouch.Index == _draggingSliderTouchIndex)
				{
					_draggingSlider = UiAction.None;
					_draggingSliderTouchIndex = -1;
				}
			}
			else if (screenTouch.Pressed)
			{
				HandlePlayingPress(screenTouch.Position, screenTouch.Index);
			}
			else
			{
				if (screenTouch.Index == _joystickTouch)
				{
					_joystickTouch = -1;
					_touchMoveActive = false;
					_touchMove = Vector2.Zero;
				}
				if (screenTouch.Index == _lookTouch) _lookTouch = -1;
				_touchShooting = false;
			}
		}

		if (inputEvent is InputEventScreenDrag screenDrag)
		{
			if (_phase == GamePhase.Playing)
			{
				if (screenDrag.Index == _joystickTouch)
					UpdateTouchJoystick(screenDrag.Position);
				else if (screenDrag.Index == _lookTouch)
				{
					_player.Angle += (screenDrag.Position.X - _lastLookPosition.X) * 0.005f * _lookSensitivity;
					_lastLookPosition = screenDrag.Position;
				}
			}
			else if (_phase == GamePhase.Shop && screenDrag.Index == _shopPointerTouch)
			{
				HandleShopPointerMove(screenDrag.Position);
			}
			else if (screenDrag.Index == _draggingSliderTouchIndex && _draggingSlider != UiAction.None)
			{
				UiHit? hit = FindUiHit(_draggingSlider);
				if (hit.HasValue) ApplySliderValue(_draggingSlider, hit.Value.Rect, screenDrag.Position.X);
			}
		}
	}

	public override void _Draw()
	{
		_uiHits.Clear();
		Vector2 size = GetViewportRect().Size;

		if (_phase == GamePhase.Playing)
		{
			DrawGame(size);
			DrawHud(size);
			DrawCrosshair(size);
			if (_player.ShieldTimeRemaining > 0f)
			{
				float pulse = 0.06f + 0.03f * Mathf.Sin(_frameTick * 0.15f);
				DrawRect(new Rect2(Vector2.Zero, size), new Color(0.25f, 0.7f, 1f, pulse));
				DrawRect(new Rect2(Vector2.Zero, size), new Color(0.6f, 0.9f, 1f, 0.5f), false, 6f);
			}
			if (!HasGamepad() && DisplayServer.IsTouchscreenAvailable()) DrawTouchControls(size);
			if (_debugOverlayEnabled) DrawDebugOverlay(size);
			return;
		}

		DrawMenuBackground(size);
		switch (_phase)
		{
			case GamePhase.MainMenu: DrawMainMenu(size); break;
			case GamePhase.Settings: DrawSettings(size); break;
			case GamePhase.LevelClear: DrawLevelClear(size); break;
			case GamePhase.Shop: DrawShop(size); break;
			case GamePhase.GameOver: DrawGameOver(size); break;
			case GamePhase.Paused: DrawPause(size); break;
			case GamePhase.Finished: DrawVictory(size); break;
			case GamePhase.MultiplayerMenu: DrawMultiplayerMenu(size); break;
		}
	}

	private void UpdateGame(float dt)
	{
		LevelDef level = GameData.Levels[_levelIndex];
		int[][] map = level.Map;
		_frameTick += 1f;

		if (_multiplayerActive)
		{
			_networkSendTimer -= dt;
			if (_networkSendTimer <= 0f)
			{
				_networkSendTimer = 0.1f;
				BroadcastLocalPlayerState();
				if (IsHostAuthority) BroadcastEnemySnapshot();
			}
		}

		for (int i = _particles.Count - 1; i >= 0; i--)
		{
			Particle particle = _particles[i];
			particle.Position += particle.Velocity * dt;
			particle.Velocity *= Mathf.Clamp(1f - 2f * dt, 0f, 1f);
			particle.Life -= dt;
			if (particle.Life <= 0f) _particles.RemoveAt(i);
		}

		if (_player.ScreenShake > 0f) _player.ScreenShake -= dt * 5f;

		if (_player.ShieldTimeRemaining > 0f) _player.ShieldTimeRemaining = Mathf.Max(0f, _player.ShieldTimeRemaining - dt);
		else if (_player.ShieldCooldownRemaining > 0f) _player.ShieldCooldownRemaining = Mathf.Max(0f, _player.ShieldCooldownRemaining - dt);

		Vector2 leftStick = ApplyDeadzone(new(Input.GetJoyAxis(0, JoyAxis.LeftX), Input.GetJoyAxis(0, JoyAxis.LeftY)));
		Vector2 rightStick = ApplyDeadzone(new(Input.GetJoyAxis(0, JoyAxis.RightX), Input.GetJoyAxis(0, JoyAxis.RightY)));
		float keyboardForward = (Input.IsKeyPressed(Key.W) ? 1f : 0f) - (Input.IsKeyPressed(Key.S) ? 1f : 0f);
		float keyboardStrafe = (Input.IsKeyPressed(Key.D) ? 1f : 0f) - (Input.IsKeyPressed(Key.A) ? 1f : 0f);
		float forward = Mathf.Clamp(keyboardForward + _touchMove.Y - leftStick.Y, -1f, 1f);
		float strafe = Mathf.Clamp(keyboardStrafe + _touchMove.X + leftStick.X, -1f, 1f);
		_player.Angle += rightStick.X * 3f * dt * _lookSensitivity;

		Vector2 direction = new(Mathf.Cos(_player.Angle), Mathf.Sin(_player.Angle));
		Vector2 side = new(-direction.Y, direction.X);
		Vector2 movement = (direction * forward + side * strafe) * 2.6f * dt;
		MoveWithWalls(ref _player.Position, movement, PlayerWallCollisionRadius, map);

		for (int i = _items.Count - 1; i >= 0; i--)
		{
			WorldItem item = _items[i];
			if (_player.Position.DistanceSquaredTo(item.Position) >= ItemPickupRadius * ItemPickupRadius) continue;

			// Conserva el comportamiento original: cualquier objeto suma al contador de recogidos.
			_player.ItemsCollected++;
			if (item.Type == ItemType.PotionRed) _player.Health = Mathf.Min(_player.MaxHealth, _player.Health + 40f);
			if (item.Type == ItemType.PotionBlue) _player.Mana = Mathf.Min(_player.MaxMana, _player.Mana + 40f);
			SpawnExplosion(item.Position, item.Color, 15);
			_items.RemoveAt(i);
		}

		if (level.Mission == MissionType.Escape && map[Mathf.FloorToInt(_player.Position.Y)][Mathf.FloorToInt(_player.Position.X)] == 5)
		{
			SetPhase(GamePhase.LevelClear);
			return;
		}

		_player.ShootCooldown -= dt;
		bool shooting = _touchShooting || _mouseShooting || Input.IsKeyPressed(Key.Space) ||
			Input.GetJoyAxis(0, JoyAxis.TriggerRight) > 0.1f || Input.IsJoyButtonPressed(0, JoyButton.RightShoulder);
		if (shooting && _player.ShootCooldown <= 0f && _player.Mana >= _player.WeaponManaCost)
		{
			_player.Mana -= _player.WeaponManaCost;
			_player.ShootCooldown = _player.WeaponCooldown;
			_projectiles.Add(new Projectile(_player.Position, _player.Angle, 7f, _player.WeaponDamage, ProjectileOwner.Player, Colors.Yellow));
			PlaySfx("snd_shoot");
		}

		float manaMultiplier = _difficulty == Difficulty.Hard ? 0.7f : 1f;
		_player.Mana = Mathf.Min(_player.MaxMana, _player.Mana + 12f * dt * manaMultiplier);
		UpdateProjectiles(dt, map);
		if (IsHostAuthority) UpdateEnemies(dt, map);

		if (_player.Health <= 0f)
		{
			_player.Health = 0f;
			SetPhase(GamePhase.GameOver);
			return;
		}

		bool missionCleared = level.Mission switch
		{
			MissionType.Eliminate => _enemies.All(enemy => !enemy.Alive),
			MissionType.Collect => _player.ItemsCollected >= level.TargetCount,
			_ => false
		};
		if (missionCleared) SetPhase(GamePhase.LevelClear);
	}

	private void UpdateProjectiles(float dt, int[][] map)
	{
		for (int p = _projectiles.Count - 1; p >= 0; p--)
		{
			Projectile projectile = _projectiles[p];
			projectile.Position += new Vector2(Mathf.Cos(projectile.Angle), Mathf.Sin(projectile.Angle)) * projectile.Speed * dt;
			bool dead = false;

			if (IsWall(projectile.Position, map))
			{
				dead = true;
				SpawnExplosion(projectile.Position, projectile.Color, 5);
			}
			else if (projectile.Owner == ProjectileOwner.Player)
			{
				foreach (Enemy enemy in _enemies)
				{
					if (!enemy.Alive) continue;
					float hitRadius = EnemyCollisionRadius(enemy);
					if (enemy.Position.DistanceSquaredTo(projectile.Position) >= hitRadius * hitRadius) continue;
					enemy.Hp -= projectile.Damage;
					enemy.HitFlash = 0.15f;
					dead = true;
					SpawnExplosion(projectile.Position, projectile.Color, 8);
					PlaySfx(EnemySound(enemy.Type));
					if (_multiplayerActive && !IsHostAuthority)
						RpcId(1, MethodName.RpcRequestEnemyHit, _enemies.IndexOf(enemy), projectile.Damage);
					if (enemy.Hp <= 0f && !enemy.Rewarded)
					{
						enemy.Rewarded = true;
						GrantReward(enemy);
						SpawnExplosion(enemy.Position, EnemyColor(enemy.Type), 20);
					}
					break;
				}
			}
			else if (_player.Position.DistanceSquaredTo(projectile.Position) < EnemyProjectilePlayerHitRadius * EnemyProjectilePlayerHitRadius)
			{
				DamagePlayer(projectile.Damage * (1f - _player.ArmorDefense), 0.3f);
				dead = true;
				SpawnExplosion(projectile.Position, projectile.Color, 8);
			}

			if (dead) _projectiles.RemoveAt(p);
		}
	}

	private void UpdateEnemies(float dt, int[][] map)
	{
		float damageMultiplier = _difficulty switch { Difficulty.Easy => 0.5f, Difficulty.Hard => 4f, _ => 1f };
		float cooldownMultiplier = _difficulty switch { Difficulty.Easy => 1.6f, Difficulty.Hard => 0.75f, _ => 1f };
		float meleeDamage = (5f + _levelIndex * 2f) * damageMultiplier;
		float rangedDamage = (10f + _levelIndex * 3f) * damageMultiplier;

		foreach (Enemy enemy in _enemies)
		{
			if (!enemy.Alive) continue;
			enemy.HitFlash -= dt;
			enemy.AttackCooldown -= dt;
			enemy.Bob += dt * 3f;
			enemy.AnimationTimer += dt;
			if (enemy.AnimationTimer >= 0.12f)
			{
				enemy.AnimationTimer = 0f;
				enemy.AnimationFrame = (enemy.AnimationFrame + 1) % 5;
			}

			Vector2 toPlayer = _player.Position - enemy.Position;
			float distanceSquared = toPlayer.LengthSquared();
			if (distanceSquared <= 0.00001f) continue;
			float distance = Mathf.Sqrt(distanceSquared);
			Vector2 direction = toPlayer / distance;
			// El enemigo "ve" al jugador exactamente cuando el jugador ve al enemigo: si se
			// dibujó al menos un píxel suyo en pantalla este fotograma (comprobado en DrawWorldSprite,
			// recortado columna a columna contra las paredes), cuenta como dentro de su campo de visión.
			bool canSee = enemy.PlayerVisible;
			if (!canSee && enemy.Type is not EnemyType.Boss and not EnemyType.Sentinel) continue;

			switch (enemy.Type)
			{
				case EnemyType.Melee:
				case EnemyType.Tank:
					float stopDistance = enemy.Type == EnemyType.Tank ? 1.2f : 0.9f;
					if (distance > stopDistance)
						MoveWithWalls(ref enemy.Position, direction * enemy.Speed * dt, EnemyCollisionRadius(enemy), map);
					else if (enemy.AttackCooldown <= 0f)
					{
						float damage = (enemy.Type == EnemyType.Tank ? meleeDamage * 1.5f : meleeDamage) * (1f - _player.ArmorDefense);
						DamagePlayer(damage, enemy.Type == EnemyType.Tank ? 0.4f : 0.3f);
						enemy.AttackCooldown = (enemy.Type == EnemyType.Tank ? 1.5f : 1.1f) * cooldownMultiplier;
					}
					break;

				case EnemyType.Ranged:
					if (distance > 7f) MoveWithWalls(ref enemy.Position, direction * enemy.Speed * 0.6f * dt, EnemyCollisionRadius(enemy), map);
					if (distance < 10f && enemy.AttackCooldown <= 0f)
					{
						_projectiles.Add(new Projectile(enemy.Position, Mathf.Atan2(toPlayer.Y, toPlayer.X), 5f, rangedDamage, ProjectileOwner.Enemy, Colors.Green));
						enemy.AttackCooldown = 1.6f * cooldownMultiplier;
					}
					break;

				case EnemyType.Boss:
					Vector2 bossDirection = direction + new Vector2(Mathf.Sin(_frameTick * 0.05f), Mathf.Cos(_frameTick * 0.05f)) * 0.5f;
					if (distance > 4f) MoveWithWalls(ref enemy.Position, bossDirection * enemy.Speed * dt, EnemyCollisionRadius(enemy), map);
					if (enemy.AttackCooldown <= 0f)
					{
						float angle = Mathf.Atan2(toPlayer.Y, toPlayer.X);
						for (int i = -1; i <= 1; i++)
							_projectiles.Add(new Projectile(enemy.Position, angle + i * 0.2f, 5f, rangedDamage * 1.2f, ProjectileOwner.Enemy, Colors.Red));
						enemy.AttackCooldown = 1.5f * cooldownMultiplier;
					}
					break;

				case EnemyType.Sentinel:
					MoveWithWalls(ref enemy.Position, direction * enemy.Speed * dt, EnemyCollisionRadius(enemy), map);
					if (enemy.AttackCooldown <= 0f)
					{
						float angle = Mathf.Atan2(toPlayer.Y, toPlayer.X);
						for (int i = 0; i < 8; i++)
							_projectiles.Add(new Projectile(enemy.Position, angle + i * Mathf.Pi / 4f, 6f, rangedDamage * 1.5f, ProjectileOwner.Enemy, Color.FromHtml("ff5722")));
						enemy.AttackCooldown = 2f * cooldownMultiplier;
					}
					break;
			}
		}
	}

	private void LoadLevel(int index)
	{
		_levelIndex = index;
		_touchMove = Vector2.Zero;
		_touchMoveActive = false;
		_touchShooting = false;
		_mouseShooting = false;
		LevelDef level = GameData.Levels[index];
		_player.Position = level.Start;
		_player.Angle = 0f;
		_player.MaxHealth = _difficulty == Difficulty.Easy ? 200f : 120f;
		_player.Health = _player.MaxHealth;
		_player.Mana = _player.MaxMana;
		_player.ItemsCollected = 0;
		_enemies.Clear();
		_projectiles.Clear();
		_particles.Clear();
		_items.Clear();

		float hpMultiplier = _difficulty switch { Difficulty.Easy => 0.5f, Difficulty.Hard => 3.5f, _ => 0.8f };
		float speedMultiplier = _difficulty switch { Difficulty.Easy => 0.8f, Difficulty.Hard => 1.45f, _ => 1f };
		foreach (EnemySpawn spawn in level.Spawns)
			_enemies.Add(new Enemy(spawn.Position, spawn.Hp * hpMultiplier, spawn.Type, spawn.Speed * speedMultiplier, spawn.ExpReward, spawn.GoldMin, spawn.GoldMax));

		if (index >= 3)
		{
			for (int i = 0; i < 2; i++)
			{
				_items.Add(new WorldItem(RandomOpenCell(level.Map), ItemType.PotionRed, Colors.Red));
				_items.Add(new WorldItem(RandomOpenCell(level.Map), ItemType.PotionBlue, Colors.Blue));
			}
		}
		if (level.Mission == MissionType.Collect)
			for (int i = 0; i < level.TargetCount; i++) _items.Add(new WorldItem(RandomOpenCell(level.Map), ItemType.Scroll, Colors.Magenta));

		SetPhase(GamePhase.Playing);
	}

	private Vector2 RandomOpenCell(int[][] map)
	{
		for (int attempt = 0; attempt < 128; attempt++)
		{
			Vector2 point = new(1f + NextFloat() * 14f, 1f + NextFloat() * 14f);
			if (!IsWall(point, map)) return point;
		}
		return _player.Position;
	}

	private void GrantReward(Enemy enemy)
	{
		_player.Exp += enemy.ExpReward;
		_player.Gold += _random.Next(enemy.GoldMin, enemy.GoldMax + 1);
		while (_player.Exp >= _player.ExpToNext)
		{
			_player.Exp -= _player.ExpToNext;
			_player.Level++;
			_player.MaxHealth += 20f;
			_player.MaxMana += 10f;
			_player.ExpToNext *= 1.4f;
			_player.Health = _player.MaxHealth;
			_player.Mana = _player.MaxMana;
		}
	}

	// Activa el Escudo Arcano si se posee, no está ya activo y no está en recarga.
	// 7.5s de inmunidad total al daño, seguidos de una recarga antes de poder reactivarlo.
	private void ActivateShield()
	{
		if (_phase != GamePhase.Playing || !_player.OwnsShield) return;
		if (_player.ShieldTimeRemaining > 0f || _player.ShieldCooldownRemaining > 0f) return;
		_player.ShieldTimeRemaining = 7.5f;
		_player.ShieldCooldownRemaining = 15f;
		PlaySfx("snd_shoot");
	}

	private void DamagePlayer(float damage, float shake)
	{
		if (_player.ShieldTimeRemaining > 0f) return;
		_player.Health -= damage;
		_player.ScreenShake = shake;
		PlaySfx("snd_player_hit");
	}

	private bool IsWall(Vector2 position, int[][] map)
	{
		int x = Mathf.FloorToInt(position.X);
		int y = Mathf.FloorToInt(position.Y);
		return y < 0 || y >= map.Length || x < 0 || x >= map[0].Length || map[y][x] != 0;
	}

	private void MoveWithWalls(ref Vector2 position, Vector2 movement, float radius, int[][] map)
	{
		Vector2 xCandidate = new(position.X + movement.X, position.Y);
		if (!CircleTouchesWall(xCandidate, radius, map)) position.X = xCandidate.X;
		Vector2 yCandidate = new(position.X, position.Y + movement.Y);
		if (!CircleTouchesWall(yCandidate, radius, map)) position.Y = yCandidate.Y;
	}

	// Comprueba el círculo completo contra todas las celdas sólidas cercanas. Las
	// pruebas anteriores solo tanteaban un punto en X/Y, por lo que un sprite ancho
	// podía introducir brazos o bordes en una esquina de pared.
	private static bool CircleTouchesWall(Vector2 center, float radius, int[][] map)
	{
		int minX = Mathf.FloorToInt(center.X - radius);
		int maxX = Mathf.FloorToInt(center.X + radius);
		int minY = Mathf.FloorToInt(center.Y - radius);
		int maxY = Mathf.FloorToInt(center.Y + radius);
		float radiusSquared = radius * radius;

		for (int y = minY; y <= maxY; y++)
		{
			for (int x = minX; x <= maxX; x++)
			{
				if (y >= 0 && y < map.Length && x >= 0 && x < map[0].Length && map[y][x] == 0) continue;
				float nearestX = Mathf.Clamp(center.X, x, x + 1f);
				float nearestY = Mathf.Clamp(center.Y, y, y + 1f);
				float offsetX = center.X - nearestX;
				float offsetY = center.Y - nearestY;
				if (offsetX * offsetX + offsetY * offsetY < radiusSquared) return true;
			}
		}

		return false;
	}

	private void SpawnExplosion(Vector2 position, Color color, int count)
	{
		for (int i = 0; i < count; i++)
		{
			float angle = NextFloat() * Mathf.Tau;
			float speed = NextFloat() * 3f + 1f;
			float life = 0.4f + NextFloat() * 0.4f;
			_particles.Add(new Particle(position, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed, life, life, color));
		}
	}

	private void DrawGame(Vector2 size)
	{
		DrawVerticalGradient(new Rect2(Vector2.Zero, new Vector2(size.X, size.Y * 0.5f)), Color.FromHtml("0d0714"), Color.FromHtml("201028"));
		DrawVerticalGradient(new Rect2(0f, size.Y * 0.5f, size.X, size.Y * 0.5f), Color.FromHtml("1a130e"), Color.FromHtml("2b2118"));

		int rayCount = _graphicsQuality switch
		{
			GraphicsQuality.Performance => Mathf.Clamp((int)(size.X * 0.25f), 100, 2000),
			GraphicsQuality.HighDefinition => Mathf.Clamp((int)size.X, 100, 2000),
			_ => Mathf.Clamp((int)(size.X * 0.5f), 100, 2000)
		};
		if (_zBuffer.Length != rayCount) _zBuffer = new float[rayCount];
		if (_wallTopBuffer.Length != rayCount) _wallTopBuffer = new float[rayCount];

		int[][] map = GameData.Levels[_levelIndex].Map;
		float screenDistance = (size.X * 0.5f) / Mathf.Tan(FieldOfView * 0.5f);
		Vector2 direction = new(Mathf.Cos(_player.Angle), Mathf.Sin(_player.Angle));
		Vector2 plane = new Vector2(-Mathf.Sin(_player.Angle), Mathf.Cos(_player.Angle)) * Mathf.Tan(FieldOfView * 0.5f);
		Vector2 shake = _player.ScreenShake > 0f ? new Vector2((NextFloat() - 0.5f) * _player.ScreenShake * 100f, (NextFloat() - 0.5f) * _player.ScreenShake * 100f) : Vector2.Zero;
		float columnWidth = size.X / rayCount;

		for (int i = 0; i < rayCount; i++)
		{
			float cameraX = 2f * i / rayCount - 1f;
			Vector2 rayDirection = direction + plane * cameraX;
			int mapX = Mathf.FloorToInt(_player.Position.X);
			int mapY = Mathf.FloorToInt(_player.Position.Y);
			float deltaX = Mathf.Abs(rayDirection.X) < 0.00001f ? float.MaxValue : Mathf.Abs(1f / rayDirection.X);
			float deltaY = Mathf.Abs(rayDirection.Y) < 0.00001f ? float.MaxValue : Mathf.Abs(1f / rayDirection.Y);
			int stepX = rayDirection.X < 0f ? -1 : 1;
			int stepY = rayDirection.Y < 0f ? -1 : 1;
			float sideX = rayDirection.X < 0f ? (_player.Position.X - mapX) * deltaX : (mapX + 1f - _player.Position.X) * deltaX;
			float sideY = rayDirection.Y < 0f ? (_player.Position.Y - mapY) * deltaY : (mapY + 1f - _player.Position.Y) * deltaY;
			int side = 0;

			for (int step = 0; step < 40; step++)
			{
				if (sideX < sideY) { sideX += deltaX; mapX += stepX; side = 0; }
				else { sideY += deltaY; mapY += stepY; side = 1; }
				if (mapY < 0 || mapY >= map.Length || mapX < 0 || mapX >= map[0].Length || map[mapY][mapX] > 0) break;
			}

			float wallDistance = Mathf.Max(0.001f, side == 0 ? sideX - deltaX : sideY - deltaY);
			_zBuffer[i] = wallDistance;
			float wallX = side == 0 ? _player.Position.Y + wallDistance * rayDirection.Y : _player.Position.X + wallDistance * rayDirection.X;
			wallX -= Mathf.Floor(wallX);
			float lineHeight = screenDistance / wallDistance;
			int code = mapY >= 0 && mapY < map.Length && mapX >= 0 && mapX < map[0].Length ? map[mapY][mapX] : 1;
			float wallTop = (size.Y - lineHeight) * 0.5f;
			_wallTopBuffer[i] = wallTop;
			Rect2 destination = new(i * columnWidth + shake.X, wallTop + shake.Y, columnWidth + 1f, lineHeight);

			if (_wallTextures.TryGetValue(code, out Texture2D? texture))
				DrawTextureRectRegion(texture, destination, new Rect2(wallX * 255f, 0f, 1f, 256f));
			else
				DrawRect(destination, WallColor(code));

			float brightness = Mathf.Clamp(1.1f - wallDistance * 0.05f, 0.2f, 1f) * (side == 1 ? 0.75f : 1f);
			DrawRect(destination, new Color(0f, 0f, 0f, 1f - brightness));
		}

		List<WorldSprite> sprites = new();
		foreach (Enemy enemy in _enemies)
		{
			if (!enemy.Alive) continue;
			enemy.PlayerVisible = false;
			sprites.Add(new WorldSprite(enemy.Position, SpriteKind.Enemy, enemy, EnemyColor(enemy.Type), EnemyScale(enemy.Type)));
		}
		foreach (Projectile projectile in _projectiles)
			sprites.Add(new WorldSprite(projectile.Position, SpriteKind.Projectile, projectile, projectile.Color, 0.25f));
		foreach (WorldItem item in _items)
			sprites.Add(new WorldSprite(item.Position, SpriteKind.Item, item, item.Color, 0.4f));
		if (_multiplayerActive)
			foreach (RemotePlayerState remote in _remotePlayers.Values)
				sprites.Add(new WorldSprite(remote.Position, SpriteKind.RemotePlayer, remote, Color.FromHtml("29b6f6"), 1.1f));
		sprites.Sort((left, right) => right.Position.DistanceSquaredTo(_player.Position).CompareTo(left.Position.DistanceSquaredTo(_player.Position)));

		foreach (WorldSprite sprite in sprites)
			DrawWorldSprite(sprite, size, screenDistance, direction, plane, shake);

		DrawWeapon(size, shake);
		foreach (Particle particle in _particles)
			DrawParticle(particle, size, screenDistance, direction, plane, shake);

		// Viñeta suave para conservar el contraste oscuro del original.
		float edge = size.X * 0.025f;
		DrawRect(new Rect2(0, 0, edge, size.Y), new Color(0, 0, 0, 0.32f));
		DrawRect(new Rect2(size.X - edge, 0, edge, size.Y), new Color(0, 0, 0, 0.32f));
	}

	private void DrawWorldSprite(WorldSprite sprite, Vector2 size, float screenDistance, Vector2 direction, Vector2 plane, Vector2 shake)
	{
		Vector2 delta = sprite.Position - _player.Position;
		float inverseDeterminant = 1f / (plane.X * direction.Y - direction.X * plane.Y);
		float transformX = inverseDeterminant * (direction.Y * delta.X - direction.X * delta.Y);
		float transformY = inverseDeterminant * (-plane.Y * delta.X + plane.X * delta.Y);
		if (transformY <= 0.1f) return;

		float screenX = size.X * 0.5f * (1f + transformX / transformY);

		// Projectile/Item se quedan con la comprobación de un único punto (barato y suficiente
		// para sprites pequeños); Enemy se recorta columna a columna más abajo.
		if (sprite.Kind != SpriteKind.Enemy)
		{
			int rayIndex = Mathf.Clamp((int)(screenX / size.X * _zBuffer.Length), 0, _zBuffer.Length - 1);
			if (transformY >= _zBuffer[rayIndex]) return;
		}

		float spriteHeight = Mathf.Abs(screenDistance / transformY) * sprite.Scale;
		float wallLineHeight = screenDistance / transformY;
		float bottom = size.Y * 0.5f + wallLineHeight * 0.5f;

		if (sprite.Kind == SpriteKind.Projectile)
		{
			DrawGlow(sprite.Color, new Vector2(screenX + shake.X, size.Y * 0.5f + shake.Y), spriteHeight);
			return;
		}

		if (sprite.Kind == SpriteKind.Item)
		{
			WorldItem item = (WorldItem)sprite.Source;
			Texture2D? texture = item.Type == ItemType.PotionRed ? _redPotion : _bluePotion;
			if (texture != null)
			{
				float itemAspect = (float)texture.GetWidth() / texture.GetHeight();
				float itemWidth = spriteHeight * itemAspect;
				Rect2 destination = new(screenX - itemWidth * 0.5f + shake.X, bottom - spriteHeight + shake.Y, itemWidth, spriteHeight);
				DrawTextureRect(texture, destination, false);
			}
			else
			{
				DrawCircle(new Vector2(screenX + shake.X, bottom - spriteHeight * 0.5f + shake.Y), spriteHeight * 0.3f, sprite.Color);
			}
			return;
		}

		if (sprite.Kind == SpriteKind.RemotePlayer)
		{
			RemotePlayerState remote = (RemotePlayerState)sprite.Source;
			Vector2 center = new(screenX + shake.X, bottom - spriteHeight * 0.5f + shake.Y);
			DrawCircle(center, spriteHeight * 0.28f, sprite.Color);
			DrawCircle(center, spriteHeight * 0.28f, Colors.White, false, 2f);
			float barWidth = spriteHeight * 0.6f;
			Vector2 barPos = center + new Vector2(-barWidth * 0.5f, -spriteHeight * 0.62f);
			DrawRect(new Rect2(barPos, new Vector2(barWidth, 4f)), new Color(0f, 0f, 0f, 0.6f));
			float healthT = remote.MaxHealth > 0f ? Mathf.Clamp(remote.Health / remote.MaxHealth, 0f, 1f) : 0f;
			DrawRect(new Rect2(barPos, new Vector2(barWidth * healthT, 4f)), Color.FromHtml("ef5350"));
			DrawText(remote.Name, new Vector2(center.X - barWidth * 0.6f, barPos.Y - 4f), 12, Colors.White);
			return;
		}

		Enemy enemy = (Enemy)sprite.Source;
		Texture2D? enemyTexture = EnemyTexture(enemy.Type);
		if (enemyTexture == null)
		{
			int rayIndex = Mathf.Clamp((int)(screenX / size.X * _zBuffer.Length), 0, _zBuffer.Length - 1);
			if (transformY >= _zBuffer[rayIndex]) return;
			enemy.PlayerVisible = true;
			DrawCircle(new Vector2(screenX + shake.X, bottom - spriteHeight * 0.5f + shake.Y), spriteHeight * 0.3f, sprite.Color);
			DrawEnemyHealthBar(enemy, screenX, bottom, spriteHeight, shake);
			return;
		}

		float frameWidth = enemyTexture.GetWidth() / 5f;
		float aspect = frameWidth / enemyTexture.GetHeight();
		float spriteWidth = spriteHeight * aspect;
		Rect2 destinationEnemy = new(screenX - spriteWidth * 0.5f + shake.X, bottom - spriteHeight + shake.Y, spriteWidth, spriteHeight);
		Rect2 source = new(enemy.AnimationFrame * frameWidth + 1f, 0f, frameWidth - 2f, enemyTexture.GetHeight());

		bool anyVisible = DrawTextureColumnsClipped(enemyTexture, destinationEnemy, source, size, transformY);
		enemy.PlayerVisible = anyVisible;
		if (anyVisible)
		{
			if (enemy.HitFlash > 0f) DrawRect(destinationEnemy, new Color(1f, 1f, 1f, 0.5f));
			DrawEnemyHealthBar(enemy, screenX, bottom, spriteHeight, shake);
		}
	}

	/// <summary>
	/// Barra de vida flotante sobre un enemigo, con el mismo estilo que la de los
	/// jugadores remotos: fondo semitransparente y relleno rojo proporcional a Hp/MaxHp.
	/// </summary>
	private void DrawEnemyHealthBar(Enemy enemy, float screenX, float bottom, float spriteHeight, Vector2 shake)
	{
		if (enemy.MaxHp <= 0f) return;
		float barWidth = spriteHeight * 0.5f;
		Vector2 center = new(screenX + shake.X, bottom - spriteHeight * 0.5f + shake.Y);
		Vector2 barPos = center + new Vector2(-barWidth * 0.5f, -spriteHeight * 0.62f);
		DrawRect(new Rect2(barPos, new Vector2(barWidth, 4f)), new Color(0f, 0f, 0f, 0.6f));
		float healthT = Mathf.Clamp(enemy.Hp / enemy.MaxHp, 0f, 1f);
		DrawRect(new Rect2(barPos, new Vector2(barWidth * healthT, 4f)), Color.FromHtml("ef5350"));
	}

	/// <summary>
	/// Dibuja una textura recortando cada columna de pantalla contra el zBuffer de las paredes,
	/// en vez de comprobar un único punto central. Así un enemigo que asoma por una esquina se
	/// revela progresivamente en vez de aparecer/desaparecer de golpe. También tiene en cuenta
	/// que las paredes solo ocupan una franja vertical limitada de la columna (dejando cielo/suelo
	/// visibles arriba/abajo): si el sprite cae fuera de esa franja no se considera tapado aunque
	/// esté más lejos que la pared. Agrupa columnas visibles consecutivas en tramos para no emitir
	/// una llamada de dibujo por cada píxel de ancho.
	/// </summary>
	private bool DrawTextureColumnsClipped(Texture2D texture, Rect2 destination, Rect2 source, Vector2 size, float depth)
	{
		if (destination.Size.X <= 0f || _zBuffer.Length == 0) return false;
		int screenStart = Mathf.Max(0, Mathf.FloorToInt(destination.Position.X));
		int screenEnd = Mathf.Min(Mathf.CeilToInt(size.X) - 1, Mathf.CeilToInt(destination.Position.X + destination.Size.X) - 1);
		if (screenEnd < screenStart) return false;

		int rayCount = _zBuffer.Length;
		bool drewAny = false;
		int runStart = -1;

		for (int column = screenStart; column <= screenEnd + 1; column++)
		{
			bool visible = false;
			if (column <= screenEnd)
			{
				int rayIndex = Mathf.Clamp((int)(column / size.X * rayCount), 0, rayCount - 1);
				if (depth < _zBuffer[rayIndex])
				{
					visible = true;
				}
				else
				{
					// Más lejos que la pared en esta columna: solo cuenta como "tapado" si la
					// franja que realmente se dibujó ahí (ni cielo ni suelo) se solapa con el
					// sprite. Por encima/debajo de esa franja no hay pared real dibujada.
					float wallTop = _wallTopBuffer[rayIndex];
					float wallBottom = size.Y - wallTop;
					visible = destination.Position.Y + destination.Size.Y <= wallTop || destination.Position.Y >= wallBottom;
				}
			}

			if (visible)
			{
				if (runStart < 0) runStart = column;
			}
			else if (runStart >= 0)
			{
				float u0 = (runStart - destination.Position.X) / destination.Size.X;
				float u1 = (column - destination.Position.X) / destination.Size.X;
				Rect2 runSource = new(source.Position.X + u0 * source.Size.X, source.Position.Y, (u1 - u0) * source.Size.X, source.Size.Y);
				Rect2 runDestination = new(runStart, destination.Position.Y, column - runStart, destination.Size.Y);
				DrawTextureRectRegion(texture, runDestination, runSource);
				drewAny = true;
				runStart = -1;
			}
		}

		return drewAny;
	}

	private void DrawParticle(Particle particle, Vector2 size, float screenDistance, Vector2 direction, Vector2 plane, Vector2 shake)
	{
		Vector2 delta = particle.Position - _player.Position;
		float inverseDeterminant = 1f / (plane.X * direction.Y - direction.X * plane.Y);
		float transformY = inverseDeterminant * (-plane.Y * delta.X + plane.X * delta.Y);
		if (transformY <= 0.1f) return;
		float transformX = inverseDeterminant * (direction.Y * delta.X - direction.X * delta.Y);
		float screenX = size.X * 0.5f * (1f + transformX / transformY);
		int rayIndex = Mathf.Clamp((int)(screenX / size.X * _zBuffer.Length), 0, _zBuffer.Length - 1);
		if (transformY >= _zBuffer[rayIndex] + 0.1f) return;
		float life = Mathf.Clamp(particle.Life / particle.MaxLife, 0f, 1f);
		float radius = Mathf.Abs(screenDistance / transformY) * 0.06f * life + 1f;
		DrawGlow(particle.Color, new Vector2(screenX + shake.X, size.Y * 0.5f + shake.Y), radius * 2f, life);
	}

	private void DrawWeapon(Vector2 size, Vector2 shake)
	{
		if (_staff == null) return;
		float height = size.Y * 0.85f;
		float width = height * _staff.GetWidth() / _staff.GetHeight();
		float bobX = Mathf.Sin(_frameTick * 0.05f) * 20f;
		float bobY = Mathf.Abs(Mathf.Cos(_frameTick * 0.05f)) * 15f;
		DrawTextureRect(_staff, new Rect2(size.X * 0.85f - width * 0.5f + bobX + shake.X, size.Y - height + bobY + shake.Y, width, height), false);
	}

	private void DrawGlow(Color color, Vector2 center, float diameter, float alpha = 1f)
	{
		float radius = Mathf.Max(1f, diameter * 0.5f);
		DrawCircle(center, radius, Alpha(color, 0.15f * alpha));
		DrawCircle(center, radius * 0.6f, Alpha(color, 0.35f * alpha));
		DrawCircle(center, radius * 0.35f, Alpha(color, alpha));
	}

	private void DrawHud(Vector2 size)
	{
		DrawRect(new Rect2(14, 14, 330, 54), new Color(0f, 0f, 0f, 0.48f));
		DrawText(GameData.Levels[_levelIndex].Name, new Vector2(26, 36), 16, Colors.White);
		DrawText($"Oro: {_player.Gold}   Nv.{_player.Level}", new Vector2(26, 58), 15, Colors.Gold);

		float barX = 356f;
		DrawHudBar("EXP", _player.Exp / _player.ExpToNext, Colors.Gold, new Vector2(barX, 24));
		DrawHudBar("VIDA", _player.Health / _player.MaxHealth, Color.FromHtml("ef5350"), new Vector2(barX, 46));
		DrawHudBar("MANA", _player.Mana / _player.MaxMana, Color.FromHtml("42a5f5"), new Vector2(barX, 68));

		if (_player.OwnsShield)
		{
			string shieldText = _player.ShieldTimeRemaining > 0f ? $"ESCUDO ACTIVO ({_player.ShieldTimeRemaining:0.0}s)"
				: _player.ShieldCooldownRemaining > 0f ? $"Escudo en recarga ({_player.ShieldCooldownRemaining:0}s)"
				: "Escudo listo (E)";
			Color shieldColor = _player.ShieldTimeRemaining > 0f ? Color.FromHtml("64c8ff")
				: _player.ShieldCooldownRemaining > 0f ? new Color(1f, 1f, 1f, 0.5f)
				: Color.FromHtml("bfe6ff");
			DrawText(shieldText, new Vector2(barX, 90), 14, shieldColor);
		}

		DrawMiniMap(new Rect2(16, 82, 128, 128));
		if (GameData.Levels[_levelIndex].Mission == MissionType.Collect)
			DrawText($"Pergaminos: {_player.ItemsCollected}/{GameData.Levels[_levelIndex].TargetCount}", new Vector2(16, 228), 14, Colors.Violet);
	}

	private void DrawHudBar(string label, float value, Color color, Vector2 position)
	{
		const float width = 190f;
		DrawText(label, position + new Vector2(0, 13), 13, Colors.White);
		Rect2 frame = new(position.X + 44f, position.Y, width, 16f);
		DrawRect(frame, new Color(0f, 0f, 0f, 0.62f));
		DrawRect(new Rect2(frame.Position + new Vector2(2, 2), new Vector2((width - 4f) * Mathf.Clamp(value, 0f, 1f), 12f)), color);
	}

	private void DrawMiniMap(Rect2 rect)
	{
		int[][] map = GameData.Levels[_levelIndex].Map;
		DrawRect(rect, new Color(0f, 0f, 0f, 0.66f));
		Vector2 cell = new(rect.Size.X / map[0].Length, rect.Size.Y / map.Length);
		for (int y = 0; y < map.Length; y++)
			for (int x = 0; x < map[y].Length; x++)
				if (map[y][x] != 0) DrawRect(new Rect2(rect.Position + new Vector2(x * cell.X, y * cell.Y), cell), Colors.Gray);

		foreach (Enemy enemy in _enemies)
			if (enemy.Alive) DrawCircle(rect.Position + new Vector2(enemy.Position.X * cell.X, enemy.Position.Y * cell.Y), 2f, Colors.Red);
		foreach (WorldItem item in _items)
			DrawCircle(rect.Position + new Vector2(item.Position.X * cell.X, item.Position.Y * cell.Y), 1.5f, item.Color);
		if (_multiplayerActive)
			foreach (RemotePlayerState remote in _remotePlayers.Values)
				DrawCircle(rect.Position + new Vector2(remote.Position.X * cell.X, remote.Position.Y * cell.Y), 2.4f, Color.FromHtml("29b6f6"));

		Vector2 player = rect.Position + new Vector2(_player.Position.X * cell.X, _player.Position.Y * cell.Y);
		Vector2 look = new(Mathf.Cos(_player.Angle), Mathf.Sin(_player.Angle));
		Vector2[] triangle = new Vector2[]
		{
			player + look * 6f,
			player + look.Rotated(2.4f) * 5f,
			player + look.Rotated(-2.4f) * 5f
		};
		DrawColoredPolygon(triangle, Colors.Cyan);
	}

	// Superposición de depuración: representa el mismo espacio de coordenadas que
	// usan las comprobaciones de colisión, para que los radios no sean estimaciones
	// visuales de los sprites sino los valores que utiliza realmente la simulación.
	private void DrawDebugOverlay(Vector2 size)
	{
		int[][] map = GameData.Levels[_levelIndex].Map;
		const float margin = 14f;
		const float padding = 8f;
		const float headerHeight = 24f;
		const float footerHeight = 18f;
		float panelSize = Mathf.Clamp(Mathf.Min(size.X * 0.34f, size.Y * 0.62f), 240f, 420f);
		Rect2 panel = new(size.X - panelSize - margin, margin, panelSize, panelSize);
		DrawRect(panel, new Color(0.015f, 0.025f, 0.05f, 0.92f));
		DrawRect(panel, Color.FromHtml("22d3ee"), false, 2f);
		DrawText("DEBUG · ENTER / Y para ocultar", panel.Position + new Vector2(10f, 17f), 13, Color.FromHtml("bff7ff"));

		float mapSize = Mathf.Min(panel.Size.X - padding * 2f, panel.Size.Y - headerHeight - footerHeight - padding * 3f);
		Rect2 mapRect = new(panel.Position.X + (panel.Size.X - mapSize) * 0.5f, panel.Position.Y + headerHeight + padding, mapSize, mapSize);
		DrawRect(mapRect, new Color(0.02f, 0.04f, 0.08f, 1f));
		Vector2 cellSize = new(mapRect.Size.X / map[0].Length, mapRect.Size.Y / map.Length);
		Color wallFill = new(1f, 0.34f, 0.08f, 0.30f);
		Color wallOutline = Color.FromHtml("ff8a3d");

		for (int y = 0; y < map.Length; y++)
		{
			for (int x = 0; x < map[y].Length; x++)
			{
				if (map[y][x] == 0) continue;
				Rect2 collisionCell = new(mapRect.Position + new Vector2(x * cellSize.X, y * cellSize.Y), cellSize);
				DrawRect(collisionCell, wallFill);
				DrawRect(collisionCell, wallOutline, false, 1f);
			}
		}

		float pixelsPerUnit = Mathf.Min(cellSize.X, cellSize.Y);
		Color movementColor = Color.FromHtml("22d3ee");
		Color hitColor = Color.FromHtml("f472ff");
		Color pickupColor = Color.FromHtml("86efac");

		foreach (WorldItem item in _items)
		{
			Vector2 point = DebugMapPoint(mapRect, cellSize, item.Position);
			DrawDebugRadius(point, ItemPickupRadius * pixelsPerUnit, pickupColor);
			DrawCircle(point, Mathf.Max(2.5f, pixelsPerUnit * 0.14f), item.Color);
		}

		foreach (Projectile projectile in _projectiles)
		{
			Vector2 point = DebugMapPoint(mapRect, cellSize, projectile.Position);
			Vector2 direction = new(Mathf.Cos(projectile.Angle), Mathf.Sin(projectile.Angle));
			DrawLine(point, point + direction * pixelsPerUnit * 0.75f, projectile.Color, 1.5f);
			DrawCircle(point, Mathf.Max(2f, pixelsPerUnit * 0.1f), projectile.Color);
		}

		foreach (RemotePlayerState remote in _remotePlayers.Values)
		{
			Vector2 point = DebugMapPoint(mapRect, cellSize, remote.Position);
			DrawDebugRadius(point, PlayerWallCollisionRadius * pixelsPerUnit, Color.FromHtml("60a5fa"));
			DrawCircle(point, Mathf.Max(3f, pixelsPerUnit * 0.16f), Color.FromHtml("60a5fa"));
		}

		foreach (Enemy enemy in _enemies)
		{
			if (!enemy.Alive) continue;
			Vector2 point = DebugMapPoint(mapRect, cellSize, enemy.Position);
			Color enemyColor = EnemyColor(enemy.Type);
			float collisionRadius = EnemyCollisionRadius(enemy);
			DrawDebugRadius(point, collisionRadius * pixelsPerUnit, hitColor);
			DrawCircle(point, collisionRadius * pixelsPerUnit, wallOutline, false, 0.75f);
			DrawCircle(point, Mathf.Max(3f, pixelsPerUnit * 0.16f), enemyColor);
			DrawText($"{DebugEnemyLabel(enemy.Type)} {enemy.Hp:0} h:{collisionRadius:0.00}", point + new Vector2(4f, -4f), 11, enemyColor);
		}

		Vector2 player = DebugMapPoint(mapRect, cellSize, _player.Position);
		DrawDebugRadius(player, EnemyProjectilePlayerHitRadius * pixelsPerUnit, hitColor);
		DrawDebugRadius(player, PlayerWallCollisionRadius * pixelsPerUnit, movementColor);
		Vector2 look = new(Mathf.Cos(_player.Angle), Mathf.Sin(_player.Angle));
		float fovLength = pixelsPerUnit * 3.25f;
		DrawLine(player, player + look.Rotated(-FieldOfView * 0.5f) * fovLength, movementColor, 1.5f);
		DrawLine(player, player + look.Rotated(FieldOfView * 0.5f) * fovLength, movementColor, 1.5f);
		DrawColoredPolygon(new[] { player + look * 5f, player + look.Rotated(2.4f) * 4f, player + look.Rotated(-2.4f) * 4f }, movementColor);

		DrawRect(mapRect, Color.FromHtml("c4f1ff"), false, 1.5f);
		DrawText("Cian: jugador · naranja: muro · violeta: impacto · verde: recoger", new Vector2(panel.Position.X + 10f, panel.End.Y - 6f), 10, new Color(1f, 1f, 1f, 0.82f));
	}

	private void ToggleDebugOverlay()
	{
		_debugOverlayEnabled = !_debugOverlayEnabled;
		QueueRedraw();
	}

	private static Vector2 DebugMapPoint(Rect2 mapRect, Vector2 cellSize, Vector2 worldPosition) => mapRect.Position + new Vector2(worldPosition.X * cellSize.X, worldPosition.Y * cellSize.Y);

	private void DrawDebugRadius(Vector2 center, float radius, Color color)
	{
		DrawCircle(center, radius, Alpha(color, 0.10f));
		DrawCircle(center, radius, color, false, 1.25f);
	}

	private static string DebugEnemyLabel(EnemyType type) => type switch
	{
		EnemyType.Melee => "M",
		EnemyType.Ranged => "R",
		EnemyType.Tank => "T",
		EnemyType.Boss => "B",
		_ => "S"
	};

	private void DrawCrosshair(Vector2 size)
	{
		Vector2 center = size * 0.5f;
		Color color = new(1f, 1f, 1f, 0.6f);
		DrawLine(center + new Vector2(-12, 0), center + new Vector2(12, 0), color, 2f);
		DrawLine(center + new Vector2(0, -12), center + new Vector2(0, 12), color, 2f);
		DrawCircle(center, 2f, color);
	}

	private void DrawTouchControls(Vector2 size)
	{
		TouchLayout layout = new(size);
		DrawCircle(layout.JoystickCenter, layout.JoystickRadius, new Color(1f, 1f, 1f, 0.10f));
		Vector2 knobOffset = new Vector2(_touchMove.X, -_touchMove.Y) * (layout.JoystickRadius - layout.JoystickKnobRadius);
		DrawCircle(layout.JoystickCenter + knobOffset, layout.JoystickKnobRadius, new Color(0f, 0.9f, 1f, 0.48f));

		DrawCircle(layout.ShootCenter, layout.ShootRadius, new Color(0.49f, 0.3f, 1f, 0.42f));
		int shootFontSize = Mathf.RoundToInt(13 * layout.UiScale);
		DrawText("DISPARAR", new Vector2(layout.ShootCenter.X - 40f * layout.UiScale, layout.ShootCenter.Y + 5f * layout.UiScale), shootFontSize, Colors.White);

		if (_player.OwnsShield)
		{
			Color shieldFill = _player.ShieldTimeRemaining > 0f ? new Color(0.25f, 0.7f, 1f, 0.55f)
				: _player.ShieldCooldownRemaining > 0f ? new Color(0.4f, 0.4f, 0.4f, 0.35f)
				: new Color(0.25f, 0.7f, 1f, 0.30f);
			DrawCircle(layout.ShieldCenter, layout.ShieldRadius, shieldFill);
			DrawCircle(layout.ShieldCenter, layout.ShieldRadius, new Color(1f, 1f, 1f, 0.5f), false, 2f);
			int shieldFontSize = Mathf.RoundToInt(11 * layout.UiScale);
			string shieldLabel = _player.ShieldTimeRemaining > 0f ? $"{_player.ShieldTimeRemaining:0.0}s"
				: _player.ShieldCooldownRemaining > 0f ? $"{_player.ShieldCooldownRemaining:0}s"
				: "ESCUDO";
			DrawText(shieldLabel, new Vector2(layout.ShieldCenter.X - 24f * layout.UiScale, layout.ShieldCenter.Y + 4f * layout.UiScale), shieldFontSize, Colors.White);
		}

		DrawCircle(layout.PauseCenter, layout.PauseRadius, new Color(0f, 0f, 0f, 0.48f));
		int pauseFontSize = Mathf.RoundToInt(18 * layout.UiScale);
		DrawText("II", new Vector2(layout.PauseCenter.X - 7f * layout.UiScale, layout.PauseCenter.Y + 7f * layout.UiScale), pauseFontSize, Colors.White);
	}

	private void DrawMenuBackground(Vector2 size)
	{
		DrawVerticalGradient(new Rect2(Vector2.Zero, size), Color.FromHtml("0d0714"), Color.FromHtml("25112f"));
		for (int x = 0; x < 10; x++)
		{
			float alpha = 0.015f + (x % 3) * 0.008f;
			DrawRect(new Rect2(x * size.X / 10f, 0, 1f, size.Y), new Color(0.5f, 0.3f, 1f, alpha));
		}
	}

	private void DrawMainMenu(Vector2 size)
	{
		if (_logo != null)
		{
			float width = Mathf.Min(size.X * 0.56f, 670f);
			float height = width * _logo.GetHeight() / _logo.GetWidth();
			DrawTextureRect(_logo, new Rect2(size.X * 0.5f - width * 0.5f, 62f, width, height), false);
		}
		else DrawCenteredText("LORD MAGICIAN", size.Y * 0.28f, 48, Colors.Gold);

		float y = size.Y * 0.58f;
		DrawButton(new Rect2(size.X * 0.5f - 145f, y, 290f, 54f), "COMENZAR", UiAction.Start, _menuIndex == 0);
		DrawButton(new Rect2(size.X * 0.5f - 145f, y + 70f, 290f, 54f), "MULTIJUGADOR", UiAction.OpenMultiplayer, _menuIndex == 1, false);
		DrawButton(new Rect2(size.X * 0.5f - 145f, y + 140f, 290f, 54f), "AJUSTES", UiAction.OpenSettings, _menuIndex == 2);
		DrawCenteredText("WASD/mando para moverte · Ratón/táctil para mirar · Espacio/R2 para disparar", size.Y - 32f, 14, new Color(1f, 1f, 1f, 0.65f));
	}

	private void DrawMultiplayerMenu(Vector2 size)
	{
		DrawCenteredText("MULTIJUGADOR", 90f, 34, Colors.Gold);
		DrawCenteredText("Cooperativo — hasta 8 jugadores en la misma partida", 126f, 14, new Color(1f, 1f, 1f, 0.65f));
		if (!string.IsNullOrEmpty(_networkStatus)) DrawCenteredText(_networkStatus, 152f, 15, Color.FromHtml("f2c94c"));

		float x = size.X * 0.5f - 170f;
		DrawButton(new Rect2(x, 190f, 340f, 52f), "ALOJAR PARTIDA", UiAction.MpHost, _menuIndex == 0, !_multiplayerActive);

		DrawText("IP a la que unirse:", new Vector2(x, 268f), 15, new Color(1f, 1f, 1f, 0.75f));
		Rect2 ipField = new(x, 278f, 340f, 46f);
		DrawRect(ipField, new Color(1f, 1f, 1f, 0.08f));
		DrawRect(ipField, _joinFieldActive ? Colors.White : new Color(1f, 1f, 1f, 0.3f), false, 2f);
		DrawString(_font, new Vector2(ipField.Position.X + 14f, ipField.Position.Y + 30f), _joinIpText + (_joinFieldActive ? "_" : ""), HorizontalAlignment.Left, ipField.Size.X - 28f, 18, Colors.White);
		_uiHits.Add(new UiHit(UiAction.MpFocusIpField, ipField));

		DrawButton(new Rect2(x, 340f, 340f, 52f), "UNIRSE", UiAction.MpJoin, _menuIndex == 1, !_multiplayerActive);
		DrawButton(new Rect2(x, 408f, 340f, 48f), _multiplayerActive ? "DESCONECTAR Y VOLVER" : "VOLVER", UiAction.MpBack, _menuIndex == 2);

		if (_multiplayerActive)
		{
			DrawCenteredText($"Jugadores conectados: {_remotePlayers.Count + 1}", 478f, 15, Colors.White);
			if (Multiplayer.HasMultiplayerPeer() && Multiplayer.IsServer())
				DrawCenteredText("Cuando estéis todos, pulsa COMENZAR en el menú principal de cualquier jugador para que el anfitrión inicie la partida.", 502f, 13, new Color(1f, 1f, 1f, 0.6f));
		}
	}

	private void DrawSettings(Vector2 size)
	{
		DrawCenteredText("AJUSTES", 70f, 38, Colors.White);

		const float panelWidth = 640f;
		float left = size.X * 0.5f - panelWidth * 0.5f;
		const float gap = 12f;
		float optionWidth = (panelWidth - gap * 2f) / 3f;
		float y = 118f;

		DrawCenteredText("DIFICULTAD", y, 17, Color.FromHtml("22d3ee"));
		y += 22f;
		Rect2 difficultyRow = new(left, y, panelWidth, 54f);
		DrawSettingsOption(new Rect2(left, y, optionWidth, 54f), "EASY", UiAction.SetDifficultyEasy, _difficulty == Difficulty.Easy);
		DrawSettingsOption(new Rect2(left + optionWidth + gap, y, optionWidth, 54f), "NORMAL", UiAction.SetDifficultyNormal, _difficulty == Difficulty.Normal);
		DrawSettingsOption(new Rect2(left + (optionWidth + gap) * 2f, y, optionWidth, 54f), "HARD", UiAction.SetDifficultyHard, _difficulty == Difficulty.Hard);
		if (_menuIndex == 0) DrawPillOutline(difficultyRow.Grow(6f), Color.FromHtml("22d3ee"), 2f);
		y += 54f + 34f;

		DrawCenteredText("GRÁFICOS", y, 17, Color.FromHtml("f2c94c"));
		y += 22f;
		Rect2 qualityRow = new(left, y, panelWidth, 54f);
		DrawSettingsOption(new Rect2(left, y, optionWidth, 54f), "Rendimiento", UiAction.SetQualityPerformance, _graphicsQuality == GraphicsQuality.Performance);
		DrawSettingsOption(new Rect2(left + optionWidth + gap, y, optionWidth, 54f), "Estándar", UiAction.SetQualityStandard, _graphicsQuality == GraphicsQuality.Standard);
		DrawSettingsOption(new Rect2(left + (optionWidth + gap) * 2f, y, optionWidth, 54f), "Alta Definición", UiAction.SetQualityHighDef, _graphicsQuality == GraphicsQuality.HighDefinition);
		if (_menuIndex == 1) DrawPillOutline(qualityRow.Grow(6f), Color.FromHtml("22d3ee"), 2f);
		y += 54f + 40f;

		const float sliderHeight = 26f;
		float sensitivityFill = Mathf.Clamp((_lookSensitivity - 0.4f) / (2.5f - 0.4f), 0f, 1f);
		DrawCenteredText($"SENSIBILIDAD ({Mathf.RoundToInt(_lookSensitivity * 100f)}%)", y, 16, Colors.White);
		y += 20f;
		DrawSettingsSlider(new Rect2(left, y, panelWidth, sliderHeight), sensitivityFill, UiAction.SetSensitivity, _menuIndex == 2);
		y += sliderHeight + 34f;

		DrawCenteredText($"VOLUMEN MÚSICA ({Mathf.RoundToInt(_musicVolume * 100f)}%)", y, 16, Colors.White);
		y += 20f;
		DrawSettingsSlider(new Rect2(left, y, panelWidth, sliderHeight), _musicVolume, UiAction.SetMusicVolume, _menuIndex == 3);
		y += sliderHeight + 34f;

		DrawCenteredText($"VOLUMEN EFECTOS ({Mathf.RoundToInt(_fxVolume * 100f)}%)", y, 16, Colors.White);
		y += 20f;
		DrawSettingsSlider(new Rect2(left, y, panelWidth, sliderHeight), _fxVolume, UiAction.SetFxVolume, _menuIndex == 4);
		y += sliderHeight + 44f;

		DrawSettingsOption(CenteredRect(size, y, 300f, 54f), "VOLVER AL TÍTULO", UiAction.SettingsBack, _menuIndex == 5);
	}

	/// <summary>Rectángulo con extremos totalmente redondeados (forma de píldora), relleno.</summary>
	private void DrawPillRect(Rect2 rect, Color color)
	{
		float radius = Mathf.Min(rect.Size.X, rect.Size.Y) * 0.5f;
		if (rect.Size.X <= rect.Size.Y + 0.01f)
		{
			DrawCircle(rect.GetCenter(), radius, color);
			return;
		}
		DrawRect(new Rect2(rect.Position.X + radius, rect.Position.Y, rect.Size.X - radius * 2f, rect.Size.Y), color);
		DrawCircle(new Vector2(rect.Position.X + radius, rect.Position.Y + radius), radius, color);
		DrawCircle(new Vector2(rect.Position.X + rect.Size.X - radius, rect.Position.Y + radius), radius, color);
	}

	/// <summary>Contorno (sin relleno) de una píldora, usado como indicador de foco de teclado/mando.</summary>
	private void DrawPillOutline(Rect2 rect, Color color, float width)
	{
		float radius = Mathf.Min(rect.Size.X, rect.Size.Y) * 0.5f;
		DrawCircle(new Vector2(rect.Position.X + radius, rect.Position.Y + radius), radius, color, false, width);
		DrawCircle(new Vector2(rect.Position.X + rect.Size.X - radius, rect.Position.Y + radius), radius, color, false, width);
		DrawRect(new Rect2(rect.Position.X + radius, rect.Position.Y, rect.Size.X - radius * 2f, rect.Size.Y), color, false, width);
	}

	/// <summary>Botón tipo píldora para Ajustes: relleno morado + contorno blanco cuando "selected" es la opción activa.</summary>
	private void DrawSettingsOption(Rect2 rect, string label, UiAction action, bool selected)
	{
		if (selected)
		{
			DrawPillRect(rect.Grow(2f), Colors.White);
			DrawPillRect(rect, Color.FromHtml("4a2e97"));
		}
		else
		{
			DrawPillRect(rect, Color.FromHtml("525252"));
		}
		DrawString(_font, new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y * 0.63f), label, HorizontalAlignment.Center, rect.Size.X, 16, Colors.White);
		_uiHits.Add(new UiHit(action, rect));
	}

	/// <summary>Barra deslizante tipo píldora: tramo relleno (azul) / vacío (lavanda), divisor y pomo.</summary>
	private void DrawSettingsSlider(Rect2 rect, float fillT, UiAction action, bool focused)
	{
		fillT = Mathf.Clamp(fillT, 0f, 1f);
		DrawPillRect(rect, Color.FromHtml("dce0f9"));
		float fillWidth = rect.Size.X * fillT;
		if (fillWidth > 1f) DrawRect(new Rect2(rect.Position.X, rect.Position.Y, fillWidth, rect.Size.Y), Color.FromHtml("435d9a"));
		DrawRect(new Rect2(rect.Position.X + fillWidth - 1.5f, rect.Position.Y - 6f, 3f, rect.Size.Y + 12f), Color.FromHtml("7b4bff"));
		DrawCircle(new Vector2(rect.Position.X + fillWidth, rect.Position.Y + rect.Size.Y * 0.5f), rect.Size.Y * 0.5f + 4f, Color.FromHtml("334155"));
		if (focused) DrawPillOutline(rect.Grow(6f), Color.FromHtml("22d3ee"), 2f);
		_uiHits.Add(new UiHit(action, rect));
	}

	private void DrawLevelClear(Vector2 size)
	{
		DrawCenteredText($"¡{GameData.Levels[_levelIndex].Name} superado!", size.Y * 0.34f, 32, Colors.Gold);
		DrawButton(CenteredRect(size, size.Y * 0.48f, 250, 52), "TIENDA", UiAction.LevelShop, _menuIndex == 0);
		DrawButton(CenteredRect(size, size.Y * 0.58f, 250, 52), "CONTINUAR", UiAction.LevelContinue, _menuIndex == 1);
	}

	private void DrawShop(Vector2 size)
	{
		DrawCenteredText("TIENDA", 50f, 34, Colors.Gold);
		DrawCenteredText($"Oro: {_player.Gold}", 80f, 18, Colors.White);

		List<ShopRow> rows = BuildShopRows();
		List<float> tops = ShopRowTops(rows);
		float totalHeight = tops.Count > 0 ? tops[^1] + ShopRowHeight(rows[^1]) : 0f;

		const float viewportTop = 116f;
		float viewportBottom = size.Y - 78f;
		float viewportHeight = Mathf.Max(0f, viewportBottom - viewportTop);
		_shopScroll = Mathf.Clamp(_shopScroll, 0f, Mathf.Max(0f, totalHeight - viewportHeight));

		const float panelWidth = 620f;
		float left = size.X * 0.5f - panelWidth * 0.5f;

		int itemIndex = -1;
		for (int i = 0; i < rows.Count; i++)
		{
			ShopRow row = rows[i];
			float rowHeight = ShopRowHeight(row);
			float y = viewportTop + tops[i] - _shopScroll;
			if (row.Entry.HasValue) itemIndex++;
			if (y + rowHeight < viewportTop || y > viewportBottom) continue;

			if (!row.Entry.HasValue)
			{
				DrawText(row.Header!, new Vector2(left, y + rowHeight * 0.72f), 17, Color.FromHtml("a78bfa"));
				continue;
			}

			DrawShopRow(new Rect2(left, y, panelWidth, rowHeight - 10f), row.Entry.Value, itemIndex == _shopFocusIndex);
		}

		Rect2 continueRect = CenteredRect(size, size.Y - 62f, 330f, 48f);
		DrawPillRect(continueRect, Color.FromHtml("4527a0"));
		DrawString(_font, new Vector2(continueRect.Position.X, continueRect.Position.Y + continueRect.Size.Y * 0.65f), "SIGUIENTE NIVEL", HorizontalAlignment.Center, continueRect.Size.X, 16, Colors.White);
		_uiHits.Add(new UiHit(UiAction.ShopContinue, continueRect));
	}

	private void DrawShopRow(Rect2 rect, ShopEntry entry, bool focused)
	{
		bool equipped = entry.Kind switch
		{
			ShopEntryKind.Weapon => _player.EquippedWeapon == entry.Index,
			ShopEntryKind.Armor => _player.EquippedArmor == entry.Index,
			ShopEntryKind.Accessory => _player.EquippedAccessory == entry.Index,
			ShopEntryKind.Shield => _player.OwnsShield,
			_ => false
		};
		bool owned = entry.Kind switch
		{
			ShopEntryKind.Weapon => _player.OwnedWeapons.Contains(entry.Index),
			ShopEntryKind.Armor => _player.OwnedArmors.Contains(entry.Index),
			ShopEntryKind.Accessory => _player.OwnedAccessories.Contains(entry.Index),
			_ => false
		};

		Color fill = entry.Kind == ShopEntryKind.Potion
			? Color.FromHtml("4a2e97")
			: equipped ? Color.FromHtml("6d3ef0") : owned ? Color.FromHtml("4a2e97") : Color.FromHtml("454545");

		if (focused)
		{
			DrawPillRect(rect.Grow(2f), Colors.White);
			DrawPillRect(rect, fill);
		}
		else
		{
			DrawPillRect(rect, fill);
		}

		string label = entry.Kind == ShopEntryKind.Potion || entry.Kind == ShopEntryKind.Shield
			? entry.Label
			: $"{entry.Label} ({ShopDetail(entry)})";
		DrawString(_font, new Vector2(rect.Position.X + 20f, rect.Position.Y + rect.Size.Y * 0.63f), label, HorizontalAlignment.Left, rect.Size.X - 130f, 17, Colors.White);
		DrawString(_font, new Vector2(rect.End.X - 110f, rect.Position.Y + rect.Size.Y * 0.63f), $"{entry.Cost} O", HorizontalAlignment.Right, 90f, 17, Colors.Gold);
	}

	private readonly record struct ShopRow(string? Header, ShopEntry? Entry);

	private List<ShopRow> BuildShopRows()
	{
		List<ShopEntry> entries = GetShopEntries();
		List<ShopRow> rows = new();
		ShopEntryKind? lastKind = null;
		foreach (ShopEntry entry in entries)
		{
			if (entry.Kind != lastKind)
			{
				string header = entry.Kind switch { ShopEntryKind.Potion => "POCIONES", ShopEntryKind.Weapon => "ARMAS", ShopEntryKind.Armor => "ARMADURAS", ShopEntryKind.Shield => "ESCUDO", _ => "ACCESORIOS" };
				rows.Add(new ShopRow(header, null));
				lastKind = entry.Kind;
			}
			rows.Add(new ShopRow(null, entry));
		}
		return rows;
	}

	private static float ShopRowHeight(ShopRow row) => row.Entry.HasValue ? 74f : 42f;

	private static List<float> ShopRowTops(List<ShopRow> rows)
	{
		List<float> tops = new(rows.Count);
		float y = 0f;
		foreach (ShopRow row in rows)
		{
			tops.Add(y);
			y += ShopRowHeight(row);
		}
		return tops;
	}

	// Fila de ítem bajo un punto de pantalla (en coordenadas de lienzo), o null si cae
	// sobre una cabecera de categoría o fuera de la lista. Usa el mismo layout que DrawShop.
	private ShopEntry? ShopEntryAt(Vector2 position, Vector2 size)
	{
		List<ShopRow> rows = BuildShopRows();
		List<float> tops = ShopRowTops(rows);
		const float viewportTop = 116f;
		float viewportBottom = size.Y - 78f;
		const float panelWidth = 620f;
		float left = size.X * 0.5f - panelWidth * 0.5f;
		if (position.X < left || position.X > left + panelWidth) return null;

		for (int i = 0; i < rows.Count; i++)
		{
			float rowHeight = ShopRowHeight(rows[i]);
			float y = viewportTop + tops[i] - _shopScroll;
			if (y + rowHeight < viewportTop || y > viewportBottom) continue;
			if (position.Y >= y && position.Y < y + rowHeight) return rows[i].Entry;
		}
		return null;
	}

	private void MoveShopFocus(int direction)
	{
		int itemCount = BuildShopRows().Count(r => r.Entry.HasValue);
		if (itemCount == 0) return;
		_shopFocusIndex = Mathf.PosMod(_shopFocusIndex + direction, itemCount);
		ScrollShopToFocus();
	}

	private void ScrollShopToFocus()
	{
		List<ShopRow> rows = BuildShopRows();
		List<float> tops = ShopRowTops(rows);
		float viewportHeight = Mathf.Max(0f, (GetViewportRect().Size.Y - 78f) - 116f);
		int itemIndex = -1;
		for (int i = 0; i < rows.Count; i++)
		{
			if (!rows[i].Entry.HasValue) continue;
			itemIndex++;
			if (itemIndex != _shopFocusIndex) continue;
			float rowTop = tops[i];
			float rowBottom = rowTop + ShopRowHeight(rows[i]);
			if (rowTop < _shopScroll) _shopScroll = rowTop;
			else if (rowBottom > _shopScroll + viewportHeight) _shopScroll = rowBottom - viewportHeight;
			return;
		}
	}

	private void ConfirmShopFocus()
	{
		List<ShopEntry> entries = BuildShopRows().Where(r => r.Entry.HasValue).Select(r => r.Entry!.Value).ToList();
		if (_shopFocusIndex >= 0 && _shopFocusIndex < entries.Count) BuyShopEntry(entries[_shopFocusIndex]);
	}



	private void DrawGameOver(Vector2 size)
	{
		DrawCenteredText("HAS MUERTO", size.Y * 0.32f, 44, Colors.Red);
		DrawCenteredText($"En {GameData.Levels[_levelIndex].Name}", size.Y * 0.38f, 18, new Color(1f, 1f, 1f, 0.66f));
		DrawButton(CenteredRect(size, size.Y * 0.48f, 280, 52), "REINTENTAR NIVEL", UiAction.Retry, _menuIndex == 0);
		DrawButton(CenteredRect(size, size.Y * 0.58f, 280, 52), "VOLVER AL TÍTULO", UiAction.GameOverMenu, _menuIndex == 1);
	}

	private void DrawPause(Vector2 size)
	{
		DrawCenteredText("PAUSA", size.Y * 0.34f, 44, Colors.White);
		DrawButton(CenteredRect(size, size.Y * 0.48f, 250, 52), "REANUDAR", UiAction.Resume, _menuIndex == 0);
		DrawButton(CenteredRect(size, size.Y * 0.58f, 250, 52), "SALIR AL MENÚ", UiAction.PauseQuit, _menuIndex == 1);
	}

	private void DrawVictory(Vector2 size)
	{
		DrawCenteredText("¡VICTORIA!", size.Y * 0.38f, 48, Colors.Gold);
		DrawCenteredText("El Corazón del Vacío ha sido derrotado.", size.Y * 0.44f, 18, Colors.White);
		DrawButton(CenteredRect(size, size.Y * 0.54f, 290, 52), "VOLVER AL MENÚ", UiAction.VictoryMenu, true);
	}

	private void DrawButton(Rect2 rect, string label, UiAction action, bool selected = false, bool enabled = true)
	{
		Color fill = enabled ? (selected ? Color.FromHtml("7c4dff") : Color.FromHtml("4527a0")) : Color.FromHtml("3f3f46");
		DrawRect(rect, fill);
		DrawRect(rect, selected ? Colors.White : new Color(1f, 1f, 1f, 0.25f), false, selected ? 2f : 1f);
		DrawString(_font, new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y * 0.65f), label, HorizontalAlignment.Center, rect.Size.X, 17, enabled ? Colors.White : new Color(1f, 1f, 1f, 0.45f));
		if (enabled) _uiHits.Add(new UiHit(action, rect));
	}

	private void DrawVerticalGradient(Rect2 rect, Color top, Color bottom)
	{
		const int steps = 32;
		for (int i = 0; i < steps; i++)
		{
			float t = i / (float)(steps - 1);
			DrawRect(new Rect2(rect.Position.X, rect.Position.Y + rect.Size.Y * t, rect.Size.X, rect.Size.Y / steps + 1f), top.Lerp(bottom, t));
		}
	}

	private void DrawText(string text, Vector2 baseline, int fontSize, Color color) => DrawString(_font, baseline, text, HorizontalAlignment.Left, -1f, fontSize, color);

	private void DrawCenteredText(string text, float baselineY, int fontSize, Color color)
	{
		Vector2 size = GetViewportRect().Size;
		DrawString(_font, new Vector2(0f, baselineY), text, HorizontalAlignment.Center, size.X, fontSize, color);
	}

	private static Rect2 CenteredRect(Vector2 size, float y, float width, float height) => new(size.X * 0.5f - width * 0.5f, y, width, height);

	// Fuente única de verdad para el layout de los controles táctiles: el dibujado
	// (DrawTouchControls) y la detección de toques (HandlePlayingPress, UpdateTouchJoystick)
	// usan exactamente los mismos valores, escalados con el alto real de pantalla en vez
	// de píxeles fijos pensados para el lienzo de diseño de 1280x720.
	private readonly struct TouchLayout
	{
		public readonly float UiScale;
		public readonly Vector2 JoystickCenter;
		public readonly float JoystickRadius;
		public readonly float JoystickKnobRadius;
		public readonly Vector2 ShootCenter;
		public readonly float ShootRadius;
		public readonly Vector2 ShieldCenter;
		public readonly float ShieldRadius;
		public readonly Vector2 PauseCenter;
		public readonly float PauseRadius;
		public readonly float JoystickZoneWidth;
		public readonly float JoystickZoneHeight;

		public TouchLayout(Vector2 size)
		{
			UiScale = Mathf.Clamp(size.Y / 720f, 0.85f, 2.5f);
			float joystickMarginX = 260f * UiScale;
			float joystickMarginBottom = 220f * UiScale;
			float shootMargin = 112f * UiScale;
			float pauseMargin = 56f * UiScale;
			JoystickCenter = new Vector2(joystickMarginX, size.Y - joystickMarginBottom);
			JoystickRadius = 130f * UiScale;
			JoystickKnobRadius = 54f * UiScale;
			ShootCenter = new Vector2(size.X - shootMargin, size.Y - shootMargin);
			ShootRadius = 84f * UiScale;
			// El botón de escudo se apoya arriba a la izquierda del de disparo, a una distancia
			// proporcional a ambos radios, para que no se solapen a ningún tamaño de pantalla.
			ShieldCenter = ShootCenter + new Vector2(-(ShootRadius + 46f * UiScale), -(ShootRadius + 46f * UiScale));
			ShieldRadius = 52f * UiScale;
			PauseCenter = new Vector2(size.X - pauseMargin, pauseMargin);
			PauseRadius = 36f * UiScale;
			// Zona de agarre del joystick: se calcula a partir de su propio centro/radio
			// (con margen extra), así siempre encaja aunque se mueva o cambie de tamaño.
			JoystickZoneWidth = JoystickCenter.X + JoystickRadius + 60f * UiScale;
			JoystickZoneHeight = (size.Y - JoystickCenter.Y) + JoystickRadius + 60f * UiScale;
		}
	}

	private void HandlePlayingPress(Vector2 position, int touchIndex)
	{
		Vector2 size = GetViewportRect().Size;
		TouchLayout layout = new(size);
		if (position.DistanceTo(layout.PauseCenter) < layout.PauseRadius + 12f * layout.UiScale)
		{
			SetPhase(GamePhase.Paused);
			return;
		}
		if (position.DistanceTo(layout.ShootCenter) < layout.ShootRadius + 12f * layout.UiScale)
		{
			_touchShooting = true;
			return;
		}
		if (_player.OwnsShield && position.DistanceTo(layout.ShieldCenter) < layout.ShieldRadius + 12f * layout.UiScale)
		{
			ActivateShield();
			return;
		}
		if (position.X < layout.JoystickZoneWidth && position.Y > size.Y - layout.JoystickZoneHeight)
		{
			_joystickTouch = touchIndex;
			_touchMoveActive = true;
			UpdateTouchJoystick(position);
			return;
		}
		_lookTouch = touchIndex;
		_lastLookPosition = position;
	}

	private void UpdateTouchJoystick(Vector2 position)
	{
		TouchLayout layout = new(GetViewportRect().Size);
		Vector2 offset = position - layout.JoystickCenter;
		if (offset.Length() > layout.JoystickRadius) offset = offset.Normalized() * layout.JoystickRadius;
		_touchMove = new Vector2(offset.X / layout.JoystickRadius, -offset.Y / layout.JoystickRadius);
	}

	private void HandleShopPointerDown(Vector2 position, int touchIndex)
	{
		if (HandleUiPress(position)) return; // ya lo consumió "SIGUIENTE NIVEL"
		_shopPointerActive = true;
		_shopPointerTouch = touchIndex;
		_shopPointerLastPos = position;
		_shopPointerTotalDrag = 0f;
	}

	private void HandleShopPointerMove(Vector2 position)
	{
		if (!_shopPointerActive) return;
		_shopScroll -= position.Y - _shopPointerLastPos.Y;
		_shopPointerTotalDrag += (position - _shopPointerLastPos).Length();
		_shopPointerLastPos = position;
	}

	private void HandleShopPointerUp(Vector2 position)
	{
		if (!_shopPointerActive) return;
		_shopPointerActive = false;
		_shopPointerTouch = -1;
		if (_shopPointerTotalDrag < 12f)
		{
			ShopEntry? entry = ShopEntryAt(position, GetViewportRect().Size);
			if (entry.HasValue) BuyShopEntry(entry.Value);
		}
	}

	private bool HandleUiPress(Vector2 position)
	{
		for (int i = _uiHits.Count - 1; i >= 0; i--)
		{
			if (_uiHits[i].Rect.HasPoint(position))
			{
				UiAction action = _uiHits[i].Action;
				if (IsSliderAction(action))
				{
					_draggingSlider = action;
					ApplySliderValue(action, _uiHits[i].Rect, position.X);
				}
				else
				{
					HandleUiAction(action);
				}
				return true;
			}
		}
		return false;
	}

	private static bool IsSliderAction(UiAction action) => action is UiAction.SetSensitivity or UiAction.SetMusicVolume or UiAction.SetFxVolume;

	private void ApplySliderValue(UiAction action, Rect2 rect, float pointerX)
	{
		float t = rect.Size.X > 0f ? Mathf.Clamp((pointerX - rect.Position.X) / rect.Size.X, 0f, 1f) : 0f;
		switch (action)
		{
			case UiAction.SetSensitivity: _lookSensitivity = Mathf.Lerp(0.4f, 2.5f, t); break;
			case UiAction.SetMusicVolume: _musicVolume = t; UpdateMusicVolume(); break;
			case UiAction.SetFxVolume: _fxVolume = t; break;
		}
	}

	private UiHit? FindUiHit(UiAction action)
	{
		for (int i = _uiHits.Count - 1; i >= 0; i--)
			if (_uiHits[i].Action == action) return _uiHits[i];
		return null;
	}

	// Ajusta con Izquierda/Derecha (teclado o mando) la fila de Ajustes actualmente enfocada
	// por teclado/mando (_menuIndex), en vez de arrastrar como con el ratón/dedo.
	private void AdjustFocusedSetting(int direction)
	{
		switch (_menuIndex)
		{
			case 0: _difficulty = (Difficulty)Mathf.PosMod((int)_difficulty + direction, 3); break;
			case 1: _graphicsQuality = (GraphicsQuality)Mathf.PosMod((int)_graphicsQuality + direction, 3); break;
			case 2: _lookSensitivity = Mathf.Clamp(_lookSensitivity + direction * 0.1f, 0.4f, 2.5f); break;
			case 3: _musicVolume = Mathf.Clamp(_musicVolume + direction * 0.05f, 0f, 1f); UpdateMusicVolume(); break;
			case 4: _fxVolume = Mathf.Clamp(_fxVolume + direction * 0.05f, 0f, 1f); break;
		}
	}

	private void MoveMenu(int direction)
	{
		if (_phase == GamePhase.Shop) { MoveShopFocus(direction); return; }
		int count = _phase switch
		{
			GamePhase.MainMenu => 3,
			GamePhase.Settings => 6,
			GamePhase.LevelClear => 2,
			GamePhase.GameOver => 2,
			GamePhase.Paused => 2,
			GamePhase.MultiplayerMenu => 3,
			_ => 1
		};
		_menuIndex = Mathf.PosMod(_menuIndex + direction, count);
	}

	private void ConfirmMenu()
	{
		if (_phase == GamePhase.Shop) { ConfirmShopFocus(); return; }
		UiAction action = _phase switch
		{
			GamePhase.MainMenu => _menuIndex switch { 0 => UiAction.Start, 1 => UiAction.None, _ => UiAction.OpenSettings },
			GamePhase.Settings => _menuIndex == 5 ? UiAction.SettingsBack : UiAction.None,
			GamePhase.LevelClear => _menuIndex == 0 ? UiAction.LevelShop : UiAction.LevelContinue,
			GamePhase.GameOver => _menuIndex == 0 ? UiAction.Retry : UiAction.GameOverMenu,
			GamePhase.Paused => _menuIndex == 0 ? UiAction.Resume : UiAction.PauseQuit,
			GamePhase.Finished => UiAction.VictoryMenu,
			GamePhase.MultiplayerMenu => _menuIndex switch { 0 => UiAction.MpHost, 1 => UiAction.MpJoin, _ => UiAction.MpBack },
			_ => UiAction.None
		};
		HandleUiAction(action);
	}

	private void CancelMenu()
	{
		switch (_phase)
		{
			case GamePhase.Settings: SetPhase(GamePhase.MainMenu); break;
			case GamePhase.Paused: SetPhase(GamePhase.Playing); break;
			case GamePhase.Shop: AdvanceLevel(); break;
			case GamePhase.LevelClear: AdvanceLevel(); break;
		}
	}

	private void HandleUiAction(UiAction action)
	{
		switch (action)
		{
			case UiAction.Start:
				LoadLevel(0);
				if (_multiplayerActive && Multiplayer.HasMultiplayerPeer() && Multiplayer.IsServer()) Rpc(MethodName.RpcStartLevel, 0);
				break;
			case UiAction.OpenSettings: SetPhase(GamePhase.Settings); break;
			case UiAction.OpenMultiplayer: _networkStatus = ""; SetPhase(GamePhase.MultiplayerMenu); break;
			case UiAction.MpHost: StartMultiplayerHost(); break;
			case UiAction.MpJoin: StartMultiplayerClient(_joinIpText); break;
			case UiAction.MpBack: StopMultiplayer(); SetPhase(GamePhase.MainMenu); break;
			case UiAction.MpFocusIpField: _joinFieldActive = true; break;
			case UiAction.SettingsBack: SetPhase(GamePhase.MainMenu); break;
			case UiAction.SetDifficultyEasy: _difficulty = Difficulty.Easy; break;
			case UiAction.SetDifficultyNormal: _difficulty = Difficulty.Normal; break;
			case UiAction.SetDifficultyHard: _difficulty = Difficulty.Hard; break;
			case UiAction.SetQualityPerformance: _graphicsQuality = GraphicsQuality.Performance; break;
			case UiAction.SetQualityStandard: _graphicsQuality = GraphicsQuality.Standard; break;
			case UiAction.SetQualityHighDef: _graphicsQuality = GraphicsQuality.HighDefinition; break;
			case UiAction.LevelShop: SetPhase(GamePhase.Shop); break;
			case UiAction.LevelContinue: AdvanceLevel(); break;
			case UiAction.ShopContinue: AdvanceLevel(); break;
			case UiAction.Retry: LoadLevel(_levelIndex); break;
			case UiAction.GameOverMenu: SetPhase(GamePhase.MainMenu); break;
			case UiAction.Resume: SetPhase(GamePhase.Playing); break;
			case UiAction.PauseQuit: SetPhase(GamePhase.MainMenu); break;
			case UiAction.VictoryMenu: SetPhase(GamePhase.MainMenu); break;
		}
	}

	private void AdvanceLevel()
	{
		if (_levelIndex + 1 < GameData.Levels.Length) LoadLevel(_levelIndex + 1);
		else SetPhase(GamePhase.Finished);
	}

	private List<ShopEntry> GetShopEntries()
	{
		List<ShopEntry> entries = new()
		{
			new("Poción de Vida", 10, ShopEntryKind.Potion, 0),
			new("Maná Máximo", 8, ShopEntryKind.Potion, 1),
			new("Escudo Arcano", 220, ShopEntryKind.Shield, 0)
		};
		for (int i = 1; i < GameData.Weapons.Length; i++) entries.Add(new ShopEntry(GameData.Weapons[i].Name, GameData.Weapons[i].Cost, ShopEntryKind.Weapon, i));
		for (int i = 1; i < GameData.Armors.Length; i++) entries.Add(new ShopEntry(GameData.Armors[i].Name, GameData.Armors[i].Cost, ShopEntryKind.Armor, i));
		for (int i = 1; i < GameData.Accessories.Length; i++) entries.Add(new ShopEntry(GameData.Accessories[i].Name, GameData.Accessories[i].Cost, ShopEntryKind.Accessory, i));
		return entries;
	}

	private string ShopDetail(ShopEntry entry) => entry.Kind switch
	{
		ShopEntryKind.Potion => entry.Index == 0 ? "+30 puntos de vida" : "+15 de maná máximo (permanente)",
		ShopEntryKind.Weapon => $"Daño: {GameData.Weapons[entry.Index].Damage:0} · Maná: {GameData.Weapons[entry.Index].ManaCost:0} · CD: {GameData.Weapons[entry.Index].Cooldown:0.00}s",
		ShopEntryKind.Armor => $"Defensa: {GameData.Armors[entry.Index].Defense * 100f:0}%",
		ShopEntryKind.Shield => "Actívalo en partida (tecla E / LB / botón táctil): inmunidad total 7.5s, con recarga",
		_ => $"Ataque: +{GameData.Accessories[entry.Index].AttackBonus * 100f:0}%"
	};



	private bool CanBuy(ShopEntry entry)
	{
		if (entry.Kind == ShopEntryKind.Potion) return _player.Gold >= entry.Cost;
		if (entry.Kind == ShopEntryKind.Shield) return _player.OwnsShield || _player.Gold >= entry.Cost;
		bool owned = entry.Kind switch
		{
			ShopEntryKind.Weapon => _player.OwnedWeapons.Contains(entry.Index),
			ShopEntryKind.Armor => _player.OwnedArmors.Contains(entry.Index),
			_ => _player.OwnedAccessories.Contains(entry.Index)
		};
		return owned || _player.Gold >= entry.Cost;
	}

	private void BuyShopEntry(ShopEntry entry)
	{
		if (!CanBuy(entry)) return;

		switch (entry.Kind)
		{
			case ShopEntryKind.Potion:
				_player.Gold -= entry.Cost;
				if (entry.Index == 0) _player.Health = Mathf.Min(_player.MaxHealth, _player.Health + 30f);
				else { _player.MaxMana += 15f; _player.Mana = _player.MaxMana; }
				break;
			case ShopEntryKind.Shield:
				if (!_player.OwnsShield) { _player.Gold -= entry.Cost; _player.OwnsShield = true; }
				break;
			case ShopEntryKind.Weapon:
				if (!_player.OwnedWeapons.Contains(entry.Index)) { _player.Gold -= entry.Cost; _player.OwnedWeapons.Add(entry.Index); }
				_player.EquippedWeapon = entry.Index;
				break;
			case ShopEntryKind.Armor:
				if (!_player.OwnedArmors.Contains(entry.Index)) { _player.Gold -= entry.Cost; _player.OwnedArmors.Add(entry.Index); }
				_player.EquippedArmor = entry.Index;
				break;
			case ShopEntryKind.Accessory:
				if (!_player.OwnedAccessories.Contains(entry.Index)) { _player.Gold -= entry.Cost; _player.OwnedAccessories.Add(entry.Index); }
				_player.EquippedAccessory = entry.Index;
				break;
		}
	}

	private void SetPhase(GamePhase phase)
	{
		_phase = phase;
		_menuIndex = 0;
		_touchShooting = false;
		_mouseShooting = false;
		_touchMove = Vector2.Zero;
		if (phase == GamePhase.Shop) { _shopScroll = 0f; _shopFocusIndex = 0; _shopPointerActive = false; }
		if (phase == GamePhase.MainMenu) PlayMusic("bgm_menu");
		if ((phase == GamePhase.Playing) && _levelIndex + 1 == 5) PlayMusic("bgm_boss5_game");
		if ((phase == GamePhase.Playing) && _levelIndex + 1 == 8) PlayMusic("bgm_boss8_game");
		if ((phase == GamePhase.Playing) && (_levelIndex + 1 != 5 && _levelIndex + 1 != 8)) PlayMusic("bgm_regular_game");

		// En PC, el ratón no se usa para apuntar (el disparo sigue _player.Angle),
		// así que se oculta durante la partida para no tapar la mira. Se usa
		// "Captured" en vez de "Hidden" para que además quede anclado a la
		// ventana: así el arrastre con el botón derecho sigue generando
		// movimiento aunque el cursor "quiera" salirse de la vista de juego.
		// En menús se vuelve a mostrar para poder pulsar los botones.
		Input.MouseMode = phase == GamePhase.Playing ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
	}

	// =====================================================================
	// MULTIJUGADOR
	// -----------------------------------------------------------------------
	// Cooperativo por IP directa usando el ENetMultiplayerPeer de Godot (todos
	// exploran/pelean en el mismo nivel). El anfitrión también juega como un
	// peer más. AVISO: de momento la posición/salud de cada jugador remoto es
	// la que ESE jugador dice tener — no hay validación en el host, así que no
	// es a prueba de trampas. Los enemigos, objetos y la tienda siguen siendo
	// locales a cada cliente (no están sincronizados todavía); ver el mensaje
	// en el chat para el porqué y los siguientes pasos.
	// =====================================================================

	private void StartMultiplayerHost()
	{
		if (_multiplayerActive) return;
		_peer = new ENetMultiplayerPeer();
		Error err = _peer.CreateServer(MultiplayerPort, 8);
		if (err != Error.Ok)
		{
			_networkStatus = $"No se pudo alojar (error: {err}).";
			_peer = null;
			return;
		}
		Multiplayer.MultiplayerPeer = _peer;
		_multiplayerActive = true;
		_networkStatus = "Partida alojada. Esperando jugadores...";
		Multiplayer.PeerConnected += OnPeerConnected;
		Multiplayer.PeerDisconnected += OnPeerDisconnected;
	}

	private void StartMultiplayerClient(string ip)
	{
		if (_multiplayerActive) return;
		_peer = new ENetMultiplayerPeer();
		Error err = _peer.CreateClient(ip, MultiplayerPort);
		if (err != Error.Ok)
		{
			_networkStatus = $"No se pudo conectar (error: {err}).";
			_peer = null;
			return;
		}
		Multiplayer.MultiplayerPeer = _peer;
		_multiplayerActive = true;
		_networkStatus = "Conectando...";
		Multiplayer.ConnectedToServer += OnConnectedToServer;
		Multiplayer.ConnectionFailed += OnConnectionFailed;
		Multiplayer.PeerConnected += OnPeerConnected;
		Multiplayer.PeerDisconnected += OnPeerDisconnected;
	}

	private void StopMultiplayer()
	{
		if (_peer != null)
		{
			Multiplayer.PeerConnected -= OnPeerConnected;
			Multiplayer.PeerDisconnected -= OnPeerDisconnected;
			Multiplayer.ConnectedToServer -= OnConnectedToServer;
			Multiplayer.ConnectionFailed -= OnConnectionFailed;
			_peer.Close();
			Multiplayer.MultiplayerPeer = null;
			_peer = null;
		}
		_multiplayerActive = false;
		_remotePlayers.Clear();
	}

	private void OnConnectedToServer()
	{
		_networkStatus = "Conectado. Vuelve al menú principal y pulsa COMENZAR.";
	}

	private void OnConnectionFailed()
	{
		_networkStatus = "No se pudo conectar — revisa la IP y el puerto (8910).";
		_multiplayerActive = false;
		_peer = null;
	}

	private void OnPeerConnected(long id)
	{
		_remotePlayers[id] = new RemotePlayerState { Name = $"Jugador {id}" };
	}

	private void OnPeerDisconnected(long id)
	{
		_remotePlayers.Remove(id);
	}

	// Se llama periódicamente desde UpdateGame mientras hay partida multijugador
	// activa, para difundir la posición/ángulo/vida propios a los demás peers.
	private void BroadcastLocalPlayerState()
	{
		if (!_multiplayerActive || !Multiplayer.HasMultiplayerPeer()) return;
		Rpc(MethodName.RpcReceivePlayerState, _player.Position.X, _player.Position.Y, _player.Angle, _player.Health, _player.MaxHealth);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcStartLevel(int levelIndex)
	{
		LoadLevel(levelIndex);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	private void RpcReceivePlayerState(float x, float y, float angle, float health, float maxHealth)
	{
		long sender = Multiplayer.GetRemoteSenderId();
		if (!_remotePlayers.TryGetValue(sender, out RemotePlayerState? remote))
		{
			remote = new RemotePlayerState { Name = $"Jugador {sender}" };
			_remotePlayers[sender] = remote;
		}
		remote.Position = new Vector2(x, y);
		remote.Angle = angle;
		remote.Health = health;
		remote.MaxHealth = maxHealth;
	}

	// El anfitrión manda un único RPC con todos los enemigos vivos, en vez de uno
	// por enemigo: (índice, x, y, hp, frameDeAnimación) por cada uno, todo seguido
	// en un solo array de floats.
	private void BroadcastEnemySnapshot()
	{
		if (!Multiplayer.HasMultiplayerPeer()) return;
		float[] data = new float[_enemies.Count * 5];
		for (int i = 0; i < _enemies.Count; i++)
		{
			Enemy enemy = _enemies[i];
			int b = i * 5;
			data[b] = i;
			data[b + 1] = enemy.Position.X;
			data[b + 2] = enemy.Position.Y;
			data[b + 3] = enemy.Hp;
			data[b + 4] = enemy.AnimationFrame;
		}
		Rpc(MethodName.RpcSyncEnemies, data);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	private void RpcSyncEnemies(float[] data)
	{
		// Los clientes no simulan enemigos: solo pintan lo último que dijo el anfitrión.
		for (int b = 0; b + 4 < data.Length; b += 5)
		{
			int index = (int)data[b];
			if (index < 0 || index >= _enemies.Count) continue;
			Enemy enemy = _enemies[index];
			enemy.Position = new Vector2(data[b + 1], data[b + 2]);
			enemy.Hp = data[b + 3];
			enemy.AnimationFrame = (int)data[b + 4];
		}
	}

	// Un cliente que golpea a un enemigo ya se lo aplica de forma local (para
	// feedback instantáneo, igual que en un jugador), y además avisa al
	// anfitrión para que su copia — la que se difunde a todos — también lo
	// refleje. El anfitrión NO concede recompensa aquí (ya se la llevó quien
	// disparó); solo mantiene su simulación al día.
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcRequestEnemyHit(int enemyIndex, float damage)
	{
		if (!IsHostAuthority || enemyIndex < 0 || enemyIndex >= _enemies.Count) return;
		Enemy enemy = _enemies[enemyIndex];
		if (!enemy.Alive) return;
		enemy.Hp -= damage;
		enemy.HitFlash = 0.15f;
	}

	private void LoadAudio()
	{
		foreach (string name in new[] { "bgm_menu", "bgm_regular_game", "bgm_boss5_game", "bgm_boss8_game", "bgm_finalboss_game", "snd_shoot", "snd_player_hit", "snd_enemy_red", "snd_enemy_green", "snd_enemy_blue", "snd_enemy_boss", "snd_enemy_sentinel" })
		{
			AudioStream? stream = GD.Load<AudioStream>($"res://assets/audio/{name}.ogg");
			if (stream == null) continue;
			// La música (bgm_*) debe repetirse sin fin; los efectos de sonido (snd_*) no.
			if (name.StartsWith("bgm_") && stream is AudioStreamMP3 mp3) mp3.Loop = true;
			_sounds[name] = stream;
		}
		_musicPlayer = new AudioStreamPlayer();
		AddChild(_musicPlayer);
	}

	private void PlayMusic(string name)
	{
		if (_musicPlayer == null || !_sounds.TryGetValue(name, out AudioStream? stream)) return;
		if (_currentMusicName == name && _musicPlayer.Playing) return; // ya está sonando: no reiniciar desde el principio
		_currentMusicName = name;
		_musicPlayer.Stop();
		_musicPlayer.Stream = stream;
		_musicPlayer.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.001f, _musicVolume));
		_musicPlayer.Play();
	}

	private void UpdateMusicVolume()
	{
		if (_musicPlayer != null) _musicPlayer.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.001f, _musicVolume));
	}

	private void PlaySfx(string name)
	{
		if (!_sounds.TryGetValue(name, out AudioStream? stream)) return;
		AudioStreamPlayer player = new() { Stream = stream, VolumeDb = Mathf.LinearToDb(Mathf.Max(0.001f, _fxVolume)) };
		AddChild(player);
		player.Finished += player.QueueFree;
		player.Play();
	}

	private void CreateWallTextures()
	{
		const int textureSize = 256;
		const int rows = 8;
		const int cols = 8;
		const float mortarPx = 3f;
		float rowHeight = textureSize / (float)rows;
		float colWidth = textureSize / (float)cols;

		foreach ((int code, Color baseColor) in WallColors)
		{
			Image image = Image.CreateEmpty(textureSize, textureSize, false, Image.Format.Rgba8);

			for (int y = 0; y < textureSize; y++)
			{
				int row = Mathf.Clamp((int)(y / rowHeight), 0, rows - 1);
				float v = y / rowHeight - row; // 0..1 vertical position within the row
				bool horizontalMortar = v < mortarPx / rowHeight || v > 1f - mortarPx / rowHeight;
				// Ladrillos a hiladas alternas (running bond), como un muro real.
				int rowOffset = row % 2 == 0 ? 0 : (int)(colWidth * 0.5f);

				for (int x = 0; x < textureSize; x++)
				{
					int shiftedX = (x + rowOffset) % textureSize;
					int col = (int)(shiftedX / colWidth);
					float u = shiftedX / colWidth - col; // 0..1 horizontal position within the brick
					bool verticalMortar = u < mortarPx / colWidth;

					if (horizontalMortar || verticalMortar)
					{
						image.SetPixel(x, y, baseColor.Lerp(Colors.Black, 0.45f));
						continue;
					}

					// Variación de tono por ladrillo determinista (no ruido por píxel/rectángulo al azar).
					int brickSeed = (row * 928371 + col * 12871 + code * 37) & 0x7fffffff;
					float variation = (brickSeed % 21 - 10) / 110f; // -0.09..0.09
					Color brick = variation >= 0f ? baseColor.Lerp(Colors.White, variation) : baseColor.Lerp(Colors.Black, -variation);
					if (v < 0.15f) brick = brick.Lerp(Colors.White, 0.06f); // realce suave arriba de cada ladrillo
					image.SetPixel(x, y, brick);
				}
			}

			image.GenerateMipmaps();
			_wallTextures[code] = ImageTexture.CreateFromImage(image);
		}
	}

	private static Texture2D? LoadTexture(string path) => GD.Load<Texture2D>(path);
	private static Color Alpha(Color color, float alpha) => new(color.R, color.G, color.B, alpha);
	private static Color WallColor(int code) => WallColors.TryGetValue(code, out Color color) ? color : Colors.Gray;
	private static Color EnemyColor(EnemyType type) => type switch
	{
		EnemyType.Melee => Color.FromHtml("b5452e"),
		EnemyType.Ranged => Color.FromHtml("4ea34e"),
		EnemyType.Tank => Color.FromHtml("455a64"),
		EnemyType.Boss => Color.FromHtml("6a1b9a"),
		_ => Colors.Black
	};
	private static float EnemyScale(EnemyType type) => type switch { EnemyType.Tank => 1.5f, EnemyType.Boss => 2.5f, EnemyType.Sentinel => 2.8f, _ => 1.2f };
	private Texture2D? EnemyTexture(EnemyType type) => type switch { EnemyType.Melee => _redEnemy, EnemyType.Ranged => _greenWizard, EnemyType.Tank => _blueTank, EnemyType.Boss => _boss, _ => _sentinel };

	// La escala de la textura en el mundo coincide con DrawWorldSprite: cada
	// spritesheet contiene cinco fotogramas horizontales. El radio es la mitad
	// del ancho visible del fotograma ya escalado, de modo que brazos y alas no
	// atraviesan las paredes y los proyectiles impactan donde se ve al enemigo.
	private float EnemyCollisionRadius(Enemy enemy)
	{
		Texture2D? texture = EnemyTexture(enemy.Type);
		if (texture == null) return EnemyScale(enemy.Type) * 0.3f;
		float frameWidth = texture.GetWidth() / 5f;
		float visibleWorldWidth = EnemyScale(enemy.Type) * frameWidth / texture.GetHeight();
		return visibleWorldWidth * 0.5f;
	}

	private static string EnemySound(EnemyType type) => type switch { EnemyType.Melee => "snd_enemy_red", EnemyType.Ranged => "snd_enemy_green", EnemyType.Tank => "snd_enemy_blue", EnemyType.Boss => "snd_enemy_boss", _ => "snd_enemy_sentinel" };
	private bool HasGamepad() => Input.GetConnectedJoypads().Count > 0;
	private float NextFloat() => (float)_random.NextDouble();

	/// <summary>
	/// Filtra el drift de los sticks analógicos: por debajo del umbral se considera reposo (0,0)
	/// y por encima se reescala suavemente para no perder rango de movimiento.
	/// </summary>
	private static Vector2 ApplyDeadzone(Vector2 stick, float deadzone = 0.2f)
	{
		float length = stick.Length();
		if (length < deadzone) return Vector2.Zero;
		float rescaled = Mathf.Min(1f, (length - deadzone) / (1f - deadzone));
		return stick.Normalized() * rescaled;
	}

	private enum SpriteKind { Enemy, Projectile, Item, RemotePlayer }
	private enum ItemType { PotionRed, PotionBlue, Scroll }
	private enum ShopEntryKind { Potion, Weapon, Armor, Accessory, Shield }
	private enum UiAction
	{
		None,
		Start, OpenSettings, OpenMultiplayer,
		SetDifficultyEasy, SetDifficultyNormal, SetDifficultyHard,
		SetQualityPerformance, SetQualityStandard, SetQualityHighDef,
		SetSensitivity, SetMusicVolume, SetFxVolume, SettingsBack,
		LevelShop, LevelContinue, ShopContinue,
		MpHost, MpJoin, MpBack, MpFocusIpField,
		Retry, GameOverMenu, Resume, PauseQuit, VictoryMenu
	}

	private readonly record struct UiHit(UiAction Action, Rect2 Rect);
	private readonly record struct ShopEntry(string Label, int Cost, ShopEntryKind Kind, int Index);
	private readonly record struct WorldSprite(Vector2 Position, SpriteKind Kind, object Source, Color Color, float Scale);

	// Estado replicado de un jugador remoto: solo lo que hace falta para dibujarlo
	// en el mundo y en el minimapa. Nada de esto se valida en el host todavía
	// (ver aviso en el chat) — es la posición que cada cliente dice tener.
	private sealed class RemotePlayerState
	{
		public Vector2 Position;
		public float Angle;
		public float Health = 100f;
		public float MaxHealth = 100f;
		public string Name = "Jugador";
	}

	private sealed class PlayerState
	{
		public Vector2 Position;
		public float Angle;
		public float Health = 100f;
		public float MaxHealth = 100f;
		public float Mana = 100f;
		public float MaxMana = 100f;
		public float ShootCooldown;
		public int Level = 1;
		public float Exp;
		public float ExpToNext = 100f;
		public int Gold;
		public int EquippedWeapon;
		public int EquippedArmor;
		public int EquippedAccessory;
		public readonly HashSet<int> OwnedWeapons = new() { 0 };
		public readonly HashSet<int> OwnedArmors = new() { 0 };
		public readonly HashSet<int> OwnedAccessories = new() { 0 };
		public bool OwnsShield;
		public float ShieldTimeRemaining;
		public float ShieldCooldownRemaining;
		public float ScreenShake;
		public int ItemsCollected;
		public float WeaponDamage => GameData.Weapons[EquippedWeapon].Damage * (1f + 0.05f * (Level - 1)) * (1f + GameData.Accessories[EquippedAccessory].AttackBonus);
		public float WeaponManaCost => GameData.Weapons[EquippedWeapon].ManaCost;
		public float WeaponCooldown => GameData.Weapons[EquippedWeapon].Cooldown;
		public float ArmorDefense => GameData.Armors[EquippedArmor].Defense;
	}

	private sealed class Enemy
	{
		public Vector2 Position;
		public float Hp;
		public float MaxHp;
		public EnemyType Type;
		public float Speed;
		public int ExpReward;
		public int GoldMin;
		public int GoldMax;
		public float AttackCooldown;
		public float HitFlash;
		public bool Rewarded;
		public float Bob;
		public int AnimationFrame;
		public float AnimationTimer;
		public bool PlayerVisible;
		public bool Alive => Hp > 0f;

		public Enemy(Vector2 position, float hp, EnemyType type, float speed, int expReward, int goldMin, int goldMax)
		{
			Position = position; Hp = hp; MaxHp = hp; Type = type; Speed = speed; ExpReward = expReward; GoldMin = goldMin; GoldMax = goldMax;
		}
	}

	private sealed class Projectile
	{
		public Vector2 Position;
		public float Angle;
		public float Speed;
		public float Damage;
		public ProjectileOwner Owner;
		public Color Color;
		public Projectile(Vector2 position, float angle, float speed, float damage, ProjectileOwner owner, Color color)
		{
			Position = position; Angle = angle; Speed = speed; Damage = damage; Owner = owner; Color = color;
		}
	}

	private readonly record struct WorldItem(Vector2 Position, ItemType Type, Color Color);
	private sealed class Particle
	{
		public Vector2 Position;
		public Vector2 Velocity;
		public float Life;
		public float MaxLife;
		public Color Color;
		public Particle(Vector2 position, Vector2 velocity, float life, float maxLife, Color color)
		{
			Position = position; Velocity = velocity; Life = life; MaxLife = maxLife; Color = color;
		}
	}
}
