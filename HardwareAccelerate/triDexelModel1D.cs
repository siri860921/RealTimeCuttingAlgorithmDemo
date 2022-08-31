using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ConstructSurface
{
    namespace TriDexelModel1D
    {
        public class triDexelModel1D
        {
            #region DataFields
            Vector3 minBoundPoint_local; // the minimum bounding point of the model object on its local coordinate
            Vector3 maxBoundPoint_local; // the maximum bounding point of the model object on its local coordinate
            float x_min; // the smallest local x coordinate value of the input mesh model
            float x_max; // the largest local x coordinate value of the input mesh model
            float y_min; // the smallest local y coordinate value of the input mesh model
            float y_max; // the larget local y coordinate value of the input mesh model
            float z_min; // the smallest local z coordinate value of the input mesh model
            float z_max; // the largest local z coordinate value of the input mesh model
            Vector3 xy_arrayOrigin;
            Vector3 zy_arrayOrigin;
            Vector3 xz_arrayOrigin;
            float gridSize; // the edge length of a grid size of the array
            float eta;
            int resolution_ref; // the resolution on the longest dimension of the input model
            int resolution_x; // resolution on the x direction
            int resolution_y; // resolutoion on the y direction
            int resolution_z; // resolution on the z direction
            int numOfDexelSegments = 10; // the number of segments in a dexel 
            int ignoreLayer; // the ID of the layer to let the raycast identify

            // tri-dexel data structure, stores the low/high depth of each dexel on one grid
            Color[] xy_array;
            Color[] zy_array;
            Color[] xz_array;
            Color[] xyNormal;
            Color[] zyNormal;
            Color[] xzNormal;

            GameObject inputModel; // the whole model of the input model
            Mesh modelMesh; // mesh data of the input model
            Transform modelTransform; // the transform of the input model
            #endregion

            #region Constructor
            public triDexelModel1D(GameObject inputModel, int resolution, int layerID)
            {
                this.inputModel = inputModel;
                modelTransform = inputModel.transform;
                this.ignoreLayer = 1 << layerID;
                if (resolution >= 10 && resolution <= 500)
                    resolution_ref = resolution;
                else resolution_ref = 10;

                // initialize and allocate tri-dexel data structure
                Initialize();

                // ray casting method to create dexel in each dexel map
                RayCasting();
            }

            public triDexelModel1D(GameObject inputModel, int resolution, int segmentCount, int layerID)
            {
                this.inputModel = inputModel;
                this.numOfDexelSegments = segmentCount;
                this.ignoreLayer = 1 << layerID;
                modelTransform = inputModel.transform;
                if (resolution >= 10 && resolution <= 500)
                    resolution_ref = resolution;
                else resolution_ref = 10;

                // initialize and allocate tri-dexel data structure
                Initialize();

                // ray casting method to create dexel in each dexel map
                RayCasting();
            }
            #endregion

            #region Class Properties
            // resolution on x direction
            public int Resolution_x { get => resolution_x; }
            // resolution on y direction
            public int Resolution_y { get => resolution_y; }
            // resolution on z direction
            public int Resolution_z { get => resolution_z; }
            public int NumOfDexelSegments { get => numOfDexelSegments; }
            // the edge length of a grid size of the array
            public float GridSize { get => gridSize; }
            // the transform of the input model
            public Transform ModelTransform { get => modelTransform; }
            // minimum bounding box point(local coordinate) of the input object 
            public Vector3 FieldOrigin { get => xy_arrayOrigin; }
            // maximum bounding box point(local coordinate) of the input object
            //public Vector3 MaxBoundPoint_local { get => maxBoundPoint_local; }
            // xy array
            public Color[] XY_array { get => xy_array; }
            // zy array
            public Color[] ZY_array { get => zy_array; }
            // xz array
            public Color[] XZ_array { get => xz_array; }
            public Color[] XYNormal { get => xyNormal; }
            public Color[] ZYNormal { get => zyNormal; }
            public Color[] XZNormal { get => xzNormal; }
            #endregion

            #region Methods
            // initialization, get the input mesh model information and construct 2D data structure
            void Initialize()
            {
                getMeshDataFromImput();
                construct1DArray(numOfDexelSegments);
            }

            // acquire basic information of the input model
            void getMeshDataFromImput()
            {
                modelMesh = inputModel.GetComponent<MeshFilter>().mesh;

                // get the local min/max xyz coordinates from the bounding box
                Bounds modelBounds = modelMesh.bounds; // get the boundary of the mesh model
                minBoundPoint_local = modelBounds.min;
                maxBoundPoint_local = modelBounds.max;
            }

            // construct 1D array data structure
            void construct1DArray(int segmentCount)
            {
                // determine the grid size and the resolution on each direction
                float x_delta = maxBoundPoint_local.x - minBoundPoint_local.x;
                float y_delta = maxBoundPoint_local.y - minBoundPoint_local.y;
                float z_delta = maxBoundPoint_local.z - minBoundPoint_local.z;
                if (x_delta >= y_delta && x_delta >= z_delta)
                {
                    gridSize = x_delta / resolution_ref;
                    eta = gridSize / 100f;
                    resolution_x = resolution_ref + 10;
                    resolution_y = Mathf.CeilToInt(y_delta / gridSize) + 10;
                    resolution_z = Mathf.CeilToInt(z_delta / gridSize) + 10;
                }
                if (y_delta >= x_delta && y_delta >= z_delta)
                {
                    gridSize = y_delta / resolution_ref;
                    eta = gridSize / 100f;
                    resolution_x = Mathf.CeilToInt(x_delta / gridSize) + 10;
                    resolution_y = resolution_ref + 10;
                    resolution_z = Mathf.CeilToInt(z_delta / gridSize) + 10;
                }
                if (z_delta >= x_delta && z_delta >= y_delta)
                {
                    gridSize = z_delta / resolution_ref;
                    eta = gridSize / 100f;
                    resolution_x = Mathf.CeilToInt(x_delta / gridSize) + 10;
                    resolution_y = Mathf.CeilToInt(y_delta / gridSize) + 10;
                    resolution_z = resolution_ref + 10;
                }
                // generate semi 3D data structure
                // by default, each dexel is given 20/2 = 10 segments of memory
                xy_array = new Color[(resolution_x + 1) * (resolution_y + 1) * numOfDexelSegments * 2];
                zy_array = new Color[(resolution_z + 1) * (resolution_y + 1) * numOfDexelSegments * 2];
                xz_array = new Color[(resolution_x + 1) * (resolution_z + 1) * numOfDexelSegments * 2];
                xyNormal = new Color[(resolution_x + 1) * (resolution_y + 1) * numOfDexelSegments * 2];
                zyNormal = new Color[(resolution_z + 1) * (resolution_y + 1) * numOfDexelSegments * 2];
                xzNormal = new Color[(resolution_x + 1) * (resolution_z + 1) * numOfDexelSegments * 2];
            }

            // ray casting method 
            void RayCasting()
            {
                // allocate world min/max xyz coordinates
                x_min = minBoundPoint_local.x;
                y_min = minBoundPoint_local.y;
                z_min = minBoundPoint_local.z;
                x_max = maxBoundPoint_local.x;
                y_max = maxBoundPoint_local.y;
                z_max = maxBoundPoint_local.z;

                // create ray instances
                RaycastHit castingRays;
                // get ray cast hit information
                // for a 1D array, such as xy array, there are resolution_x * resolution_y ray casting informations
                // the ray cast origins should add offsets to ensure collision with the object
                Vector3 zDirection_offset = new Vector3(0f, 0f, (z_max - z_min) / 50000f);
                Vector3 xDirection_offset = new Vector3((x_max - x_min) / 50000f, 0f, 0f);
                Vector3 yDirection_offset = new Vector3(0f, (y_max - y_min) / 50000f, 0f);
                xy_arrayOrigin = new Vector3(x_min - xDirection_offset.x, y_min - yDirection_offset.y, z_min - zDirection_offset.z); // xy array origin in world coordinate
                zy_arrayOrigin = new Vector3(x_min - xDirection_offset.x, y_min - yDirection_offset.y, z_min - zDirection_offset.z); // zy array origin in world coordinate
                xz_arrayOrigin = new Vector3(x_min - xDirection_offset.x, y_min - yDirection_offset.y, z_min - zDirection_offset.z); // xz array origin in world coordinate
                //xy_arrayOrigin = new Vector3(x_min, y_min, z_min); // xy array origin in world coordinate
                //zy_arrayOrigin = new Vector3(x_min, y_min, z_min); // zy array origin in world coordinate
                //xz_arrayOrigin = new Vector3(x_min, y_min, z_min); // xz array origin in world coordinate
                bool isHit; // the hit to get low dexel point
                bool isAnotherHit; // the hit to get high dexel point
                Vector3 lowPoint; // low dexel point buffer
                Vector3 highPoint; // high dexel point buffer
                Vector3 hitNormal1; // low normal point buffer
                Vector3 hitNormal2; // high normal point buffer

                // raycasting on z direction (store data to xy array)
                for (int i = 0; i < (resolution_x + 1) * (resolution_y + 1); i++)
                {
                    bool keepCasting = true;
                    Vector3 rayOrigin = xy_arrayOrigin;
                    int idxCounter = 0;
                    while (keepCasting)
                    {
                        isHit = Physics.Raycast(modelTransform.TransformPoint(rayOrigin + (i / (resolution_x + 1)) * gridSize * Vector3.up + (i % (resolution_x + 1)) * gridSize * Vector3.right - zDirection_offset),
                            modelTransform.TransformDirection(Vector3.forward), out castingRays, modelTransform.TransformVector(Vector3.forward * (z_max - z_min)).magnitude, ignoreLayer);
                        // if the ray colides with the input medol with a point(low dexel point), store into 1D array
                        // and set that point as the new ray origin to find the point on the other side(high dexel point)
                        // else set the corresponding 1D array element to null
                        if (isHit)
                        {
                            lowPoint = modelTransform.InverseTransformPoint(castingRays.point);
                            if ((lowPoint.z - xy_arrayOrigin.z) % gridSize == 0) lowPoint.z += eta;
                            hitNormal1 = modelTransform.InverseTransformVector(castingRays.normal);
                            Physics.queriesHitBackfaces = true; // enable ray to hit the backface of a mesh
                            isAnotherHit = Physics.Raycast(modelTransform.TransformPoint(lowPoint + zDirection_offset), modelTransform.TransformDirection(Vector3.forward),
                                out castingRays, modelTransform.TransformVector(Vector3.forward * (z_max - z_min)).magnitude, ignoreLayer);
                            // check if the ray really collides on the other side, if not then use the spotted point as the new origin to find the next collided point
                            if (isAnotherHit)
                            {
                                while (castingRays.triangleIndex == -1)
                                    isAnotherHit = Physics.Raycast(castingRays.point + modelTransform.TransformVector(zDirection_offset), modelTransform.TransformDirection(Vector3.forward),
                                        out castingRays, modelTransform.TransformVector(Vector3.forward * (z_max - z_min)).magnitude, ignoreLayer);
                                Physics.queriesHitBackfaces = false; // disable ray to hit the backface of a mesh
                                highPoint = modelTransform.InverseTransformPoint(castingRays.point);
                                if ((highPoint.z - xy_arrayOrigin.z) % gridSize == 0) highPoint.z += eta;
                                hitNormal2 = modelTransform.InverseTransformVector(castingRays.normal);
                                rayOrigin += Vector3.forward * (highPoint.z - rayOrigin.z);
                                if (highPoint.z - lowPoint.z > gridSize / 100f)
                                {
                                    xy_array[(i % (resolution_x + 1)) + (i / (resolution_x + 1)) * (resolution_x + 1) + (idxCounter * (resolution_x + 1) * (resolution_y + 1))].r = lowPoint.z;
                                    xy_array[(i % (resolution_x + 1)) + (i / (resolution_x + 1)) * (resolution_x + 1) + ((idxCounter + 1) * (resolution_x + 1) * (resolution_y + 1))].r = highPoint.z;
                                    xyNormal[(i % (resolution_x + 1)) + (i / (resolution_x + 1)) * (resolution_x + 1) + (idxCounter * (resolution_x + 1) * (resolution_y + 1))] = new Color(hitNormal1.x, hitNormal1.y, hitNormal1.z);
                                    xyNormal[(i % (resolution_x + 1)) + (i / (resolution_x + 1)) * (resolution_x + 1) + ((idxCounter + 1) * (resolution_x + 1) * (resolution_y + 1))] = new Color(hitNormal2.x, hitNormal2.y, hitNormal2.z);
                                    idxCounter += 2;
                                }
                                //Debug.DrawLine(modelTransform.TransformPoint(lowPoint), modelTransform.TransformPoint(lowPoint + hitNormal1 * 0.1f), Color.green, 100000f);
                                //Debug.DrawLine(modelTransform.TransformPoint(highPoint), modelTransform.TransformPoint(highPoint + hitNormal2 * 0.1f), Color.green, 100000f);
                            }
                            else
                            {
                                Physics.queriesHitBackfaces = false;
                                keepCasting = false;
                                idxCounter = 0;
                            }
                        }
                        else
                        {
                            keepCasting = false;
                            idxCounter = 0;
                        }
                    }
                }

                // raycasting on x direction (store data to zy array)
                for (int i = 0; i < (resolution_z + 1) * (resolution_y + 1); i++)
                {
                    bool keepCasting = true;
                    Vector3 rayOrigin = zy_arrayOrigin;
                    int idxCounter = 0;
                    while (keepCasting)
                    {
                        isHit = Physics.Raycast(modelTransform.TransformPoint(rayOrigin + (i / (resolution_z + 1)) * gridSize * Vector3.up + (i % (resolution_z + 1)) * gridSize * Vector3.forward - xDirection_offset),
                            modelTransform.TransformDirection(Vector3.right), out castingRays, modelTransform.TransformVector(Vector3.right * (x_max - x_min)).magnitude, ignoreLayer);
                        // if the ray colides with the input medol with a point(low dexel point), store into 1D array
                        // and set that point as the new ray origin to find the point on the other side(high dexel point)
                        // else set the corresponding 1D array element to null
                        if (isHit)
                        {
                            lowPoint = modelTransform.InverseTransformPoint(castingRays.point);
                            if ((lowPoint.x - zy_arrayOrigin.x) % gridSize == 0) lowPoint.x += eta;
                            hitNormal1 = modelTransform.InverseTransformVector(castingRays.normal);
                            Physics.queriesHitBackfaces = true; // enable ray to hit the backface of a mesh
                            isAnotherHit = Physics.Raycast(modelTransform.TransformPoint(lowPoint + xDirection_offset), modelTransform.TransformDirection(Vector3.right),
                                out castingRays, modelTransform.TransformVector(Vector3.right * (x_max - x_min)).magnitude, ignoreLayer);
                            // check if the ray really collides on the other side, if not then use the spotted point as the new origin to find the next collided point
                            if (isAnotherHit)
                            {
                                while (castingRays.triangleIndex == -1)
                                    isAnotherHit = Physics.Raycast(castingRays.point + modelTransform.TransformVector(xDirection_offset), modelTransform.TransformDirection(Vector3.right),
                                        out castingRays, modelTransform.TransformVector(Vector3.right * (x_max - x_min)).magnitude, ignoreLayer);
                                Physics.queriesHitBackfaces = false; // disable ray to hit the backface of a mesh
                                highPoint = modelTransform.InverseTransformPoint(castingRays.point);
                                if ((highPoint.x - zy_arrayOrigin.x) % gridSize == 0) highPoint.x += eta;
                                hitNormal2 = modelTransform.InverseTransformPoint(castingRays.normal);
                                rayOrigin += Vector3.right * (highPoint.x - rayOrigin.x);
                                if (highPoint.x - lowPoint.x > gridSize / 100f)
                                {
                                    zy_array[(i % (resolution_z + 1)) + (i / (resolution_z + 1)) * (resolution_z + 1) + (idxCounter * (resolution_z + 1) * (resolution_y + 1))].r = lowPoint.x;
                                    zy_array[(i % (resolution_z + 1)) + (i / (resolution_z + 1)) * (resolution_z + 1) + ((idxCounter + 1) * (resolution_z + 1) * (resolution_y + 1))].r = highPoint.x;
                                    zyNormal[(i % (resolution_z + 1)) + (i / (resolution_z + 1)) * (resolution_z + 1) + (idxCounter * (resolution_z + 1) * (resolution_y + 1))] = new Color(hitNormal1.x, hitNormal1.y, hitNormal1.z);
                                    zyNormal[(i % (resolution_z + 1)) + (i / (resolution_z + 1)) * (resolution_z + 1) + ((idxCounter + 1) * (resolution_z + 1) * (resolution_y + 1))] = new Color(hitNormal2.x, hitNormal2.y, hitNormal2.z);
                                    idxCounter += 2;
                                }
                                //Debug.DrawLine(modelTransform.TransformPoint(lowPoint), modelTransform.TransformPoint(lowPoint + hitNormal1 * 0.1f), Color.green, 100000f);
                                //Debug.DrawLine(modelTransform.TransformPoint(highPoint), modelTransform.TransformPoint(highPoint + hitNormal2 * 0.1f), Color.green, 100000f);
                            }
                            else
                            {
                                Physics.queriesHitBackfaces = false;
                                keepCasting = false;
                                idxCounter = 0;
                            }
                        }
                        else
                        {
                            keepCasting = false;
                            idxCounter = 0;
                        }
                    }
                }

                // raycasting on y direction (store data to xz array)
                for (int i = 0; i < (resolution_x + 1) * (resolution_z + 1); i++)
                {
                    bool keepCasting = true;
                    Vector3 rayOrigin = xz_arrayOrigin;
                    int idxCounter = 0;
                    while (keepCasting)
                    {
                        isHit = Physics.Raycast(modelTransform.TransformPoint(rayOrigin + (i / (resolution_x + 1)) * gridSize * Vector3.forward + (i % (resolution_x + 1)) * gridSize * Vector3.right - yDirection_offset),
                            modelTransform.TransformDirection(Vector3.up), out castingRays, modelTransform.TransformVector(Vector3.up * (y_max - y_min)).magnitude, ignoreLayer);
                        // if the ray colides with the input medol with a point(low dexel point), store into 1D array
                        // and set that point as the new ray origin to find the point on the other side(high dexel point)
                        // else set the corresponding 1D array element to null
                        if (isHit)
                        {
                            lowPoint = modelTransform.InverseTransformPoint(castingRays.point);
                            if ((lowPoint.y - xz_arrayOrigin.y) % gridSize == 0) lowPoint.y += eta;
                            hitNormal1 = modelTransform.InverseTransformVector(castingRays.normal);
                            Physics.queriesHitBackfaces = true; // enable ray to hit the backface of a mesh
                            isAnotherHit = Physics.Raycast(modelTransform.TransformPoint(lowPoint + yDirection_offset), modelTransform.TransformDirection(Vector3.up),
                                out castingRays, modelTransform.TransformVector(Vector3.up * (y_max - y_min)).magnitude, ignoreLayer);
                            // check if the ray really collides on the other side, if not then use the spotted point as the new origin to find the next collided point
                            if (isAnotherHit)
                            {
                                while (castingRays.triangleIndex == -1)
                                    isAnotherHit = Physics.Raycast(castingRays.point + modelTransform.TransformVector(yDirection_offset), modelTransform.TransformDirection(Vector3.up),
                                        out castingRays, modelTransform.TransformVector(Vector3.up * (y_max - y_min)).magnitude, ignoreLayer);
                                Physics.queriesHitBackfaces = false; // disable ray to hit the backface of a mesh
                                highPoint = modelTransform.InverseTransformPoint(castingRays.point);
                                if ((highPoint.y - xz_arrayOrigin.y) % gridSize == 0) highPoint.y += eta;
                                hitNormal2 = modelTransform.InverseTransformVector(castingRays.normal);
                                rayOrigin += Vector3.up * (highPoint.y - rayOrigin.y);
                                if (highPoint.y - lowPoint.y > gridSize / 100f)
                                {
                                    xz_array[(i % (resolution_x + 1)) + (i / (resolution_x + 1)) * (resolution_x + 1) + (idxCounter * (resolution_x + 1) * (resolution_z + 1))].r = lowPoint.y;
                                    xz_array[(i % (resolution_x + 1)) + (i / (resolution_x + 1)) * (resolution_x + 1) + ((idxCounter + 1) * (resolution_x + 1) * (resolution_z + 1))].r = highPoint.y;
                                    xzNormal[(i % (resolution_x + 1)) + (i / (resolution_x + 1)) * (resolution_x + 1) + (idxCounter * (resolution_x + 1) * (resolution_z + 1))] = new Color(hitNormal1.x, hitNormal1.y, hitNormal1.z);
                                    xzNormal[(i % (resolution_x + 1)) + (i / (resolution_x + 1)) * (resolution_x + 1) + ((idxCounter + 1) * (resolution_x + 1) * (resolution_z + 1))] = new Color(hitNormal2.x, hitNormal2.y, hitNormal2.z);
                                    idxCounter += 2;
                                }
                                //Debug.DrawLine(modelTransform.TransformPoint(lowPoint), modelTransform.TransformPoint(lowPoint + hitNormal1 * 0.1f), Color.green, 100000f);
                                //Debug.DrawLine(modelTransform.TransformPoint(highPoint), modelTransform.TransformPoint(highPoint + hitNormal2 * 0.1f), Color.green, 100000f);
                            }
                            else
                            {
                                Physics.queriesHitBackfaces = false;
                                keepCasting = false;
                                idxCounter = 0;
                            }
                        }
                        else
                        {
                            keepCasting = false;
                            idxCounter = 0;
                        }
                    }
                }
            }
            #endregion
        }
    }

}

