using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ConstructSurface.TriDexelModel2D;

namespace ConstructSurface
{
    namespace SurfaceGenerationMethod
    {
        public class ShellCubes
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
            Vector3 fieldOrigin; // the local origin coordinate of the model field

            List<Vector3> regenVertices; // regenerated vertices
            List<int> regenTriangles; // regenerated triangles
            List<Vector3> regenNormals; // regenerated normals

            Dictionary<Vector3, int> verticeDic = new Dictionary<Vector3, int>();
            Vector3[] vertice = new Vector3[12];
            Vector3[] normals = new Vector3[12];

            // 12 dexels and their corresponding offsets
            // the index offsets are relative to the cube origin (idx_x, idx_y, idx_z)
            // for each offsets arrays, the first element indicates the the map type
            // 0: z map(xy array), 1: x map(zy array), 2: y map(xz array)
            static int[][] dexelIndice =
            {
                new int[4]{0, 0, 0, 0}, new int[4]{1, 0, 0, 1}, new int[4]{0, 1, 0, 0}, new int[4]{1, 0, 0, 0},
                new int[4]{0, 0, 1, 0}, new int[4]{1, 0, 1, 1}, new int[4]{0, 1, 1, 0}, new int[4]{1, 0, 1, 0},
                new int[4]{2, 0, 0, 0}, new int[4]{2, 0, 0, 1}, new int[4]{2, 1, 0, 1}, new int[4]{2, 1, 0, 0}, 
            };

            bool[,,] voxelStates; // records the state of each cube, only for testing purpose
            #endregion

            #region Constructor
            public ShellCubes(triDexelModel2D triDexelData)
            {
                this.triDexel = triDexelData;
                GetData(triDexel);
                this.cubeInfos = new Cube[resolution_x, resolution_y, resolution_z];
                this.regenVertices = new List<Vector3>();
                this.regenTriangles = new List<int>();
                this.regenNormals = new List<Vector3>();
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
            }
            #endregion

