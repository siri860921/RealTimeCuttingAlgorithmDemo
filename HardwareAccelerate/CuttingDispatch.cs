using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ConstructSurface.SurfaceGenerationMethod;

public class CuttingDispatch : MonoBehaviour
{
    public List<GameObject> colliderObjects; // sampling points
    [SerializeField]
    GameObject thicknessObject1; // first reference thickness point, used to determine cutting thickness
    [SerializeField]
    GameObject thicknessObject2; // second reference thickness point, used to determine cutting thickness
    [SerializeField]
    GameObject depthObject1; // the base reference depth point, used to determine maximum depth of cut
    [SerializeField]
    GameObject depthObejct2; // the top reference depth point, used to determine maximum depth of cut
    [SerializeField]
    ComputeShader triDexelModifier; // cutting algorithm shader

    bool collide = false; // cutting control variable
    Transform modelTransform; // the transform of the sliced object
    MeshCollider modelCollider; // the collider of the sliced object

    CuttingSegment[] cuttingAreas; // the cutting trajectory of the cutting tool composed of cutting segments
    ShellCubesDispatch meshUpdater; // the Shell Cubes algorithm component of the sliced object

    ComputeBuffer areaBuffer; // compute buffer for the cutting area
    RenderTexture xyTexture; // xy dexel set
    RenderTexture zyTexture; // zy dexel set
    RenderTexture xzTexture; // xz dexel set

    int kernelModifierXY; 
    int kernelModifierZY;
    int kernelModifierXZ;

    // single cutting segment composed of two sampling points
    struct CuttingSegment
    {
        public Vector3 boundPoint1;
        public Vector3 boundPoint2;
    }

    void Start()
    {
        cuttingAreas = new CuttingSegment[colliderObjects.Count - 1];
    }
    
    void OnTriggerEnter(Collider other)
    {
        // filter out unwanted collision
        if (other.gameObject.tag != "BeCut") return;
        
        // get the collider and Shell Cubes algorthm components from the sliced object
        modelTransform = other.transform;
        if (modelCollider == null || meshUpdater == null)
        {
            modelCollider = other.GetComponent<MeshCollider>();
            meshUpdater = other.GetComponent<ShellCubesDispatch>();
        }

        // enable cutting algorithm and Shell Cubes algorithm execution
        collide = true;
        meshUpdater.enabled = true;
    }

    private void OnTriggerExit(Collider other)
    {
        try 
        {
            // disable cutting algorithm and enable the 3DCCL algorithm 
            collide = false;
            meshUpdater.EnableCCL = true;

            // update the collider of the sliced object
            modelCollider.sharedMesh = meshUpdater.ModelMesh;
            areaBuffer.Release();
        }
        catch {}
        modelCollider = null;
        meshUpdater = null;
        areaBuffer = null;
    }

    void Update()
    {
        if (collide)
        {
            // calculate the cutting tool thickness and maximum depth of cut
            // transform them to sliced object coordinate
            Vector3 thick = modelTransform.InverseTransformVector(thicknessObject2.transform.position - thicknessObject1.transform.position);
            Vector3 maxCutVec = modelTransform.InverseTransformVector(depthObject1.transform.position - depthObejct2.transform.position);
            float maxCut = maxCutVec.magnitude;

            // get the current sampling points positions and transform them to sliced object coordinate
            for (int i = 0; i < cuttingAreas.Length; ++i)
            {
                CuttingSegment area;
                area.boundPoint1 = modelTransform.transform.InverseTransformPoint(colliderObjects[i].transform.position);
                area.boundPoint2 = modelTransform.transform.InverseTransformPoint(colliderObjects[i + 1].transform.position);
                cuttingAreas[i] = area;
            }

            // execute cutting
            Dispatch(ref thick, ref maxCut);
        }
    }

    // initialize compute shader 
    void Initialize()
    {
        // allocate compute buffers
        areaBuffer = new ComputeBuffer(cuttingAreas.Length, 6 * sizeof(float));
        xyTexture = meshUpdater.XYTexture;
        zyTexture = meshUpdater.ZYTexture;
        xzTexture = meshUpdater.XZTexture;

        // find kernel ID
        kernelModifierXY = triDexelModifier.FindKernel("TriDexelModifier_XY");
        kernelModifierZY = triDexelModifier.FindKernel("TriDexelModifier_ZY");
        kernelModifierXZ = triDexelModifier.FindKernel("TriDexelModifier_XZ");

        // set shader parameters
        triDexelModifier.SetInt("resolution_x", meshUpdater.Resolution_x);
        triDexelModifier.SetInt("resolution_y", meshUpdater.Resolution_y);
        triDexelModifier.SetInt("resolution_z", meshUpdater.Resolution_z);
        triDexelModifier.SetInt("dexelSegments", meshUpdater.DexelSegments);
        triDexelModifier.SetFloat("gridSize", meshUpdater.GridSize);
        triDexelModifier.SetFloats("fieldOrigin", new float[4] { meshUpdater.FieldOrigin.x, meshUpdater.FieldOrigin.y, meshUpdater.FieldOrigin.z, 0});
    }

