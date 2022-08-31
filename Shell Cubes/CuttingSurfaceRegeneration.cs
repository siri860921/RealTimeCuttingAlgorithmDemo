using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ConstructSurface.TriDexelModel2D;
using ConstructSurface.SurfaceGenerationMethod;


public class CuttingSurfaceRegeneration : MonoBehaviour
{
    enum ConstructSurfaceMethod { Compute, LookUp };

    [SerializeField]
    [Tooltip("the object to regenerate surface")]
    GameObject inputModel;
    [SerializeField]
    [Tooltip("the resolution of the surface reconstruction method")]
    int resolution;
    [SerializeField]
    [Tooltip("the resolution on the x direction")]
    int resolution_x;
    [SerializeField]
    [Tooltip("the resolution on the y direction")]
    int resolution_y;
    [SerializeField]
    [Tooltip("the resolution on the z direction")]
    int resolution_z;
    [SerializeField]
    [Tooltip("the grid length")]
    float gridLength;

    public int idxStart_x;
    public int idxStart_y;
    public int idxStart_z;
    public int idxEnd_x;
    public int idxEnd_y;
    public int idxEnd_z;

    public bool zMap;
    public bool xMap;
    public bool yMap;

    [SerializeField]
    ConstructSurfaceMethod method;

    private Mesh modelMesh;
    private Vector3[] reconstructedVertices;
    private int[] reconstructedTriangles;
    private triDexelModel2D triDexel;
    private ShellCubes meshExtraction;
    private surfaceMeshCompute meshExtractionCompute;
    private Transform modelTransform;

    private void Start()
    {
        // substitute the original collider on the input model with mesh collider
        Destroy(inputModel.GetComponent<Collider>());
        inputModel.AddComponent<MeshCollider>();
        modelTransform = inputModel.transform;

        // dexelize the input model into tri-dexel data structure
        DateTime start = DateTime.Now;
        triDexel = new triDexelModel2D(inputModel, resolution);
        DateTime end = DateTime.Now;
        //Debug.Log("Dexelization time: " + (end - start).TotalSeconds);
        // assign mesh class reference
        modelMesh = inputModel.GetComponent<MeshFilter>().mesh;
        construcSurface(triDexel);
    }

    private void Update()
    {
        //construcSurface(triDexel);
        triDexelVisualization();
    }

