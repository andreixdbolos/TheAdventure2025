using Silk.NET.Maths;

namespace TheAdventure.Models;

public class SpeedBoostPowerUp : TemporaryGameObject
{
    private const double EffectDuration = 5.0; 
    private const double SpeedMultiplier = 2.0; 

    public SpeedBoostPowerUp(SpriteSheet spriteSheet, (int X, int Y) position) 
        : base(spriteSheet, EffectDuration, position)
    {
    }

    public static double GetSpeedMultiplier()
    {
        return SpeedMultiplier;
    }
} 