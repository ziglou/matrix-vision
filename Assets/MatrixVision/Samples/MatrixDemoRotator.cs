using UnityEngine;

namespace MatrixVision.Samples
{
    /// <summary>Fait tourner l'objet en continu (démo : le code Matrix suit l'objet).</summary>
    public sealed class MatrixDemoRotator : MonoBehaviour
    {
        [SerializeField] Vector3 degreesPerSecond = new(0f, 30f, 0f);

        void Update() => transform.Rotate(degreesPerSecond * Time.deltaTime, Space.Self);
    }
}
