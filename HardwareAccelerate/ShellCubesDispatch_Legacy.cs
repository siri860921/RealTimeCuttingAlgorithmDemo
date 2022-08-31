using System;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ConstructSurface.TriDexelModel1D;
using UnityEditor;
using Unity.Collections;
using UnityEngine.Rendering;

namespace ConstructSurface
{
    namespace SurfaceGenerationMethod
    {
        public class ShellCubesDispatch_Legacy : MonoBehaviour
        {
            private struct Vertex
            {
                public Vector3 position;
                //public Vector3 normal;
            }

            private struct Triangle
            {
                public Vertex vert1;
                public Vertex vert2;
                public Vertex vert3;
            }

            private struct Label
            {
                public int label;
            }

            [SerializeField]
            int maxLabelCount = 10;
            [SerializeField]
            int refResolution;
            [SerializeField]
            int resolution_x;
            [SerializeField]
            int resolution_y;
            [SerializeField]
            int resolution_z;
            [SerializeField]
            float gridLength;
            [SerializeField]
            ComputeShader triDexelInitializer;
            [SerializeField]
            ComputeShader cubeIdentifier;
            [SerializeField]
            ComputeShader cubeLabeler;
            [SerializeField]
            ComputeShader lookUpTriDexel;

            triDexelModel1D triDexelData;
            int dexelSegments;
            Vector3 fieldOrigin;

            ComputeBuffer cubeBuffer;
            ComputeBuffer triangleBuffer;
            ComputeBuffer rootLabelBuffer;
            ComputeBuffer argBuffer;

            int kernelInitialize;
            int kernelIdentifier;
            int kernelLabeler;
            int kernelFindRootLabel;
            int kernelLookUp;
            int kernelArg;
            const int GROUPSIZE2D = 8;
            const int GROUPSIZE3D = 8;
            const int MAXITERATIONS = 100; // max iteration for the CCL

            Mesh modelMesh;
            bool readBack = true;
            Thread meshReadBackThread;
            List<Vector3> newVerts = new List<Vector3>();
            List<Vector3> newNorms = new List<Vector3>();
            List<int> newTriangles = new List<int>();

            public RenderTexture XYTexture { get; set; }
            public RenderTexture ZYTexture { get; set; }
            public RenderTexture XZTexture { get; set; }
            public RenderTexture LabelTexture1 { get; set; }
            public RenderTexture LabelTexture2 { get; set; }
            public int Resolution_x { get => resolution_x; }
            public int Resolution_y { get => resolution_y; }
            public int Resolution_z { get => resolution_z; }
            public int DexelSegments { get => dexelSegments; }
            public float GridSize { get => gridLength; }
            public Vector3 FieldOrigin { get => fieldOrigin; }
            public Mesh ModelMesh { get => modelMesh; }
            public bool EnableCCL { get; set; }

