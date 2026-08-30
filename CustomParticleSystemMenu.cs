using UnityEngine;
using UnityEditor;

public class CustomParticleSystemMenu : MonoBehaviour
{
    [MenuItem("GameObject/TvTTools/创建空粒子发射器", false, 10)]
    static void CreateCustomEmptyEmitter()
    {
        GameObject go = new GameObject("Custom Empty Emitter");
        Undo.RegisterCreatedObjectUndo(go, "Create Custom Empty Emitter");

        // Add a particle system component
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        var mainModule = ps.main;
        var emissionModule = ps.emission;
        var shapeModule = ps.shape;
        var rendererModule = ps.GetComponent<Renderer>() as ParticleSystemRenderer;

        // Set the properties as specified
        mainModule.duration = 10f;
        mainModule.loop = false;
        mainModule.startLifetime = 1f;
        mainModule.startSpeed = 0f;
        mainModule.startSize = 0f;
        mainModule.scalingMode = ParticleSystemScalingMode.Hierarchy;
        mainModule.maxParticles = 0;

        emissionModule.enabled = false;
        shapeModule.enabled = false;

        if (rendererModule != null)
        {
            rendererModule.material = null;
            rendererModule.renderMode = ParticleSystemRenderMode.None;
        }

        Selection.activeGameObject = go;
    }
}



