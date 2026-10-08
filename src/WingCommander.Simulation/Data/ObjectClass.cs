namespace WingCommander.Simulation.Data;

/// <summary>
/// Object class (<c>aeObjectClass</c>). 0 marks a free slot. The numeric order is
/// load-bearing: <c>&gt;= Projectile</c> = collidable, <c>&gt;= Missile</c> = steerable and
/// targetable, <c>&gt;= Ship</c> = has shields, armor, weapons, fuel and AI.
/// </summary>
/// <remarks>C: enum ObjectClass (include/wcdata.h).</remarks>
public enum ObjectClass
{
    Null = 0,
    Futurion = 1,
    Star = 2,
    Planet = 3,
    Dust = 4,
    Explosion = 5,
    Debris = 6,
    FixedObject = 7,
    Projectile = 8,
    Asteroid = 9,
    Mine = 10,
    Missile = 11,
    Ship = 12,
    CapitalShip = 13,
}