            private void Awake()
            {
                //Destroy(inputModel.GetComponent<Collider>());
                //inputModel.AddComponent<MeshCollider>();

                // create tri dexel data
                triDexelData = new triDexelModel1D(this.gameObject, refResolution, 10, 6);
                resolution_x = triDexelData.Resolution_x;
                resolution_y = triDexelData.Resolution_y;
                resolution_z = triDexelData.Resolution_z;
                dexelSegments = triDexelData.NumOfDexelSegments;
                gridLength = triDexelData.GridSize;
                fieldOrigin = triDexelData.FieldOrigin;

                // generate semi 3D data structure
                // by default, each dexel is given 20/2 = 10 segments of memory
                XYTexture = new RenderTexture(resolution_x + 1, resolution_y + 1, 0, RenderTextureFormat.RFloat);
                ZYTexture = new RenderTexture(resolution_z + 1, resolution_y + 1, 0, RenderTextureFormat.RFloat);
                XZTexture = new RenderTexture(resolution_x + 1, resolution_z + 1, 0, RenderTextureFormat.RFloat);
                XYTexture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
                XYTexture.volumeDepth = dexelSegments * 2;
                XYTexture.enableRandomWrite = true;
                XYTexture.wrapMode = TextureWrapMode.Clamp;
                XYTexture.filterMode = FilterMode.Point;
                ZYTexture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
                ZYTexture.volumeDepth = dexelSegments * 2;
                ZYTexture.enableRandomWrite = true;
                ZYTexture.wrapMode = TextureWrapMode.Clamp;
                ZYTexture.filterMode = FilterMode.Point;
                XZTexture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
                XZTexture.volumeDepth = dexelSegments * 2;
                XZTexture.enableRandomWrite = true;
                XZTexture.wrapMode = TextureWrapMode.Clamp;
                XZTexture.filterMode = FilterMode.Point;
                XYTexture.Create();
                ZYTexture.Create();
                XZTexture.Create();
                this.enabled = false;
                EnableCCL = false;

                // generate label buffers
                LabelTexture1 = new RenderTexture(resolution_x, resolution_y, 0, RenderTextureFormat.RFloat);
                LabelTexture2 = new RenderTexture(resolution_x, resolution_y, 0, RenderTextureFormat.RFloat);
                LabelTexture1.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
                LabelTexture1.volumeDepth = resolution_z;
                LabelTexture1.enableRandomWrite = true;
                LabelTexture1.wrapMode = TextureWrapMode.Clamp;
                LabelTexture1.filterMode = FilterMode.Point;
                LabelTexture2.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
                LabelTexture2.volumeDepth = resolution_z;
                LabelTexture2.enableRandomWrite = true;
                LabelTexture2.wrapMode = TextureWrapMode.Clamp;
                LabelTexture2.filterMode = FilterMode.Point;
            }

            private void Start()
            {
                // set model pointer and change mesh format to store more mesh data
                modelMesh = this.gameObject.GetComponent<MeshFilter>().mesh;
                modelMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

                // allocate compute buffers
                cubeBuffer = new ComputeBuffer(resolution_x * resolution_y * resolution_z, sizeof(int) + sizeof(int) * 8);
                triangleBuffer = new ComputeBuffer(((resolution_x * resolution_y) + (resolution_y * resolution_z) + (resolution_x * resolution_z)) * dexelSegments * 2 * 6,
                    sizeof(float) * 9, ComputeBufferType.Append);
                rootLabelBuffer = new ComputeBuffer(10, sizeof(int), ComputeBufferType.Append);
                argBuffer = new ComputeBuffer(4, sizeof(int), ComputeBufferType.IndirectArguments);

                // find kernel ID
                kernelInitialize = cubeIdentifier.FindKernel("InitializeCubeStates");
                kernelIdentifier = cubeIdentifier.FindKernel("UpdateCubeStates");
                kernelLabeler = cubeLabeler.FindKernel("CCL3D");
                kernelFindRootLabel = cubeLabeler.FindKernel("FindRootLabel");
                kernelLookUp = lookUpTriDexel.FindKernel("LookUpTriDexel");
                kernelArg = lookUpTriDexel.FindKernel("GetVerticeCount");

                // set shader parameters
                cubeIdentifier.SetFloats("fieldOrigin", new float[4] { triDexelData.FieldOrigin.x, triDexelData.FieldOrigin.y, triDexelData.FieldOrigin.z, 0 });
                cubeIdentifier.SetFloat("gridLength", triDexelData.GridSize);
                cubeIdentifier.SetInt("resolution_x", resolution_x);
                cubeIdentifier.SetInt("resolution_y", resolution_y);
                cubeIdentifier.SetInt("resolution_z", resolution_z);
                cubeIdentifier.SetInt("dexelSegments", triDexelData.NumOfDexelSegments);

                cubeLabeler.SetInt("resolutionX", resolution_x);
                cubeLabeler.SetInt("resolutionY", resolution_y);
                cubeLabeler.SetInt("resolutionZ", resolution_z);

                lookUpTriDexel.SetFloats("fieldOrigin", new float[4] { triDexelData.FieldOrigin.x, triDexelData.FieldOrigin.y, triDexelData.FieldOrigin.z, 0 });
                lookUpTriDexel.SetFloat("gridLength", triDexelData.GridSize);
                lookUpTriDexel.SetInt("resolution_x", resolution_x);
                lookUpTriDexel.SetInt("resolution_y", resolution_y);
                lookUpTriDexel.SetInt("resolution_z", resolution_z);
                lookUpTriDexel.SetInt("dexelSegments", triDexelData.NumOfDexelSegments);

                // set compute buffer to computeshader
                cubeIdentifier.SetBuffer(kernelInitialize, "cubeBuffer", cubeBuffer);
                cubeIdentifier.SetBuffer(kernelIdentifier, "cubeBuffer", cubeBuffer);
                cubeLabeler.SetBuffer(kernelFindRootLabel, "rootLabelBuffer", rootLabelBuffer);
                lookUpTriDexel.SetBuffer(kernelLookUp, "cubeBuffer", cubeBuffer);
                lookUpTriDexel.SetBuffer(kernelLookUp, "triangleBuffer", triangleBuffer);

                // initialize tri dexel data
                InitializeTriDexel();
            }

