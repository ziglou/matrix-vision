using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace MatrixVision.AR
{
    /// <summary>
    /// Maillage d'un plan AR avec un bord adouci : l'alpha du vertex color vaut 1 au cœur du plan
    /// et 0 sur son contour, sur une bande de <see cref="edgeWidth"/> mètres. Le shader MatrixSurface
    /// s'en sert pour que le code s'estompe en bord de plan au lieu d'être coupé net.
    /// Remplace ARPlaneMeshVisualizer ; reconstruit à chaque changement de contour du plan.
    /// </summary>
    [RequireComponent(typeof(ARPlane), typeof(MeshFilter))]
    public sealed class MatrixARPlaneMesh : MonoBehaviour
    {
        [Tooltip("Largeur, en mètres, de la bande où le code s'estompe en bord de plan.")]
        [SerializeField] float edgeWidth = 0.15f;

        ARPlane plane;
        Mesh mesh;
        readonly List<Vector3> vertices = new();
        readonly List<Color> colors = new();
        readonly List<Vector3> normals = new();
        readonly List<int> triangles = new();

        void Awake()
        {
            plane = GetComponent<ARPlane>();
            mesh = new Mesh { name = "MatrixARPlane" };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        void OnEnable() => plane.boundaryChanged += OnBoundaryChanged;

        void OnDisable() => plane.boundaryChanged -= OnBoundaryChanged;

        void OnDestroy() => Destroy(mesh);

        void OnBoundaryChanged(ARPlaneBoundaryChangedEventArgs args) => Rebuild();

        void Rebuild()
        {
            var boundary = plane.boundary;
            int count = boundary.Length;
            mesh.Clear();
            if (count < 3)
                return;

            Vector2 centroid = Vector2.zero;
            for (int i = 0; i < count; i++)
                centroid += boundary[i];
            centroid /= count;

            vertices.Clear();
            colors.Clear();
            normals.Clear();
            triangles.Clear();

            // 0 : centre ; 1..n : anneau intérieur (opaque) ; n+1..2n : contour (transparent).
            AddVertex(centroid, 1f);
            for (int i = 0; i < count; i++)
            {
                Vector2 point = boundary[i];
                Vector2 toCenter = centroid - point;
                float length = toCenter.magnitude;
                Vector2 inner = length > 1e-4f ? point + toCenter / length * Mathf.Min(edgeWidth, length * 0.7f) : point;
                AddVertex(inner, 1f);
            }
            for (int i = 0; i < count; i++)
                AddVertex(boundary[i], 0f);

            // Les plans ARCore sont convexes : éventail au centre, bande de quads sur le bord.
            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                int innerI = 1 + i, innerJ = 1 + j;
                int outerI = 1 + count + i, outerJ = 1 + count + j;
                triangles.Add(0); triangles.Add(innerJ); triangles.Add(innerI);
                triangles.Add(innerI); triangles.Add(innerJ); triangles.Add(outerJ);
                triangles.Add(innerI); triangles.Add(outerJ); triangles.Add(outerI);
            }

            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            // Collider à jour : sert au placement du personnage (sol libre, pas de table ni de mur).
            if (TryGetComponent<MeshCollider>(out var meshCollider))
            {
                meshCollider.sharedMesh = null;
                meshCollider.sharedMesh = mesh;
            }
        }

        // Espace du plan : x, z dans le plan, y selon sa normale.
        void AddVertex(Vector2 planePoint, float alpha)
        {
            vertices.Add(new Vector3(planePoint.x, 0f, planePoint.y));
            colors.Add(new Color(1f, 1f, 1f, alpha));
            normals.Add(Vector3.up);
        }
    }
}
