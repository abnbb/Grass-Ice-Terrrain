// PackedGrassAsset.cs
using UnityEngine;

[CreateAssetMenu(fileName = "PackedGrassData", menuName = "Grass/Packed Asset")]
public class PackedGrassAsset : ScriptableObject
{
    public Vector4[] packedData;        // 每个实例一个 float4
    public float maxScale = 4.0f;      // 解压时所需的最大缩放值
}