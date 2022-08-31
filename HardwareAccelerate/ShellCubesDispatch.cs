using System;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;
using ConstructSurface.TriDexelModel1D;

namespace ConstructSurface
{
    namespace SurfaceGenerationMethod
    {
        [RequireComponent(typeof(MeshCollider))]
        public class ShellCubesDispatch : MonoBehaviour
        {
            #region README
            // voxel corner conventions
            //
            //           5____________6
            //           /|          /|
            //          / |         / |
            //        4/__|________/7 |
            //         | 1|________|__|2
            //         |  /        |  /
            //         | /         | /
            //         |/__________|/  
            //        0             3
            //

            // voxel edge convention
            //
            //            ______5_____ 
            //           /|          /|
            //          4 9         6 10
            //         /__|___7____/  |
            //         |  |_____1__|__|
            //         8  /        11 /
            //         | 0         | 2
            //         |/____3_____|/  
            //                     
            //

            // in this project, the meaning of "cube" is equal to voxel
            #endregion

            // vertex structure
            private struct Vertex
            {
                public Vector3 position;
            }

            // triangle structure
            private struct Triangle
            {
                public Vertex vert1;
                public Vertex vert2;
                public Vertex vert3;
            }

            [SerializeField]
            int maxLabelCount = 2; // maximum number of parts can be divided to, normally 2
            [SerializeField]
            int refResolution; // the resolution on the longest dimension of the model
            [SerializeField] 
            int resolution_x; // resolution on the x direction
            [SerializeField] 
            int resolution_y; // resolution on the y direction
            [SerializeField] 
            int resolution_z; // resolution on the z direction
            [SerializeField]
            float gridLength; // the edge length of the grid
            [SerializeField]
            ComputeShader triDexelInitializer;
            [SerializeField]
            ComputeShader cubeIdentifier;
            [SerializeField]
            ComputeShader cubeLabeler;
            [SerializeField]
            ComputeShader lookUpTriDexel;
            [SerializeField]
            ComputeShader lookUpTriDexelCCL3D;
            [SerializeField]
            ComputeShader InactiveCubesSetter;

            triDexelModel1D triDexelData; // tri-dexel data
            int dexelSegments; // the number of segments in a dexel
            Vector3 fieldOrigin; // the origin of the model space

            ComputeBuffer cubeBuffer;
            ComputeBuffer triangleBuffer1;
            ComputeBuffer triangleBuffer2;
            ComputeBuffer rootLabelBuffer;
            ComputeBuffer voxelGroup1;
            ComputeBuffer voxelGroup2;
            ComputeBuffer argBuffer;

            int kernelInitialize;
            int kernelIdentifier;
            int kernelLabeler;
            int kernelFindRootLabel;
            int kernelLookUp;
            int kernelLookUpCCL3D;
            int kernelSetInactiveCubes;

            const int GROUPSIZE2D = 8; // 2D dispatch group size
            const int GROUPSIZE3D = 8; // 3D dispatch group size
            const int MAXITERATIONS = 20; // maximum iteration for the CCL
           
            Mesh modelMesh;
            bool readBack = true;
            bool readBackCCL3D = true;
            Thread meshReadBackThread; // default post-processing thread
            Thread meshReadBackThread1; // post-processing thread as the object is divided, used to handel the main object part
            Thread meshReadBackThread2; // post-processing thread as the object is divided, used to handel the sub object part

            // mesh buffers
            List<Vector3> newVerts = new List<Vector3>();
            List<Vector3> newVertsMain = new List<Vector3>();
            List<Vector3> newVertsSub = new List<Vector3>();
            List<int> newTriangles = new List<int>();
            List<int> newTrianglesMain = new List<int>();
            List<int> newTrianglesSub = new List<int>();

            public RenderTexture XYTexture { get; set; } // xy dexel set
            public RenderTexture ZYTexture { get; set; } // zy dexel set
            public RenderTexture XZTexture { get; set; } // xz dexel set
            public RenderTexture LabelTexture1 { get; set; } // label map buffer 1
            public RenderTexture LabelTexture2 { get; set; } // label map buffer 2
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
                // create tri-dexel data
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

                // change object tag
                gameObject.tag = "BeCut";
            }