    void construcSurface(triDexelModel2D tridexel)
    {
        idxStart_x = 0;
        idxStart_y = 0;
        idxStart_z = 0;
        idxEnd_x = triDexel.Resolution_x;
        idxEnd_y = triDexel.Resolution_y;
        idxEnd_z = triDexel.Resolution_z;

        if(method == ConstructSurfaceMethod.Compute)
        {
            DateTime start = DateTime.Now;
            meshExtractionCompute = new surfaceMeshCompute(tridexel);
            resolution_x = meshExtractionCompute.Resolution_x;
            resolution_y = meshExtractionCompute.Resolution_y;
            resolution_z = meshExtractionCompute.Resolution_z;
            gridLength = (modelTransform.TransformVector(meshExtractionCompute.GridLength * Vector3.back)).magnitude;

            meshExtractionCompute.ComputeSurfaceMesh();

            modelMesh.Clear();
            modelMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            modelMesh.vertices = meshExtractionCompute.RegenVertices;
            modelMesh.triangles = meshExtractionCompute.RegenTriangles;
            DateTime end = DateTime.Now;
            Debug.Log($"Compute: {(end - start).TotalMilliseconds}ms");

            Debug.Log($"Number of vertices: {modelMesh.vertices.Length}");
            Debug.Log($"Number of triangles: {modelMesh.triangles.Length / 3}");
        }

        else if(method == ConstructSurfaceMethod.LookUp)
        {
            DateTime start = DateTime.Now;
            meshExtraction = new ShellCubes(tridexel);
            resolution_x = meshExtraction.Resolution_x;
            resolution_y = meshExtraction.Resolution_y;
            resolution_z = meshExtraction.Resolutiob_z;
            gridLength = (modelTransform.TransformVector(meshExtraction.GridLength * Vector3.back)).magnitude;

            meshExtraction.ComputeSurfaceMesh();

            modelMesh.Clear();
            modelMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            modelMesh.vertices = meshExtraction.RegenVertices;
            modelMesh.triangles = meshExtraction.RegenTriangles;
            modelMesh.normals = meshExtraction.RegenNormals;
            DateTime end = DateTime.Now;
            Debug.Log($"Compute: {(end - start).TotalMilliseconds}ms");

            Debug.Log($"Number of vertices: {modelMesh.vertices.Length}");
            Debug.Log($"Number of triangles: {modelMesh.triangles.Length / 3}");
        }

        modelMesh.RecalculateNormals();
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
                            Debug.DrawLine(modelTransform.TransformPoint(xy_origin + i * meshExtraction.GridLength * Vector3.up + j * meshExtraction.GridLength * Vector3.right + Vector3.forward * (xy[i, j].DexelPoints[k] - xy_origin.z)),
                                modelTransform.TransformPoint(xy_origin + i * meshExtraction.GridLength * Vector3.up + j * meshExtraction.GridLength * Vector3.right + Vector3.forward * (xy[i, j].DexelPoints[k + 1] - xy_origin.z)),
                                Color.blue);
                        }
                    }
                }
            }
        }

        if (xMap)
        {
            for (int i = idxStart_y; i < idxEnd_y + 1; i++) // x direction (zy array)
            {
                for (int j = idxStart_z; j < idxEnd_z + 1; j++)
                {
                    if (zy[i, j].DexelPoints != null)
                    {
                        for (int k = 0; k < zy[i, j].DexelPoints.Count; k += 2)
                        {
                            Debug.DrawLine(modelTransform.TransformPoint(zy_origin + i * meshExtraction.GridLength * Vector3.up + j * meshExtraction.GridLength * Vector3.forward + Vector3.right * (zy[i, j].DexelPoints[k] - zy_origin.x)),
                                modelTransform.TransformPoint(zy_origin + i * meshExtraction.GridLength * Vector3.up + j * meshExtraction.GridLength * Vector3.forward + Vector3.right * (zy[i, j].DexelPoints[k + 1] - zy_origin.x)),
                                new Color(0, 0.5f, 0, 1));
                        }
                    }
                }
            }
        }

        if (yMap)
        {
            for (int i = idxStart_z; i < idxEnd_z + 1; i++) // y direction (xz array)
            {
                for (int j = idxStart_x; j < idxEnd_x + 1; j++)
                {
                    if (xz[i, j].DexelPoints != null)
                    {
                        for (int k = 0; k < xz[i, j].DexelPoints.Count; k += 2)
                        {
                            Debug.DrawLine(modelTransform.TransformPoint(xz_origin + i * meshExtraction.GridLength * Vector3.forward + j * meshExtraction.GridLength * Vector3.right + Vector3.up * (xz[i, j].DexelPoints[k] - xz_origin.y)),
                                modelTransform.TransformPoint(xz_origin + i * meshExtraction.GridLength * Vector3.forward + j * meshExtraction.GridLength * Vector3.right + Vector3.up * (xz[i, j].DexelPoints[k + 1] - xz_origin.y)),
                                Color.red);
                        }
                    }
                }
            }
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 0f, 0f, 0.5f);
        //Gizmos.color = Color.black;
        Vector3 origin = triDexel.FieldOrigin;
        if (origin == null) return;
        bool[,,] voxelStates = meshExtraction.VoxelStates;
        Transform modelTransform = inputModel.transform;
        for (int i = 0; i < resolution_x; i++)
        {
            for (int j = 0; j < resolution_y; j++)
            {
                for (int k = 0; k < resolution_z; k++)
                {
                    if (voxelStates[i, j, k] == true)
                    {
                        Vector3 center = modelTransform.TransformPoint((origin + Vector3.right * (i + 0.5f) * meshExtraction.GridLength + Vector3.up * (j + 0.5f) * meshExtraction.GridLength + Vector3.forward * (k + 0.5f) * meshExtraction.GridLength));
                        float gridLengthWorld_x = modelTransform.TransformVector(Vector3.right * meshExtraction.GridLength).magnitude;
                        float gridLengthWorld_y = modelTransform.TransformVector(Vector3.up * meshExtraction.GridLength).magnitude;
                        float gridLengthWorld_z = modelTransform.TransformVector(Vector3.forward * meshExtraction.GridLength).magnitude;
                        Gizmos.DrawWireCube(center, new Vector3(gridLengthWorld_x, gridLengthWorld_y, gridLengthWorld_z));
                    }
                }
            }
        }
    }
}