            private void OnTriggerExit()
            {
                Debug.Log("Exit");
                if (EnableCCL)
                {
                    DateTime start = DateTime.Now;
                    CCL3D();
                    DateTime end = DateTime.Now;
                    Debug.Log($"CCL3D compute time: {(end - start).TotalMilliseconds}ms");
                    EnableCCL = false;
                    this.enabled = false;
                }
            }

            private void Update()
            {
                DateTime start = DateTime.Now;
                // update model mesh each frame
                GeometryUpdateDispatch();
                DateTime end = DateTime.Now;
                //Debug.Log($"Shell Cubes Compute: {(end - start).TotalMilliseconds}");

                // execute when the mesh data is read back
                if (!meshReadBackThread.IsAlive)
                {
                    modelMesh.Clear();
                    modelMesh.SetVertices(newVerts);
                    modelMesh.SetTriangles(newTriangles, 0);
                    //modelMesh.normals = newNorms.ToArray();                   
                    modelMesh.RecalculateNormals();

                    readBack = true;
                    newVerts.Clear();
                    newTriangles.Clear();
                    //newNorms.Clear();
                }
            }

            // executes the shell cubes algorithm in the compute shader
            void GeometryUpdateDispatch()
            {
                // set tridexel array data to identifier shader
                cubeIdentifier.SetTexture(kernelIdentifier, "xy_array", XYTexture);
                cubeIdentifier.SetTexture(kernelIdentifier, "zy_array", ZYTexture);
                cubeIdentifier.SetTexture(kernelIdentifier, "xz_array", XZTexture);
                cubeIdentifier.SetTexture(kernelIdentifier, "labelBuffer1", LabelTexture1);
                cubeIdentifier.SetTexture(kernelIdentifier, "labelBuffer2", LabelTexture2);

                // identify surface cubes
                cubeIdentifier.Dispatch(kernelIdentifier, Mathf.CeilToInt(resolution_x / GROUPSIZE3D), Mathf.CeilToInt(resolution_y / GROUPSIZE3D), Mathf.CeilToInt(resolution_z / GROUPSIZE3D));

                // set tridexel array data to mesh generating shader
                lookUpTriDexel.SetTexture(kernelLookUp, "xy_array", XYTexture);
                lookUpTriDexel.SetTexture(kernelLookUp, "zy_array", ZYTexture);
                lookUpTriDexel.SetTexture(kernelLookUp, "xz_array", XZTexture);

                // create mesh from surface cubes
                triangleBuffer.SetCounterValue(0);
                int[] args = new int[] { 0, 1, 0, 0 };
                argBuffer.SetData(args);
                lookUpTriDexel.Dispatch(kernelLookUp, Mathf.CeilToInt(resolution_x / GROUPSIZE3D), Mathf.CeilToInt(resolution_y / GROUPSIZE3D), Mathf.CeilToInt(resolution_z / GROUPSIZE3D));

                // compute the number of vertice
                ComputeBuffer.CopyCount(triangleBuffer, argBuffer, 0);
                //lookUpTriDexel.SetBuffer(kernelArg, "argBuffer", argBuffer);
                //lookUpTriDexel.Dispatch(kernelArg, 1, 1, 1);

                // allocate a new thread to asynchronously read mesh data back from gpu
                if (readBack)
                {
                    argBuffer.GetData(args);
                    Triangle[] triangles = new Triangle[args[0]];
                    triangleBuffer.GetData(triangles);
                    meshReadBackThread = new Thread(() => AsyncReadBackMesh(ref triangles));
                    meshReadBackThread.Start();
                    readBack = false;
                }
            }         

