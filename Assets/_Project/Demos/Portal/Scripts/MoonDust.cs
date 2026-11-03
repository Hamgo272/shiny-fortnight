using UnityEngine;

namespace CardboardXR.Demos.Portal
{
    // Regolith thrown up by a fingertip or a step. With no air, dust doesn't drift or hang in a cloud:
    // every grain flies a clean arc under the Moon's gravity (1.62 m/s², a sixth of Earth's) and lands,
    // which is how Apollo footage showed it. A particle system, emitting only when asked; a grain dies
    // where it comes down, on a plane at the ground's height where it was thrown.
    public class MoonDust : MonoBehaviour
    {
        const float k_MoonGravity = 1.62f;

        ParticleSystem m_System;
        Transform m_Ground;

        public void Init(Material material)
        {
            m_System = gameObject.AddComponent<ParticleSystem>();
            m_System.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = m_System.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.startLifetime = 4f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.0025f, 0.005f);
            main.startColor = new Color(0.55f, 0.54f, 0.51f);
            main.gravityModifier = k_MoonGravity / 9.81f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1500;
            var emission = m_System.emission;
            emission.enabled = false;
            var shape = m_System.shape;
            shape.enabled = false;
            m_Ground = new GameObject("Dust Ground").transform;
            m_Ground.SetParent(transform, false);
            var collision = m_System.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.Planes;
            collision.SetPlane(0, m_Ground);
            collision.lifetimeLoss = 1f;
            collision.bounce = 0f;
            var renderer = GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            m_System.Play();
        }

        // Throws count grains from a point on the ground (world space), mostly up and along direction.
        public void Kick(Vector3 ground, Vector3 direction, float speed, int count)
        {
            if (m_System == null)
                return;
            // A grain dies on this plane: the ground where it was thrown, a little under it.
            m_Ground.SetPositionAndRotation(ground - Vector3.up * 0.002f, Quaternion.identity);
            var parameters = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (var i = 0; i < count; i++)
            {
                var s = speed * Random.Range(0.25f, 1.1f);
                var spread = Random.insideUnitCircle * 0.6f;
                parameters.position = ground + Vector3.up * 0.003f;
                parameters.velocity = new Vector3(direction.x * 0.5f + spread.x, Random.Range(0.6f, 1.5f), direction.z * 0.5f + spread.y) * s;
                m_System.Emit(parameters, 1);
            }
        }
    }
}
