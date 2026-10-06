using UnityEngine;

public class TerrainTextureDetector : MonoBehaviour
{
    public Terrain terrain;

    public int GetTextureAtPoint(Vector3 point)
    {
        TerrainData terrainData = terrain.terrainData;
        Vector3 terrainPos = terrain.transform.position;

        // Convert world coordinates to terrain coordinates
        int mapX = Mathf.FloorToInt((point.x - terrainPos.x) / terrainData.size.x * terrainData.alphamapWidth);
        int mapZ = Mathf.FloorToInt((point.z - terrainPos.z) / terrainData.size.z * terrainData.alphamapHeight);

        // Get the alpha map at the given coordinates
        float[,,] splatmapData = terrainData.GetAlphamaps(mapX, mapZ, 1, 1);
        float max = 0;
        int maxIndex = 0;

        // Determine which texture has the highest weight
        for (int i = 0; i < splatmapData.GetLength(2); i++)
        {
            if (splatmapData[0, 0, i] > max)
            {
                max = splatmapData[0, 0, i];
                maxIndex = i;
            }
        }

        //Debug.Log("Texture index detected at point: " + maxIndex);
        return maxIndex;
    }

    public string MapTextureToCategory(int textureIndex)
    {
        // Map specific texture indices to general categories
        switch (textureIndex)
        {
            case 0: // Grass_1
            case 2: // Grass_5
            case 5: // Grass_3
            case 6: // Grass_4
            case 7: // Grass_8
            case 12: // Grass_2
            case 13: // Grass_7
            case 14: // Grass_6
                return "grass";
            case 3: // Sand_3
            case 8: // Sand_1
            case 11: // Sand_6
            case 15: // Sand_2
            case 16: // Sand_7
                return "sand";
            case 1: // Rockwall1
            case 4: // Rockwall2
                return "rock";
            case 9: // Snow
                return "snow";
            case 10: // Swamp
                return "swamp";
            default:
                return "default"; // Fallback surface type
        }
    }
}