            // initialize tri-dexel data in the compute shader
            void InitializeTriDexel()
            {
                // allocate buffer 
                ComputeBuffer initialXY = new ComputeBuffer(triDexelData.XY_array.Length, 4 * sizeof(float));
                ComputeBuffer initialZY = new ComputeBuffer(triDexelData.ZY_array.Length, 4 * sizeof(float));
                ComputeBuffer initialXZ = new ComputeBuffer(triDexelData.XZ_array.Length, 4 * sizeof(float));
                initialXY.SetData(triDexelData.XY_array);
                initialZY.SetData(triDexelData.ZY_array);
                initialXZ.SetData(triDexelData.XZ_array);

                // initialize parameters in the initializer shader
                int kernelIniXY = triDexelInitializer.FindKernel("InitializeTriDexelXY");
                int kernelIniZY = triDexelInitializer.FindKernel("InitializeTriDexelZY");
                int kernelIniXZ = triDexelInitializer.FindKernel("InitializeTriDexelXZ");
                triDexelInitializer.SetInt("resolution_x", resolution_x);
                triDexelInitializer.SetInt("resolution_y", resolution_y);
                triDexelInitializer.SetInt("resolution_z", resolution_z);
                triDexelInitializer.SetInt("dexelSegments", dexelSegments);

                // set buffer to the initializer shader
                triDexelInitializer.SetTexture(kernelIniXY, "xyTexture", XYTexture);
                triDexelInitializer.SetTexture(kernelIniZY, "zyTexture", ZYTexture);
                triDexelInitializer.SetTexture(kernelIniXZ, "xzTexture", XZTexture);
                triDexelInitializer.SetBuffer(kernelIniXY, "xyArray", initialXY);
                triDexelInitializer.SetBuffer(kernelIniZY, "zyArray", initialZY);
                triDexelInitializer.SetBuffer(kernelIniXZ, "xzArray", initialXZ);

                // execute initializer shader
                triDexelInitializer.Dispatch(kernelIniXY, Mathf.CeilToInt((resolution_x + 1) / GROUPSIZE2D), Mathf.CeilToInt((resolution_y + 1) / GROUPSIZE2D), 1);
                triDexelInitializer.Dispatch(kernelIniZY, 1, Mathf.CeilToInt((resolution_y + 1) / GROUPSIZE2D), Mathf.CeilToInt((resolution_z + 1) / GROUPSIZE2D));
                triDexelInitializer.Dispatch(kernelIniXZ, Mathf.CeilToInt((resolution_x + 1) / GROUPSIZE2D), 1, Mathf.CeilToInt((resolution_z + 1) / GROUPSIZE2D));

                // release buffer memory
                initialXY.Release();
                initialZY.Release();
                initialXZ.Release();

                // initialize cube state buffer
                cubeIdentifier.Dispatch(kernelInitialize, Mathf.CeilToInt(resolution_x / GROUPSIZE3D), Mathf.CeilToInt(resolution_y / GROUPSIZE3D), Mathf.CeilToInt(resolution_z / GROUPSIZE3D));
            }

