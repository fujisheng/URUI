using UnityEngine;

namespace Solstice.Scene
{
    /// <summary>
    /// URP 3D scenic stage behind the semi-translucent URUI overlay:
    /// displays the painted sanctuary/battlefield panorama on a parallax quad
    /// with ambient atmospheric embers so URP RenderGraph compositing is always crisp.
    /// </summary>
    public sealed class SolsticeStageController : MonoBehaviour
    {
        public enum StageBackdrop
        {
            Sanctuary,
            Battlefield
        }

        [SerializeField] float parallaxStrength = 0.14f;

        Transform stageRoot;
        Renderer backdropRenderer;
        Material backdropMaterial;
        Texture2D lobbyTex;
        Texture2D combatTex;
        ParticleSystem embersSystem;
        Vector3 baseCameraPos;
        Quaternion baseCameraRot;
        StageBackdrop currentBackdrop = StageBackdrop.Sanctuary;
        float parallaxPhase;

        public void Initialize()
        {
            var cam = Camera.main;
            if (cam)
            {
                baseCameraPos = cam.transform.position;
                baseCameraRot = cam.transform.rotation;
            }

            if (stageRoot)
            {
                return;
            }

            lobbyTex = Resources.Load<Texture2D>("URUI/Solstice/Images/bg-lobby");
            combatTex = Resources.Load<Texture2D>("URUI/Solstice/Images/bg-combat");

            stageRoot = new GameObject("Solstice3DBackdrop").transform;
            stageRoot.SetParent(transform, false);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "PanoramaPlane";
            quad.transform.SetParent(stageRoot, false);
            // Positioned perpendicular to Main Camera looking along +Z so 16:9 covers the entire viewport with margin for parallax
            quad.transform.localPosition = new Vector3(0f, 0.3f, 8.2f);
            quad.transform.localScale = new Vector3(17.8f, 10.0f, 1f);
            var col = quad.GetComponent<Collider>();
            if (col) Destroy(col);

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            backdropMaterial = new Material(shader) { name = "SolsticeBackdropMat" };
            ApplyBackdropTexture(StageBackdrop.Sanctuary);

            backdropRenderer = quad.GetComponent<Renderer>();
            backdropRenderer.sharedMaterial = backdropMaterial;
            backdropRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            backdropRenderer.receiveShadows = false;

            CreateAmbientEmbers();
        }

        public void SetBackdrop(StageBackdrop mode)
        {
            if (currentBackdrop == mode)
            {
                return;
            }
            currentBackdrop = mode;
            ApplyBackdropTexture(mode);
        }

        public void SetThemeTint(string themeClass)
        {
            if (!backdropMaterial)
            {
                return;
            }
            var tint = themeClass switch
            {
                "theme-crimson" => new Color(1.0f, 0.88f, 0.86f, 1f),
                "theme-alabaster" => new Color(1.02f, 1.0f, 0.96f, 1f),
                _ => new Color(0.95f, 0.97f, 1.04f, 1f)
            };
            SetMaterialColor(backdropMaterial, tint);
        }

        public void Tick(float deltaTime, bool reducedMotion)
        {
            var cam = Camera.main;
            if (!cam || reducedMotion)
            {
                return;
            }
            parallaxPhase += deltaTime * 0.42f;
            var offset = new Vector3(
                Mathf.Sin(parallaxPhase) * parallaxStrength,
                Mathf.Cos(parallaxPhase * 0.7f) * (parallaxStrength * 0.35f),
                0f);
            cam.transform.position = baseCameraPos + offset;
            cam.transform.rotation = baseCameraRot * Quaternion.Euler(
                Mathf.Cos(parallaxPhase * 0.5f) * 0.35f,
                Mathf.Sin(parallaxPhase * 0.65f) * 0.45f,
                0f);
        }

        void ApplyBackdropTexture(StageBackdrop mode)
        {
            if (!backdropMaterial)
            {
                return;
            }
            var tex = mode == StageBackdrop.Battlefield ? (combatTex ? combatTex : lobbyTex) : lobbyTex;
            if (backdropMaterial.HasProperty("_BaseMap"))
            {
                backdropMaterial.SetTexture("_BaseMap", tex);
            }
            backdropMaterial.mainTexture = tex;
            SetMaterialColor(backdropMaterial, Color.white);
        }

        static void SetMaterialColor(Material mat, Color color)
        {
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }
            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", color);
            }
        }

        void CreateAmbientEmbers()
        {
            // Ambient glow particles are rendered directly inside URUI layers via <urui-particles>,
            // keeping the 3D URP PanoramaPlane crystal clean with zero opaque particle quad artifacts.
        }

        void OnDestroy()
        {
            if (backdropMaterial)
            {
                Destroy(backdropMaterial);
            }
            if (embersSystem)
            {
                var r = embersSystem.GetComponent<ParticleSystemRenderer>();
                if (r && r.sharedMaterial)
                {
                    Destroy(r.sharedMaterial);
                }
            }
        }
    }
}
