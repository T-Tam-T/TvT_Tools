using UnityEngine;
using System.Collections.Generic;
using UnityEditor;

namespace ResourceManager.Core
{
    public class ResourceAnalyzer
    {
        public GameObject SelectedPrefab { get; set; }
        public GameObject DetectedObject { get; set; }
        public Dictionary<Object, List<string>> ResourceUsage { get; private set; } = new Dictionary<Object, List<string>>();
        public Dictionary<RuntimeAnimatorController, int> AnimatorClipCount { get; private set; } = new Dictionary<RuntimeAnimatorController, int>();

        // 修改AnalyzeResources方法
        public void AnalyzeResources()
        {
            ResourceUsage.Clear();
            AnimatorClipCount.Clear();

            if (SelectedPrefab != null)
            {
                // 使用自定义根节点名"Root"
                AnalyzeGameObject(SelectedPrefab.transform, "");
            }
        }

        // 修改AnalyzeDetectedObject方法
        public void AnalyzeDetectedObject()
        {
            ResourceUsage.Clear();
            AnimatorClipCount.Clear();

            if (DetectedObject != null)
            {
                // 使用自定义根节点名"Root"
                AnalyzeGameObject(DetectedObject.transform, "");
            }
        }

        private void AnalyzeGameObject(Transform transform, string path)
        {
            // 构建路径，去掉"Root"前缀
            string newPath = string.IsNullOrEmpty(path) ?
                transform.name :
                $"{path}/{transform.name}";

            // 分析各种资源
            AnalyzeMesh(transform, newPath);
            AnalyzeRenderer(transform, newPath);
            AnalyzeAnimator(transform, newPath);
            AnalyzeParticleSystem(transform, newPath); // 确保分析粒子系统

            // 递归遍历子物体
            foreach (Transform child in transform)
            {
                AnalyzeGameObject(child, newPath);
            }
        }

        private void AnalyzeMesh(Transform transform, string path)
        {
            MeshFilter meshFilter = transform.GetComponent<MeshFilter>();
            SkinnedMeshRenderer skinnedMeshRenderer = transform.GetComponent<SkinnedMeshRenderer>();

            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                AddResourceUsage(meshFilter.sharedMesh, $"{path}/{transform.name}");
            }

            if (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMesh != null)
            {
                AddResourceUsage(skinnedMeshRenderer.sharedMesh, $"{path}/{transform.name}");
            }
        }

        private void AnalyzeRenderer(Transform transform, string path)
        {
            Renderer renderer = transform.GetComponent<Renderer>();
            if (renderer != null)
            {
                foreach (Material mat in renderer.sharedMaterials)
                {
                    if (mat != null)
                    {
                        AddResourceUsage(mat, $"{path}/{transform.name}");
                        ExtractTexturesFromMaterial(mat, $"{path}/{transform.name}");
                    }
                }
            }
        }

        private void ExtractTexturesFromMaterial(Material material, string path)
        {
            if (material == null) return;

            Shader shader = material.shader;
            if (shader == null) return;

            for (int i = 0; i < ShaderUtil.GetPropertyCount(shader); i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    string propertyName = ShaderUtil.GetPropertyName(shader, i);
                    Texture texture = material.GetTexture(propertyName);
                    if (texture != null && texture is Texture2D)
                    {
                        AddResourceUsage(texture, $"{path}/{propertyName}");
                    }
                }
            }
        }

        private void AnalyzeAnimator(Transform transform, string path)
        {
            Animator animator = transform.GetComponent<Animator>();
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                RuntimeAnimatorController controller = animator.runtimeAnimatorController;
                AddResourceUsage(controller, $"{path}/{transform.name}/Animator Controller");

                // 统计每个Animator中的动画数量
                if (!AnimatorClipCount.ContainsKey(controller))
                {
                    AnimatorClipCount[controller] = 0;
                }

                AnimationClip[] clips = controller.animationClips;
                AnimatorClipCount[controller] = clips.Length;
            }
        }

        private void AnalyzeParticleSystem(Transform transform, string path)
        {
            ParticleSystem particleSystem = transform.GetComponent<ParticleSystem>();
            if (particleSystem != null)
            {
                string fullPath = $"{path}/{transform.name}";
                AddResourceUsage(particleSystem, fullPath);

                AddResourceUsage(particleSystem, $"{path}/{transform.name}");
                var shapeModule = particleSystem.shape;
                if (shapeModule.enabled && shapeModule.shapeType == ParticleSystemShapeType.Mesh && shapeModule.mesh != null)
                {
                    AddResourceUsage(shapeModule.mesh, $"{path}/{transform.name}/ParticleSystem Shape");
                }

                ParticleSystemRenderer particleSystemRenderer = particleSystem.GetComponent<ParticleSystemRenderer>();
                if (particleSystemRenderer != null)
                {
                    if (particleSystemRenderer.mesh != null)
                    {
                        AddResourceUsage(particleSystemRenderer.mesh, $"{path}/{transform.name}/ParticleSystem Renderer");
                    }
                }
            }
        }

        public void AddResourceUsage(Object resource, string usagePath)
        {
            if (resource == null) return;

            if (!ResourceUsage.ContainsKey(resource))
            {
                ResourceUsage[resource] = new List<string>();
            }
            ResourceUsage[resource].Add(usagePath);
        }

        public List<Object> GetResourcesByType(System.Type type)
        {
            List<Object> resources = new List<Object>();
            foreach (var kvp in ResourceUsage)
            {
                if (type.IsInstanceOfType(kvp.Key))
                {
                    resources.Add(kvp.Key);
                }
            }
            return resources;
        }
    }
}