using UnityEngine;
[CreateAssetMenu(fileName = "CustomTerrainLayer", menuName = "CustomTrrain/Custom Terrain Layer")]
public class CustomTerrainLayer : ScriptableObject
{
    [Header("Textures")]
    public Texture2D DiffuseMap;
    public Texture2D normalMap;
    public Texture2D roughnessMap;
}