            // read back mesh data from gpu, async thread method
            void AsyncReadBackMesh(ref Triangle[] triangles)
            {
                DateTime start = DateTime.Now;
                Dictionary<Vector3, int> verticeDic = new Dictionary<Vector3, int>();
                int triangleCount = triangles.Length;

                for (int i = 0; i < triangleCount; ++i)
                {
                    bool isInDic = verticeDic.ContainsKey(triangles[i].vert1.position);
                    if (!isInDic)
                    {
                        verticeDic.Add(triangles[i].vert1.position, newVerts.Count);
                        newTriangles.Add(newVerts.Count);
                        newVerts.Add(triangles[i].vert1.position);
                        //newNorms.Add(triangles[i].vert1.normal);
                    }
                    else
                        newTriangles.Add(verticeDic[triangles[i].vert1.position]);

                    isInDic = verticeDic.ContainsKey(triangles[i].vert2.position);
                    if (!isInDic)
                    {
                        verticeDic.Add(triangles[i].vert2.position, newVerts.Count);
                        newTriangles.Add(newVerts.Count);
                        newVerts.Add(triangles[i].vert2.position);
                        //newNorms.Add(triangles[i].vert2.normal);
                    }
                    else
                        newTriangles.Add(verticeDic[triangles[i].vert2.position]);

                    isInDic = verticeDic.ContainsKey(triangles[i].vert3.position);
                    if (!isInDic)
                    {
                        verticeDic.Add(triangles[i].vert3.position, newVerts.Count);
                        newTriangles.Add(newVerts.Count);
                        newVerts.Add(triangles[i].vert3.position);
                        //newNorms.Add(triangles[i].vert3.normal);
                    }
                    else
                        newTriangles.Add(verticeDic[triangles[i].vert3.position]);
                }

                DateTime end = DateTime.Now;
                //Debug.Log("Read back time: " + (end - start).TotalMilliseconds);
            }

            // parallel connected component
            public void CCL3D()
            {
                //3D connected component labeling
                for (int i = 0; i < MAXITERATIONS; ++i)
                {
                    if (i % 2 == 0)
                        cubeLabeler.SetInt("currentIteration", 0);
                    else
                        cubeLabeler.SetInt("currentIteration", 1);

                    cubeLabeler.SetTexture(kernelLabeler, "labelBuffer1", LabelTexture1);
                    cubeLabeler.SetTexture(kernelLabeler, "labelBuffer2", LabelTexture2);
                    cubeLabeler.SetBuffer(kernelLabeler, "cubeBuffer", cubeBuffer);
                    cubeLabeler.Dispatch(kernelLabeler, Mathf.CeilToInt(resolution_x / GROUPSIZE3D), Mathf.CeilToInt(resolution_y / GROUPSIZE3D), Mathf.CeilToInt(resolution_z / GROUPSIZE3D));
                }

                // find root label
                cubeLabeler.SetTexture(kernelFindRootLabel, "labelBuffer1", LabelTexture1);
                cubeLabeler.SetBuffer(kernelFindRootLabel, "rootLabelBuffer", rootLabelBuffer);
                cubeLabeler.SetBuffer(kernelFindRootLabel, "cubeBuffer", cubeBuffer);
                int[] args1 = new int[] { 0, 1, 0, 0 };
                ComputeBuffer args1Buffer = new ComputeBuffer(4, sizeof(int), ComputeBufferType.IndirectArguments);
                args1Buffer.SetData(args1);
                rootLabelBuffer.SetCounterValue(0);
                cubeLabeler.Dispatch(kernelFindRootLabel, Mathf.CeilToInt(resolution_x / GROUPSIZE3D), Mathf.CeilToInt(resolution_y / GROUPSIZE3D), Mathf.CeilToInt(resolution_z / GROUPSIZE3D));
                ComputeBuffer.CopyCount(rootLabelBuffer, args1Buffer, 0);
                args1Buffer.GetData(args1);
                Debug.Log($"Number of labels: {args1[0]}");
                Label[] labels = new Label[args1[0]];
                rootLabelBuffer.GetData(labels);
                for (int i = 0; i < labels.Length; ++i)
                {
                    Debug.Log($"Label {labels[i].label}");
                }
                args1Buffer.Release();

            }
            private void OnDestroy()
            {
                if (cubeBuffer != null) cubeBuffer.Release();
                if (rootLabelBuffer != null) rootLabelBuffer.Release();
                if (argBuffer != null) argBuffer.Release();
                if (triangleBuffer != null) triangleBuffer.Release();
            }
        }
    }
}