            #region Properties
            // the resolution on the x direction
            public int Resolution_x { get => resolution_x; }
            // the resolution on the y direction
            public int Resolution_y { get => resolution_y; }
            // the resolution on the z direction
            public int Resolutiob_z { get => resolution_z; }
            // the grid edge length of a voxel
            public float GridLength { get => gridLength; }
            // new generated vertices
            public Vector3[] RegenVertices { get => regenVertices.ToArray(); }
            // new generated triangles
            public int[] RegenTriangles { get => regenTriangles.ToArray(); }
            // new generated normals
            public Vector3[] RegenNormals { get => regenNormals.ToArray(); }
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
                                //Debug.Log(fieldOrigin.z);
                                if (idx_z == resolution_z) idx_z -= 1;
                                if (idx_z < 0) idx_z = 0;
                                //Debug.Log(j + " " + i + " " + idx_z);
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
                                try
                                {
                                    if (cubeInfos[j - 1, i, idx_z] == null)
                                    {
                                        bool[] cubePointStates = CubePointStateVerify(j - 1, i, idx_z, out int numOfOccupations);
                                        if (numOfOccupations > 0 && numOfOccupations < 8)
                                        {
                                            cubeInfos[j - 1, i, idx_z] = new Cube(numOfOccupations, cubePointStates);
                                            voxelStates[j - 1, i, idx_z] = true;
                                        }
                                    }
                                }
                                catch (Exception ex) { }
                                try
                                {
                                    if (cubeInfos[j - 1, i - 1, idx_z] == null)
                                    {
                                        bool[] cubePointStates = CubePointStateVerify(j - 1, i - 1, idx_z, out int numOfOccupations);
                                        if (numOfOccupations > 0 && numOfOccupations < 8)
                                        {
                                            cubeInfos[j - 1, i - 1, idx_z] = new Cube(numOfOccupations, cubePointStates);
                                            voxelStates[j - 1, i - 1, idx_z] = true;
                                        }
                                    }
                                }
                                catch (Exception ex) { }
                                try
                                {
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
                                catch (Exception ex) { }
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
                                if (idx_x == resolution_x) idx_x -= 1;
                                if (idx_x < 0) idx_x = 0;
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
                                try
                                {
                                    if (cubeInfos[idx_x, i, j - 1] == null)
                                    {
                                        bool[] cubePointStates = CubePointStateVerify(idx_x, i, j - 1, out int numOfOccupations);
                                        if (numOfOccupations > 0 && numOfOccupations < 8)
                                        {
                                            cubeInfos[idx_x, i, j - 1] = new Cube(numOfOccupations, cubePointStates);
                                            voxelStates[idx_x, i, j - 1] = true;
                                        }
                                    }
                                }
                                catch (Exception ex) { }
                                try
                                {
                                    if (cubeInfos[idx_x, i - 1, j - 1] == null)
                                    {
                                        bool[] cubePointStates = CubePointStateVerify(idx_x, i - 1, j - 1, out int numOfOccupations);
                                        if (numOfOccupations > 0 && numOfOccupations < 8)
                                        {
                                            cubeInfos[idx_x, i - 1, j - 1] = new Cube(numOfOccupations, cubePointStates);
                                            voxelStates[idx_x, i - 1, j - 1] = true;
                                        }
                                    }
                                }
                                catch (Exception ex) { }
                                try
                                {
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
                                catch (Exception ex) { }
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
                                if (idx_y == resolution_y) idx_y -= 1;
                                if (idx_y < 0) idx_y = 0;
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
                                try
                                {
                                    if (cubeInfos[j - 1, idx_y, i] == null)
                                    {
                                        bool[] cubePointStates = CubePointStateVerify(j - 1, idx_y, i, out int numOfOccupations);
                                        if (numOfOccupations > 0 && numOfOccupations < 8)
                                        {
                                            cubeInfos[j - 1, idx_y, i] = new Cube(numOfOccupations, cubePointStates);
                                            voxelStates[j - 1, idx_y, i] = true;
                                        }
                                    }
                                }
                                catch (Exception e) { }
                                try
                                {
                                    if (cubeInfos[j - 1, idx_y, i - 1] == null)
                                    {
                                        bool[] cubePointStates = CubePointStateVerify(j - 1, idx_y, i - 1, out int numOfOccupations);
                                        if (numOfOccupations > 0 && numOfOccupations < 8)
                                        {
                                            cubeInfos[j - 1, idx_y, i - 1] = new Cube(numOfOccupations, cubePointStates);
                                            voxelStates[j - 1, idx_y, i - 1] = true;
                                        }
                                    }
                                }
                                catch (Exception e) { }
                                try
                                {
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
                                catch (Exception e) { }
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

                // a cube point is occupied when dexels respectively from xyz directions pass through the cube point
                // point 0
                if (IsInDexel(dexel_z1, zCoordinate) && IsInDexel(dexel_x1, xCoordinate) && IsInDexel(dexel_y1, yCoordinate))
                    cubePointStates[0] = true;
                // point 1
                if (IsInDexel(dexel_z1, zCoordinate_side) && IsInDexel(dexel_x2, xCoordinate) && IsInDexel(dexel_y3, yCoordinate))
                    cubePointStates[1] = true;
                // point 2
                if (IsInDexel(dexel_z2, zCoordinate_side) && IsInDexel(dexel_x2, xCoordinate_side) && IsInDexel(dexel_y4, yCoordinate))
                    cubePointStates[2] = true;
                // point 3
                if (IsInDexel(dexel_z2, zCoordinate) && IsInDexel(dexel_x1, xCoordinate_side) && IsInDexel(dexel_y2, yCoordinate))
                    cubePointStates[3] = true;
                // point 4
                if (IsInDexel(dexel_z3, zCoordinate) && IsInDexel(dexel_x3, xCoordinate) && IsInDexel(dexel_y1, yCoordinate_side))
                    cubePointStates[4] = true;
                // point 5
                if (IsInDexel(dexel_z3, zCoordinate_side) && IsInDexel(dexel_x4, xCoordinate) && IsInDexel(dexel_y3, yCoordinate_side))
                    cubePointStates[5] = true;
                // point 6
                if (IsInDexel(dexel_z4, zCoordinate_side) && IsInDexel(dexel_x4, xCoordinate_side) && IsInDexel(dexel_y4, yCoordinate_side))
                    cubePointStates[6] = true;
                // point 7
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
                if (dexelPoints != null)
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

            // verify the cube case from the cube point states
            int CubeCaseVerify(bool[] cubePointStates)
            {
                int cubeCaseIdx = 0;
                if (cubePointStates[0]) cubeCaseIdx |= 1;
                if (cubePointStates[1]) cubeCaseIdx |= 2;
                if (cubePointStates[2]) cubeCaseIdx |= 4;
                if (cubePointStates[3]) cubeCaseIdx |= 8;
                if (cubePointStates[4]) cubeCaseIdx |= 16;
                if (cubePointStates[5]) cubeCaseIdx |= 32;
                if (cubePointStates[6]) cubeCaseIdx |= 64;
                if (cubePointStates[7]) cubeCaseIdx |= 128;

                return cubeCaseIdx;
            }

            // get vertex coordinate from tri-dexel data
            Vector3 GetVertexFromDexel(int idx_x, int idx_y, int idx_z, int edgeIdx)
            {
                int[] mapTypeAndOffsets = dexelIndice[edgeIdx];
                float xEnterCoordinate = (idx_x + mapTypeAndOffsets[1]) * gridLength + fieldOrigin.x;
                float yEnterCoordinate = (idx_y + mapTypeAndOffsets[2]) * gridLength + fieldOrigin.y;
                float zEnterCoordinate = (idx_z + mapTypeAndOffsets[3]) * gridLength + fieldOrigin.z;
                Vector3 vertex = Vector3.zero;

                // fetch the dexel lying on the edge and the coordinates on two side of the edge
                Dexel dexelOnEdge = null;
                float dexelIn = 0f;
                float dexelOut = 0f;
                switch (mapTypeAndOffsets[0])
                {
                    case 0: // z map
                        dexelOnEdge = triDexel.XY_array[idx_y + mapTypeAndOffsets[2], idx_x + mapTypeAndOffsets[1]];
                        dexelIn = zEnterCoordinate;
                        dexelOut = zEnterCoordinate + gridLength;
                        break;
                    case 1: // x map
                        dexelOnEdge = triDexel.ZY_array[idx_y + mapTypeAndOffsets[2], idx_z + mapTypeAndOffsets[3]];
                        dexelIn = xEnterCoordinate;
                        dexelOut = xEnterCoordinate + gridLength;
                        break;
                    case 2: // y map
                        dexelOnEdge = triDexel.XZ_array[idx_z + mapTypeAndOffsets[3], idx_x + mapTypeAndOffsets[1]];
                        dexelIn = yEnterCoordinate;
                        dexelOut = yEnterCoordinate + gridLength;
                        break;
                }

                // find vertex on the dexel
                foreach (float dexelValue in dexelOnEdge.DexelPoints)
                {
                    if(dexelValue >= dexelIn && dexelValue <= dexelOut)
                    {
                        switch (mapTypeAndOffsets[0])
                        {
                            case 0:
                                vertex += new Vector3(xEnterCoordinate, yEnterCoordinate, dexelValue);
                                break;
                            case 1:
                                vertex += new Vector3(dexelValue, yEnterCoordinate, zEnterCoordinate);
                                break;
                            case 2:
                                vertex += new Vector3(xEnterCoordinate, dexelValue, zEnterCoordinate);
                                break;
                        }
                        break;
                    }
                }

                if (vertex == Vector3.zero)
                {
                    //Debug.Log($"Edge {edgeIdx} no vertex [{idx_x}, {idx_y}, {idx_z}] Case: {cubeCase}");
                    vertex += new Vector3(xEnterCoordinate, yEnterCoordinate, zEnterCoordinate);
                }
                return vertex;
            }

            // get vertex and normal from tri-dexel data
            Vector3 GetVertexAndNormalFromDexel(int idx_x, int idx_y, int idx_z, int edgeIdx, int cubeCase, out Vector3 normal)
            {
                int[] mapTypeAndOffsets = dexelIndice[edgeIdx];
                float xEnterCoordinate = (idx_x + mapTypeAndOffsets[1]) * gridLength + fieldOrigin.x;
                float yEnterCoordinate = (idx_y + mapTypeAndOffsets[2]) * gridLength + fieldOrigin.y;
                float zEnterCoordinate = (idx_z + mapTypeAndOffsets[3]) * gridLength + fieldOrigin.z;
                Vector3 vertex = Vector3.zero;
                normal = Vector3.zero;

                // fetch the dexel lying on the edge and the coordinates on two side of the edge
                Dexel dexelOnEdge = null;
                float dexelIn = 0f;
                float dexelOut = 0f;
                switch (mapTypeAndOffsets[0])
                {
                    case 0: // z map
                        dexelOnEdge = triDexel.XY_array[idx_y + mapTypeAndOffsets[2], idx_x + mapTypeAndOffsets[1]];
                        dexelIn = zEnterCoordinate;
                        dexelOut = zEnterCoordinate + gridLength;
                        break;
                    case 1: // x map
                        dexelOnEdge = triDexel.ZY_array[idx_y + mapTypeAndOffsets[2], idx_z + mapTypeAndOffsets[3]];
                        dexelIn = xEnterCoordinate;
                        dexelOut = xEnterCoordinate + gridLength;
                        break;
                    case 2: // y map
                        dexelOnEdge = triDexel.XZ_array[idx_z + mapTypeAndOffsets[3], idx_x + mapTypeAndOffsets[1]];
                        dexelIn = yEnterCoordinate;
                        dexelOut = yEnterCoordinate + gridLength;
                        break;
                }

                // find vertex on the dexel
                for(int i = 0; i < dexelOnEdge.DexelPoints.Count; ++i)
                {
                    if(dexelOnEdge.DexelPoints[i] >= dexelIn && dexelOnEdge.DexelPoints[i] <= dexelOut)
                    {
                        switch (mapTypeAndOffsets[0])
                        {
                            case 0:
                                vertex += new Vector3(xEnterCoordinate, yEnterCoordinate, dexelOnEdge.DexelPoints[i]);
                                normal += dexelOnEdge.Normals[i];
                                break;
                            case 1:
                                vertex += new Vector3(dexelOnEdge.DexelPoints[i], yEnterCoordinate, zEnterCoordinate);
                                normal += dexelOnEdge.Normals[i];
                                break;
                            case 2:
                                vertex += new Vector3(xEnterCoordinate, dexelOnEdge.DexelPoints[i], zEnterCoordinate);
                                normal += dexelOnEdge.Normals[i];
                                break;
                        }
                        break;
                    }
                }

                if (vertex == Vector3.zero)
                {
                    //Debug.Log($"Edge {edgeIdx} no vertex [{idx_x}, {idx_y}, {idx_z}] Case: {cubeCase}");
                    vertex += new Vector3(xEnterCoordinate, yEnterCoordinate, zEnterCoordinate);
                    normal += Vector3.one;
                }
                return vertex;
            }

            // reconstruct the model surface mesh according to the cube state array
            public void ComputeSurfaceMesh()
            {
                regenVertices.Clear();
                regenTriangles.Clear();
                verticeDic.Clear();

                // update current cube field
                UpdateCurrentCubeStates();
                //Debug.Log("State update time: " + (end - start).TotalSeconds);

                for (int i = 0; i < resolution_x; i++)
                {
                    for(int j = 0; j < resolution_y; j++)
                    {
                        for(int k = 0; k < resolution_z; k++)
                        {
                            if(cubeInfos[i, j, k] != null)
                            {
                                //for (int o = 0; o < 12; o++) vertice[o] = new Vector3(-1, -1, -1);
                                bool[] cubePointStates = cubeInfos[i, j, k].CubePointStates;
                                int caseIdx = CubeCaseVerify(cubePointStates);
                                //Debug.Log($"[{i}, {j}, {k}]: case: {caseIdx}");

                                // get vertex on each edge
                                for (int e = 0; e < 12; e++)
                                {
                                    if ((edgeTable[caseIdx] & (1 << e)) != 0)
                                        vertice[e] = GetVertexFromDexel(i, j, k, e);
                                }

                                // update vertex and triangle list
                                for (int t = 0; t < 6; t++)
                                {
                                    if (triTable[caseIdx][3 * t] < 0) break;
                                    int numOfVertice = regenVertices.Count;

                                    int addCount = 0;
                                    for (int n = 0; n < 3; n++)
                                    {
                                        int vertexIdx = triTable[caseIdx][(3 * t + 2) - n];

                                        bool isInDic = verticeDic.ContainsKey(vertice[vertexIdx]);
                                        if (!isInDic)
                                        {
                                            verticeDic.Add(vertice[vertexIdx], numOfVertice + addCount);
                                            regenVertices.Add(vertice[vertexIdx]);
                                            regenTriangles.Add(numOfVertice + addCount);
                                            //regenNormals.Add(normals[vertexIdx].normalized);
                                            addCount++;
                                        }
                                        else
                                            regenTriangles.Add(verticeDic[vertice[vertexIdx]]);
                                        //regenVertices.Add(vertice[vertexIdx]);
                                        //regenTriangles.Add(numOfVertice + addCount);
                                        //regenNormals.Add(normals[vertexIdx].normalized);
                                        //addCount++;
                                    }
                                }
                                //Debug.Log(GC.GetTotalMemory(false) / 1000000f + "MB");
                            }
                        }
                    }
                }

                //verticeHashtable.Clear();
                //Debug.Log("Mesh generate time: " + (end - start).TotalSeconds);
            }
            #endregion

            #region Look-up Tables
            public static readonly int[] edgeTable = new int[256]
            {
                0x0  , 0x109, 0x203, 0x30a, 0x406, 0x50f, 0x605, 0x70c,
                0x80c, 0x905, 0xa0f, 0xb06, 0xc0a, 0xd03, 0xe09, 0xf00,
                0x190, 0x99 , 0x393, 0x29a, 0x596, 0x49f, 0x795, 0x69c,
                0x99c, 0x895, 0xb9f, 0xa96, 0xd9a, 0xc93, 0xf99, 0xe90,
                0x230, 0x339, 0x33 , 0x13a, 0x636, 0x73f, 0x435, 0x53c,
                0xa3c, 0xb35, 0x83f, 0x936, 0xe3a, 0xf33, 0xc39, 0xd30,
                0x3a0, 0x2a9, 0x1a3, 0xaa , 0x7a6, 0x6af, 0x5a5, 0x4ac,
                0xbac, 0xaa5, 0x9af, 0x8a6, 0xfaa, 0xea3, 0xda9, 0xca0,
                0x460, 0x569, 0x663, 0x76a, 0x66 , 0x16f, 0x265, 0x36c,
                0xc6c, 0xd65, 0xe6f, 0xf66, 0x86a, 0x963, 0xa69, 0xb60,
                0x5f0, 0x4f9, 0x7f3, 0x6fa, 0x1f6, 0xff , 0x3f5, 0x2fc,
                0xdfc, 0xcf5, 0xfff, 0xef6, 0x9fa, 0x8f3, 0xbf9, 0xaf0,
                0x650, 0x759, 0x453, 0x55a, 0x256, 0x35f, 0x55 , 0x15c,
                0xe5c, 0xf55, 0xc5f, 0xd56, 0xa5a, 0xb53, 0x859, 0x950,
                0x7c0, 0x6c9, 0x5c3, 0x4ca, 0x3c6, 0x2cf, 0x1c5, 0xcc ,
                0xfcc, 0xec5, 0xdcf, 0xcc6, 0xbca, 0xac3, 0x9c9, 0x8c0,
                0x8c0, 0x9c9, 0xac3, 0xbca, 0xcc6, 0xdcf, 0xec5, 0xfcc,
                0xcc , 0x1c5, 0x2cf, 0x3c6, 0x4ca, 0x5c3, 0x6c9, 0x7c0,
                0x950, 0x859, 0xb53, 0xa5a, 0xd56, 0xc5f, 0xf55, 0xe5c,
                0x15c, 0x55 , 0x35f, 0x256, 0x55a, 0x453, 0x759, 0x650,
                0xaf0, 0xbf9, 0x8f3, 0x9fa, 0xef6, 0xfff, 0xcf5, 0xdfc,
                0x2fc, 0x3f5, 0xff , 0x1f6, 0x6fa, 0x7f3, 0x4f9, 0x5f0,
                0xb60, 0xa69, 0x963, 0x86a, 0xf66, 0xe6f, 0xd65, 0xc6c,
                0x36c, 0x265, 0x16f, 0x66 , 0x76a, 0x663, 0x569, 0x460,
                0xca0, 0xda9, 0xea3, 0xfaa, 0x8a6, 0x9af, 0xaa5, 0xbac,
                0x4ac, 0x5a5, 0x6af, 0x7a6, 0xaa , 0x1a3, 0x2a9, 0x3a0,
                0xd30, 0xc39, 0xf33, 0xe3a, 0x936, 0x83f, 0xb35, 0xa3c,
                0x53c, 0x435, 0x73f, 0x636, 0x13a, 0x33 , 0x339, 0x230,
                0xe90, 0xf99, 0xc93, 0xd9a, 0xa96, 0xb9f, 0x895, 0x99c,
                0x69c, 0x795, 0x49f, 0x596, 0x29a, 0x393, 0x99 , 0x190,
                0xf00, 0xe09, 0xd03, 0xc0a, 0xb06, 0xa0f, 0x905, 0x80c,
                0x70c, 0x605, 0x50f, 0x406, 0x30a, 0x203, 0x109, 0x0
            };
            public static readonly int[][] triTable = {
                new int[18]{ -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
                new int[18]{ 0, 8, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
                new int[18]{ 0, 1, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
                new int[18]{ 1, 8, 3, 9, 8, 1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
                new int[18]{ 1, 2, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
                new int[18]{ 0, 8, 1, 1, 8, 10, 2, 8, 3, 2, 10, 8, -1, -1, -1, -1, -1, -1 },
                new int[18]{ 9, 2, 10, 0, 2, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
                new int[18]{ 2, 8, 3, 2, 10, 8, 10, 9, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
                new int[18]{ 3, 11, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
                new int[18]{ 0, 11, 2, 8, 11, 0, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
                new int[18]{ 0, 3, 11, 0, 11, 9, 2, 9, 11, 2, 1, 9, -1, -1, -1, -1, -1, -1},
                new int[18]{ 1, 11, 2, 1, 9, 11, 9, 8, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
                new int[18]{ 3, 10, 1, 11, 10, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
                new int[18]{ 0, 10, 1, 0, 8, 10, 8, 11, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
                new int[18]{ 3, 9, 0, 3, 11, 9, 11, 10, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
                new int[18]{ 9, 8, 10, 10, 8, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
                new int[18]{ 4, 7, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
                new int[18]{ 4, 3, 0, 7, 3, 4, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
                new int[18]{ 0, 7, 8, 0, 1, 7, 4, 7, 9, 1, 9, 7, -1, -1, -1, -1, -1, -1 },
                new int[18]{ 4, 1, 9, 4, 7, 1, 7, 3, 1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
                new int[18]{ 1, 2, 8, 2, 7, 8, 2, 10, 7, 4, 7, 10, 1, 4, 10, 1, 8, 4},
                new int[18]{ 4, 7, 10, 1, 4, 10, 1, 0, 7, 2, 7, 3, 7, 2, 10, -1, -1, -1},
                new int[18]{ 7, 2, 10, 4, 7, 10, 9, 4, 10, 2, 8, 0, 2, 7, 8, -1, -1, -1},
                new int[18]{ 2, 10, 9, 2, 9, 7, 2, 7, 3, 7, 9, 4, -1, -1, -1, -1, -1, -1},
                new int[18]{ 2, 4, 7, 2, 7, 11, 4, 3, 8, 4, 2, 3, -1, -1, -1, -1, -1, -1 },
                new int[18]{ 11, 4, 7, 11, 2, 4, 2, 0, 4, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 0, 3, 8, 4, 7, 9, 1, 9, 2, 2, 7, 11, 2, 9, 7, -1, -1, -1},
                new int[18]{ 4, 7, 11, 9, 4, 11, 9, 11, 2, 9, 2, 1, -1, -1, -1, -1, -1, -1 },
                new int[18]{ 4, 7, 10, 7, 11, 10, 1, 4, 10, 1, 3, 4, 4, 3, 8, -1, -1, -1},
                new int[18]{ 1, 11, 10, 1, 4, 11, 1, 0, 4, 7, 11, 4, -1, -1, -1, -1, -1, -1},
                new int[18]{ 0, 3, 8, 4, 7, 9, 7, 11, 9, 11, 10, 9, -1, -1, -1, -1, -1, -1},
                new int[18]{ 4, 7, 11, 4, 11, 9, 9, 11, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 9, 5, 4, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 4, 8, 5, 8, 3, 0, 9, 5, 0, 5, 3, -1, -1, -1, -1, -1, -1},
                new int[18]{ 0, 5, 4, 1, 5, 0, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 8, 5, 4, 8, 3, 5, 3, 1, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 4, 10, 4, 2, 10, 2, 9, 1, 2, 4, 9, -1, -1, -1, -1, -1, -1},
                new int[18]{ 0, 9, 1, 5, 4, 10, 4, 3, 10, 3, 2, 10, 3, 4, 8, -1, -1, -1},
                new int[18]{ 5, 2, 10, 5, 4, 2, 4, 0, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 2, 10, 5, 3, 2, 5, 3, 5, 4, 3, 4, 8, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 4, 11, 4, 9, 11, 9, 3, 11, 2, 9, 5, 2, 5, 11, 9, 2, 3},
                new int[18]{ 2, 5, 11, 0, 9, 5, 0, 5, 2, 11, 5, 4, 11, 4, 8, -1, -1, -1},
                new int[18]{ 5, 4, 11, 4, 0, 3, 4, 3, 11, 5, 11, 2, 5, 2, 1, -1, -1, -1},
                new int[18]{ 2, 1, 5, 2, 5, 8, 2, 8, 11, 4, 8, 5, -1, -1, -1, -1, -1, -1},
                new int[18]{ 4, 3, 11, 5, 4, 11, 5, 11, 10, 4, 9, 1, 4, 1, 3, -1, -1, -1},
                new int[18]{ 0, 9, 1, 5, 4, 8, 5, 8, 11, 5, 11, 10, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 4, 0, 5, 0, 11, 5, 11, 10, 11, 0, 3, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 4, 8, 5, 8, 10, 10, 8, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 9, 7, 8, 5, 7, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 9, 3, 0, 9, 5, 3, 5, 7, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 0, 7, 8, 0, 1, 7, 1, 5, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 1, 5, 3, 3, 5, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 7, 8, 2, 5, 7, 2, 5, 2, 10, 8, 1, 2, 8, 9, 1, -1, -1, -1},
                new int[18]{ 1, 0, 9, 3, 5, 7, 3, 2, 5, 5, 2, 10, -1, -1, -1, -1, -1, -1},
                new int[18]{ 8, 0, 2, 8, 2, 5, 8, 5, 7, 10, 5, 2, -1, -1, -1, -1, -1, -1},
                new int[18]{ 2, 10, 5, 2, 5, 3, 3, 5, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 9, 5, 2, 5, 7, 11, 5, 11, 2, 9, 3, 8, 9, 2, 3, -1, -1, -1},
                new int[18]{ 9, 5, 7, 9, 7, 2, 9, 2, 0, 2, 7, 11, -1, -1, -1, -1, -1, -1},
                new int[18]{ 0, 3, 8, 5, 7, 11, 5, 11, 2, 5, 2, 1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 11, 2, 1, 11, 1, 7, 7, 1, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 11, 10, 5, 11, 5, 7, 9, 1, 3, 9, 3, 8, -1, -1, -1, -1, -1, -1},
                new int[18]{ 1, 0, 9, 5, 11, 10, 5, 7, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 8, 0, 3, 11, 10, 5, 11, 5, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 11, 10, 5, 7, 11, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 10, 6, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 8, 6, 0, 10, 3, 6, 8, 3, 6, 3, 10, 8, 5, 0, 5, 10, 0},
                new int[18]{ 6, 5, 9, 6, 9, 0, 6, 1, 10, 6, 0, 1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 3, 6, 8, 6, 5, 9, 6, 9, 8, 6, 1, 10, 6, 3, 1, -1, -1, -1},
                new int[18]{ 1, 6, 5, 2, 6, 1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 8, 6, 5, 1, 0, 8, 5, 0, 6, 8, 3, 6, 3, 2, -1, -1, -1},
                new int[18]{ 9, 6, 5, 9, 0, 6, 0, 2, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 9, 8, 5, 8, 2, 5, 2, 6, 3, 2, 8, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 11, 6, 5, 3, 11, 3, 10, 2, 3, 5, 10, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 0, 8, 5, 8, 11, 5, 11, 6, 0, 10, 2, 0, 5, 10, -1, -1, -1},
                new int[18]{ 1, 10, 2, 6, 9, 3, 5, 9, 6, 9, 0, 3, 6, 3, 11, -1, -1, -1},
                new int[18]{ 1, 10, 2, 8, 5, 9, 8, 6, 5, 8, 11, 6, -1, -1, -1, -1, -1, -1},
                new int[18]{ 6, 3, 11, 6, 5, 3, 5, 1, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 0, 8, 11, 0, 11, 5, 0, 5, 1, 5, 11, 6, -1, -1, -1, -1, -1, -1},
                new int[18]{ 3, 11, 6, 0, 3, 6, 0, 6, 5, 0, 5, 9, -1, -1, -1, -1, -1, -1},
                new int[18]{ 6, 5, 9, 6, 9, 11, 11, 9, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 10, 6, 8, 6, 7, 8, 4, 5, 10, 4, 10, 8, -1, -1, -1, -1, -1, -1},
                new int[18]{ 0, 10, 3, 6, 7, 3, 6, 3, 10, 10, 0, 4, 10, 4, 5, -1 ,-1, -1},
                new int[18]{ 4, 5, 9, 1, 6, 8, 1, 10, 6, 8, 6, 7, 0, 1, 8, -1, -1, -1},
                new int[18]{ 5, 9, 4, 1, 10, 3, 3, 10, 6, 3, 6, 7, -1, -1, -1, -1, -1, -1},
                new int[18]{ 1, 2, 8, 7, 8, 2, 7, 2, 6, 4, 1, 8, 4, 5, 1, -1, -1, -1},
                new int[18]{ 4, 5, 1, 4, 1, 0, 2, 6, 7, 2, 7, 3, -1, -1, -1, -1, -1, -1},
                new int[18]{ 4, 5, 9, 2, 8, 0, 2, 7, 8, 2, 6, 7, -1, -1, -1, -1, -1, -1},
                new int[18]{ 4, 5, 9, 6, 7, 2, 7, 3, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 6, 7, 11, 4, 5, 10, 4, 10, 3, 4, 3, 8, 10, 2, 3, -1, -1, -1},
                new int[18]{ 6, 7, 11, 0, 4, 5, 0, 5, 10, 0, 10, 2, -1, -1, -1, -1, -1, -1},
                new int[18]{ 4, 5, 9, 6, 7, 11, 2, 1, 10, 8, 0, 3, -1, -1, -1, -1, -1, -1},
                new int[18]{ 4, 5, 9, 6, 7, 11, 2, 1, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 6, 7, 11, 1, 4, 5, 1, 8, 4, 1, 3, 8, -1, -1, -1, -1, -1, -1},
                new int[18]{ 6, 7, 11, 1, 4, 5, 4, 1, 0, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 6, 7, 11, 0, 3, 8, 4, 5, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 4, 5, 9, 6, 7, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 10, 4, 9, 6, 4, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 6, 3, 10, 6, 4, 8, 6, 8, 3, 10, 0, 9, 10, 3, 0, -1, -1, -1},
                new int[18]{ 10, 0, 1, 10, 6, 0, 6, 4, 0, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 8, 3, 1, 8, 1, 6, 8, 6, 4, 6, 1, 10, -1, -1, -1, -1, -1, -1},
                new int[18]{ 1, 4, 9, 1, 2, 4, 2, 6, 4, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 1, 0, 9, 6, 4, 8, 6, 8, 3, 6, 3, 2, -1, -1, -1, -1, -1, -1},
                new int[18]{ 0, 2, 4, 4, 2, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 8, 3, 2, 8, 2, 4, 4, 2, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 4, 9, 3, 9, 10, 3, 10, 2, 3, 6, 4, 3, 6, 3, 11, -1, -1, -1},
                new int[18]{ 6, 4, 8, 6, 8, 11, 9, 10, 2, 9, 2, 0, -1, -1, -1, -1, -1, -1},
                new int[18]{ 10, 2, 1, 6, 4, 0, 6, 0, 1, 11, 0, 3, -1, -1, -1, -1, -1, -1},
                new int[18]{ 10, 2, 1, 6, 4, 8, 6, 8, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 9, 6, 4, 9, 3, 6, 9, 1, 3, 11, 6, 3, -1, -1, -1, -1, -1, -1},
                new int[18]{ 0, 9, 1, 6, 4, 8, 6, 8, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 3, 11, 6, 3, 6, 0, 0, 6, 4, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 6, 4, 8, 11, 6, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 7, 10, 6, 7, 8, 10, 8, 9, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 0, 7, 3, 0, 10, 7, 0, 9, 10, 6, 7, 10, -1, -1, -1, -1, -1, -1},
                new int[18]{ 10, 6, 7, 1, 10, 7, 1, 7, 8, 1, 8, 0, -1, -1, -1, -1, -1, -1},
                new int[18]{ 10, 6, 7, 10, 7, 1, 1, 7, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 1, 2, 6, 1, 6, 8, 1, 8, 9, 8, 6, 7, -1, -1, -1, -1, -1, -1},
                new int[18]{ 9, 1, 0, 6, 7, 2, 7, 3, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 7, 8, 0, 7, 0, 6, 6, 0, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 7, 3, 2, 6, 7, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 6, 7, 11, 9, 10, 2, 9, 2, 3, 9, 3, 8, -1, -1, -1, -1, -1, -1},
                new int[18]{ 6, 7, 11, 9, 10, 2, 9, 2, 0, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 6, 7, 11, 10, 2, 1, 8, 0, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 6, 7, 11, 10, 2, 1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 6, 7, 11, 8, 9, 1, 8, 1, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 6, 7, 11, 9, 1, 0, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 8, 0, 3, 6, 7, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 7, 11, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 7, 6, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 7, 0, 8, 6, 0, 7, 0, 11, 3, 0, 6, 11, -1, -1, -1, -1, -1, -1},
                new int[18]{ 6, 9, 7, 1, 11, 0, 9, 0, 11, 9, 11, 7, 9, 11, 1, 9, 6, 11},
                new int[18]{ 9, 6, 1, 6, 9, 8, 6, 8, 7, 1, 6, 11, 1, 11, 3, -1, -1, -1},
                new int[18]{ 7, 1, 2, 7, 2, 11, 10, 1, 7, 10, 7, 6, -1, -1, -1, -1, -1, -1},
                new int[18]{ 11, 3, 2, 6, 10, 1, 6, 1, 8, 6, 8, 7, 8, 1, 0, -1, -1, -1},
                new int[18]{ 7, 9, 0, 7, 0, 2, 7, 2, 11, 10, 9, 7, 10, 7, 6, -1, -1, -1},
                new int[18]{ 11, 3, 2, 8, 10, 9, 8, 6, 10, 8, 7, 6, -1, -1, -1, -1, -1, -1},
                new int[18]{ 7, 2, 3, 6, 2, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 7, 0, 8, 7, 6, 0, 6, 2, 0, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 6, 9, 7, 7, 9, 0, 7, 0, 3, 9, 2, 1, 9, 6, 2, -1, -1, -1},
                new int[18]{ 1, 6, 2, 1, 8, 6, 1, 9, 8, 8, 7, 6, -1, -1, -1, -1, -1, -1},
                new int[18]{ 10, 7, 6, 10, 1, 7, 1, 3, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 10, 7, 6, 1, 7, 10, 1, 8, 7, 1, 0, 8, -1, -1, -1, -1, -1, -1},
                new int[18]{ 0, 3, 7, 0, 7, 10, 0, 10, 9, 6, 10, 7, -1, -1, -1, -1, -1, -1},
                new int[18]{ 7, 6, 10, 7, 10, 8, 8, 10, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 6, 8, 4, 11, 8, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 3, 6, 11, 3, 0, 6, 0, 4, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 6, 11, 1, 6, 9, 4, 6, 1, 9, 1, 8, 0, 1, 11, 8, -1, -1, -1},
                new int[18]{ 9, 4, 6, 9, 6, 3, 9, 3, 1, 11, 3, 6, -1, -1, -1, -1, -1, -1},
                new int[18]{ 4, 1, 8, 8, 1, 2, 8, 2, 11, 1, 6, 10, 1, 4, 6, -1, -1, -1},
                new int[18]{ 3, 2, 11, 4, 1, 0, 4, 10, 1, 4, 6, 10, -1, -1, -1, -1, -1, -1},
                new int[18]{ 4, 10, 9, 4, 6, 10, 8, 0, 2, 8, 2, 11, -1, -1, -1, -1, -1, -1},
                new int[18]{ 3, 2, 11, 4, 10, 9, 4, 6, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 8, 2, 3, 8, 4, 2, 4, 6, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 0, 4, 2, 4, 6, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 0, 3, 8, 4, 6, 9, 9, 6, 1, 1, 6, 2, -1, -1, -1, -1, -1, -1},
                new int[18]{ 1, 9, 4, 1, 4, 2, 2, 4, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 8, 1, 3, 8, 6, 1, 8, 4, 6, 6, 10, 1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 10, 1, 0, 10, 0, 6, 6, 0, 4, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 8, 0, 3, 4, 10, 9, 6, 10, 4, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 10, 9, 4, 6, 10, 4, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 7, 4, 9, 7, 9, 11, 5, 6, 11, 5, 11, 9, -1, -1, -1, -1, -1, -1},
                new int[18]{ 4, 8, 7, 5, 11, 0, 5, 0, 9, 5, 6, 11, 11, 3, 0, -1, -1, -1},
                new int[18]{ 0, 1, 11, 4, 0, 11, 4, 11, 7, 5, 6, 11, 5, 11, 1, -1, -1, -1},
                new int[18]{ 4, 8, 7, 1, 5, 6, 1, 6, 11, 1, 11, 3, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 6, 10, 4, 1, 11, 4, 9, 1, 7, 4, 11 ,11, 1, 2, -1, -1, -1},
                new int[18]{ 5, 6, 10, 7, 4, 8, 9, 1, 0, 11, 3, 2, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 6, 10, 0, 7, 4, 0, 11, 7, 0, 2, 11, -1, -1, -1, -1, -1, -1},
                new int[18]{ 11, 3, 2, 7, 4, 8, 5, 6, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 9, 2, 3, 4, 9, 3, 4, 3, 7, 5, 2, 9, 5, 6, 2, -1, -1, -1},
                new int[18]{ 7, 4, 8, 2, 0, 9, 2, 9, 5, 2, 5, 6, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 2, 1, 5, 6, 2, 7, 4, 0, 7, 0, 3, -1, -1, -1, -1, -1, -1},
                new int[18]{ 4, 8, 7, 5, 2, 1, 5, 6, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 6, 10, 3, 9, 1, 3, 4, 9, 3, 7, 4, -1, -1, -1, -1, -1, -1},
                new int[18]{ 4, 8, 7, 9, 1, 0, 5, 6, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 6, 10, 4, 0, 3, 4, 3, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 6, 10, 7, 4, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 6, 9, 5, 6, 11, 9, 11, 8, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 3, 6, 11, 0, 6, 3, 0, 5, 6, 0, 9, 5, -1, -1, -1, -1, -1, -1},
                new int[18]{ 0, 11, 8, 0, 5, 11, 0, 1, 5, 5, 6, 11, -1, -1, -1, -1, -1, -1},
                new int[18]{ 6, 11, 3, 6, 3, 5, 5, 3, 1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 6, 10, 9, 11, 8, 9, 2, 11, 9, 1, 2, -1, -1, -1, -1, -1, -1},
                new int[18]{ 9, 1, 0, 5, 6, 10, 3, 2, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 2, 8, 0, 2, 11, 8, 5, 6, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
                new int[18]{ 11, 3, 2, 5, 6, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 8, 9, 5, 2, 8, 5, 6, 2, 3, 8, 2, -1, -1, -1, -1, -1, -1},
                new int[18]{ 9, 5, 6, 9, 6, 0, 0, 6, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 0, 3, 8, 5, 2, 1, 5, 6, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 1, 5, 6, 2, 1, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 6, 10, 8, 9, 1, 8, 1, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 6, 10, 9, 1, 0, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 8, 0, 3, 5, 6, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 10, 5, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 11, 5, 10, 7, 5, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 10, 0, 0, 8, 5, 5, 8, 7, 10, 3, 0, 10, 11, 3, -1, -1, -1},
                new int[18]{ 7, 0, 11, 5, 9, 0, 5, 0, 7, 10, 11, 0, 10, 0, 1, -1, -1, -1},
                new int[18]{ 5, 9, 7, 7, 9, 8, 10, 11, 1, 11, 3, 1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 11, 1, 2, 11, 7, 1, 7, 5, 1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 1, 0, 5, 5, 0, 8, 5, 8, 7, 11, 3, 2, -1, -1, -1, -1, -1, -1},
                new int[18]{ 9, 7, 5, 9, 2, 7, 9, 0, 2, 2, 11, 7, -1, -1, -1, -1, -1, -1},
                new int[18]{ 3, 2, 11, 5, 9, 8, 5, 8, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 2, 5, 10, 2, 3, 5, 3, 7, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 8, 2, 0, 8, 5, 2, 8, 7, 5, 10, 2, 5, -1, -1, -1, -1, -1, -1},
                new int[18]{ 10, 2, 1, 7, 5, 9, 7, 9, 0, 7, 0, 3, -1, -1, -1, -1, -1, -1},
                new int[18]{ 10, 2, 1, 5, 9, 8, 5, 8, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 1, 3, 5, 3, 7, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 0, 8, 7, 0, 7, 1, 1, 7, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 9, 0, 3, 9, 3, 5, 5, 3, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 9, 8, 7, 5, 9, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 8, 4, 5, 10, 8, 10, 11, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 0, 4, 5, 11, 0, 5, 10, 11, 11, 3, 0, -1, -1, -1, -1, -1, -1},
                new int[18]{ 4, 5, 9, 1, 10, 11, 0, 1, 11, 8, 0, 11, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 9, 4, 10, 3, 1, 10, 11, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 2, 5, 1, 2, 8, 5, 2, 11, 8, 4, 5, 8, -1, -1, -1, -1, -1, -1},
                new int[18]{ 3, 2, 11, 4, 5, 1, 4, 1, 0, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 9, 4, 8, 0, 2, 8, 2, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 9, 4, 3, 2, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 2, 5, 10, 3, 5, 2, 3, 4, 5, 3, 8, 4, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 10, 2, 5, 2, 4, 4, 2, 0, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 9, 4, 2, 1, 10, 0, 3, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 9, 4, 10, 2, 1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 8, 4, 5, 8, 5, 3, 3, 5, 1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 0, 4, 5, 1, 0, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 5, 9, 4, 8, 0, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 9, 4, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 4, 11, 7, 4, 9, 11, 9, 10, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 4, 8, 7, 10, 0, 9, 10, 3, 0, 10, 11, 3, -1, -1, -1, -1, -1, -1},
                new int[18]{ 1, 10, 11, 1, 11, 4, 1, 4, 0, 7, 4, 11, -1, -1, -1, -1, -1, -1},
                new int[18]{ 7, 4, 8, 10, 11, 3, 10, 3, 1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 4, 11, 7, 9, 11, 4, 9, 2, 11, 9, 1, 2, -1, -1, -1, -1, -1, -1},
                new int[18]{ 9, 1, 0, 7, 4, 8, 11, 3, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 11, 7, 4, 11, 4, 2, 2, 4, 0, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 7, 4, 8, 11, 3, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 2, 9, 10, 2, 7, 9, 2, 3, 7, 7, 4, 9, -1, -1, -1, -1, -1, -1},
                new int[18]{ 7, 4, 8, 10, 2, 0, 10 , 0, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 10, 2, 1, 4, 0, 3, 4, 3, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 7, 4, 8, 10, 2, 1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 4, 9, 1, 4, 1, 7, 7, 1, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 7, 4, 8, 9, 1, 0, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 4, 0, 3, 7, 4, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 4, 8, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 9, 10, 8, 10, 11, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 3, 0, 9, 3, 9, 11, 11, 9, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 0, 1, 10, 0, 10, 8, 8, 10, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 3, 1, 10, 11, 3, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 1, 2, 11, 1, 11, 9, 9, 11, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 9, 1, 0, 11, 3, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 0, 2, 11, 8, 0, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 3, 2, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 2, 3, 8, 2, 8, 10, 10, 8, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 9, 10, 2, 0, 9, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 8, 0, 3, 10, 2, 1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 1, 10, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 1, 3, 8, 9, 1, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 0, 9, 1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ 0, 3, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
                new int[18]{ -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
            };
            #endregion
        }  
    }
}
