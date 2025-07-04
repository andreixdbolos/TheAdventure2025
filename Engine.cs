using System.Reflection;
using System.Text.Json;
using Silk.NET.Maths;
using TheAdventure.Models;
using TheAdventure.Models.Data;
using TheAdventure.Scripting;

namespace TheAdventure;

public class Engine
{
    private readonly GameRenderer _renderer;
    private readonly Input _input;
    private readonly ScriptEngine _scriptEngine = new();
    private readonly Random _random = new();
    private DateTimeOffset _lastPowerUpSpawn = DateTimeOffset.Now;
    private const double PowerUpSpawnInterval = 10.0; 

    private readonly Dictionary<int, GameObject> _gameObjects = new();
    private readonly Dictionary<string, TileSet> _loadedTileSets = new();
    private readonly Dictionary<int, Tile> _tileIdMap = new();
    private bool _isInvincible = false;
    private DateTimeOffset _lastInvincibilityToggle = DateTimeOffset.Now;
    private const double InvincibilityToggleCooldown = 0.5; // Half second cooldown between toggles

    private Level _currentLevel = new();
    private PlayerObject? _player;

    private DateTimeOffset _lastUpdate = DateTimeOffset.Now;

    public Engine(GameRenderer renderer, Input input)
    {
        _renderer = renderer;
        _input = input;

        _input.OnMouseClick += (_, coords) => AddBomb(coords.x, coords.y);
    }

    public void SetupWorld()
    {
        _player = new(SpriteSheet.Load(_renderer, "Player.json", "Assets"), 100, 100);

        var levelContent = File.ReadAllText(Path.Combine("Assets", "terrain.tmj"));
        var level = JsonSerializer.Deserialize<Level>(levelContent);
        if (level == null)
        {
            throw new Exception("Failed to load level");
        }

        foreach (var tileSetRef in level.TileSets)
        {
            var tileSetContent = File.ReadAllText(Path.Combine("Assets", tileSetRef.Source));
            var tileSet = JsonSerializer.Deserialize<TileSet>(tileSetContent);
            if (tileSet == null)
            {
                throw new Exception("Failed to load tile set");
            }

            foreach (var tile in tileSet.Tiles)
            {
                tile.TextureId = _renderer.LoadTexture(Path.Combine("Assets", tile.Image), out _);
                _tileIdMap.Add(tile.Id!.Value, tile);
            }

            _loadedTileSets.Add(tileSet.Name, tileSet);
        }

        if (level.Width == null || level.Height == null)
        {
            throw new Exception("Invalid level dimensions");
        }

        if (level.TileWidth == null || level.TileHeight == null)
        {
            throw new Exception("Invalid tile dimensions");
        }

        _renderer.SetWorldBounds(new Rectangle<int>(0, 0, level.Width.Value * level.TileWidth.Value,
            level.Height.Value * level.TileHeight.Value));

        _currentLevel = level;

        _scriptEngine.LoadAll(Path.Combine("Assets", "Scripts"));
    }

    public void ProcessFrame()
    {
        var currentTime = DateTimeOffset.Now;
        var msSinceLastFrame = (currentTime - _lastUpdate).TotalMilliseconds;
        _lastUpdate = currentTime;

        if (_player == null)
        {
            return;
        }

        if (_input.IsKeyIPressed())
        {
            var timeSinceLastToggle = (currentTime - _lastInvincibilityToggle).TotalSeconds;
            if (timeSinceLastToggle >= InvincibilityToggleCooldown)
            {
                _isInvincible = !_isInvincible;
                _lastInvincibilityToggle = currentTime;
                Console.WriteLine($"Invincibility {( _isInvincible ? "enabled" : "disabled" )}");
            }
        }

        var timeSinceLastSpawn = (currentTime - _lastPowerUpSpawn).TotalSeconds;
        if (timeSinceLastSpawn >= PowerUpSpawnInterval)
        {
            Console.WriteLine($"Time to spawn power-up! {timeSinceLastSpawn} seconds since last spawn");
            SpawnPowerUp();
            _lastPowerUpSpawn = currentTime;
        }

        double up = _input.IsUpPressed() ? 1.0 : 0.0;
        double down = _input.IsDownPressed() ? 1.0 : 0.0;
        double left = _input.IsLeftPressed() ? 1.0 : 0.0;
        double right = _input.IsRightPressed() ? 1.0 : 0.0;
        bool isAttacking = _input.IsKeyAPressed() && (up + down + left + right <= 1);
        bool addBomb = _input.IsKeyBPressed();

        _player.UpdatePosition(up, down, left, right, 48, 48, msSinceLastFrame);
        if (isAttacking)
        {
            _player.Attack();
        }
        
        _scriptEngine.ExecuteAll(this);

        if (addBomb)
        {
            AddBomb(_player.Position.X, _player.Position.Y, false);
        }

        CheckPowerUpCollection();
    }

