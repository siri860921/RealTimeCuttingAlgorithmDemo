using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ConstructSurface
{
    namespace TriDexelModel2D
    {
        public class Dexel
        {
            #region DataFirlds
            List<float> dexelPoints; // a list storing the coodinates of low and high dexel points on a particular direction 
            List<Vector3> normals; // a list storing the normals from the ray casting method
            #endregion

            #region Properties
            // // a list storing the coodinates of low and high dexel points on a particular direction
            public List<float> DexelPoints { get => dexelPoints; set => dexelPoints = value; }
            // a list storing the normals from the ray casting method
            public List<Vector3> Normals { get => normals; set => normals = value; }
            #endregion

            #region Constructor
            public Dexel(List<float> dexelPoints, List<Vector3> normals)
            {
                this.dexelPoints = dexelPoints;
                this.normals = normals;
            }
            #endregion
        }

        public class triDexelModel2D
        {
            #region DataFields
            Vector3 minBoundPoint_local; // the minimum bounding point of the model object on its local coordinate
            Vector3 maxBoundPoint_local; // the maximum bounding point of the model object on its local coordinate
            Vector3 xy_arrayOrigin;
            Vector3 zy_arrayOrigin;
            Vector3 xz_arrayOrigin;
            float x_min; // the smallest local x coordinate value of the input mesh model
            float x_max; // the largest local x coordinate value of the input mesh model
            float y_min; // the smallest local y coordinate value of the input mesh model
            float y_max; // the larget local y coordinate value of the input mesh model
            float z_min; // the smallest local z coordinate value of the input mesh model
            float z_max; // the largest local z coordinate value of the input mesh model
            float grid_size; // the edge length of a grid size of the array
            float eta;
            int resolution_ref; // the resolution on the longest dimension of the input model
            int resolution_x; // resolution on the x direction
            int resolution_y; // resolutoion on the y direction
            int resolution_z; // resolution on the z direction


            // tri-dexel data structure, stores the low/high depth of each dexel on one grid
            Dexel[,] xy_array; // x-y 2D array
            Dexel[,] zy_array; // y-z 2D array
            Dexel[,] xz_array; // z-x 2D array

            GameObject inputModel; // the whole model of the input model
            Mesh modelMesh; // mesh data of the input model
            Transform modelTransform; // the transform of the input model
            Vector3[] model_vertice; // vertice data of the input model
            Vector3[] model_normals; // normal data of the input model
            Vector3[] model_reversed_normals; // normal data of the input model, with reversed direction
            int[] model_triangles; // triangle data of the input model
            int[] model_reversed_triangles; // triangle data of the input model, with reversed order
            #endregion

            #region Constructor
            public triDexelModel2D(GameObject inputModel, int resolution)
            {
                this.inputModel = inputModel;
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
            // the edge length of a grid size of the array
            public float GridSize { get => grid_size; }
            // the transform of the input model
            public Transform ModelTransform { get => modelTransform; }
            // minimum bounding box point(local coordinate) of the input object 
            public Vector3 FieldOrigin { get => xy_arrayOrigin; }
            // maximum bounding box point(local coordinate) of the input object
            //public Vector3 MaxBoundPoint_local { get => maxBoundPoint_local; }
            // xy array
            public Dexel[,] XY_array { get => xy_array; }
            // zy array
            public Dexel[,] ZY_array { get => zy_array; }
            // xz array
            public Dexel[,] XZ_array { get => xz_array; }
            #endregion

            #region Methods
            // initialization, get the input mesh model information and construct 2D data structure
            void Initialize()
            {
                getMeshDataFromImput();
                construct2DArray();
            }

            // acquire basic information of the input model
            void getMeshDataFromImput()
            {
                modelMesh = inputModel.GetComponent<MeshFilter>().mesh;
                model_vertice = modelMesh.vertices;
                model_triangles = modelMesh.triangles;
                model_reversed_triangles = new int[model_triangles.Length];
                model_normals = modelMesh.normals;
                model_reversed_normals = new Vector3[model_normals.Length];
                // calculate reverse triangle data
                for (int i = 0; i < model_triangles.Length - 2; i = i + 3)
                {
                    model_reversed_triangles[i] = model_triangles[i + 2];
                    model_reversed_triangles[i + 2] = model_triangles[i + 1];
                    model_reversed_triangles[i + 1] = model_triangles[i];
                }
                // caculate reverse normals
                for (int i = 0; i < model_normals.Length; i++)
                    model_reversed_normals[i] = -model_normals[i];

                // get the local min/max xyz coordinates from the bounding box
                Bounds modelBounds = modelMesh.bounds; // get the boundary of the mesh model
                minBoundPoint_local = modelBounds.min;
                maxBoundPoint_local = modelBounds.max;
            }

            // construct 2D array data structure
            void construct2DArray()
            {
                // determine the grid size and the resolution on each direction
                float x_delta = maxBoundPoint_local.x - minBoundPoint_local.x;
                float y_delta = maxBoundPoint_local.y - minBoundPoint_local.y;
                float z_delta = maxBoundPoint_local.z - minBoundPoint_local.z;
                if (x_delta >= y_delta && x_delta >= z_delta)
                {
                    grid_size = x_delta / resolution_ref;
                    eta = grid_size / 100f;
                    resolution_x = resolution_ref + 10;
                    resolution_y = Mathf.CeilToInt(y_delta / grid_size) + 10;
                    resolution_z = Mathf.CeilToInt(z_delta / grid_size) + 10;
                }
                else if (y_delta >= x_delta && y_delta >= z_delta)
                {
                    grid_size = y_delta / resolution_ref;
                    eta = grid_size / 100f;
                    resolution_x = Mathf.CeilToInt(x_delta / grid_size) + 10;
                    resolution_y = resolution_ref + 10;
                    resolution_z = Mathf.CeilToInt(z_delta / grid_size) + 10;
                }
                else if (z_delta >= x_delta && z_delta >= y_delta)
                {
                    grid_size = z_delta / resolution_ref;
                    eta = grid_size / 100f;
                    resolution_x = Mathf.CeilToInt(x_delta / grid_size) + 10;
                    resolution_y = Mathf.CeilToInt(y_delta / grid_size) + 10;
                    resolution_z = resolution_ref + 10;
                }

                //Debug.Log($"{resolution_x}, {resolution_y}, {resolution_z}");
                
                // generate 2D data structure
                xy_array = new Dexel[resolution_y + 1, resolution_x + 1];
                zy_array = new Dexel[resolution_y + 1, resolution_z + 1];
                xz_array = new Dexel[resolution_z + 1, resolution_x + 1];
                if (resolution_x == resolution_y && resolution_y == resolution_z)
                {
                    // if all three resolutions are the same, construct 2D arrays at a time
                    for (int i = 0; i < resolution_y + 1; i++)
                    {
                        for (int j = 0; j < resolution_x + 1; j++)
                        {
                            //Debug.Log($"{j}, {i}");
                            xy_array[i, j] = new Dexel(new List<float>(), new List<Vector3>());
                            zy_array[i, j] = new Dexel(new List<float>(), new List<Vector3>());
                            xz_array[i, j] = new Dexel(new List<float>(), new List<Vector3>());
                        }
                    }
                }
                else
                {
                    // consrtruct xy array
                    for (int i = 0; i < resolution_y + 1; i++)
                    {
                        for (int j = 0; j < resolution_x + 1; j++)
                            xy_array[i, j] = new Dexel(new List<float>(), new List<Vector3>());
                    }
                    // construct zy array
                    for (int i = 0; i < resolution_y + 1; i++)
                    {
                        for (int j = 0; j < resolution_z + 1; j++)
                            zy_array[i, j] = new Dexel(new List<float>(), new List<Vector3>());
                    }
                    // construct xz array
                    for (int i = 0; i < resolution_z + 1; i++)
                    {
                        for (int j = 0; j < resolution_x + 1; j++)
                            xz_array[i, j] = new Dexel(new List<float>(), new List<Vector3>());
                    }
                }
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
                // for a 2D array, such as xy array, there are resolution_x * resolution_y ray casting informations
                // the ray cast origins should add offsets to ensure collision with the object
                Vector3 zDirection_offset = new Vector3(0f, 0f, (z_max - z_min) / 50000f);
                Vector3 xDirection_offset = new Vector3((x_max - x_min) / 50000f, 0f, 0f);
                Vector3 yDirection_offset = new Vector3(0f, (y_max - y_min) / 50000f, 0f);
                xy_arrayOrigin = new Vector3(x_min - xDirection_offset.x, y_min - yDirection_offset.y, z_min - zDirection_offset.z); // xy array origin in world coordinate
                zy_arrayOrigin = new Vector3(x_min - xDirection_offset.x, y_min - yDirection_offset.y, z_min - zDirection_offset.z); // zy array origin in world coordinate
                xz_arrayOrigin = new Vector3(x_min - xDirection_offset.x, y_min - yDirection_offset.y, z_min - zDirection_offset.z); // xz array origin in world coordinate
                //Vector3 xy_arrayOrigin = new Vector3(x_min, y_min, z_min); // xy array origin in world coordinate
                //Vector3 zy_arrayOrigin = new Vector3(x_min, y_min, z_min); // xy array origin in world coordinate
                //Vector3 xz_arrayOrigin = new Vector3(x_min, y_min, z_min); // xy array origin in world coordinate

                bool isHit; // the hit to get low dexel point
                bool isAnotherHit; // the hit to get high dexel point
                Vector3 lowPoint; // low dexel point buffer
                Vector3 highPoint; // high dexel point buffer
                Vector3 hitNormal1; // low normal point buffer
                Vector3 hitNormal2; // high normal point buffer

                // raycasting on z direction (store data to xy array)
                for (int i = 0; i < resolution_y + 1; i++)
                {
                    for (int j = 0; j < resolution_x + 1; j++)
                    {
                        bool keepCasting = true;
                        Vector3 rayOrigin = xy_arrayOrigin;
                        while (keepCasting)
                        {
                            isHit = Physics.Raycast(modelTransform.TransformPoint(rayOrigin + i * grid_size * Vector3.up + j * grid_size * Vector3.right - zDirection_offset), 
                                modelTransform.TransformDirection(Vector3.forward), out castingRays, modelTransform.TransformVector(Vector3.forward * (z_max - z_min)).magnitude);
                            // if the ray colides with the input medol with a point(low dexel point), store into 2D array
                            // and set that point as the new ray origin to find the point on the other side(high dexel point)
                            // else set the corresponding 2D array element to null
                            if (isHit)
                            {
                                lowPoint = modelTransform.InverseTransformPoint(castingRays.point);
                                if ((lowPoint.z - xy_arrayOrigin.z) % grid_size == 0) lowPoint.z += eta;
                                hitNormal1 = modelTransform.InverseTransformVector(castingRays.normal);
                                Physics.queriesHitBackfaces = true; // enable ray to hit the backface of a mesh
                                isAnotherHit = Physics.Raycast(modelTransform.TransformPoint(lowPoint + zDirection_offset), modelTransform.TransformDirection(Vector3.forward),
                                    out castingRays, modelTransform.TransformVector(Vector3.forward * (z_max - z_min)).magnitude);
                                // check if the ray really collides on the other side, if not then use the spotted point as the new origin to find the next collided point
                                if (isAnotherHit)
                                {
                                    while (castingRays.triangleIndex == -1)
                                        isAnotherHit = Physics.Raycast(castingRays.point + modelTransform.TransformVector(zDirection_offset), modelTransform.TransformDirection(Vector3.forward),
                                            out castingRays, modelTransform.TransformVector(Vector3.forward * (z_max - z_min)).magnitude);
                                    Physics.queriesHitBackfaces = false; // disable ray to hit the backface of a mesh
                                    highPoint = modelTransform.InverseTransformPoint(castingRays.point);
                                    if ((highPoint.z - xy_arrayOrigin.z) % grid_size == 0) highPoint.z += eta;
                                    hitNormal2 = modelTransform.InverseTransformVector(castingRays.normal);
                                    rayOrigin += Vector3.forward * (highPoint.z - rayOrigin.z);
                                    if(highPoint.z - lowPoint.z > grid_size / 100f)
                                    {
                                        xy_array[i, j].DexelPoints.Add(lowPoint.z);
                                        xy_array[i, j].DexelPoints.Add(highPoint.z);
                                        xy_array[i, j].Normals.Add(hitNormal1);
                                        xy_array[i, j].Normals.Add(hitNormal2);
                                    }
                                    //Debug.DrawLine(modelTransform.TransformPoint(lowPoint), modelTransform.TransformPoint(lowPoint + hitNormal1 * 0.1f), Color.green, 100000f);
                                    //Debug.DrawLine(modelTransform.TransformPoint(highPoint), modelTransform.TransformPoint(highPoint + hitNormal2 * 0.1f), Color.green, 100000f);
                                }
                                else
                                {
                                    Physics.queriesHitBackfaces = false;
                                    keepCasting = false;
                                }
                            }
                            else
                            {
                                if (xy_array[i, j].DexelPoints.Count == 0)
                                {
                                    xy_array[i, j].DexelPoints = null;
                                    xy_array[i, j].Normals = null;
                                }
                                keepCasting = false;
                            }
                        }
                    }
                }

                // raycasting on x direction (store data to zy array)
                for (int i = 0; i < resolution_y + 1; i++)
                {
                    for (int j = 0; j < resolution_z + 1; j++)
                    {
                        bool keepCasting = true;
                        Vector3 rayOrigin = zy_arrayOrigin;
                        while (keepCasting)
                        {
                            isHit = Physics.Raycast(modelTransform.TransformPoint(rayOrigin + i * grid_size * Vector3.up + j * grid_size * Vector3.forward - xDirection_offset),
                                modelTransform.TransformDirection(Vector3.right), out castingRays, modelTransform.TransformVector(Vector3.right * (x_max - x_min)).magnitude);
                            // if the ray colides with the input medol with a point(low dexel point), store into 2D array
                            // and set that point as the new ray origin to find the point on the other side(high dexel point)
                            // else set the corresponding 2D array element to null
                            if (isHit)
                            {
                                lowPoint = modelTransform.InverseTransformPoint(castingRays.point);
                                if ((lowPoint.x - zy_arrayOrigin.x) % grid_size == 0) lowPoint.x += eta;
                                hitNormal1 = modelTransform.InverseTransformVector(castingRays.normal);
                                Physics.queriesHitBackfaces = true; // enable ray to hit the backface of a mesh
                                isAnotherHit = Physics.Raycast(modelTransform.TransformPoint(lowPoint + xDirection_offset), modelTransform.TransformDirection(Vector3.right),
                                    out castingRays, modelTransform.TransformVector(Vector3.right * (x_max - x_min)).magnitude);
                                // check if the ray really collides on the other side, if not then use the spotted point as the new origin to find the next collided point
                                if (isAnotherHit)
                                {
                                    while (castingRays.triangleIndex == -1)
                                        isAnotherHit = Physics.Raycast(castingRays.point + modelTransform.TransformVector(xDirection_offset), modelTransform.TransformDirection(Vector3.right),
                                            out castingRays, modelTransform.TransformVector(Vector3.right * (x_max - x_min)).magnitude);
                                    Physics.queriesHitBackfaces = false; // disable ray to hit the backface of a mesh
                                    highPoint = modelTransform.InverseTransformPoint(castingRays.point);
                                    if ((highPoint.x - zy_arrayOrigin.x) % grid_size == 0) highPoint.x += eta;
                                    hitNormal2 = modelTransform.InverseTransformPoint(castingRays.normal);
                                    rayOrigin += Vector3.right * (highPoint.x - rayOrigin.x);
                                    if(highPoint.x - lowPoint.x > grid_size / 100f)
                                    {
                                        zy_array[i, j].DexelPoints.Add(lowPoint.x);
                                        zy_array[i, j].DexelPoints.Add(highPoint.x);
                                        zy_array[i, j].Normals.Add(hitNormal1);
                                        zy_array[i, j].Normals.Add(hitNormal2);
                                    }
                                    //Debug.DrawLine(modelTransform.TransformPoint(lowPoint), modelTransform.TransformPoint(lowPoint + hitNormal1 * 0.1f), Color.green, 100000f);
                                    //Debug.DrawLine(modelTransform.TransformPoint(highPoint), modelTransform.TransformPoint(highPoint + hitNormal2 * 0.1f), Color.green, 100000f);
                                }
                                else
                                {
                                    Physics.queriesHitBackfaces = false;
                                    keepCasting = false;
                                }
                            }
                            else
                            {
                                if (zy_array[i, j].DexelPoints.Count == 0)
                                {
                                    zy_array[i, j].DexelPoints = null;
                                    zy_array[i, j].Normals = null;
                                }
                                keepCasting = false;
                            }
                        }
                    }
                }

                // raycasting on y direction (store data to xz array)
                for (int i = 0; i < resolution_z + 1; i++)
                {
                    for (int j = 0; j < resolution_x + 1; j++)
                    {
                        bool keepCasting = true;
                        Vector3 rayOrigin = xz_arrayOrigin;
                        while (keepCasting)
                        {
                            isHit = Physics.Raycast(modelTransform.TransformPoint(rayOrigin + i * grid_size * Vector3.forward + j * grid_size * Vector3.right - yDirection_offset),
                                modelTransform.TransformDirection(Vector3.up), out castingRays, modelTransform.TransformVector(Vector3.up * (y_max - y_min)).magnitude);
                            // if the ray colides with the input medol with a point(low dexel point), store into 2D array
                            // and set that point as the new ray origin to find the point on the other side(high dexel point)
                            // else set the corresponding 2D array element to null
                            if (isHit)
                            {
                                lowPoint = modelTransform.InverseTransformPoint(castingRays.point);
                                if ((lowPoint.y - xz_arrayOrigin.y) % grid_size == 0) lowPoint.y += eta;
                                hitNormal1 = modelTransform.InverseTransformVector(castingRays.normal);
                                Physics.queriesHitBackfaces = true; // enable ray to hit the backface of a mesh
                                isAnotherHit = Physics.Raycast(modelTransform.TransformPoint(lowPoint + yDirection_offset), modelTransform.TransformDirection(Vector3.up),
                                    out castingRays, modelTransform.TransformVector(Vector3.up * (y_max - y_min)).magnitude);
                                // check if the ray really collides on the other side, if not then use the spotted point as the new origin to find the next collided point
                                if (isAnotherHit)
                                {
                                    while (castingRays.triangleIndex == -1)
                                        isAnotherHit = Physics.Raycast(castingRays.point + modelTransform.TransformVector(yDirection_offset), modelTransform.TransformDirection(Vector3.up),
                                            out castingRays, modelTransform.TransformVector(Vector3.up * (y_max - y_min)).magnitude);
                                    Physics.queriesHitBackfaces = false; // disable ray to hit the backface of a mesh
                                    highPoint = modelTransform.InverseTransformPoint(castingRays.point);
                                    if ((highPoint.y - xz_arrayOrigin.y) % grid_size == 0) highPoint.y += eta;
                                    hitNormal2 = modelTransform.InverseTransformVector(castingRays.normal);
                                    rayOrigin += Vector3.up * (highPoint.y - rayOrigin.y);
                                    if(highPoint.y - lowPoint.y > grid_size / 100f)
                                    {
                                        xz_array[i, j].DexelPoints.Add(lowPoint.y);
                                        xz_array[i, j].DexelPoints.Add(highPoint.y);
                                        xz_array[i, j].Normals.Add(hitNormal1);
                                        xz_array[i, j].Normals.Add(hitNormal2);
                                    }
                                    //Debug.DrawLine(modelTransform.TransformPoint(lowPoint), modelTransform.TransformPoint(lowPoint + hitNormal1 * 0.1f), Color.green, 100000f);
                                    //Debug.DrawLine(modelTransform.TransformPoint(highPoint), modelTransform.TransformPoint(highPoint + hitNormal2 * 0.1f), Color.green, 100000f);
                                }
                                else
                                {
                                    Physics.queriesHitBackfaces = false;
                                    keepCasting = false;
                                }
                            }
                            else
                            {
                                if (xz_array[i, j].DexelPoints.Count == 0)
                                {
                                    xz_array[i, j].DexelPoints = null;
                                    xz_array[i, j].Normals = null;
                                }
                                keepCasting = false;
                            }
                        }
                    }
                }
            }
            #endregion
        }
    }
}