    // execute the cutting algorithm
    void Dispatch(ref Vector3 thick, ref float maxCut)
    {
        // initialize computeshader if one of the buffers is null
        if (areaBuffer == null) Initialize();

        // set cutter information
        triDexelModifier.SetFloat("maxCut", maxCut);
        triDexelModifier.SetFloats("cutterWidth", new float[4] { thick.x, thick.y, thick.z, 0f });
        areaBuffer.SetData(cuttingAreas);

        // set tri-dexel data to compute shader
        triDexelModifier.SetTexture(kernelModifierXY, "xyTexture", xyTexture);
        triDexelModifier.SetTexture(kernelModifierZY, "zyTexture", zyTexture);
        triDexelModifier.SetTexture(kernelModifierXZ, "xzTexture", xzTexture);

        // upload cutting segments to compute shader
        triDexelModifier.SetBuffer(kernelModifierXY, "cuttingAreas", areaBuffer);
        triDexelModifier.SetBuffer(kernelModifierZY, "cuttingAreas", areaBuffer);
        triDexelModifier.SetBuffer(kernelModifierXZ, "cuttingAreas", areaBuffer);

        //Debug.Log($"{deltaDisplacement.x.ToString("#0.00000000000000")}, {deltaDisplacement.y.ToString("#0.00000000000000")}, {deltaDisplacement.z.ToString("#0.00000000000000")}");
        //selectively dispatch the compute shader
        //float max = Mathf.Max(Mathf.Abs(deltaDisplacement.x), Mathf.Abs(deltaDisplacement.y), Mathf.Abs(deltaDisplacement.z));
        //if (max == Mathf.Abs(deltaDisplacement.x))
        //{
        //    Debug.Log("NO X");
        //    triDexelModifier.Dispatch(kernelModifierXY, Mathf.CeilToInt((meshUpdater.Resolution_x + 1) / 8), Mathf.CeilToInt((meshUpdater.Resolution_y + 1) / 8), 1);
        //    triDexelModifier.Dispatch(kernelModifierXZ, Mathf.CeilToInt((meshUpdater.Resolution_x + 1) / 8), 1, Mathf.CeilToInt((meshUpdater.Resolution_z + 1) / 8));
        //}
        //else if (max == Mathf.Abs(deltaDisplacement.y))
        //{
        //    Debug.Log("NO Y");
        //    triDexelModifier.Dispatch(kernelModifierXY, Mathf.CeilToInt((meshUpdater.Resolution_x + 1) / 8), Mathf.CeilToInt((meshUpdater.Resolution_y + 1) / 8), 1);
        //    triDexelModifier.Dispatch(kernelModifierZY, 1, Mathf.CeilToInt((meshUpdater.Resolution_y + 1) / 8), Mathf.CeilToInt((meshUpdater.Resolution_z + 1) / 8));
        //}
        //else if (max == Mathf.Abs(deltaDisplacement.z))
        //{
        //    Debug.Log("NO Z");
        //    triDexelModifier.Dispatch(kernelModifierZY, 1, Mathf.CeilToInt((meshUpdater.Resolution_y + 1) / 8), Mathf.CeilToInt((meshUpdater.Resolution_z + 1) / 8));
        //    triDexelModifier.Dispatch(kernelModifierXZ, Mathf.CeilToInt((meshUpdater.Resolution_x + 1) / 8), 1, Mathf.CeilToInt((meshUpdater.Resolution_z + 1) / 8));
        //}

        triDexelModifier.Dispatch(kernelModifierXY, Mathf.CeilToInt((meshUpdater.Resolution_x + 1) / 8), Mathf.CeilToInt((meshUpdater.Resolution_y + 1) / 8), 1);
        triDexelModifier.Dispatch(kernelModifierZY, 1, Mathf.CeilToInt((meshUpdater.Resolution_y + 1) / 8), Mathf.CeilToInt((meshUpdater.Resolution_z + 1) / 8));
        triDexelModifier.Dispatch(kernelModifierXZ, Mathf.CeilToInt((meshUpdater.Resolution_x + 1) / 8), 1, Mathf.CeilToInt((meshUpdater.Resolution_z + 1) / 8));


        // feed back edited tri-dexel data to mesh updater
        meshUpdater.XYTexture = xyTexture;
        meshUpdater.ZYTexture = zyTexture;
        meshUpdater.XZTexture = xzTexture;
    }

    private void OnDestroy()
    {
        if (areaBuffer != null) areaBuffer.Release();
        if (xyTexture != null) xyTexture.Release();
        if (zyTexture != null) zyTexture.Release();
        if (xzTexture != null) xzTexture.Release();
    }
}

