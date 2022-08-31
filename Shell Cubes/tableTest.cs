using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ConstructSurface.SurfaceGenerationMethod;

public class tableTest : MonoBehaviour
{
    Vector3[] edges = new Vector3[] {
        new Vector3(0, 0, 0.5f), new Vector3(0.5f, 0, 1f), new Vector3(1f, 0, 0.5f), new Vector3(0.5f, 0, 0),
        new Vector3(0, 1f, 0.5f), new Vector3(0.5f, 1f, 1f), new Vector3(1f, 1f, 0.5f), new Vector3(0.5f, 1f, 0),
        new Vector3(0, 0.5f, 0), new Vector3(0, 0.5f, 1f), new Vector3(1f, 0.5f, 1f), new Vector3(1f, 0.5f, 0f)
    };  
    // Start is called before the first frame update
    void Start()
    {
        for(int i = 0; i < 256; i++)
        {
            Vector3[] vertice = new Vector3[12];
            for (int o = 0; o < 12; o++) vertice[o] = new Vector3(-1f, -1f, -1f);
            for(int e = 0; e < 12; e++)
            {
                if ((ShellCubes.edgeTable[i] & 1 << e) != 0)
                    vertice[e] = edges[e];
            }
            for(int k = 0; k < 18; k += 3)
            {
                if (ShellCubes.triTable[i][k] < 0) break;
                //Debug.Log(triTable[i][k] + $"Case: {i}");
                Vector3 vert1 = vertice[ShellCubes.triTable[i][k]];
                Vector3 vert2 = vertice[ShellCubes.triTable[i][k + 1]];
                Vector3 vert3 = vertice[ShellCubes.triTable[i][k + 2]];
                if(vert1 == new Vector3(-1f, -1f, -1f) || vert2 == new Vector3(-1f, -1f, -1f) || vert3 == new Vector3(-1f, -1f, -1f))
                {
                    Debug.Log($"Case with Error: {i}");
                    //break;
                }
            }
        }

        Debug.Log("Done");
    }
}