    public void RenderFrame()
    {
        _renderer.SetDrawColor(0, 0, 0, 255);
        _renderer.ClearScreen();

        var playerPosition = _player!.Position;
        _renderer.CameraLookAt(playerPosition.X, playerPosition.Y);

        RenderTerrain();
        RenderAllObjects();

        var statusText = $"Invincibility: {(_isInvincible ? "ON" : "OFF")}";
        _renderer.RenderText(statusText, _renderer.Window.Size.Width - 200, 20, 
            _isInvincible ? (byte)0 : (byte)255, 
            _isInvincible ? (byte)255 : (byte)0, 
            (byte)0);

        _renderer.PresentFrame();
    }

    public void RenderAllObjects()
    {
        var toRemove = new List<int>();
        foreach (var gameObject in GetRenderables())
        {
            gameObject.Render(_renderer);
            if (gameObject is TemporaryGameObject { IsExpired: true } tempGameObject)
            {
                toRemove.Add(tempGameObject.Id);
            }
        }

        foreach (var id in toRemove)
        {
            _gameObjects.Remove(id, out var gameObject);

            if (_player == null)
            {
                continue;
            }

            var tempGameObject = (TemporaryGameObject)gameObject!;
            var deltaX = Math.Abs(_player.Position.X - tempGameObject.Position.X);
            var deltaY = Math.Abs(_player.Position.Y - tempGameObject.Position.Y);
            if (deltaX < 32 && deltaY < 32 && !_isInvincible)
            {
                _player.GameOver();
            }
        }

        _player?.Render(_renderer);
    }

    public void RenderTerrain()
    {
        foreach (var currentLayer in _currentLevel.Layers)
        {
            for (int i = 0; i < _currentLevel.Width; ++i)
            {
                for (int j = 0; j < _currentLevel.Height; ++j)
                {
                    int? dataIndex = j * currentLayer.Width + i;
                    if (dataIndex == null)
                    {
                        continue;
                    }

                    var currentTileId = currentLayer.Data[dataIndex.Value] - 1;
                    if (currentTileId == null)
                    {
                        continue;
                    }

                    var currentTile = _tileIdMap[currentTileId.Value];

                    var tileWidth = currentTile.ImageWidth ?? 0;
                    var tileHeight = currentTile.ImageHeight ?? 0;

                    var sourceRect = new Rectangle<int>(0, 0, tileWidth, tileHeight);
                    var destRect = new Rectangle<int>(i * tileWidth, j * tileHeight, tileWidth, tileHeight);
                    _renderer.RenderTexture(currentTile.TextureId, sourceRect, destRect);
                }
            }
        }
    }

    public IEnumerable<RenderableGameObject> GetRenderables()
    {
        foreach (var gameObject in _gameObjects.Values)
        {
            if (gameObject is RenderableGameObject renderableGameObject)
            {
                yield return renderableGameObject;
            }
        }
    }

    public (int X, int Y) GetPlayerPosition()
    {
        return _player!.Position;
    }

    public void AddBomb(int X, int Y, bool translateCoordinates = true)
    {
        var worldCoords = translateCoordinates ? _renderer.ToWorldCoordinates(X, Y) : new Vector2D<int>(X, Y);

        SpriteSheet spriteSheet = SpriteSheet.Load(_renderer, "BombExploding.json", "Assets");
        spriteSheet.ActivateAnimation("Explode");

        TemporaryGameObject bomb = new(spriteSheet, 2.1, (worldCoords.X, worldCoords.Y));
        _gameObjects.Add(bomb.Id, bomb);
    }

    private void SpawnPowerUp()
    {
        if (_currentLevel.Width == null || _currentLevel.Height == null)
        {
            Console.WriteLine("Cannot spawn power-up: Level dimensions are null");
            return;
        }

        int x = _random.Next(0, _currentLevel.Width.Value * (_currentLevel.TileWidth ?? 32));
        int y = _random.Next(0, _currentLevel.Height.Value * (_currentLevel.TileHeight ?? 32));

        Console.WriteLine($"Attempting to spawn power-up at position ({x}, {y})");
        
        try
        {
            SpriteSheet spriteSheet = SpriteSheet.Load(_renderer, "SpeedBoost.json", "Assets");
            spriteSheet.ActivateAnimation("Idle");
            var powerUp = new SpeedBoostPowerUp(spriteSheet, (x, y));
            _gameObjects.Add(powerUp.Id, powerUp);
            Console.WriteLine($"Successfully spawned power-up with ID {powerUp.Id}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to spawn power-up: {ex.Message}");
        }
    }

    private void CheckPowerUpCollection()
    {
        if (_player == null) return;

        var toRemove = new List<int>();
        foreach (var gameObject in _gameObjects.Values)
        {
            if (gameObject is SpeedBoostPowerUp powerUp)
            {
                var deltaX = Math.Abs(_player.Position.X - powerUp.Position.X);
                var deltaY = Math.Abs(_player.Position.Y - powerUp.Position.Y);
                
                if (deltaX < 32 && deltaY < 32)
                {
                    _player.ApplySpeedBoost(SpeedBoostPowerUp.GetSpeedMultiplier(), 5.0);
                    toRemove.Add(powerUp.Id);
                }
            }
        }

        foreach (var id in toRemove)
        {
            _gameObjects.Remove(id);
        }
    }
}