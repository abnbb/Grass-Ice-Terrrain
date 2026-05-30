// GrassMatrixAsset.cs
using UnityEngine;

[CreateAssetMenu(fileName = "GrassMatrices", menuName = "Grass/Matrix Asset")]
public class GrassMatrixAsset : ScriptableObject
{
    public Matrix4x4[] matrices;
}