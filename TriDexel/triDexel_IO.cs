using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ConstructSurface.TriDexelModel2D;

public class triDexel_IO : MonoBehaviour
{
    [SerializeField]
    int resolution_ref;
    [SerializeField]
    int x_resolution;
    [SerializeField]
    int y_resolution;
    [SerializeField]
    int z_resolution;
    [SerializeField]
    float gridSize;
    [SerializeField]
    GameObject inputModel;

    public bool zMap;
    public bool xMap;
    public bool yMap;

    public int idxStart_x;
    public int idxStart_y;
    public int idxStart_z;
    public int idxEnd_x;
    public int idxEnd_y;
    public int idxEnd_z;

    triDexelModel2D triDexel;
    Transform modelTransform;

    private void Start()
    {
        DateTime start = DateTime.Now;
        // check if the input object has a collider, if yes then replace it with mesh collider whatsoever
        Destroy(inputModel.GetComponent<Collider>());
        inputModel.AddComponent<MeshCollider>();
        // create tri-dexel object
        triDexel = new triDexelModel2D(inputModel, resolution_ref);
        modelTransform = inputModel.transform;
        DateTime end = DateTime.Now;
        //Debug.Log("tridexelization time: " + (end - start).TotalMilliseconds);

        idxStart_x = 0;
        idxStart_y = 0;
        idxStart_z = 0;
        idxEnd_x = triDexel.Resolution_x;
        idxEnd_y = triDexel.Resolution_y;
        idxEnd_z = triDexel.Resolution_z;
    }

    private void Update()
    {
         triDexelVisualization();
    }

    void triDexelVisualization()
    {
        Vector3 minBoundPoint = triDexel.FieldOrigin;
        
        Vector3 xy_origin = new Vector3(minBoundPoint.x, minBoundPoint.y, minBoundPoint.z);
        Vector3 zy_origin = new Vector3(minBoundPoint.x, minBoundPoint.y, minBoundPoint.z);
        Vector3 xz_origin = new Vector3(minBoundPoint.x, minBoundPoint.y, minBoundPoint.z);

        // get 2D array data from tri-dexel object
        Dexel[,] xy = (Dexel[,])triDexel.XY_array.Clone();
        Dexel[,] zy = (Dexel[,])triDexel.ZY_array.Clone();
        Dexel[,] xz = (Dexel[,])triDexel.XZ_array.Clone();

        x_resolution = triDexel.Resolution_x;
        y_resolution = triDexel.Resolution_y;
        z_resolution = triDexel.Resolution_z;     
        gridSize = triDexel.GridSize;

        // visualize tri-dexel data
        if (zMap)
        {
            for (int i = idxStart_y; i < idxEnd_y + 1; i++) // z direction (xy array)
            {
                for (int j = idxStart_x; j < idxEnd_x + 1; j++)
                {
                    if (xy[i, j].DexelPoints != null)
                    {
                        for (int k = 0; k < xy[i, j].DexelPoints.Count; k += 2)
                        {
                            Debug.DrawLine(modelTransform.TransformPoint(xy_origin + i * gridSize * Vector3.up + j * gridSize * Vector3.right + Vector3.forward * (xy[i, j].DexelPoints[k] - xy_origin.z)),
                                modelTransform.TransformPoint(xy_origin + i * gridSize * Vector3.up + j * gridSize * Vector3.right + Vector3.forward * (xy[i, j].DexelPoints[k + 1] - xy_origin.z)),
                                Color.cyan);
                        }
                    }
                }
            }
        }

        if(xMap)
        {
            for (int i = idxStart_y; i < idxEnd_y + 1; i++) // x direction (zy array)
            {
                for (int j = idxStart_z; j < idxEnd_z + 1; j++)
                {
                    if (zy[i, j].DexelPoints != null)
                    {
                        for (int k = 0; k < zy[i, j].DexelPoints.Count; k += 2)
                        {
                            Debug.DrawLine(modelTransform.TransformPoint(zy_origin + i * gridSize * Vector3.up + j * gridSize * Vector3.forward + Vector3.right * (zy[i, j].DexelPoints[k] - zy_origin.x)),
                                modelTransform.TransformPoint(zy_origin + i * gridSize * Vector3.up + j * gridSize * Vector3.forward + Vector3.right * (zy[i, j].DexelPoints[k + 1] - zy_origin.x)),
                                Color.yellow);
                        }
                    }
                }
            }
        }

        if(yMap)
        {
            for (int i = idxStart_z; i < idxEnd_z + 1; i++) // y direction (xz array)
            {
                for (int j = idxStart_x; j < idxEnd_x + 1; j++)
                {
                    if (xz[i, j].DexelPoints != null)
                    {
                        for (int k = 0; k < xz[i, j].DexelPoints.Count; k += 2)
                        {
                            Debug.DrawLine(modelTransform.TransformPoint(xz_origin + i * gridSize * Vector3.forward + j * gridSize * Vector3.right + Vector3.up * (xz[i, j].DexelPoints[k] - xz_origin.y)),
                                modelTransform.TransformPoint(xz_origin + i * gridSize * Vector3.forward + j * gridSize * Vector3.right + Vector3.up * (xz[i, j].DexelPoints[k + 1] - xz_origin.y)),
                                Color.red);
                        }
                    }
                }
            }
        }
    }

    //private void OnDrawGizmos()
    //{
    //    Transform modelTransform = inputModel.transform;

    //    Gizmos.color = Color.black;
    //    Gizmos.DrawSphere(modelTransform.TransformPoint(triDexel.MinBoundPoint_local), 0.1f);
    //    Debug.Log(modelTransform.TransformPoint(triDexel.MinBoundPoint_local));
    //    Gizmos.DrawSphere(modelTransform.TransformPoint(triDexel.MaxBoundPoint_local), 0.1f);
    //    Debug.Log(modelTransform.TransformPoint(triDexel.MaxBoundPoint_local));
    //}

    //private void OnDrawGizmos()
    //{
    //    Gizmos.color = Color.yellow;
    //    Vector3 origin = triDexel.MinBoundPoint_local;
    //    Transform modelTransform = inputModel.transform;
    //    for (int i = idxStart_x; i <= idxEnd_x; i++)
    //    {
    //        for (int j = idxStart_y; j <= idxEnd_y; j++)
    //        {
    //            for (int k = idxStart_z; k <= idxEnd_z; k++)
    //            {
    //                Vector3 center = modelTransform.TransformPoint((origin + Vector3.right * (i + 0.5f) * triDexel.GridSize + Vector3.up * (j + 0.5f) * triDexel.GridSize + Vector3.forward * (k + 0.5f) * triDexel.GridSize));
    //                float gridLengthWorld_x = modelTransform.TransformVector(Vector3.right * triDexel.GridSize).magnitude;
    //                float gridLengthWorld_y = modelTransform.TransformVector(Vector3.up * triDexel.GridSize).magnitude;
    //                float gridLengthWorld_z = modelTransform.TransformVector(Vector3.forward * triDexel.GridSize).magnitude;
    //                Gizmos.DrawCube(center, new Vector3(gridLengthWorld_x, gridLengthWorld_y, gridLengthWorld_z));
    //            }
    //        }
    //    }
    //}
}
