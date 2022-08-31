using ConstructSurface.TriDexelModel2D;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ConstructSurface
{
    namespace SurfaceGenerationMethod
    {
        public class surfaceMeshCompute
        {
            private class Cube
            {
                #region DataFields
                int numOfOccupations; // number of occupied cube points
                bool[] cubePointStates; // the state of each cube point
                #endregion

                #region Properties
                // number of occupied cube points
                public int NumberOfOccupations { get => numOfOccupations; }
                // the state of each cube point
                public bool[] CubePointStates { get => cubePointStates; }
                #endregion

                #region Constructor
                public Cube(int numOfOccupations, bool[] cubePointStates)
                {
                    this.numOfOccupations = numOfOccupations;
                    this.cubePointStates = cubePointStates;
                }
                #endregion
            }

            #region DataFields
            triDexelModel2D triDexel; // the data from tri-dexel structure
            Cube[,,] cubeInfos; // stores the state of each cube, cubes are used to generate meshes
            int resolution_x; // the resolution on the x direction
            int resolution_y; // the resolution on the y direction
            int resolution_z; // the resolution on the z direction
            float gridLength; // the grid edge length of a voxel
            int verticeToFindPerDexel = 2; // the maximum number of vertice that can be found in a cube by a single vertice
            Vector3 fieldOrigin; // the local origin coordinate of the model field

            List<Vector3> regenVertices; // regenerated vertices
            List<int> regenTriangles; // regenerated triangles

            bool[,,] voxelStates; // records the state of each cube, only for testing purpose

            // the connection relation between nodes e.g. node 0 connects with node 1, 3 and 4   
            static int[,] edgeConnection = new int[,]
            {
                {1, 3, 4}, {0, 2, 5}, {1, 3, 6}, {0, 2, 7},
                {0, 5, 7}, {1, 4, 6}, {2, 5, 7}, {3, 4, 6},
            };

            // 12 dexels and their corresponding offsets
            // the index offsets are relative to the cube origin (idx_x, idx_y, idx_z)
            // for each offsets arrays, the first element indicates the the map type
            // 0: z map(xy array), 1: x map(zy array), 2: y map(xz array)
            static int[][] dexelIndice =
            {
                new int[4]{0, 0, 0, 0}, new int[4]{0, 1, 0, 0}, new int[4]{0, 0, 1, 0}, new int[4]{0, 1, 1, 0},
                new int[4]{1, 0, 0, 0}, new int[4]{1, 0, 0, 1}, new int[4]{1, 0, 1, 0}, new int[4]{1, 0, 1, 1},
                new int[4]{2, 0, 0, 0}, new int[4]{2, 1, 0, 0}, new int[4]{2, 0, 0, 1}, new int[4]{2, 1, 0, 1},
            };
            #endregion

            #region Constructor
            public surfaceMeshCompute(triDexelModel2D triDexelData)
            {
                this.triDexel = triDexelData;
                GetData(triDexel);
                this.cubeInfos = new Cube[resolution_x, resolution_y, resolution_z];
                this.regenVertices = new List<Vector3>();
                this.regenTriangles = new List<int>();
                this.fieldOrigin = triDexel.FieldOrigin;

                // initialize voxel state
                this.voxelStates = new bool[resolution_x, resolution_y, resolution_z];
                for (int i = 0; i < resolution_x; i++)
                {
                    for (int j = 0; j < resolution_y; j++)
                    {
                        for (int k = 0; k < resolution_z; k++)
                            voxelStates[i, j, k] = false;
                    }
                }

                //ComputeSurfaceMesh();
            }
            #endregion

            #region Properties
            // the resolution on the x direction
            public int Resolution_x { get => resolution_x; }
            // the resolution on the y direction
            public int Resolution_y { get => resolution_y; }
            // the resolution on the z direction
            public int Resolution_z { get => resolution_z; }
            // the grid edge length of a voxel
            public float GridLength { get => gridLength; }
            // new generated vertices
            public Vector3[] RegenVertices 
            { 
                get
                {
                    return regenVertices.ToArray();
                }
            }
            // mew generated triangles
            public int[] RegenTriangles 
            { 
                get
                {
                    return regenTriangles.ToArray();
                }
            }
            // records the state of each cube, only for testing purpose
            public bool[,,] VoxelStates { get => voxelStates; }
            #endregion

            #region Methods
            // fetch basic data from the tri-dexel structure
            void GetData(triDexelModel2D triDexelData)
            {
                resolution_x = triDexelData.Resolution_x;
                resolution_y = triDexelData.Resolution_y;
                resolution_z = triDexelData.Resolution_z;
                gridLength = triDexelData.GridSize;
            }

            // update current cube states
            void UpdateCurrentCubeStates()
            {
                // get tri-dexel data
                Dexel[,] zMap = (Dexel[,])triDexel.XY_array.Clone();
                Dexel[,] xMap = (Dexel[,])triDexel.ZY_array.Clone();
                Dexel[,] yMap = (Dexel[,])triDexel.XZ_array.Clone();

                // check cube states via z map (xy)
                // this process only checks the un-null elements in the map
                for (int i = 0; i < resolution_y; i++)
                {
                    for (int j = 0; j < resolution_x; j++)
                    {
                        if (zMap[i, j].DexelPoints != null)
                        {
                            foreach (float z_value in zMap[i, j].DexelPoints.ToArray())
                            {
                                // get index on z direction
                                int idx_z = Mathf.FloorToInt((z_value - fieldOrigin.z) / gridLength);
                                if(cubeInfos[j, i, idx_z] == null)
                                {
                                    // get the occupation condition of the 8 points on a cube
                                    // if 0 < occupation < 8 then create a new cube instance
                                    bool[] cubePointStates = CubePointStateVerify(j, i, idx_z, out int numOfOccupations);
                                    if (numOfOccupations > 0 && numOfOccupations < 8)
                                    {
                                        cubeInfos[j, i, idx_z] = new Cube(numOfOccupations, cubePointStates);
                                        cubePointStates = CubePointStateVerify(j - 1, i, idx_z, out numOfOccupations);
                                        cubeInfos[j - 1, i, idx_z] = new Cube(numOfOccupations, cubePointStates);
                                        cubePointStates = CubePointStateVerify(j - 1, i - 1, idx_z, out numOfOccupations);
                                        cubeInfos[j - 1, i - 1, idx_z] = new Cube(numOfOccupations, cubePointStates);
                                        cubePointStates = CubePointStateVerify(j, i - 1, idx_z, out numOfOccupations);
                                        cubeInfos[j, i - 1, idx_z] = new Cube(numOfOccupations, cubePointStates);
                                        voxelStates[j, i, idx_z] = true;
                                        voxelStates[j - 1, i, idx_z] = true;
                                        voxelStates[j - 1, i - 1, idx_z] = true;
                                        voxelStates[j, i - 1, idx_z] = true;
                                    }
                                }
                            }
                        }
                    }
                }
                // check voxel state via x map (zy)
                // this process only checks the un-null elements in the map
                for (int i = 0; i < resolution_y; i++)
                {
                    for (int j = 0; j < resolution_z; j++)
                    {
                        if (xMap[i, j].DexelPoints != null)
                        {
                            foreach (float x_value in xMap[i, j].DexelPoints.ToArray())
                            {
                                // get index on x direction
                                int idx_x = Mathf.FloorToInt((x_value - fieldOrigin.x) / gridLength);
                                if(cubeInfos[idx_x, i, j] == null)
                                {
                                    // get the occupation condition of the 8 points on a cube
                                    // if 0 < occupation < 8 then create a new cube instance
                                    bool[] cubePointStates = CubePointStateVerify(idx_x, i, j, out int numOfOccupations);
                                    if(numOfOccupations > 0 && numOfOccupations < 8)
                                    {
                                        cubeInfos[idx_x, i, j] = new Cube(numOfOccupations, cubePointStates);
                                        cubePointStates = CubePointStateVerify(idx_x, i, j - 1, out numOfOccupations);
                                        cubeInfos[idx_x, i, j - 1] = new Cube(numOfOccupations, cubePointStates);
                                        cubePointStates = CubePointStateVerify(idx_x, i - 1, j - 1, out numOfOccupations);
                                        cubeInfos[idx_x, i - 1, j - 1] = new Cube(numOfOccupations, cubePointStates);
                                        cubePointStates = CubePointStateVerify(idx_x, i - 1, j, out numOfOccupations);
                                        cubeInfos[idx_x, i - 1, j] = new Cube(numOfOccupations, cubePointStates);
                                        voxelStates[idx_x, i, j] = true;
                                        voxelStates[idx_x, i, j - 1] = true;
                                        voxelStates[idx_x, i - 1, j - 1] = true;
                                        voxelStates[idx_x, i - 1, j] = true;
                                    }
                                }
                            }
                        }
                    }
                }
                // check voxel state via y map (xz)
                // this process only checks the un-null elements in the map
                for (int i = 0; i < resolution_z; i++)
                {
                    for (int j = 0; j < resolution_x; j++)
                    {
                        if (yMap[i, j].DexelPoints != null)
                        {
                            foreach (float y_value in yMap[i, j].DexelPoints.ToArray())
                            {
                                // get index on y direction
                                int idx_y = Mathf.FloorToInt((y_value - fieldOrigin.y) / gridLength);
                                if(cubeInfos[j, idx_y, i] == null)
                                {
                                    // get the occupation condition of the 8 points on a cube
                                    // if 0 < occupation < 8 then create a new cube instance
                                    bool[] cubePointStates = CubePointStateVerify(j, idx_y, i, out int numOfOccupations);
                                    if(numOfOccupations > 0 && numOfOccupations < 8)
                                    {
                                        cubeInfos[j, idx_y, i] = new Cube(numOfOccupations, cubePointStates);
                                        cubePointStates = CubePointStateVerify(j - 1, idx_y, i, out numOfOccupations);
                                        cubeInfos[j - 1, idx_y, i] = new Cube(numOfOccupations, cubePointStates);
                                        cubePointStates = CubePointStateVerify(j - 1, idx_y, i - 1, out numOfOccupations);
                                        cubeInfos[j - 1, idx_y, i - 1] = new Cube(numOfOccupations, cubePointStates);
                                        cubePointStates = CubePointStateVerify(j, idx_y, i - 1, out numOfOccupations);
                                        cubeInfos[j, idx_y, i - 1] = new Cube(numOfOccupations, cubePointStates);
                                        voxelStates[j, idx_y, i] = true;
                                        voxelStates[j - 1, idx_y, i] = true;
                                        voxelStates[j - 1, idx_y, i - 1] = true;
                                        voxelStates[j, idx_y, i - 1] = true;
                                    }
                                }
                            }
                        }
                    }
                }

                // debug, count the number of cubes
                int count = 0;
                for (int i = 0; i < resolution_x; i++)
                {
                    for (int j = 0; j < resolution_y; j++)
                    {
                        for (int k = 0; k < resolution_z; k++)
                        {
                            if (voxelStates[i, j, k] == true)
                                count++;
                        }
                    }
                }
                Debug.Log("Number of Cubes: " + count);
            }

            void UpdateCurrentCubeStates_dev()
            {
                // get tri-dexel data
                Dexel[,] zMap = (Dexel[,])triDexel.XY_array.Clone();
                Dexel[,] xMap = (Dexel[,])triDexel.ZY_array.Clone();
                Dexel[,] yMap = (Dexel[,])triDexel.XZ_array.Clone();

                // check cube states via z map (xy)
                // this process only checks the un-null elements in the map
                for (int i = 0; i < resolution_y; i++)
                {
                    for (int j = 0; j < resolution_x; j++)
                    {
                        if (zMap[i, j].DexelPoints != null)
                        {
                            foreach (float z_value in zMap[i, j].DexelPoints.ToArray())
                            {
                                // get index on z direction
                                int idx_z = Mathf.FloorToInt((z_value - fieldOrigin.z) / gridLength);
                                if (cubeInfos[j, i, idx_z] == null)
                                {
                                    // get the occupation condition of the 8 points on a cube
                                    // if 0 < occupation < 8 then create a new cube instance
                                    bool[] cubePointStates = CubePointStateVerify(j, i, idx_z, out int numOfOccupations);
                                    if (numOfOccupations > 0 && numOfOccupations < 8)
                                    {
                                        cubeInfos[j, i, idx_z] = new Cube(numOfOccupations, cubePointStates);
                                        voxelStates[j, i, idx_z] = true;
                                    }
                                }
                                if (cubeInfos[j - 1, i, idx_z] == null)
                                {
                                    bool[] cubePointStates = CubePointStateVerify(j - 1, i, idx_z, out int numOfOccupations);
                                    if (numOfOccupations > 0 && numOfOccupations < 8)
                                    {
                                        cubeInfos[j - 1, i, idx_z] = new Cube(numOfOccupations, cubePointStates);
                                        voxelStates[j - 1, i, idx_z] = true;
                                    }
                                }
                                if (cubeInfos[j - 1, i - 1, idx_z] == null)
                                {
                                    bool[] cubePointStates = CubePointStateVerify(j - 1, i - 1, idx_z, out int numOfOccupations);
                                    if (numOfOccupations > 0 && numOfOccupations < 8)
                                    {
                                        cubeInfos[j - 1, i - 1, idx_z] = new Cube(numOfOccupations, cubePointStates);
                                        voxelStates[j - 1, i - 1, idx_z] = true;
                                    }
                                }
                                if (cubeInfos[j, i - 1, idx_z] == null)
                                {
                                    bool[] cubePointStates = CubePointStateVerify(j, i - 1, idx_z, out int numOfOccupations);
                                    if (numOfOccupations > 0 && numOfOccupations < 8)
                                    {
                                        cubeInfos[j, i - 1, idx_z] = new Cube(numOfOccupations, cubePointStates);
                                        voxelStates[j, i - 1, idx_z] = true;
                                    }
                                }
                            }
                        }
                    }
                }
                // check voxel state via x map (zy)
                // this process only checks the un-null elements in the map
                for (int i = 0; i < resolution_y; i++)
                {
                    for (int j = 0; j < resolution_z; j++)
                    {
                        if (xMap[i, j].DexelPoints != null)
                        {
                            foreach (float x_value in xMap[i, j].DexelPoints.ToArray())
                            {
                                // get index on x direction
                                int idx_x = Mathf.FloorToInt((x_value - fieldOrigin.x) / gridLength);
                                if (cubeInfos[idx_x, i, j] == null)
                                {
                                    // get the occupation condition of the 8 points on a cube
                                    // if 0 < occupation < 8 then create a new cube instance
                                    bool[] cubePointStates = CubePointStateVerify(idx_x, i, j, out int numOfOccupations);
                                    if (numOfOccupations > 0 && numOfOccupations < 8)
                                    {
                                        cubeInfos[idx_x, i, j] = new Cube(numOfOccupations, cubePointStates);
                                        voxelStates[idx_x, i, j] = true;
                                    }
                                }
                                if (cubeInfos[idx_x, i, j - 1] == null)
                                {
                                    bool[] cubePointStates = CubePointStateVerify(idx_x, i, j - 1, out int numOfOccupations);
                                    if (numOfOccupations > 0 && numOfOccupations < 8)
                                    {
                                        cubeInfos[idx_x, i, j - 1] = new Cube(numOfOccupations, cubePointStates);
                                        voxelStates[idx_x, i, j - 1] = true;
                                    }
                                }
                                if (cubeInfos[idx_x, i - 1, j - 1] == null)
                                {
                                    bool[] cubePointStates = CubePointStateVerify(idx_x, i - 1, j - 1, out int numOfOccupations);
                                    if (numOfOccupations > 0 && numOfOccupations < 8)
                                    {
                                        cubeInfos[idx_x, i - 1, j - 1] = new Cube(numOfOccupations, cubePointStates);
                                        voxelStates[idx_x, i - 1, j - 1] = true;
                                    }
                                }
                                if (cubeInfos[idx_x, i - 1, j] == null)
                                {
                                    bool[] cubePointStates = CubePointStateVerify(idx_x, i - 1, j, out int numOfOccupations);
                                    if (numOfOccupations > 0 && numOfOccupations < 8)
                                    {
                                        cubeInfos[idx_x, i - 1, j] = new Cube(numOfOccupations, cubePointStates);
                                        voxelStates[idx_x, i - 1, j] = true;
                                    }
                                }
                            }
                        }
                    }
                }
                // check voxel state via y map (xz)
                // this process only checks the un-null elements in the map
                for (int i = 0; i < resolution_z; i++)
                {
                    for (int j = 0; j < resolution_x; j++)
                    {
                        if (yMap[i, j].DexelPoints != null)
                        {
                            foreach (float y_value in yMap[i, j].DexelPoints.ToArray())
                            {
                                // get index on y direction
                                int idx_y = Mathf.FloorToInt((y_value - fieldOrigin.y) / gridLength);
                                if (cubeInfos[j, idx_y, i] == null)
                                {
                                    // get the occupation condition of the 8 points on a cube
                                    // if 0 < occupation < 8 then create a new cube instance
                                    bool[] cubePointStates = CubePointStateVerify(j, idx_y, i, out int numOfOccupations);
                                    if (numOfOccupations > 0 && numOfOccupations < 8)
                                    {
                                        cubeInfos[j, idx_y, i] = new Cube(numOfOccupations, cubePointStates);
                                        voxelStates[j, idx_y, i] = true;
                                    }
                                }
                                if (cubeInfos[j - 1, idx_y, i] == null)
                                {
                                    bool[] cubePointStates = CubePointStateVerify(j - 1, idx_y, i, out int numOfOccupations);
                                    if (numOfOccupations > 0 && numOfOccupations < 8)
                                    {
                                        cubeInfos[j - 1, idx_y, i] = new Cube(numOfOccupations, cubePointStates);
                                        voxelStates[j - 1, idx_y, i] = true;
                                    }
                                }
                                if (cubeInfos[j - 1, idx_y, i - 1] == null)
                                {
                                    bool[] cubePointStates = CubePointStateVerify(j - 1, idx_y, i - 1, out int numOfOccupations);
                                    if (numOfOccupations > 0 && numOfOccupations < 8)
                                    {
                                        cubeInfos[j - 1, idx_y, i - 1] = new Cube(numOfOccupations, cubePointStates);
                                        voxelStates[j - 1, idx_y, i - 1] = true;
                                    }
                                }
                                if (cubeInfos[j, idx_y, i - 1] == null)
                                {
                                    bool[] cubePointStates = CubePointStateVerify(j, idx_y, i - 1, out int numOfOccupations);
                                    if (numOfOccupations > 0 && numOfOccupations < 8)
                                    {
                                        cubeInfos[j, idx_y, i - 1] = new Cube(numOfOccupations, cubePointStates);
                                        voxelStates[j, idx_y, i - 1] = true;
                                    }
                                }
                            }
                        }
                    }
                }

                //// debug, count the number of cubes
                //int count = 0;
                //for (int i = 0; i < resolution_x; i++)
                //{
                //    for (int j = 0; j < resolution_y; j++)
                //    {
                //        for (int k = 0; k < resolution_z; k++)
                //        {
                //            if (voxelStates[i, j, k] == true)
                //                count++;
                //        }
                //    }
                //}
                //Debug.Log("Number of Cubes: " + count);
            }

            // verify the occupation state of 8 cube points on a single cube, and return an array with the 8 cube point states
            bool[] CubePointStateVerify(int idx_x, int idx_y, int idx_z, out int occupiedCubePoints)
            {
                // create initial cube state array
                bool[] cubePointStates = new bool[8] { false, false, false, false, false, false, false, false };

                // compute min/max local coordinate of the cube 
                float xCoordinate = idx_x * gridLength + fieldOrigin.x;
                float yCoordinate = idx_y * gridLength + fieldOrigin.y;
                float zCoordinate = idx_z * gridLength + fieldOrigin.z;
                float xCoordinate_side = (idx_x + 1) * gridLength + fieldOrigin.x;
                float yCoordinate_side = (idx_y + 1) * gridLength + fieldOrigin.y;
                float zCoordinate_side = (idx_z + 1) * gridLength + fieldOrigin.z;

                // get 12 dexels from three maps(for dexels for each map)
                // if the dexel does not exist then assisgn null
                float[] dexel_x1 = triDexel.ZY_array[idx_y, idx_z].DexelPoints == null ? null : triDexel.ZY_array[idx_y, idx_z].DexelPoints.ToArray();
                float[] dexel_x2 = triDexel.ZY_array[idx_y, idx_z + 1].DexelPoints == null ? null : triDexel.ZY_array[idx_y, idx_z + 1].DexelPoints.ToArray();
                float[] dexel_x3 = triDexel.ZY_array[idx_y + 1, idx_z].DexelPoints == null ? null : triDexel.ZY_array[idx_y + 1, idx_z].DexelPoints.ToArray();
                float[] dexel_x4 = triDexel.ZY_array[idx_y + 1, idx_z + 1].DexelPoints == null ? null : triDexel.ZY_array[idx_y + 1, idx_z + 1].DexelPoints.ToArray();
                float[] dexel_y1 = triDexel.XZ_array[idx_z, idx_x].DexelPoints == null ? null : triDexel.XZ_array[idx_z, idx_x].DexelPoints.ToArray();
                float[] dexel_y2 = triDexel.XZ_array[idx_z, idx_x + 1].DexelPoints == null ? null : triDexel.XZ_array[idx_z, idx_x + 1].DexelPoints.ToArray();
                float[] dexel_y3 = triDexel.XZ_array[idx_z + 1, idx_x].DexelPoints == null ? null : triDexel.XZ_array[idx_z + 1, idx_x].DexelPoints.ToArray();
                float[] dexel_y4 = triDexel.XZ_array[idx_z + 1, idx_x + 1].DexelPoints == null ? null : triDexel.XZ_array[idx_z + 1, idx_x + 1].DexelPoints.ToArray();
                float[] dexel_z1 = triDexel.XY_array[idx_y, idx_x].DexelPoints == null ? null : triDexel.XY_array[idx_y, idx_x].DexelPoints.ToArray();
                float[] dexel_z2 = triDexel.XY_array[idx_y, idx_x + 1].DexelPoints == null ? null : triDexel.XY_array[idx_y, idx_x + 1].DexelPoints.ToArray();
                float[] dexel_z3 = triDexel.XY_array[idx_y + 1, idx_x].DexelPoints == null ? null : triDexel.XY_array[idx_y + 1, idx_x].DexelPoints.ToArray();
                float[] dexel_z4 = triDexel.XY_array[idx_y + 1, idx_x + 1].DexelPoints == null ? null : triDexel.XY_array[idx_y + 1, idx_x + 1].DexelPoints.ToArray();

                // a cube point is occupied when dexels respectively from xyz directions go through the cube point
                // point 1
                if (IsInDexel(dexel_z1, zCoordinate) && IsInDexel(dexel_x1, xCoordinate) && IsInDexel(dexel_y1, yCoordinate))
                    cubePointStates[0] = true;
                // point 2
                if (IsInDexel(dexel_z1, zCoordinate_side) && IsInDexel(dexel_x2, xCoordinate) && IsInDexel(dexel_y3, yCoordinate))
                    cubePointStates[1] = true;
                // point 3
                if (IsInDexel(dexel_z2, zCoordinate_side) && IsInDexel(dexel_x2, xCoordinate_side) && IsInDexel(dexel_y4, yCoordinate))
                    cubePointStates[2] = true;
                // point 4
                if (IsInDexel(dexel_z2, zCoordinate) && IsInDexel(dexel_x1, xCoordinate_side) && IsInDexel(dexel_y2, yCoordinate))
                    cubePointStates[3] = true;
                // point 5
                if (IsInDexel(dexel_z3, zCoordinate) && IsInDexel(dexel_x3, xCoordinate) && IsInDexel(dexel_y1, yCoordinate_side))
                    cubePointStates[4] = true;
                // point 6
                if (IsInDexel(dexel_z3, zCoordinate_side) && IsInDexel(dexel_x4, xCoordinate) && IsInDexel(dexel_y3, yCoordinate_side))
                    cubePointStates[5] = true;
                // point 7
                if (IsInDexel(dexel_z4, zCoordinate_side) && IsInDexel(dexel_x4, xCoordinate_side) && IsInDexel(dexel_y4, yCoordinate_side))
                    cubePointStates[6] = true;
                // point 8
                if (IsInDexel(dexel_z4, zCoordinate) && IsInDexel(dexel_x3, xCoordinate_side) && IsInDexel(dexel_y2, yCoordinate_side))
                    cubePointStates[7] = true;

                occupiedCubePoints = CubePointStateCount(cubePointStates);

                return cubePointStates;
            }

            // count the number of occupied cube points
            int CubePointStateCount(bool[] cubePointStates)
            {
                int stateCount = 0;
                foreach (bool state in cubePointStates)
                    if (state) stateCount += 1;

                return stateCount;
            }

            // verfy a cube point is in a dexel or not
            bool IsInDexel(float[] dexelPoints, float cubePoint)
            {
                bool inDexel = false;
                if(dexelPoints != null)
                {
                    for (int i = 0; i < dexelPoints.Length; i += 2)
                    {
                        if (cubePoint >= dexelPoints[i] && cubePoint <= dexelPoints[i + 1])
                        {
                            inDexel = true;
                            break;
                        }
                    }
                }

                return inDexel;
            }

            // get vertice and vertice normal data in a single cube from tri-dexel data structure
            // the normal(average) of the contour formed by the vertices is also returned
            Vector3[] GetVerticeAndNormalFromDexelData(int idx_x, int idx_y, int idx_z, out Vector3 contourNormal, out Vector3[] verticeNormals)
            {
                // compute min/max local coordinate of the cube 
                float xCoordinate = idx_x * gridLength + fieldOrigin.x;
                float yCoordinate = idx_y * gridLength + fieldOrigin.y;
                float zCoordinate = idx_z * gridLength + fieldOrigin.z;
                float xCoordinate_side = (idx_x + 1) * gridLength + fieldOrigin.x;
                float yCoordibate_side = (idx_y + 1) * gridLength + fieldOrigin.y;
                float zCoordinate_side = (idx_z + 1) * gridLength + fieldOrigin.z;

                List<Vector3> vertexFind = new List<Vector3>();
                List<Vector3> normalFind = new List<Vector3>();
                List<Vector3> vertexBuffer = new List<Vector3>();
                List<Vector3> normalBuffer = new List<Vector3>();
                Vector3 sumOfNormals = Vector3.zero;
                int vertexCount = 0;

                // get vertex on z direction
                if (triDexel.XY_array[idx_y, idx_x].DexelPoints != null)
                {
                    int numOfVertice = 0;  
                    foreach (float z_value in triDexel.XY_array[idx_y, idx_x].DexelPoints.ToArray())
                    {
                        if (z_value >= zCoordinate && z_value <= zCoordinate_side)
                        {
                            int dexelPointIdx = triDexel.XY_array[idx_y, idx_x].DexelPoints.IndexOf(z_value);
                            vertexFind.Add(new Vector3(xCoordinate, yCoordinate, z_value));
                            normalFind.Add(triDexel.XY_array[idx_y, idx_x].Normals[dexelPointIdx]);
                            sumOfNormals += triDexel.XY_array[idx_y, idx_x].Normals[dexelPointIdx];
                            vertexCount++;
                            numOfVertice++;

                            if (numOfVertice == verticeToFindPerDexel) break;
                        }
                    }
                }
                if (triDexel.XY_array[idx_y, idx_x + 1].DexelPoints != null)
                {
                    foreach (float z_value in triDexel.XY_array[idx_y, idx_x + 1].DexelPoints.ToArray())
                    {
                        int numOfVertice = 0;
                        if (z_value >= zCoordinate && z_value <= zCoordinate_side)
                        {
                            int dexelPointIdx = triDexel.XY_array[idx_y, idx_x + 1].DexelPoints.IndexOf(z_value);
                            vertexFind.Add(new Vector3(xCoordinate_side, yCoordinate, z_value));
                            normalFind.Add(triDexel.XY_array[idx_y, idx_x + 1].Normals[dexelPointIdx]);
                            sumOfNormals += triDexel.XY_array[idx_y, idx_x + 1].Normals[dexelPointIdx];
                            vertexCount++;
                            numOfVertice++;

                            if (numOfVertice == verticeToFindPerDexel) break;
                        }
                    }
                }
                if (triDexel.XY_array[idx_y + 1, idx_x].DexelPoints != null)
                {
                    foreach (float z_value in triDexel.XY_array[idx_y + 1, idx_x].DexelPoints.ToArray())
                    {
                        int numOfVertice = 0;
                        if (z_value >= zCoordinate && z_value <= zCoordinate_side)
                        {
                            int dexelPointIdx = triDexel.XY_array[idx_y + 1, idx_x].DexelPoints.IndexOf(z_value);
                            vertexFind.Add(new Vector3(xCoordinate, yCoordibate_side, z_value));
                            normalFind.Add(triDexel.XY_array[idx_y + 1, idx_x].Normals[dexelPointIdx]);
                            sumOfNormals += triDexel.XY_array[idx_y + 1, idx_x].Normals[dexelPointIdx];
                            vertexCount++;
                            numOfVertice++;

                            if (numOfVertice == verticeToFindPerDexel) break;
                        }
                    }
                }
                if (triDexel.XY_array[idx_y + 1, idx_x + 1].DexelPoints != null)
                {
                    foreach (float z_value in triDexel.XY_array[idx_y + 1, idx_x + 1].DexelPoints.ToArray())
                    {
                        int numOfVertice = 0;
                        if (z_value >= zCoordinate && z_value <= zCoordinate_side)
                        {
                            int dexelPointIdx = triDexel.XY_array[idx_y + 1, idx_x + 1].DexelPoints.IndexOf(z_value);
                            vertexFind.Add(new Vector3(xCoordinate_side, yCoordibate_side, z_value));
                            normalFind.Add(triDexel.XY_array[idx_y + 1, idx_x + 1].Normals[dexelPointIdx]);
                            sumOfNormals += triDexel.XY_array[idx_y + 1, idx_x + 1].Normals[dexelPointIdx];
                            vertexCount++;
                            numOfVertice++;

                            if (numOfVertice == verticeToFindPerDexel) break;
                        }
                    }
                }

                // get vertex on x direction
                if (triDexel.ZY_array[idx_y, idx_z].DexelPoints != null)
                {
                    foreach (float x_value in triDexel.ZY_array[idx_y, idx_z].DexelPoints.ToArray())
                    {
                        int numOfVertice = 0;
                        if (x_value >= xCoordinate && x_value <= xCoordinate_side)
                        {
                            int dexelPointIdx = triDexel.ZY_array[idx_y, idx_z].DexelPoints.IndexOf(x_value);
                            vertexFind.Add(new Vector3(x_value, yCoordinate, zCoordinate));
                            normalFind.Add(triDexel.ZY_array[idx_y, idx_z].Normals[dexelPointIdx]);
                            sumOfNormals += triDexel.ZY_array[idx_y, idx_z].Normals[dexelPointIdx];
                            vertexCount++;
                            numOfVertice++;

                            if (numOfVertice == verticeToFindPerDexel) break;
                        }
                    }
                }
                if (triDexel.ZY_array[idx_y, idx_z + 1].DexelPoints != null)
                {
                    foreach (float x_value in triDexel.ZY_array[idx_y, idx_z + 1].DexelPoints.ToArray())
                    {
                        int numOfVertice = 0;
                        if (x_value >= xCoordinate && x_value <= xCoordinate_side)
                        {
                            int dexelPointIdx = triDexel.ZY_array[idx_y, idx_z + 1].DexelPoints.IndexOf(x_value);
                            vertexFind.Add(new Vector3(x_value, yCoordinate, zCoordinate_side));
                            normalFind.Add(triDexel.ZY_array[idx_y, idx_z + 1].Normals[dexelPointIdx]);
                            sumOfNormals += triDexel.ZY_array[idx_y, idx_z + 1].Normals[dexelPointIdx];
                            vertexCount++;
                            numOfVertice++;

                            if (numOfVertice == verticeToFindPerDexel) break;
                        }
                    }
                }
                if (triDexel.ZY_array[idx_y + 1, idx_z].DexelPoints != null)
                {
                    foreach (float x_value in triDexel.ZY_array[idx_y + 1, idx_z].DexelPoints.ToArray())
                    {
                        int numOfVertice = 0;
                        if (x_value >= xCoordinate && x_value <= xCoordinate_side)
                        {
                            int dexelPointIdx = triDexel.ZY_array[idx_y + 1, idx_z].DexelPoints.IndexOf(x_value);
                            vertexFind.Add(new Vector3(x_value, yCoordibate_side, zCoordinate));
                            normalFind.Add(triDexel.ZY_array[idx_y + 1, idx_z].Normals[dexelPointIdx]);
                            sumOfNormals += triDexel.ZY_array[idx_y + 1, idx_z].Normals[dexelPointIdx];
                            vertexCount++;
                            numOfVertice++;

                            if (numOfVertice == verticeToFindPerDexel) break;
                        }
                    }
                }
                if (triDexel.ZY_array[idx_y + 1, idx_z + 1].DexelPoints != null)
                {
                    foreach (float x_value in triDexel.ZY_array[idx_y + 1, idx_z + 1].DexelPoints.ToArray())
                    {
                        int numOfVertice = 0;
                        if (x_value >= xCoordinate && x_value <= xCoordinate_side)
                        {
                            int dexelPointIdx = triDexel.ZY_array[idx_y + 1, idx_z + 1].DexelPoints.IndexOf(x_value);
                            vertexFind.Add(new Vector3(x_value, yCoordibate_side, zCoordinate_side));
                            normalFind.Add(triDexel.ZY_array[idx_y + 1, idx_z + 1].Normals[dexelPointIdx]);
                            sumOfNormals += triDexel.ZY_array[idx_y + 1, idx_z + 1].Normals[dexelPointIdx];
                            vertexCount++;
                            numOfVertice++;

                            if (numOfVertice == verticeToFindPerDexel) break;
                        }
                    }
                }

                // get vertex on y direction
                if (triDexel.XZ_array[idx_z, idx_x].DexelPoints != null)
                {
                    foreach (float y_value in triDexel.XZ_array[idx_z, idx_x].DexelPoints.ToArray())
                    {
                        int numOfVertice = 0;
                        if (y_value >= yCoordinate && y_value <= yCoordibate_side)
                        {
                            int dexelPointIdx = triDexel.XZ_array[idx_z, idx_x].DexelPoints.IndexOf(y_value);
                            vertexFind.Add(new Vector3(xCoordinate, y_value, zCoordinate));
                            normalFind.Add(triDexel.XZ_array[idx_z, idx_x].Normals[dexelPointIdx]);
                            sumOfNormals += triDexel.XZ_array[idx_z, idx_x].Normals[dexelPointIdx];
                            vertexCount++;
                            numOfVertice++;

                            if (numOfVertice == verticeToFindPerDexel) break;
                        }
                    }
                }
                if (triDexel.XZ_array[idx_z, idx_x + 1].DexelPoints != null)
                {
                    foreach (float y_value in triDexel.XZ_array[idx_z, idx_x + 1].DexelPoints.ToArray())
                    {
                        int numOfVertice = 0;
                        if (y_value >= yCoordinate && y_value <= yCoordibate_side)
                        {
                            int dexelPointIdx = triDexel.XZ_array[idx_z, idx_x + 1].DexelPoints.IndexOf(y_value);
                            vertexFind.Add(new Vector3(xCoordinate_side, y_value, zCoordinate));
                            normalFind.Add(triDexel.XZ_array[idx_z, idx_x + 1].Normals[dexelPointIdx]);
                            sumOfNormals += triDexel.XZ_array[idx_z, idx_x + 1].Normals[dexelPointIdx];
                            vertexCount++;
                            numOfVertice++;

                            if (numOfVertice == verticeToFindPerDexel) break;
                        }
                    }
                }
                if (triDexel.XZ_array[idx_z + 1, idx_x].DexelPoints != null)
                {
                    foreach (float y_value in triDexel.XZ_array[idx_z + 1, idx_x].DexelPoints.ToArray())
                    {
                        int numOfVertice = 0;
                        if (y_value >= yCoordinate && y_value <= yCoordibate_side)
                        {
                            int dexelPointIdx = triDexel.XZ_array[idx_z + 1, idx_x].DexelPoints.IndexOf(y_value);
                            vertexFind.Add(new Vector3(xCoordinate, y_value, zCoordinate_side));
                            normalFind.Add(triDexel.XZ_array[idx_z + 1, idx_x].Normals[dexelPointIdx]);
                            sumOfNormals += triDexel.XZ_array[idx_z + 1, idx_x].Normals[dexelPointIdx];
                            vertexCount++;
                            numOfVertice++;

                            if (numOfVertice == verticeToFindPerDexel) break;
                        }
                    }
                }
                if (triDexel.XZ_array[idx_z + 1, idx_x + 1].DexelPoints != null)
                {
                    foreach (float y_value in triDexel.XZ_array[idx_z + 1, idx_x + 1].DexelPoints.ToArray())
                    {
                        int numOfVertice = 0;
                        if (y_value >= yCoordinate && y_value <= yCoordibate_side)
                        {
                            int dexelPointIdx = triDexel.XZ_array[idx_z + 1, idx_x + 1].DexelPoints.IndexOf(y_value);
                            vertexFind.Add(new Vector3(xCoordinate_side, y_value, zCoordinate_side));
                            normalFind.Add(triDexel.XZ_array[idx_z + 1, idx_x + 1].Normals[dexelPointIdx]);
                            sumOfNormals += triDexel.XZ_array[idx_z + 1, idx_x + 1].Normals[dexelPointIdx];
                            vertexCount++;
                            numOfVertice++;

                            if (numOfVertice == verticeToFindPerDexel) break;
                        }
                    }
                }

                // compute the average normal of the contour
                contourNormal = sumOfNormals / vertexCount;

                // return vertice normals
                verticeNormals = normalFind.ToArray();

                return vertexFind.ToArray();
            }

            // get vertice and vertice normal data in a single cube from tri-dexel data structure
            // the normal(average) of the contour formed by the vertices is also returned
            Vector3[] GetVerticeAndNormalFromDexelData_dev(int idx_x, int idx_y, int idx_z, out Vector3 contourNormal, out Vector3[] verticeNormals)
            {
                // compute min/max local coordinate of the cube 
                float xCoordinate = idx_x * gridLength + fieldOrigin.x;
                float yCoordinate = idx_y * gridLength + fieldOrigin.y;
                float zCoordinate = idx_z * gridLength + fieldOrigin.z;
                float xCoordinate_side = (idx_x + 1) * gridLength + fieldOrigin.x;
                float yCoordibate_side = (idx_y + 1) * gridLength + fieldOrigin.y;
                float zCoordinate_side = (idx_z + 1) * gridLength + fieldOrigin.z;

                List<Vector3> vertexFind = new List<Vector3>();
                List<Vector3> normalFind = new List<Vector3>();
                List<int> indexBuffer = new List<int>();
                Vector3 sumOfNormals = Vector3.zero;
                int vertexCount = 0;

                // get vertex on z direction
                if (triDexel.XY_array[idx_y, idx_x].DexelPoints != null)
                {
                    int numOfVertice = 0;
                    foreach (float z_value in triDexel.XY_array[idx_y, idx_x].DexelPoints.ToArray())
                    {
                        if (z_value >= zCoordinate && z_value <= zCoordinate_side)
                        {
                            int dexelPointIdx = triDexel.XY_array[idx_y, idx_x].DexelPoints.IndexOf(z_value);
                            indexBuffer.Add(dexelPointIdx);
                            numOfVertice++;
                        }
                        if (numOfVertice == verticeToFindPerDexel) break;
                    }
                    if (indexBuffer.Count == 1)
                    {
                        vertexFind.Add(new Vector3(xCoordinate, yCoordinate, triDexel.XY_array[idx_y, idx_x].DexelPoints[indexBuffer[0]]));
                        normalFind.Add(triDexel.XY_array[idx_y, idx_x].Normals[indexBuffer[0]]);
                        sumOfNormals += triDexel.XY_array[idx_y, idx_x].Normals[indexBuffer[0]];
                        vertexCount++;
                    }
                    else if(indexBuffer.Count == 2 && indexBuffer[0] % 2 == 1 && indexBuffer[1] % 2 == 0)
                    {
                        vertexFind.Add(new Vector3(xCoordinate, yCoordinate, triDexel.XY_array[idx_y, idx_x].DexelPoints[indexBuffer[0]]));
                        vertexFind.Add(new Vector3(xCoordinate, yCoordinate, triDexel.XY_array[idx_y, idx_x].DexelPoints[indexBuffer[1]]));
                        normalFind.Add(triDexel.XY_array[idx_y, idx_x].Normals[indexBuffer[0]]);
                        normalFind.Add(triDexel.XY_array[idx_y, idx_x].Normals[indexBuffer[1]]);
                        sumOfNormals += triDexel.XY_array[idx_y, idx_x].Normals[indexBuffer[0]] + triDexel.XY_array[idx_y, idx_x].Normals[indexBuffer[1]];
                        vertexCount += 2;
                    }
                    
                    indexBuffer.Clear();
                }
                if (triDexel.XY_array[idx_y, idx_x + 1].DexelPoints != null)
                {
                    foreach (float z_value in triDexel.XY_array[idx_y, idx_x + 1].DexelPoints.ToArray())
                    {
                        int numOfVertice = 0;
                        if (z_value >= zCoordinate && z_value <= zCoordinate_side)
                        {
                            int dexelPointIdx = triDexel.XY_array[idx_y, idx_x + 1].DexelPoints.IndexOf(z_value);
                            indexBuffer.Add(dexelPointIdx);
                            numOfVertice++;
                        }
                        if (numOfVertice == verticeToFindPerDexel) break;
                    }
                    if(indexBuffer.Count == 1)
                    {
                        vertexFind.Add(new Vector3(xCoordinate_side, yCoordinate, triDexel.XY_array[idx_y, idx_x + 1].DexelPoints[indexBuffer[0]]));
                        normalFind.Add(triDexel.XY_array[idx_y, idx_x + 1].Normals[indexBuffer[0]]);
                        sumOfNormals += triDexel.XY_array[idx_y, idx_x + 1].Normals[indexBuffer[0]];
                        vertexCount ++;
                    }
                    else if(indexBuffer.Count == 2 && indexBuffer[0] % 2 == 1 && indexBuffer[1] % 2 == 0)
                    {
                        vertexFind.Add(new Vector3(xCoordinate_side, yCoordinate, triDexel.XY_array[idx_y, idx_x + 1].DexelPoints[indexBuffer[0]]));
                        vertexFind.Add(new Vector3(xCoordinate_side, yCoordinate, triDexel.XY_array[idx_y, idx_x + 1].DexelPoints[indexBuffer[1]]));
                        normalFind.Add(triDexel.XY_array[idx_y, idx_x + 1].Normals[indexBuffer[0]]);
                        normalFind.Add(triDexel.XY_array[idx_y, idx_x + 1].Normals[indexBuffer[1]]);
                        sumOfNormals += triDexel.XY_array[idx_y, idx_x + 1].Normals[indexBuffer[0]] + triDexel.XY_array[idx_y, idx_x + 1].Normals[indexBuffer[1]];
                        vertexCount += 2;
                    }
                    indexBuffer.Clear();
                }
                if (triDexel.XY_array[idx_y + 1, idx_x].DexelPoints != null)
                {
                    foreach (float z_value in triDexel.XY_array[idx_y + 1, idx_x].DexelPoints.ToArray())
                    {
                        int numOfVertice = 0;
                        if (z_value >= zCoordinate && z_value <= zCoordinate_side)
                        {
                            int dexelPointIdx = triDexel.XY_array[idx_y + 1, idx_x].DexelPoints.IndexOf(z_value);
                            indexBuffer.Add(dexelPointIdx);
                            numOfVertice++;
                        }
                        if (numOfVertice == verticeToFindPerDexel) break;
                    }
                    if(indexBuffer.Count == 1)
                    {
                        vertexFind.Add(new Vector3(xCoordinate, yCoordibate_side, triDexel.XY_array[idx_y + 1, idx_x].DexelPoints[indexBuffer[0]]));
                        normalFind.Add(triDexel.XY_array[idx_y + 1, idx_x].Normals[indexBuffer[0]]);
                        sumOfNormals += triDexel.XY_array[idx_y + 1, idx_x].Normals[indexBuffer[0]];
                        vertexCount++;
                    }
                    else if(indexBuffer.Count == 2 && indexBuffer[0] % 2 == 1 && indexBuffer[1] % 2 == 0)
                    {
                        vertexFind.Add(new Vector3(xCoordinate, yCoordibate_side, triDexel.XY_array[idx_y + 1, idx_x].DexelPoints[indexBuffer[0]]));
                        vertexFind.Add(new Vector3(xCoordinate, yCoordibate_side, triDexel.XY_array[idx_y + 1, idx_x].DexelPoints[indexBuffer[1]]));
                        normalFind.Add(triDexel.XY_array[idx_y + 1, idx_x].Normals[indexBuffer[0]]);
                        normalFind.Add(triDexel.XY_array[idx_y + 1, idx_x].Normals[indexBuffer[1]]);
                        sumOfNormals += triDexel.XY_array[idx_y + 1, idx_x].Normals[indexBuffer[0]] + triDexel.XY_array[idx_y + 1, idx_x].Normals[indexBuffer[1]];
                        vertexCount += 2;
                    }
                    indexBuffer.Clear();
                }
                if (triDexel.XY_array[idx_y + 1, idx_x + 1].DexelPoints != null)
                {
                    foreach (float z_value in triDexel.XY_array[idx_y + 1, idx_x + 1].DexelPoints.ToArray())
                    {
                        int numOfVertice = 0;
                        if (z_value >= zCoordinate && z_value <= zCoordinate_side)
                        {
                            int dexelPointIdx = triDexel.XY_array[idx_y + 1, idx_x + 1].DexelPoints.IndexOf(z_value);
                            indexBuffer.Add(dexelPointIdx);
                            numOfVertice++;
                        }
                        if (numOfVertice == verticeToFindPerDexel) break;
                    }
                    if(indexBuffer.Count == 1)
                    {
                        vertexFind.Add(new Vector3(xCoordinate_side, yCoordibate_side, triDexel.XY_array[idx_y + 1, idx_x + 1].DexelPoints[indexBuffer[0]]));
                        normalFind.Add(triDexel.XY_array[idx_y + 1, idx_x + 1].Normals[indexBuffer[0]]);
                        sumOfNormals += triDexel.XY_array[idx_y + 1, idx_x + 1].Normals[indexBuffer[0]];
                        vertexCount++;
                    }
                    else if(indexBuffer.Count == 2 && indexBuffer[0] % 2 == 1 && indexBuffer[1] % 2 == 0)
                    {
                        vertexFind.Add(new Vector3(xCoordinate_side, yCoordibate_side, triDexel.XY_array[idx_y + 1, idx_x + 1].DexelPoints[indexBuffer[0]]));
                        vertexFind.Add(new Vector3(xCoordinate_side, yCoordibate_side, triDexel.XY_array[idx_y + 1, idx_x + 1].DexelPoints[indexBuffer[1]]));
                        normalFind.Add(triDexel.XY_array[idx_y + 1, idx_x + 1].Normals[indexBuffer[0]]);
                        normalFind.Add(triDexel.XY_array[idx_y + 1, idx_x + 1].Normals[indexBuffer[1]]);
                        sumOfNormals += triDexel.XY_array[idx_y + 1, idx_x + 1].Normals[indexBuffer[0]] + triDexel.XY_array[idx_y + 1, idx_x + 1].Normals[indexBuffer[1]];
                        vertexCount += 2;
                    }
                    indexBuffer.Clear();
                }

                // get vertex on x direction
                if (triDexel.ZY_array[idx_y, idx_z].DexelPoints != null)
                {
                    foreach (float x_value in triDexel.ZY_array[idx_y, idx_z].DexelPoints.ToArray())
                    {
                        int numOfVertice = 0;
                        if (x_value >= xCoordinate && x_value <= xCoordinate_side)
                        {
                            int dexelPointIdx = triDexel.ZY_array[idx_y, idx_z].DexelPoints.IndexOf(x_value);
                            indexBuffer.Add(dexelPointIdx);
                            numOfVertice++;
                        }
                        if (numOfVertice == verticeToFindPerDexel) break;
                    }
                    if(indexBuffer.Count == 1)
                    {
                        vertexFind.Add(new Vector3(triDexel.ZY_array[idx_y, idx_z].DexelPoints[indexBuffer[0]], yCoordinate, zCoordinate));
                        normalFind.Add(triDexel.ZY_array[idx_y, idx_z].Normals[indexBuffer[0]]);
                        sumOfNormals += triDexel.ZY_array[idx_y, idx_z].Normals[indexBuffer[0]];
                        vertexCount++;
                    }
                    else if(indexBuffer.Count == 2 && indexBuffer[0] % 2 == 1 && indexBuffer[1] % 2 == 0)
                    {
                        vertexFind.Add(new Vector3(triDexel.ZY_array[idx_y, idx_z].DexelPoints[indexBuffer[0]], yCoordinate, zCoordinate));
                        vertexFind.Add(new Vector3(triDexel.ZY_array[idx_y, idx_z].DexelPoints[indexBuffer[1]], yCoordinate, zCoordinate));
                        normalFind.Add(triDexel.ZY_array[idx_y, idx_z].Normals[indexBuffer[0]]);
                        normalFind.Add(triDexel.ZY_array[idx_y, idx_z].Normals[indexBuffer[1]]);
                        sumOfNormals += triDexel.ZY_array[idx_y, idx_z].Normals[indexBuffer[0]] + triDexel.ZY_array[idx_y, idx_z].Normals[indexBuffer[1]];
                        vertexCount += 2;
                    }
                    indexBuffer.Clear();
                }
                if (triDexel.ZY_array[idx_y, idx_z + 1].DexelPoints != null)
                {
                    foreach (float x_value in triDexel.ZY_array[idx_y, idx_z + 1].DexelPoints.ToArray())
                    {
                        int numOfVertice = 0;
                        if (x_value >= xCoordinate && x_value <= xCoordinate_side)
                        {
                            int dexelPointIdx = triDexel.ZY_array[idx_y, idx_z + 1].DexelPoints.IndexOf(x_value);
                            indexBuffer.Add(dexelPointIdx);
                            numOfVertice++;
                        }
                        if (numOfVertice == verticeToFindPerDexel) break;
                    }
                    if(indexBuffer.Count == 1)
                    {
                        vertexFind.Add(new Vector3(triDexel.ZY_array[idx_y, idx_z + 1].DexelPoints[indexBuffer[0]], yCoordinate, zCoordinate_side));
                        normalFind.Add(triDexel.ZY_array[idx_y, idx_z + 1].Normals[indexBuffer[0]]);
                        sumOfNormals += triDexel.ZY_array[idx_y, idx_z + 1].Normals[indexBuffer[0]];
                        vertexCount++;
                    }
                    else if(indexBuffer.Count == 2 && indexBuffer[0] % 2 == 1 && indexBuffer[1] % 2 == 0)
                    {
                        vertexFind.Add(new Vector3(triDexel.ZY_array[idx_y, idx_z + 1].DexelPoints[indexBuffer[0]], yCoordinate, zCoordinate_side));
                        vertexFind.Add(new Vector3(triDexel.ZY_array[idx_y, idx_z + 1].DexelPoints[indexBuffer[1]], yCoordinate, zCoordinate_side));
                        normalFind.Add(triDexel.ZY_array[idx_y, idx_z + 1].Normals[indexBuffer[0]]);
                        normalFind.Add(triDexel.ZY_array[idx_y, idx_z + 1].Normals[indexBuffer[1]]);
                        sumOfNormals += triDexel.ZY_array[idx_y, idx_z + 1].Normals[indexBuffer[0]] + triDexel.ZY_array[idx_y, idx_z + 1].Normals[indexBuffer[1]];
                        vertexCount += 2;
                    }
                    indexBuffer.Clear();
                }
                if (triDexel.ZY_array[idx_y + 1, idx_z].DexelPoints != null)
                {
                    foreach (float x_value in triDexel.ZY_array[idx_y + 1, idx_z].DexelPoints.ToArray())
                    {
                        int numOfVertice = 0;
                        if (x_value >= xCoordinate && x_value <= xCoordinate_side)
                        {
                            int dexelPointIdx = triDexel.ZY_array[idx_y + 1, idx_z].DexelPoints.IndexOf(x_value);
                            indexBuffer.Add(dexelPointIdx);
                            numOfVertice++;
                        }
                        if (numOfVertice == verticeToFindPerDexel) break;
                    }
                    if(indexBuffer.Count == 1)
                    {
                        vertexFind.Add(new Vector3(triDexel.ZY_array[idx_y + 1, idx_z].DexelPoints[indexBuffer[0]], yCoordibate_side, zCoordinate));
                        normalFind.Add(triDexel.ZY_array[idx_y + 1, idx_z].Normals[indexBuffer[0]]);
                        sumOfNormals += triDexel.ZY_array[idx_y + 1, idx_z].Normals[indexBuffer[0]];
                        vertexCount++;
                    }
                    else if(indexBuffer.Count == 2 && indexBuffer[0] % 2 == 1 && indexBuffer[1] % 2 == 0)
                    {
                        vertexFind.Add(new Vector3(triDexel.ZY_array[idx_y + 1, idx_z].DexelPoints[indexBuffer[0]], yCoordibate_side, zCoordinate));
                        vertexFind.Add(new Vector3(triDexel.ZY_array[idx_y + 1, idx_z].DexelPoints[indexBuffer[1]], yCoordibate_side, zCoordinate));
                        normalFind.Add(triDexel.ZY_array[idx_y + 1, idx_z].Normals[indexBuffer[0]]);
                        normalFind.Add(triDexel.ZY_array[idx_y + 1, idx_z].Normals[indexBuffer[1]]);
                        sumOfNormals += triDexel.ZY_array[idx_y + 1, idx_z].Normals[indexBuffer[0]] + triDexel.ZY_array[idx_y + 1, idx_z].Normals[indexBuffer[1]];
                        vertexCount += 2;
                    }
                    indexBuffer.Clear();
                }
                if (triDexel.ZY_array[idx_y + 1, idx_z + 1].DexelPoints != null)
                {
                    foreach (float x_value in triDexel.ZY_array[idx_y + 1, idx_z + 1].DexelPoints.ToArray())
                    {
                        int numOfVertice = 0;
                        if (x_value >= xCoordinate && x_value <= xCoordinate_side)
                        {
                            int dexelPointIdx = triDexel.ZY_array[idx_y + 1, idx_z + 1].DexelPoints.IndexOf(x_value);
                            indexBuffer.Add(dexelPointIdx);
                            numOfVertice++;
                        }
                        if (numOfVertice == verticeToFindPerDexel) break;
                    }
                    if(indexBuffer.Count == 1)
                    {
                        vertexFind.Add(new Vector3(triDexel.ZY_array[idx_y + 1, idx_z + 1].DexelPoints[indexBuffer[0]], yCoordibate_side, zCoordinate_side));
                        normalFind.Add(triDexel.ZY_array[idx_y + 1, idx_z + 1].Normals[indexBuffer[0]]);
                        sumOfNormals += triDexel.ZY_array[idx_y + 1, idx_z + 1].Normals[indexBuffer[0]];
                        vertexCount++;
                    }
                    else if(indexBuffer.Count == 2 && indexBuffer[0] % 2 == 1 && indexBuffer[1] % 2 == 0)
                    {
                        vertexFind.Add(new Vector3(triDexel.ZY_array[idx_y + 1, idx_z + 1].DexelPoints[indexBuffer[0]], yCoordibate_side, zCoordinate_side));
                        vertexFind.Add(new Vector3(triDexel.ZY_array[idx_y + 1, idx_z + 1].DexelPoints[indexBuffer[1]], yCoordibate_side, zCoordinate_side));
                        normalFind.Add(triDexel.ZY_array[idx_y + 1, idx_z + 1].Normals[indexBuffer[0]]);
                        normalFind.Add(triDexel.ZY_array[idx_y + 1, idx_z + 1].Normals[indexBuffer[1]]);
                        sumOfNormals += triDexel.ZY_array[idx_y + 1, idx_z + 1].Normals[indexBuffer[0]] + triDexel.ZY_array[idx_y + 1, idx_z + 1].Normals[indexBuffer[1]];
                        vertexCount += 2;
                    }
                    indexBuffer.Clear();
                }

                // get vertex on y direction
                if (triDexel.XZ_array[idx_z, idx_x].DexelPoints != null)
                {
                    foreach (float y_value in triDexel.XZ_array[idx_z, idx_x].DexelPoints.ToArray())
                    {
                        int numOfVertice = 0;
                        if (y_value >= yCoordinate && y_value <= yCoordibate_side)
                        {
                            int dexelPointIdx = triDexel.XZ_array[idx_z, idx_x].DexelPoints.IndexOf(y_value);
                            indexBuffer.Add(dexelPointIdx);
                            numOfVertice++;
                        }
                        if (numOfVertice == verticeToFindPerDexel) break;
                    }
                    if(indexBuffer.Count == 1)
                    {
                        vertexFind.Add(new Vector3(xCoordinate, triDexel.XZ_array[idx_z, idx_x].DexelPoints[indexBuffer[0]], zCoordinate));
                        normalFind.Add(triDexel.XZ_array[idx_z, idx_x].Normals[indexBuffer[0]]);
                        sumOfNormals += triDexel.XZ_array[idx_z, idx_x].Normals[indexBuffer[0]];
                        vertexCount++;
                    }
                    else if(indexBuffer.Count == 2 && indexBuffer[0] % 2 == 1 && indexBuffer[1] % 2 == 0)
                    {
                        vertexFind.Add(new Vector3(xCoordinate, triDexel.XZ_array[idx_z, idx_x].DexelPoints[indexBuffer[0]], zCoordinate));
                        vertexFind.Add(new Vector3(xCoordinate, triDexel.XZ_array[idx_z, idx_x].DexelPoints[indexBuffer[1]], zCoordinate));
                        normalFind.Add(triDexel.XZ_array[idx_z, idx_x].Normals[indexBuffer[0]]);
                        normalFind.Add(triDexel.XZ_array[idx_z, idx_x].Normals[indexBuffer[1]]);
                        sumOfNormals += triDexel.XZ_array[idx_z, idx_x].Normals[indexBuffer[0]] + triDexel.XZ_array[idx_z, idx_x].Normals[indexBuffer[1]];
                        vertexCount += 2;
                    }
                    indexBuffer.Clear();
                }
                if (triDexel.XZ_array[idx_z, idx_x + 1].DexelPoints != null)
                {
                    foreach (float y_value in triDexel.XZ_array[idx_z, idx_x + 1].DexelPoints.ToArray())
                    {
                        int numOfVertice = 0;
                        if (y_value >= yCoordinate && y_value <= yCoordibate_side)
                        {
                            int dexelPointIdx = triDexel.XZ_array[idx_z, idx_x + 1].DexelPoints.IndexOf(y_value);
                            indexBuffer.Add(dexelPointIdx);
                            numOfVertice++;
                        }
                        if (numOfVertice == verticeToFindPerDexel) break;
                    }
                    if(indexBuffer.Count == 1)
                    {
                        vertexFind.Add(new Vector3(xCoordinate_side, triDexel.XZ_array[idx_z, idx_x + 1].DexelPoints[indexBuffer[0]], zCoordinate));
                        normalFind.Add(triDexel.XZ_array[idx_z, idx_x + 1].Normals[indexBuffer[0]]);
                        sumOfNormals += triDexel.XZ_array[idx_z, idx_x + 1].Normals[indexBuffer[0]];
                        vertexCount++;
                    }
                    else if(indexBuffer.Count == 2 && indexBuffer[0] % 2 == 1 && indexBuffer[1] % 2 == 0)
                    {
                        vertexFind.Add(new Vector3(xCoordinate_side, triDexel.XZ_array[idx_z, idx_x + 1].DexelPoints[indexBuffer[0]], zCoordinate));
                        vertexFind.Add(new Vector3(xCoordinate_side, triDexel.XZ_array[idx_z, idx_x + 1].DexelPoints[indexBuffer[1]], zCoordinate));
                        normalFind.Add(triDexel.XZ_array[idx_z, idx_x + 1].Normals[indexBuffer[0]]);
                        normalFind.Add(triDexel.XZ_array[idx_z, idx_x + 1].Normals[indexBuffer[1]]);
                        sumOfNormals += triDexel.XZ_array[idx_z, idx_x + 1].Normals[indexBuffer[0]] + triDexel.XZ_array[idx_z, idx_x + 1].Normals[indexBuffer[1]];
                        vertexCount += 2;
                    }
                    indexBuffer.Clear();
                }
                if (triDexel.XZ_array[idx_z + 1, idx_x].DexelPoints != null)
                {
                    foreach (float y_value in triDexel.XZ_array[idx_z + 1, idx_x].DexelPoints.ToArray())
                    {
                        int numOfVertice = 0;
                        if (y_value >= yCoordinate && y_value <= yCoordibate_side)
                        {
                            int dexelPointIdx = triDexel.XZ_array[idx_z + 1, idx_x].DexelPoints.IndexOf(y_value);
                            indexBuffer.Add(dexelPointIdx);
                            numOfVertice++;
                        }
                        if (numOfVertice == verticeToFindPerDexel) break;
                    }
                    if (indexBuffer.Count == 1)
                    {
                        vertexFind.Add(new Vector3(xCoordinate, triDexel.XZ_array[idx_z + 1, idx_x].DexelPoints[indexBuffer[0]], zCoordinate_side));
                        normalFind.Add(triDexel.XZ_array[idx_z + 1, idx_x].Normals[indexBuffer[0]]);
                        sumOfNormals += triDexel.XZ_array[idx_z + 1, idx_x].Normals[indexBuffer[0]];
                        vertexCount++;
                    }
                    else if (indexBuffer.Count == 2 && indexBuffer[0] % 2 == 1 && indexBuffer[1] % 2 == 0)
                    {
                        vertexFind.Add(new Vector3(xCoordinate, triDexel.XZ_array[idx_z + 1, idx_x].DexelPoints[indexBuffer[0]], zCoordinate_side));
                        vertexFind.Add(new Vector3(xCoordinate, triDexel.XZ_array[idx_z + 1, idx_x].DexelPoints[indexBuffer[1]], zCoordinate_side));
                        normalFind.Add(triDexel.XZ_array[idx_z + 1, idx_x].Normals[indexBuffer[0]]);
                        normalFind.Add(triDexel.XZ_array[idx_z + 1, idx_x].Normals[indexBuffer[1]]);
                        sumOfNormals += triDexel.XZ_array[idx_z + 1, idx_x].Normals[indexBuffer[0]] + triDexel.XZ_array[idx_z + 1, idx_x].Normals[indexBuffer[1]];
                        vertexCount += 2;
                    }
                    indexBuffer.Clear();
                }
                if (triDexel.XZ_array[idx_z + 1, idx_x + 1].DexelPoints != null)
                {
                    foreach (float y_value in triDexel.XZ_array[idx_z + 1, idx_x + 1].DexelPoints.ToArray())
                    {
                        int numOfVertice = 0;
                        if (y_value >= yCoordinate && y_value <= yCoordibate_side)
                        {
                            int dexelPointIdx = triDexel.XZ_array[idx_z + 1, idx_x + 1].DexelPoints.IndexOf(y_value);
                            indexBuffer.Add(dexelPointIdx);
                            numOfVertice++;
                        }
                        if (numOfVertice == verticeToFindPerDexel) break;
                    }
                    if(indexBuffer.Count == 1)
                    {
                        vertexFind.Add(new Vector3(xCoordinate_side, triDexel.XZ_array[idx_z + 1, idx_x + 1].DexelPoints[indexBuffer[0]], zCoordinate_side));
                        normalFind.Add(triDexel.XZ_array[idx_z + 1, idx_x + 1].Normals[indexBuffer[0]]);
                        sumOfNormals += triDexel.XZ_array[idx_z + 1, idx_x + 1].Normals[indexBuffer[0]];
                        vertexCount++;
                    }
                    else if(indexBuffer.Count == 2 && indexBuffer[0] % 2 == 1 && indexBuffer[1] % 2 == 0)
                    {
                        vertexFind.Add(new Vector3(xCoordinate_side, triDexel.XZ_array[idx_z + 1, idx_x + 1].DexelPoints[indexBuffer[0]], zCoordinate_side));
                        vertexFind.Add(new Vector3(xCoordinate_side, triDexel.XZ_array[idx_z + 1, idx_x + 1].DexelPoints[indexBuffer[1]], zCoordinate_side));
                        normalFind.Add(triDexel.XZ_array[idx_z + 1, idx_x + 1].Normals[indexBuffer[0]]);
                        normalFind.Add(triDexel.XZ_array[idx_z + 1, idx_x + 1].Normals[indexBuffer[1]]);
                        sumOfNormals += triDexel.XZ_array[idx_z + 1, idx_x + 1].Normals[indexBuffer[0]] + triDexel.XZ_array[idx_z + 1, idx_x + 1].Normals[indexBuffer[1]];
                        vertexCount += 2;
                    }
                    indexBuffer.Clear();
                }

                // compute the average normal of the contour
                contourNormal = sumOfNormals / vertexCount;

                // return vertice normals
                verticeNormals = normalFind.ToArray();

                return vertexFind.ToArray();
            }

            // arrange the vertex array in a certain order to form contour
            // classic greedy(distance-based) search is applied to arrange the vertex order
            // the number of elements in vertex array must be at least 3
            Vector3[] PutVertexInOrder(Vector3[] vertices, Vector3[] verticeNormals)
            {
                for (int i = 0; i < vertices.Length - 1; i++)
                {
                    Vector3 vertexBuffer = vertices[i + 1];
                    Vector3 normalBuffer = verticeNormals[i + 1];
                    int nearestVertexIndx = -1;
                    float minDistance = float.MaxValue;

                    // classic greedy search
                    for (int k = i + 1; k < vertices.Length; k++)
                    {
                        float distance = Vector3.Distance(vertices[i], vertices[k]);
                        //if (vertices[k].x == vertices[i].x && vertices[k].y == vertices[i].y || vertices[k].y == vertices[i].y && vertices[k].z == vertices[i].z || vertices[k].x == vertices[i].x && vertices[k].z == vertices[i].z)
                        //    distance *= 0.01f;
                        if (vertices[k].x == vertices[i].x || vertices[k].y == vertices[i].y || vertices[k].z == vertices[i].z)
                            distance *= 0.5f;
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            nearestVertexIndx = k;
                        }
                    }
                    vertices[i + 1] = vertices[nearestVertexIndx];
                    vertices[nearestVertexIndx] = vertexBuffer;

                    verticeNormals[i + 1] = verticeNormals[nearestVertexIndx];
                    verticeNormals[nearestVertexIndx] = normalBuffer;
                }

                return vertices;
            }

            // create triangles from a given vertex contour
            // the normals of the vertices is considered t0 give the right index order
            int[] CreateTriangleFromContour(Vector3 contourNormal, Vector3[] vertices, Vector3[] verticeNormals)
            {
                int numOfVertices = vertices.Length;
                int numOfTriangles = numOfVertices - 2;
                List<int> triangles = new List<int>();
                int numOfPositiveSides = 0;
                int numOfNegativeSides = 0;

                // create triangles
                for (int i = 0; i < numOfTriangles; i++)
                {
                    Vector3 side1 = vertices[i + 1] - vertices[0];
                    Vector3 side2 = vertices[i + 2] - vertices[i + 1];
                    Vector3 crossNormal = Vector3.Cross(side1, side2);
                    int sameOrient = 0;
                    int oppositeOrient = 0;
                    for(int j = 0; j < 3; j++)
                    {
                        float dotResult = Vector3.Dot(crossNormal, contourNormal);
                        if (dotResult > 0) sameOrient++;
                        else oppositeOrient++;
                    }
                    if(sameOrient > oppositeOrient)
                    {
                        numOfPositiveSides += 1;
                        triangles.Add(0);
                        triangles.Add(i + 1);
                        triangles.Add(i + 2);
                    }
                    else
                    {
                        triangles.Add(0);
                        triangles.Add(i + 2);
                        triangles.Add(i + 1);
                        numOfNegativeSides += 1;
                    }
                }

                return triangles.ToArray();
            }

            // reconstruct the model surface mesh according to the cube state array
            public void ComputeSurfaceMesh()
            {
                // update current cube field
                UpdateCurrentCubeStates_dev();

                int totalVertice = 0;
                for (int i = 0; i < resolution_x; i++)
                {
                    for (int j = 0; j < resolution_y; j++)
                    {
                        for (int k = 0; k < resolution_z; k++)
                        {
                            if (cubeInfos[i, j, k] != null)
                            {
                                //if (i != 0 || j != 2 || k != 3) continue;
                                //// get the cube state 
                                //cubePointStates = CubePointStateVerify_dev(i, j, k, out int occupationCount);
                                //if (occupationCount < 1 || occupationCount >= 8)
                                //    voxelStates[i, j, k] = false;

                                bool[] cubePointStates = cubeInfos[i, j, k].CubePointStates;
                                int occupationCount = cubeInfos[i, j, k].NumberOfOccupations;
                                //Debug.Log($"[{i}, {j}, {k}]: " + cubePointStates[0] + " " + cubePointStates[1] + " " + cubePointStates[2] + " " + cubePointStates[3] + " " + cubePointStates[4] + " "
                                //    + cubePointStates[5] + " " + cubePointStates[6] + " " + cubePointStates[7] + " " + $"Occupation Count: {occupationCount}");

                                Vector3[] vertices = GetVerticeAndNormalFromDexelData_dev(i, j, k, out Vector3 contourNormal, out Vector3[] verticeNormals);

                                // arrange the vertice order according to the given contour normal
                                // it is to insure the formation of the triangle mesh to visualize correctly
                                if (occupationCount > 0 && occupationCount < 8)
                                {
                                    vertices = PutVertexInOrder(vertices, verticeNormals);
                                    //string verticeString = "";
                                    //for (int m = 0; m < vertices.Length; m++)
                                    //{
                                    //    verticeString += $"{vertices[m].ToString("#0.0000")}, ";
                                    //}
                                    //Debug.Log("Vertice in this cube: " + verticeString);
                                    //for (int t = 0; t < vertices.Length; t++)
                                    //{
                                    //    if (t == vertices.Length - 1)
                                    //        Debug.DrawLine(triDexel.ModelTransform.TransformPoint(vertices[t]), triDexel.ModelTransform.TransformPoint(vertices[0]), Color.blue, 100000f);
                                    //    else
                                    //        Debug.DrawLine(triDexel.ModelTransform.TransformPoint(vertices[t]), triDexel.ModelTransform.TransformPoint(vertices[t + 1]), Color.blue, 100000f);
                                    //}
                                    // append new vertices and triangles
                                    int[] triangles = CreateTriangleFromContour(contourNormal, vertices, verticeNormals);
                                    int numOfVertices = regenVertices.Count;
                                    totalVertice += vertices.Length;
                                    
                                    regenVertices.AddRange(vertices);
                                    //string regenVerticeString = "";
                                    //for (int m = 0; m < regenVertices.Count; m++)
                                    //{
                                    //    regenVerticeString += $"{regenVertices[m].ToString("#0.0000")}, ";
                                    //}
                                    //Debug.Log("Regenerated vertice: " + regenVerticeString);

                                    for (int t = 0; t < triangles.Length; t++)
                                        triangles[t] = triangles[t] + numOfVertices;                                   
                                    regenTriangles.AddRange(triangles);
                                    //string regenTriangleString = ""; 
                                    //for(int m = 0; m < triangles.Length; m++)
                                    //{
                                    //    regenTriangleString += $"{triangles[m]}, ";
                                    //}
                                    //Debug.Log("Regenerated triangles: " + regenTriangleString);

                                    //Debug.Log($"[{i},{j},{k}]Number of vertices: {vertices.Length}; Number of triangles: {triangles.Length / 3}; CubeOccupation: {occupationCount}");
                                }
                            }
                        }
                    }
                }
            }
            #endregion
        }
    }
}
