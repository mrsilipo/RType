using Microsoft.Xna.Framework;

namespace RType.Camera;

public readonly record struct SceneCamera(Matrix View, Matrix Projection, Vector3 Position);
