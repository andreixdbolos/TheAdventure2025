using Silk.NET.Maths;

namespace TheAdventure.Models;

public class PlayerObject : RenderableGameObject
{
    private const int _baseSpeed = 128; 
    private double _speedMultiplier = 1.0;
    private DateTimeOffset _speedBoostEndTime;
    private bool _hasSpeedBoost => _speedMultiplier > 1.0 && DateTimeOffset.Now < _speedBoostEndTime;

    public double RemainingBoostTime => _hasSpeedBoost 
        ? (_speedBoostEndTime - DateTimeOffset.Now).TotalSeconds 
        : 0.0;

    public bool HasSpeedBoost => _hasSpeedBoost;

    public enum PlayerStateDirection
    {
        None = 0,
        Down,
        Up,
        Left,
        Right,
    }

    public enum PlayerState
    {
        None = 0,
        Idle,
        Move,
        Attack,
        GameOver
    }

    public (PlayerState State, PlayerStateDirection Direction) State { get; private set; }

    public PlayerObject(SpriteSheet spriteSheet, int x, int y) : base(spriteSheet, (x, y))
    {
        SetState(PlayerState.Idle, PlayerStateDirection.Down);
    }

    public void ApplySpeedBoost(double multiplier, double duration)
    {
        _speedMultiplier = multiplier;
        _speedBoostEndTime = DateTimeOffset.Now.AddSeconds(duration);
    }

    public void SetState(PlayerState state)
    {
        SetState(state, State.Direction);
    }

    public void SetState(PlayerState state, PlayerStateDirection direction)
    {
        if (State.State == PlayerState.GameOver)
        {
            return;
        }

        if (State.State == state && State.Direction == direction)
        {
            return;
        }

        if (state == PlayerState.None && direction == PlayerStateDirection.None)
        {
            SpriteSheet.ActivateAnimation(null);
        }

        else if (state == PlayerState.GameOver)
        {
            SpriteSheet.ActivateAnimation(Enum.GetName(state));
        }
        else
        {
            var animationName = Enum.GetName(state) + Enum.GetName(direction);
            SpriteSheet.ActivateAnimation(animationName);
        }

        State = (state, direction);
    }

    public void GameOver()
    {
        SetState(PlayerState.GameOver, PlayerStateDirection.None);
    }

    public void Attack()
    {
        if (State.State == PlayerState.GameOver)
        {
            return;
        }

        var direction = State.Direction;
        SetState(PlayerState.Attack, direction);
    }

    public void UpdatePosition(double up, double down, double left, double right, int width, int height, double time)
    {
        if (State.State == PlayerState.GameOver)
        {
            return;
        }

        if (_speedMultiplier > 1.0 && DateTimeOffset.Now >= _speedBoostEndTime)
        {
            _speedMultiplier = 1.0;
        }

        var currentSpeed = _baseSpeed * _speedMultiplier;
        var pixelsToMove = currentSpeed * (time / 1000.0);

        var x = Position.X + (int)(right * pixelsToMove);
        x -= (int)(left * pixelsToMove);

        var y = Position.Y + (int)(down * pixelsToMove);
        y -= (int)(up * pixelsToMove);

        var newState = State.State;
        var newDirection = State.Direction;

        if (x == Position.X && y == Position.Y)
        {
            if (State.State == PlayerState.Attack)
            {
                if (SpriteSheet.AnimationFinished)
                {
                    newState = PlayerState.Idle;
                }
            }
            else
            {
                newState = PlayerState.Idle;
            }
        }
        else
        {
            newState = PlayerState.Move;
            
            if (y < Position.Y && newDirection != PlayerStateDirection.Up)
            {
                newDirection = PlayerStateDirection.Up;
            }

            if (y > Position.Y && newDirection != PlayerStateDirection.Down)
            {
                newDirection = PlayerStateDirection.Down;
            }

            if (x < Position.X && newDirection != PlayerStateDirection.Left)
            {
                newDirection = PlayerStateDirection.Left;
            }

            if (x > Position.X && newDirection != PlayerStateDirection.Right)
            {
                newDirection = PlayerStateDirection.Right;
            }
        }

        if (newState != State.State || newDirection != State.Direction)
        {
            SetState(newState, newDirection);
        }

        Position = (x, y);
    }

    public void Render(GameRenderer renderer)
    {
        base.Render(renderer);

        if (HasSpeedBoost)
        {
            const int barWidth = 32;
            const int barHeight = 4;
            const int barYOffset = -20; 

            renderer.SetDrawColor(100, 100, 100, 255);
            renderer.FillRect(new Rectangle<int>(
                Position.X - barWidth/2,
                Position.Y + barYOffset,
                barWidth,
                barHeight
            ));

            var progress = RemainingBoostTime / 5.0; 
            renderer.SetDrawColor(255, 255, 0, 255);
            renderer.FillRect(new Rectangle<int>(
                Position.X - barWidth/2,
                Position.Y + barYOffset,
                (int)(barWidth * progress),
                barHeight
            ));
        }
    }
}