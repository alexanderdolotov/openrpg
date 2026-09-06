using Godot;
using System.Collections.Generic;

// Implemented by anything that physically blocks movement and should
// therefore show up in PathGrid — Home's wall footprint, an
// AppleTree's trunk, a river crossing. Separate from IHasVisualBounds:
// not everything drawn is solid (FishingSpot has visual bounds but no
// collision), and not everything solid needs the same shape for
// camera-fit vs pathfinding purposes.
public interface IObstacle
{
    // One or more circles (in the node's own local space, offset from
    // its Position) that together approximate this object's solid
    // footprint. Most things need just one; an elongated obstacle (a
    // river crossing, a forest edge) chains several along its length
    // instead of being approximated by one giant circle that would
    // block far more than the shape itself. The same circles drive
    // both PathGrid and each object's own real CollisionShape2D setup,
    // so the two can't drift apart.
    IEnumerable<(Vector2 LocalOffset, float Radius)> GetObstacleCircles();
}