            private void Start()
            {
                // set model pointer and change mesh format to store more mesh data
                modelMesh = this.gameObject.GetComponent<MeshFilter>().mesh;
                modelMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                
                // allocate compute buffers
                cubeBuffer = new ComputeBuffer(resolution_x * resolution_y * resolution_z, sizeof(int) * 10);
                triangleBuffer1 = new ComputeBuffer(((resolution_x * resolution_y) + (resolution_y * resolution_z) + (resolution_x * resolution_z)) * 6 * 2,
                    sizeof(float) * 9, ComputeBufferType.Append);

                rootLabelBuffer = new ComputeBuffer(maxLabelCount, sizeof(int), ComputeBufferType.Append);
                argBuffer = new ComputeBuffer(4, sizeof(int), ComputeBufferType.IndirectArguments);

                // find kernel ID
                kernelInitialize = cubeIdentifier.FindKernel("InitializeCubeStates");
                kernelIdentifier = cubeIdentifier.FindKernel("UpdateCubeStates");
                kernelLabeler = cubeLabeler.FindKernel("CCL3D");
                kernelFindRootLabel = cubeLabeler.FindKernel("FindRootLabel");
                kernelLookUp = lookUpTriDexel.FindKernel("LookUpTriDexel");
                kernelLookUpCCL3D = lookUpTriDexelCCL3D.FindKernel("MeshSegmentation");
                kernelSetInactiveCubes = InactiveCubesSetter.FindKernel("SetInactiveCubes");

                // set shader parameters
                cubeIdentifier.SetFloats("fieldOrigin", new float[4] { triDexelData.FieldOrigin.x, triDexelData.FieldOrigin.y, triDexelData.FieldOrigin.z, 0 });
                cubeIdentifier.SetFloat("gridLength", triDexelData.GridSize);
                cubeIdentifier.SetInt("resolution_x", resolution_x);
                cubeIdentifier.SetInt("resolution_y", resolution_y);
                cubeIdentifier.SetInt("resolution_z", resolution_z);
                cubeIdentifier.SetInt("dexelSegments", triDexelData.NumOfDexelSegments);

                lookUpTriDexel.SetFloats("fieldOrigin", new float[4] { triDexelData.FieldOrigin.x, triDexelData.FieldOrigin.y, triDexelData.FieldOrigin.z, 0 });
                lookUpTriDexel.SetFloat("gridLength", triDexelData.GridSize);
                lookUpTriDexel.SetInt("resolution_x", resolution_x);
                lookUpTriDexel.SetInt("resolution_y", resolution_y);
                lookUpTriDexel.SetInt("resolution_z", resolution_z);
                lookUpTriDexel.SetInt("dexelSegments", triDexelData.NumOfDexelSegments);

                cubeLabeler.SetInt("resolutionX", resolution_x);
                cubeLabeler.SetInt("resolutionY", resolution_y);
                cubeLabeler.SetInt("resolutionZ", resolution_z);

                lookUpTriDexelCCL3D.SetFloats("fieldOrigin", new float[4] { triDexelData.FieldOrigin.x, triDexelData.FieldOrigin.y, triDexelData.FieldOrigin.z, 0 });
                lookUpTriDexelCCL3D.SetFloat("gridLength", triDexelData.GridSize);
                lookUpTriDexelCCL3D.SetInt("resolution_x", resolution_x);
                lookUpTriDexelCCL3D.SetInt("resolution_y", resolution_y);
                lookUpTriDexelCCL3D.SetInt("resolution_z", resolution_z);
                lookUpTriDexelCCL3D.SetInt("dexelSegments", triDexelData.NumOfDexelSegments);

                InactiveCubesSetter.SetInt("resolutionX", resolution_x);
                InactiveCubesSetter.SetInt("resolutionY", resolution_y);
                InactiveCubesSetter.SetInt("resolutionZ", resolution_z);

                // set constant compute buffer to computeshader
                cubeIdentifier.SetBuffer(kernelInitialize, "cubeBuffer", cubeBuffer);
                cubeIdentifier.SetBuffer(kernelIdentifier, "cubeBuffer", cubeBuffer);
                lookUpTriDexel.SetBuffer(kernelLookUp, "cubeBuffer", cubeBuffer);
                lookUpTriDexel.SetBuffer(kernelLookUp, "triangleBuffer", triangleBuffer1);
                cubeLabeler.SetBuffer(kernelFindRootLabel, "rootLabelBuffer", rootLabelBuffer);
                lookUpTriDexelCCL3D.SetBuffer(kernelLookUpCCL3D, "cubeBuffer", cubeBuffer);
                lookUpTriDexelCCL3D.SetBuffer(kernelLookUpCCL3D, "triangleBuffer1", triangleBuffer1);
                lookUpTriDexelCCL3D.SetBuffer(kernelLookUpCCL3D, "rootLabelBuffer", rootLabelBuffer);


                // initialize tri dexel data
                InitializeTriDexel();
            }

