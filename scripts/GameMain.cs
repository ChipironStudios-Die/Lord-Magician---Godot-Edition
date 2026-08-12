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

	private readonly Random _random = new();
	private readonly PlayerState _player = new();
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

	private GamePhase _phase = GamePhase.MainMenu;
	private Difficulty _difficulty = Difficulty.Normal;
	private GraphicsQuality _graphicsQuality = GraphicsQuality.Standard;
	private int _levelIndex;
	private int _menuIndex;
	private int _shopIndex;
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
	private float[] _zBuffer = Array.Empty<float>();

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
			if (_phase == GamePhase.Playing)
			{
				if (key.Keycode is Key.Escape or Key.P)
					SetPhase(GamePhase.Paused);
			}
			else
			{
				if (key.Keycode is Key.Up or Key.W) MoveMenu(-1);
				if (key.Keycode is Key.Down or Key.S) MoveMenu(1);
				if (key.Keycode is Key.Enter or Key.Space) ConfirmMenu();
				if (key.Keycode is Key.Escape or Key.Backspace) CancelMenu();
				if (_phase == GamePhase.Shop && key.Keycode is Key.Left or Key.A) ChangeShop(-1);
				if (_phase == GamePhase.Shop && key.Keycode is Key.Right or Key.D) ChangeShop(1);
			}
		}

		if (inputEvent is InputEventJoypadButton joyButton && joyButton.Pressed)
		{
			if (_phase == GamePhase.Playing)
			{
				if (joyButton.ButtonIndex is JoyButton.Start or JoyButton.Back)
					SetPhase(GamePhase.Paused);
			}
			else
			{
				if (joyButton.ButtonIndex == JoyButton.DpadUp) MoveMenu(-1);
				if (joyButton.ButtonIndex == JoyButton.DpadDown) MoveMenu(1);
				if (joyButton.ButtonIndex == JoyButton.DpadLeft && _phase == GamePhase.Shop) ChangeShop(-1);
				if (joyButton.ButtonIndex == JoyButton.DpadRight && _phase == GamePhase.Shop) ChangeShop(1);
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
			else if (mouseButton.Pressed)
			{
				HandleUiPress(mouseButton.Position);
			}
		}

		if (inputEvent is InputEventMouseMotion mouseMotion && _phase == GamePhase.Playing)
			_player.Angle += mouseMotion.Relative.X * 0.005f * _lookSensitivity;

		if (inputEvent is InputEventScreenTouch screenTouch)
		{
			if (_phase != GamePhase.Playing)
			{
				if (screenTouch.Pressed) HandleUiPress(screenTouch.Position);
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

		if (inputEvent is InputEventScreenDrag screenDrag && _phase == GamePhase.Playing)
		{
			if (screenDrag.Index == _joystickTouch)
				UpdateTouchJoystick(screenDrag.Position);
			else if (screenDrag.Index == _lookTouch)
			{
				_player.Angle += (screenDrag.Position.X - _lastLookPosition.X) * 0.005f * _lookSensitivity;
				_lastLookPosition = screenDrag.Position;
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
			if (!HasGamepad() && DisplayServer.IsTouchscreenAvailable()) DrawTouchControls(size);
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
		}
	}

	private void UpdateGame(float dt)
	{
		LevelDef level = GameData.Levels[_levelIndex];
		int[][] map = level.Map;
		_frameTick += 1f;

		for (int i = _particles.Count - 1; i >= 0; i--)
		{
			Particle particle = _particles[i];
			particle.Position += particle.Velocity * dt;
			particle.Velocity *= Mathf.Clamp(1f - 2f * dt, 0f, 1f);
			particle.Life -= dt;
			if (particle.Life <= 0f) _particles.RemoveAt(i);
		}

		if (_player.ScreenShake > 0f) _player.ScreenShake -= dt * 5f;

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
		MoveWithWalls(ref _player.Position, movement, 0.22f, map);

		for (int i = _items.Count - 1; i >= 0; i--)
		{
			WorldItem item = _items[i];
			if (_player.Position.DistanceSquaredTo(item.Position) >= 0.25f) continue;

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
		UpdateEnemies(dt, map);

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
					if (!enemy.Alive || enemy.Position.DistanceSquaredTo(projectile.Position) >= 0.25f) continue;
					enemy.Hp -= projectile.Damage;
					enemy.HitFlash = 0.15f;
					dead = true;
					SpawnExplosion(projectile.Position, projectile.Color, 8);
					PlaySfx(EnemySound(enemy.Type));
					if (enemy.Hp <= 0f && !enemy.Rewarded)
					{
						enemy.Rewarded = true;
						GrantReward(enemy);
						SpawnExplosion(enemy.Position, EnemyColor(enemy.Type), 20);
					}
					break;
				}
			}
			else if (_player.Position.DistanceSquaredTo(projectile.Position) < 0.16f)
			{
				_player.Health -= projectile.Damage * (1f - _player.ArmorDefense);
				_player.ScreenShake = 0.3f;
				dead = true;
				SpawnExplosion(projectile.Position, projectile.Color, 8);
				PlaySfx("snd_player_hit");
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
			bool canSee = HasLineOfSight(enemy.Position, direction, distance, map);
			if (!canSee && enemy.Type is not EnemyType.Boss and not EnemyType.Sentinel) continue;

			switch (enemy.Type)
			{
				case EnemyType.Melee:
				case EnemyType.Tank:
					float stopDistance = enemy.Type == EnemyType.Tank ? 1.2f : 0.9f;
					if (distance > stopDistance)
						MoveWithWalls(ref enemy.Position, direction * enemy.Speed * dt, 0.3f, map);
					else if (enemy.AttackCooldown <= 0f)
					{
						float damage = (enemy.Type == EnemyType.Tank ? meleeDamage * 1.5f : meleeDamage) * (1f - _player.ArmorDefense);
						DamagePlayer(damage, enemy.Type == EnemyType.Tank ? 0.4f : 0.3f);
						enemy.AttackCooldown = (enemy.Type == EnemyType.Tank ? 1.5f : 1.1f) * cooldownMultiplier;
					}
					break;

				case EnemyType.Ranged:
					if (distance > 7f) MoveWithWalls(ref enemy.Position, direction * enemy.Speed * 0.6f * dt, 0.3f, map);
					if (distance < 10f && enemy.AttackCooldown <= 0f)
					{
						_projectiles.Add(new Projectile(enemy.Position, Mathf.Atan2(toPlayer.Y, toPlayer.X), 4f, rangedDamage, ProjectileOwner.Enemy, Colors.Green));
						enemy.AttackCooldown = 2f * cooldownMultiplier;
					}
					break;

				case EnemyType.Boss:
					Vector2 bossDirection = direction + new Vector2(Mathf.Sin(_frameTick * 0.05f), Mathf.Cos(_frameTick * 0.05f)) * 0.5f;
					if (distance > 4f) MoveWithWalls(ref enemy.Position, bossDirection * enemy.Speed * dt, 0.4f, map);
					if (enemy.AttackCooldown <= 0f)
					{
						float angle = Mathf.Atan2(toPlayer.Y, toPlayer.X);
						for (int i = -1; i <= 1; i++)
							_projectiles.Add(new Projectile(enemy.Position, angle + i * 0.2f, 5f, rangedDamage * 1.2f, ProjectileOwner.Enemy, Colors.Red));
						enemy.AttackCooldown = 1.5f * cooldownMultiplier;
					}
					break;

				case EnemyType.Sentinel:
					MoveWithWalls(ref enemy.Position, direction * enemy.Speed * dt, 0.3f, map);
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

	private void DamagePlayer(float damage, float shake)
	{
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

	private bool HasLineOfSight(Vector2 origin, Vector2 direction, float distance, int[][] map)
	{
		if (distance >= 12f) return false;
		for (float checkDistance = 0.5f; checkDistance < distance; checkDistance += 0.5f)
			if (IsWall(origin + direction * checkDistance, map)) return false;
		return true;
	}

	private void MoveWithWalls(ref Vector2 position, Vector2 movement, float radius, int[][] map)
	{
		Vector2 xTest = new(position.X + movement.X + (movement.X > 0f ? radius : -radius), position.Y);
		if (!IsWall(xTest, map)) position.X += movement.X;
		Vector2 yTest = new(position.X, position.Y + movement.Y + (movement.Y > 0f ? radius : -radius));
		if (!IsWall(yTest, map)) position.Y += movement.Y;
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
			Rect2 destination = new(i * columnWidth + shake.X, (size.Y - lineHeight) * 0.5f + shake.Y, columnWidth + 1f, lineHeight);

			if (_wallTextures.TryGetValue(code, out Texture2D? texture))
				DrawTextureRectRegion(texture, destination, new Rect2(wallX * 255f, 0f, 1f, 256f));
			else
				DrawRect(destination, WallColor(code));

			float brightness = Mathf.Clamp(1.1f - wallDistance * 0.05f, 0.2f, 1f) * (side == 1 ? 0.75f : 1f);
			DrawRect(destination, new Color(0f, 0f, 0f, 1f - brightness));
		}

		List<WorldSprite> sprites = new();
		foreach (Enemy enemy in _enemies)
			if (enemy.Alive) sprites.Add(new WorldSprite(enemy.Position, SpriteKind.Enemy, enemy, EnemyColor(enemy.Type), EnemyScale(enemy.Type)));
		foreach (Projectile projectile in _projectiles)
			sprites.Add(new WorldSprite(projectile.Position, SpriteKind.Projectile, projectile, projectile.Color, 0.25f));
		foreach (WorldItem item in _items)
			sprites.Add(new WorldSprite(item.Position, SpriteKind.Item, item, item.Color, 0.4f));
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
		int rayIndex = Mathf.Clamp((int)(screenX / size.X * _zBuffer.Length), 0, _zBuffer.Length - 1);
		if (transformY >= _zBuffer[rayIndex]) return;

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
			Rect2 destination = new(screenX - spriteHeight * 0.5f + shake.X, bottom - spriteHeight + shake.Y, spriteHeight, spriteHeight);
			if (texture != null) DrawTextureRect(texture, destination, false);
			else DrawCircle(destination.GetCenter(), spriteHeight * 0.3f, sprite.Color);
			return;
		}

		Enemy enemy = (Enemy)sprite.Source;
		Texture2D? enemyTexture = EnemyTexture(enemy.Type);
		if (enemyTexture == null)
		{
			DrawCircle(new Vector2(screenX + shake.X, bottom - spriteHeight * 0.5f + shake.Y), spriteHeight * 0.3f, sprite.Color);
			return;
		}

		float frameWidth = enemyTexture.GetWidth() / 5f;
		float aspect = frameWidth / enemyTexture.GetHeight();
		float spriteWidth = spriteHeight * aspect;
		Rect2 destinationEnemy = new(screenX - spriteWidth * 0.5f + shake.X, bottom - spriteHeight + shake.Y, spriteWidth, spriteHeight);
		Rect2 source = new(enemy.AnimationFrame * frameWidth + 1f, 0f, frameWidth - 2f, enemyTexture.GetHeight());
		DrawTextureRectRegion(enemyTexture, destinationEnemy, source);
		if (enemy.HitFlash > 0f) DrawRect(destinationEnemy, new Color(1f, 1f, 1f, 0.5f));
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
		DrawButton(new Rect2(size.X * 0.5f - 145f, y + 70f, 290f, 54f), "AJUSTES", UiAction.OpenSettings, _menuIndex == 1);
		DrawCenteredText("WASD/mando para moverte · Ratón/táctil para mirar · Espacio/R2 para disparar", size.Y - 32f, 14, new Color(1f, 1f, 1f, 0.65f));
	}

	private void DrawSettings(Vector2 size)
	{
		DrawCenteredText("AJUSTES", 76f, 38, Colors.Gold);
		float x = size.X * 0.5f - 240f;
		float y = 118f;
		const float width = 480f;
		DrawButton(new Rect2(x, y, width, 42), $"Dificultad: {DifficultyName(_difficulty)}", UiAction.CycleDifficulty, _menuIndex == 0); y += 51f;
		DrawButton(new Rect2(x, y, width, 42), $"Resolución del raycaster: {QualityName(_graphicsQuality)}", UiAction.CycleQuality, _menuIndex == 1); y += 51f;
		DrawButton(new Rect2(x, y, 232, 42), $"Música -  {Mathf.RoundToInt(_musicVolume * 100f)}%", UiAction.MusicDown, _menuIndex == 2); 
		DrawButton(new Rect2(x + 248, y, 232, 42), "Música +", UiAction.MusicUp, _menuIndex == 3); y += 51f;
		DrawButton(new Rect2(x, y, 232, 42), $"Efectos -  {Mathf.RoundToInt(_fxVolume * 100f)}%", UiAction.FxDown, _menuIndex == 4);
		DrawButton(new Rect2(x + 248, y, 232, 42), "Efectos +", UiAction.FxUp, _menuIndex == 5); y += 51f;
		DrawButton(new Rect2(x, y, 232, 42), $"Giro -  x{_lookSensitivity:0.0}", UiAction.SensitivityDown, _menuIndex == 6);
		DrawButton(new Rect2(x + 248, y, 232, 42), "Giro +", UiAction.SensitivityUp, _menuIndex == 7); y += 72f;
		DrawButton(new Rect2(x, y, width, 48), "VOLVER AL TÍTULO", UiAction.SettingsBack, _menuIndex == 8);
	}

	private void DrawLevelClear(Vector2 size)
	{
		DrawCenteredText($"¡{GameData.Levels[_levelIndex].Name} superado!", size.Y * 0.34f, 32, Colors.Gold);
		DrawButton(CenteredRect(size, size.Y * 0.48f, 250, 52), "TIENDA", UiAction.LevelShop, _menuIndex == 0);
		DrawButton(CenteredRect(size, size.Y * 0.58f, 250, 52), "CONTINUAR", UiAction.LevelContinue, _menuIndex == 1);
	}

	private void DrawShop(Vector2 size)
	{
		DrawCenteredText("TIENDA", 60f, 38, Colors.Gold);
		DrawCenteredText($"Oro: {_player.Gold}", 88f, 20, Colors.White);
		List<ShopEntry> entries = GetShopEntries();
		ShopEntry entry = entries[_shopIndex];
		string category = entry.Kind switch { ShopEntryKind.Potion => "POCIONES", ShopEntryKind.Weapon => "ARMAS", ShopEntryKind.Armor => "ARMADURAS", _ => "ACCESORIOS" };
		DrawCenteredText(category, 152f, 18, Color.FromHtml("a78bfa"));

		Rect2 card = CenteredRect(size, 176f, 560f, 185f);
		DrawRect(card, new Color(0.12f, 0.06f, 0.24f, 0.92f));
		DrawRect(card, Color.FromHtml("7c4dff"), false, 2f);
		DrawCenteredText(entry.Label, 226f, 24, Colors.White);
		DrawCenteredText(ShopDetail(entry), 260f, 18, Color.FromHtml("d8b4fe"));
		DrawCenteredText($"{entry.Cost} oro", 300f, 22, Colors.Gold);
		DrawCenteredText(ShopStatus(entry), 334f, 15, new Color(1f, 1f, 1f, 0.7f));

		DrawButton(new Rect2(card.Position.X, 390f, 120f, 48f), "<", UiAction.ShopPrevious, _menuIndex == 0);
		DrawButton(new Rect2(card.Position.X + 135f, 390f, 290f, 48f), "COMPRAR / EQUIPAR", UiAction.ShopBuy, _menuIndex == 1, CanBuy(entry));
		DrawButton(new Rect2(card.End.X - 120f, 390f, 120f, 48f), ">", UiAction.ShopNext, _menuIndex == 2);
		DrawCenteredText($"Objeto {_shopIndex + 1} de {entries.Count}", 468f, 15, new Color(1f, 1f, 1f, 0.6f));
		DrawButton(CenteredRect(size, 516f, 330f, 52f), "SIGUIENTE NIVEL", UiAction.ShopContinue, _menuIndex == 3);
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

	private void HandleUiPress(Vector2 position)
	{
		for (int i = _uiHits.Count - 1; i >= 0; i--)
		{
			if (_uiHits[i].Rect.HasPoint(position))
			{
				HandleUiAction(_uiHits[i].Action);
				return;
			}
		}
	}

	private void MoveMenu(int direction)
	{
		int count = _phase switch
		{
			GamePhase.MainMenu => 2,
			GamePhase.Settings => 9,
			GamePhase.LevelClear => 2,
			GamePhase.Shop => 4,
			GamePhase.GameOver => 2,
			GamePhase.Paused => 2,
			_ => 1
		};
		_menuIndex = Mathf.PosMod(_menuIndex + direction, count);
	}

	private void ConfirmMenu()
	{
		UiAction action = _phase switch
		{
			GamePhase.MainMenu => _menuIndex == 0 ? UiAction.Start : UiAction.OpenSettings,
			GamePhase.Settings => (UiAction)((int)UiAction.CycleDifficulty + _menuIndex),
			GamePhase.LevelClear => _menuIndex == 0 ? UiAction.LevelShop : UiAction.LevelContinue,
			GamePhase.Shop => new[] { UiAction.ShopPrevious, UiAction.ShopBuy, UiAction.ShopNext, UiAction.ShopContinue }[_menuIndex],
			GamePhase.GameOver => _menuIndex == 0 ? UiAction.Retry : UiAction.GameOverMenu,
			GamePhase.Paused => _menuIndex == 0 ? UiAction.Resume : UiAction.PauseQuit,
			GamePhase.Finished => UiAction.VictoryMenu,
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
			case UiAction.Start: LoadLevel(0); break;
			case UiAction.OpenSettings: SetPhase(GamePhase.Settings); break;
			case UiAction.SettingsBack: SetPhase(GamePhase.MainMenu); break;
			case UiAction.CycleDifficulty: _difficulty = (Difficulty)(((int)_difficulty + 1) % 3); break;
			case UiAction.CycleQuality: _graphicsQuality = (GraphicsQuality)(((int)_graphicsQuality + 1) % 3); break;
			case UiAction.MusicDown: _musicVolume = Mathf.Max(0f, _musicVolume - 0.1f); UpdateMusicVolume(); break;
			case UiAction.MusicUp: _musicVolume = Mathf.Min(1f, _musicVolume + 0.1f); UpdateMusicVolume(); break;
			case UiAction.FxDown: _fxVolume = Mathf.Max(0f, _fxVolume - 0.1f); break;
			case UiAction.FxUp: _fxVolume = Mathf.Min(1f, _fxVolume + 0.1f); break;
			case UiAction.SensitivityDown: _lookSensitivity = Mathf.Max(0.4f, _lookSensitivity - 0.1f); break;
			case UiAction.SensitivityUp: _lookSensitivity = Mathf.Min(2.5f, _lookSensitivity + 0.1f); break;
			case UiAction.LevelShop: SetPhase(GamePhase.Shop); break;
			case UiAction.LevelContinue: AdvanceLevel(); break;
			case UiAction.ShopPrevious: ChangeShop(-1); break;
			case UiAction.ShopNext: ChangeShop(1); break;
			case UiAction.ShopBuy: BuyCurrentShopItem(); break;
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
			new("Poción de Maná", 8, ShopEntryKind.Potion, 1)
		};
		for (int i = 1; i < GameData.Weapons.Length; i++) entries.Add(new ShopEntry(GameData.Weapons[i].Name, GameData.Weapons[i].Cost, ShopEntryKind.Weapon, i));
		for (int i = 1; i < GameData.Armors.Length; i++) entries.Add(new ShopEntry(GameData.Armors[i].Name, GameData.Armors[i].Cost, ShopEntryKind.Armor, i));
		for (int i = 1; i < GameData.Accessories.Length; i++) entries.Add(new ShopEntry(GameData.Accessories[i].Name, GameData.Accessories[i].Cost, ShopEntryKind.Accessory, i));
		return entries;
	}

	private void ChangeShop(int direction)
	{
		int count = GetShopEntries().Count;
		_shopIndex = Mathf.PosMod(_shopIndex + direction, count);
	}

	private string ShopDetail(ShopEntry entry) => entry.Kind switch
	{
		ShopEntryKind.Potion => entry.Index == 0 ? "+30 puntos de vida" : "+30 puntos de maná",
		ShopEntryKind.Weapon => $"Daño: {GameData.Weapons[entry.Index].Damage:0} · Maná: {GameData.Weapons[entry.Index].ManaCost:0} · CD: {GameData.Weapons[entry.Index].Cooldown:0.00}s",
		ShopEntryKind.Armor => $"Defensa: {GameData.Armors[entry.Index].Defense * 100f:0}%",
		_ => $"Ataque: +{GameData.Accessories[entry.Index].AttackBonus * 100f:0}%"
	};

	private string ShopStatus(ShopEntry entry)
	{
		bool owned = entry.Kind switch
		{
			ShopEntryKind.Weapon => _player.OwnedWeapons.Contains(entry.Index),
			ShopEntryKind.Armor => _player.OwnedArmors.Contains(entry.Index),
			ShopEntryKind.Accessory => _player.OwnedAccessories.Contains(entry.Index),
			_ => false
		};
		bool equipped = entry.Kind switch
		{
			ShopEntryKind.Weapon => _player.EquippedWeapon == entry.Index,
			ShopEntryKind.Armor => _player.EquippedArmor == entry.Index,
			ShopEntryKind.Accessory => _player.EquippedAccessory == entry.Index,
			_ => false
		};
		if (equipped) return "EQUIPADO";
		if (owned) return "Ya lo tienes: pulsa para equiparlo";
		return _player.Gold >= entry.Cost ? "Disponible" : "Oro insuficiente";
	}

	private bool CanBuy(ShopEntry entry)
	{
		if (entry.Kind == ShopEntryKind.Potion) return _player.Gold >= entry.Cost;
		bool owned = entry.Kind switch
		{
			ShopEntryKind.Weapon => _player.OwnedWeapons.Contains(entry.Index),
			ShopEntryKind.Armor => _player.OwnedArmors.Contains(entry.Index),
			_ => _player.OwnedAccessories.Contains(entry.Index)
		};
		return owned || _player.Gold >= entry.Cost;
	}

	private void BuyCurrentShopItem()
	{
		ShopEntry entry = GetShopEntries()[_shopIndex];
		if (!CanBuy(entry)) return;

		switch (entry.Kind)
		{
			case ShopEntryKind.Potion:
				_player.Gold -= entry.Cost;
				if (entry.Index == 0) _player.Health = Mathf.Min(_player.MaxHealth, _player.Health + 30f);
				else _player.Mana = Mathf.Min(_player.MaxMana, _player.Mana + 30f);
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
		if (phase == GamePhase.MainMenu) PlayMusic("bgm_menu");
		if (phase == GamePhase.Playing) PlayMusic("bgm_game");

		// En PC, el ratón no se usa para apuntar (el disparo sigue _player.Angle),
		// así que se oculta durante la partida para no tapar la mira. Se usa
		// "Captured" en vez de "Hidden" para que además quede anclado a la
		// ventana: así el arrastre con el botón derecho sigue generando
		// movimiento aunque el cursor "quiera" salirse de la vista de juego.
		// En menús se vuelve a mostrar para poder pulsar los botones.
		Input.MouseMode = phase == GamePhase.Playing ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
	}

	private void LoadAudio()
	{
		foreach (string name in new[] { "bgm_menu", "bgm_game", "snd_shoot", "snd_player_hit", "snd_enemy_red", "snd_enemy_green", "snd_enemy_blue", "snd_enemy_boss", "snd_enemy_sentinel" })
		{
			AudioStream? stream = GD.Load<AudioStream>($"res://assets/audio/{name}.mp3");
			if (stream != null) _sounds[name] = stream;
		}
		_musicPlayer = new AudioStreamPlayer();
		AddChild(_musicPlayer);
	}

	private void PlayMusic(string name)
	{
		if (_musicPlayer == null || !_sounds.TryGetValue(name, out AudioStream? stream)) return;
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
	private static string EnemySound(EnemyType type) => type switch { EnemyType.Melee => "snd_enemy_red", EnemyType.Ranged => "snd_enemy_green", EnemyType.Tank => "snd_enemy_blue", EnemyType.Boss => "snd_enemy_boss", _ => "snd_enemy_sentinel" };
	private static string DifficultyName(Difficulty value) => value switch { Difficulty.Easy => "Fácil", Difficulty.Hard => "Difícil", _ => "Normal" };
	private static string QualityName(GraphicsQuality value) => value switch { GraphicsQuality.Performance => "Rendimiento", GraphicsQuality.HighDefinition => "Alta definición", _ => "Estándar" };
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

	private enum SpriteKind { Enemy, Projectile, Item }
	private enum ItemType { PotionRed, PotionBlue, Scroll }
	private enum ShopEntryKind { Potion, Weapon, Armor, Accessory }
	private enum UiAction
	{
		None,
		Start, OpenSettings,
		CycleDifficulty, CycleQuality, MusicDown, MusicUp, FxDown, FxUp, SensitivityDown, SensitivityUp, SettingsBack,
		LevelShop, LevelContinue, ShopPrevious, ShopBuy, ShopNext, ShopContinue,
		Retry, GameOverMenu, Resume, PauseQuit, VictoryMenu
	}

	private readonly record struct UiHit(UiAction Action, Rect2 Rect);
	private readonly record struct ShopEntry(string Label, int Cost, ShopEntryKind Kind, int Index);
	private readonly record struct WorldSprite(Vector2 Position, SpriteKind Kind, object Source, Color Color, float Scale);

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
		public bool Alive => Hp > 0f;

		public Enemy(Vector2 position, float hp, EnemyType type, float speed, int expReward, int goldMin, int goldMax)
		{
			Position = position; Hp = hp; Type = type; Speed = speed; ExpReward = expReward; GoldMin = goldMin; GoldMax = goldMax;
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
