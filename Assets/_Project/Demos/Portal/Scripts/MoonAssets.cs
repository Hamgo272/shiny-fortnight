using UnityEngine;

namespace CardboardXR.Demos.Portal
{
    // What the Moon world is built from, made by ProjectBootstrap's DemoScenes (EnsureMoon): the
    // scanned regolith and rocks (Poly Haven, CC0), the Earth (NASA, public domain), and the
    // materials, all on CardboardXR/Moon apart from the sky, the stars and the dust.
    [CreateAssetMenu(menuName = "CardboardXR/Moon Assets")]
    public class MoonAssets : ScriptableObject
    {
        [Header("Ground")]
        public Material Ground;
        public Material FarGround;
        [Tooltip("Scanned moon rocks, each with its own material at the same index.")]
        public Mesh[] RockMeshes;
        public Material[] RockMaterials;

        [Header("Sky")]
        public Material Sky;
        public Material Stars;
        public Material Earth;

        [Header("Suit")]
        public Material Fabric;
        public Material Rubber;
        public Material Metal;
        [Tooltip("The colored band on the glove's wrist ring.")]
        public Material Band;

        [Header("Other")]
        public Material Dust;
        [Tooltip("CardboardXR/DepthMask: shows the passthrough image, for the real hand.")]
        public Material Mask;
    }
}