            private void OnTriggerExit()
            {
                if (EnableCCL)
                {
                    CCL3D();
                    EnableCCL = false;
                }
            }

            private void Update()
            {
                // update model mesh each frame
                GeometryUpdateDispatch();

                // execute when only one mesh group is post-processed
                if (!meshReadBackThread.IsAlive)
                {
                    modelMesh.Clear();
                    modelMesh.SetVertices(newVerts);
                    modelMesh.SetTriangles(newTriangles, 0);                  
                    modelMesh.RecalculateNormals();

                    readBack = true;
                    newVerts.Clear();
                    newTriangles.Clear();
                }

                // execute when multiple mesh group data are post-processed
                if(meshReadBackThread1 != null && meshReadBackThread2 != null)
                {
                    if(!meshReadBackThread1.IsAlive && !meshReadBackThread2.IsAlive)
                    {
                        modelMesh.Clear();
                        modelMesh.SetVertices(newVertsMain);
                        modelMesh.SetTriangles(newTrianglesMain, 0);
                        modelMesh.RecalculateNormals();
                        newVertsMain.Clear();
                        newTrianglesMain.Clear();

                        gameObject.GetComponent<MeshCollider>().sharedMesh = modelMesh;

                        Mesh newMesh = new Mesh();
                        newMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                        newMesh.SetVertices(newVertsSub);
                        newMesh.SetTriangles(newTrianglesSub, 0);
                        newMesh.RecalculateNormals();
                        newMesh.RecalculateBounds();

                        newVertsSub.Clear();
                        newTrianglesSub.Clear();

                        CreateSubMeshGameObject(ref newMesh);
                        meshReadBackThread1 = null;
                        meshReadBackThread2 = null;
                        readBackCCL3D = true;
                        this.enabled = false;
                    }
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
                triangleBuffer1.SetCounterValue(0);
                int[] args = new int[] { 0, 1, 0, 0 };
                argBuffer.SetData(args);
                lookUpTriDexel.Dispatch(kernelLookUp, Mathf.CeilToInt(resolution_x / GROUPSIZE3D), Mathf.CeilToInt(resolution_y / GROUPSIZE3D), Mathf.CeilToInt(resolution_z / GROUPSIZE3D));

                // compute the number of triangles
                ComputeBuffer.CopyCount(triangleBuffer1, argBuffer, 0);

                // allocate a new thread to asynchronously read mesh data back from gpu
                if (readBack)
                {
                    argBuffer.GetData(args);
                    Triangle[] triangles = new Triangle[args[0]];
                    triangleBuffer1.GetData(triangles);
                    meshReadBackThread = new Thread(() => AsyncPostProcessMesh(ref triangles, ref newVerts, ref newTriangles));
                    meshReadBackThread.Start();
                    readBack = false;
                }
            }

            // initialize tri-dexel data in the compute shader
            void InitializeTriDexel()
            {
                // allocate buffers and upload tri-dexel data to GPU
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

                // write the tri-dexel data to render texture 
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

            // post-process on raw mesh data to remove duplicate vertices, async thread method
            void AsyncPostProcessMesh(ref Triangle[] triangles, ref List<Vector3> finalVerts, ref List<int> finalTriangles)
            {
                Dictionary<Vector3, int> verticeDic = new Dictionary<Vector3, int>();
                int triangleCount = triangles.Length;

                for (int i = 0; i < triangleCount; ++i)
                {
                    // first vertice
                    bool isInDic = verticeDic.ContainsKey(triangles[i].vert1.position);
                    if (!isInDic)
                    {
                        verticeDic.Add(triangles[i].vert1.position, finalVerts.Count);
                        finalTriangles.Add(finalVerts.Count);
                        finalVerts.Add(triangles[i].vert1.position);
                    }
                    else
                        finalTriangles.Add(verticeDic[triangles[i].vert1.position]);

                    // second vertice
                    isInDic = verticeDic.ContainsKey(triangles[i].vert2.position);
                    if (!isInDic)
                    {
                        verticeDic.Add(triangles[i].vert2.position, finalVerts.Count);
                        finalTriangles.Add(finalVerts.Count);
                        finalVerts.Add(triangles[i].vert2.position);
                    }
                    else
                        finalTriangles.Add(verticeDic[triangles[i].vert2.position]);

                    // thrid vertice
                    isInDic = verticeDic.ContainsKey(triangles[i].vert3.position);
                    if (!isInDic)
                    {
                        verticeDic.Add(triangles[i].vert3.position, finalVerts.Count);
                        finalTriangles.Add(finalVerts.Count);
                        finalVerts.Add(triangles[i].vert3.position);
                    }
                    else
                        finalTriangles.Add(verticeDic[triangles[i].vert3.position]);
                }        
            }

            // parallel 3D connected component labeling
            public void CCL3D()
            {
                // 3D connected component labeling
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
                rootLabelBuffer.SetCounterValue(0);
                cubeLabeler.Dispatch(kernelFindRootLabel, Mathf.CeilToInt(resolution_x / GROUPSIZE3D), Mathf.CeilToInt(resolution_y / GROUPSIZE3D), Mathf.CeilToInt(resolution_z / GROUPSIZE3D));

                // set tri-dexel data to shader
                lookUpTriDexelCCL3D.SetTexture(kernelLookUpCCL3D, "xy_array", XYTexture);
                lookUpTriDexelCCL3D.SetTexture(kernelLookUpCCL3D, "zy_array", ZYTexture);
                lookUpTriDexelCCL3D.SetTexture(kernelLookUpCCL3D, "xz_array", XZTexture);
                lookUpTriDexelCCL3D.SetTexture(kernelLookUpCCL3D, "labelBuffer", LabelTexture1);

                // allocate memory to triangle buffer 2 and all the voxel group buffers
                triangleBuffer2 = new ComputeBuffer(((resolution_x * resolution_y) + (resolution_y * resolution_z) + (resolution_x * resolution_z)) * 2 * 6,
                    sizeof(float) * 9, ComputeBufferType.Append);
                voxelGroup1 = new ComputeBuffer(resolution_x * resolution_y * resolution_z, sizeof(int) * 3, ComputeBufferType.Append);
                voxelGroup2 = new ComputeBuffer(resolution_x * resolution_y * resolution_z, sizeof(int) * 3, ComputeBufferType.Append);

                // mesh segmentation
                triangleBuffer1.SetCounterValue(0);
                triangleBuffer2.SetCounterValue(0);
                voxelGroup1.SetCounterValue(0);
                voxelGroup2.SetCounterValue(0);
                lookUpTriDexelCCL3D.SetBuffer(kernelLookUpCCL3D, "triangleBuffer2", triangleBuffer2);
                lookUpTriDexelCCL3D.SetBuffer(kernelLookUpCCL3D, "voxelGroup1", voxelGroup1);
                lookUpTriDexelCCL3D.SetBuffer(kernelLookUpCCL3D, "voxelGroup2", voxelGroup2);
                lookUpTriDexelCCL3D.Dispatch(kernelLookUpCCL3D, Mathf.CeilToInt(resolution_x / GROUPSIZE3D), Mathf.CeilToInt(resolution_y / GROUPSIZE3D), Mathf.CeilToInt(resolution_z / GROUPSIZE3D));

                // copy the number of triangles in each triangle buffer
                int[] args1 = new int[] { 0, 1, 0, 0 };
                int[] args2= new int[] { 0, 1, 0, 0 };
                ComputeBuffer argsBuffer1 = new ComputeBuffer(4, sizeof(int), ComputeBufferType.IndirectArguments);
                ComputeBuffer argsBuffer2 = new ComputeBuffer(4, sizeof(int), ComputeBufferType.IndirectArguments);
                argsBuffer1.SetData(args1);
                argsBuffer2.SetData(args2);
                ComputeBuffer.CopyCount(triangleBuffer1, argsBuffer1, 0);
                ComputeBuffer.CopyCount(triangleBuffer2, argsBuffer2, 0);

                argsBuffer1.GetData(args1);
                argsBuffer2.GetData(args2);

                // if no sub mesh group, release uneeded compute buffers
                if (args2[0] == 0)
                {
                    this.enabled = false;

                    argsBuffer1.Release();
                    argsBuffer2.Release();
                    triangleBuffer2.Release();
                    voxelGroup1.Release();
                    voxelGroup2.Release();
                    return;
                }

                Triangle[] triangles1 = new Triangle[args1[0]];
                Triangle[] triangles2 = new Triangle[args2[0]];
                triangleBuffer1.GetData(triangles1);
                triangleBuffer2.GetData(triangles2);

                // thread1 is obligated to process on the main mesh
                ComputeBuffer.CopyCount(voxelGroup1, argsBuffer1, 0);
                ComputeBuffer.CopyCount(voxelGroup2, argsBuffer2, 0);
                argsBuffer1.GetData(args1);
                argsBuffer2.GetData(args2);
                if (args1[0] >= args2[0])
                {
                    meshReadBackThread1 = new Thread(() => AsyncPostProcessMesh(ref triangles1, ref newVertsMain, ref newTrianglesMain));
                    meshReadBackThread2 = new Thread(() => AsyncPostProcessMesh(ref triangles2, ref newVertsSub, ref newTrianglesSub));
                }
                else
                {
                    meshReadBackThread1 = new Thread(() => AsyncPostProcessMesh(ref triangles2, ref newVertsMain, ref newTrianglesMain));
                    meshReadBackThread2 = new Thread(() => AsyncPostProcessMesh(ref triangles1, ref newVertsSub, ref newTrianglesSub));
                }
                meshReadBackThread1.Start();
                meshReadBackThread2.Start();
                readBackCCL3D = false;

                // inactive the voxels that belongs to the sub object
                ComputeBuffer.CopyCount(voxelGroup1, argsBuffer1, 0);
                ComputeBuffer.CopyCount(voxelGroup2, argsBuffer2, 0);
                argsBuffer1.GetData(args1);
                argsBuffer2.GetData(args2);
                int numOfThreads = args1[0] >= args2[0] ? args1[0] : args2[0];
                InactiveCubesSetter.SetBuffer(kernelSetInactiveCubes, "argBuffer1", argsBuffer1);
                InactiveCubesSetter.SetBuffer(kernelSetInactiveCubes, "argBuffer2", argsBuffer2);
                InactiveCubesSetter.SetBuffer(kernelSetInactiveCubes, "voxelGroup1", voxelGroup1);
                InactiveCubesSetter.SetBuffer(kernelSetInactiveCubes, "voxelGroup2", voxelGroup2);
                InactiveCubesSetter.SetBuffer(kernelSetInactiveCubes, "cubeBuffer", cubeBuffer);
                if (numOfThreads < 1024)
                    InactiveCubesSetter.Dispatch(kernelSetInactiveCubes, 1, 1, 1);
                else
                    InactiveCubesSetter.Dispatch(kernelSetInactiveCubes, Mathf.CeilToInt(numOfThreads / 1024), 1, 1);

                // release compute buffers
                argsBuffer1.Release();
                argsBuffer2.Release();
                triangleBuffer2.Release();
                voxelGroup1.Release();
                voxelGroup2.Release();
            }

            // create individual sub mesh game object
            void CreateSubMeshGameObject(ref Mesh subMesh)
            {
                GameObject subMeshGameObject = new GameObject();
                
                // get materials from the main object
                Material[] originalMaterials = gameObject.GetComponent<MeshRenderer>().materials;

                // add meshFilter and meshRenderer
                MeshFilter meshFilter = subMeshGameObject.AddComponent<MeshFilter>();
                subMeshGameObject.AddComponent<MeshRenderer>();
                meshFilter.mesh = subMesh;
                subMeshGameObject.GetComponent<MeshRenderer>().materials = originalMaterials;
                
                // set transform data to sub object
                Transform subMeshTransform = subMeshGameObject.transform;
                Transform thisObjectTransform = gameObject.transform;
                subMeshTransform.localScale = thisObjectTransform.localScale;
                subMeshTransform.rotation = thisObjectTransform.rotation;
                subMeshTransform.position = thisObjectTransform.position;

                // add rigidBody
                Rigidbody rb = subMeshGameObject.AddComponent<Rigidbody>();
                rb.useGravity = true;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                rb.AddForce(new Vector3(-0.5f, -0.5f, -0.5f), ForceMode.VelocityChange);

                // add colliders
                if (subMesh.vertexCount > 20)
                {
                    // add mesh collider, will cause lattency
                    MeshCollider subMeshCollider = subMeshGameObject.AddComponent<MeshCollider>();
                    subMeshCollider.sharedMesh = subMesh;
                    subMeshCollider.convex = true;
                }

                // optional, for demo purpose
                subMeshGameObject.AddComponent<MouseGrab>();
            }

            // release buffers to avoid memory leak
            private void OnDestroy()
            {
                if (cubeBuffer != null) cubeBuffer.Release();
                if (rootLabelBuffer != null) rootLabelBuffer.Release();
                if (argBuffer != null) argBuffer.Release();
                if (triangleBuffer1 != null) triangleBuffer1.Release();
                if (triangleBuffer2 != null) triangleBuffer2.Release();
                if (XYTexture != null) XYTexture.Release();
                if (ZYTexture != null) ZYTexture.Release();
                if (XZTexture != null) XZTexture.Release();
                if (LabelTexture1 != null) LabelTexture1.Release();
                if (LabelTexture2 != null) LabelTexture2.Release();
                if (voxelGroup1 != null) voxelGroup1.Release();
                if (voxelGroup2 != null) voxelGroup2.Release();
            }
        }
    }
}

