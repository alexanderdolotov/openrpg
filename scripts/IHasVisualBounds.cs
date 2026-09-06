using Godot;

// Implemented by anything Main.cs needs to fit the camera around — a
// local-space (relative to the node's own Position) bounding rect of
// what's actually drawn. This is what lets FitCameraToWorld() adapt to
// whatever the world contains instead of the camera's position/zoom
// being hand-computed constants in Main.tscn that silently go stale
// every time the layout changes.
public interface IHasVisualBounds
{
    Rect2 GetLocalBounds();
}
