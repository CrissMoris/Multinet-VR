using System;
using System.Collections.Generic;
using MultiTravel.Core.Session;
using MultiTravel.Gameplay.Common;
using UnityEngine;

namespace MultiTravel.Gameplay.Presentation
{
    /// <summary>Lighting mood of the stage (OVERHAUL_PLAN §5).</summary>
    public enum MoodPreset
    {
        /// <summary>Welcome / registration: 60 %.</summary>
        Idle = 0,

        /// <summary>Instructions: 75 % with a warm key on the suitcase.</summary>
        Tutorial = 1,

        /// <summary>Loading / countdown: 55 % with the wardrobe spots up.</summary>
        Countdown = 2,

        /// <summary>Playing: 100 %.</summary>
        Playing = 3,

        /// <summary>Completed: general light down, the suitcase spot narrows.</summary>
        Completed = 4
    }

    /// <summary>Role of a light group in the presets.</summary>
    public enum LightGroupRole
    {
        /// <summary>Room key / fill / downlights.</summary>
        General = 0,

        /// <summary>Key light aimed at the suitcase (warm during the tutorial).</summary>
        SuitcaseKey = 1,

        /// <summary>Spots on the wardrobe modules.</summary>
        WardrobeSpots = 2,

        /// <summary>Narrowing spot on the suitcase at completion.</summary>
        SuitcaseSpot = 3,

        /// <summary>Backdrop LED strips (emissive meshes; breathe in Welcome).</summary>
        Led = 4
    }

    /// <summary>
    /// Named group of realtime lights and emissive renderers with their base values captured at start.
    /// Baked lights (<see cref="LightBakingOutput.isBaked"/> with <c>Baked</c> type) are skipped automatically.
    /// </summary>
    [Serializable]
    public sealed class LightGroup
    {
        public string Name = "Group";
        public LightGroupRole Role = LightGroupRole.General;
        public List<Light> Lights = new List<Light>();
        public List<Renderer> Emissives = new List<Renderer>();

        [NonSerialized] public float[] BaseIntensity;
        [NonSerialized] public float[] BaseSpotAngle;
        [NonSerialized] public Color[] BaseColor;
        [NonSerialized] public Color[] BaseEmission;
        [NonSerialized] public bool[] Realtime;
    }

    /// <summary>
    /// Smoothly blends the stage lighting between presets (0.6 s) by scaling realtime light intensities / spot angles and
    /// emissive renderers through <see cref="MaterialPropertyBlock"/>s, so it works with a baked GI setup. Follows the
    /// session automatically (Welcome → Idle, Instructions → Tutorial, Loading / Countdown → Countdown, Playing →
    /// Playing, Completed … Finished → Completed). <see cref="AttractMode"/> adds the LED breathing in Welcome.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MoodLighting : MonoBehaviour
    {
        private const int RoleCount = 5;

        [SerializeField]
        private List<LightGroup> groups = new List<LightGroup>();

        [SerializeField]
        [Min(0.05f)]
        private float blendSeconds = 0.6f;

        [SerializeField]
        [Tooltip("LED breathing period in Welcome (seconds).")]
        [Min(0.5f)]
        private float breathPeriod = 4f;

        [SerializeField]
        [Tooltip("Warm tint multiplied into the suitcase key during the tutorial.")]
        private Color tutorialWarmTint = new Color(1f, 0.86f, 0.7f, 1f);

        [SerializeField]
        [Tooltip("Follow the session state automatically.")]
        private bool followSession = true;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        // Target and current multipliers per role: intensity, spot-angle factor, warmth (0..1).
        private readonly float[] targetIntensity = new float[RoleCount];
        private readonly float[] targetSpot = new float[RoleCount];
        private readonly float[] targetWarm = new float[RoleCount];
        private readonly float[] fromIntensity = new float[RoleCount];
        private readonly float[] fromSpot = new float[RoleCount];
        private readonly float[] fromWarm = new float[RoleCount];
        private readonly float[] currentIntensity = new float[RoleCount];
        private readonly float[] currentSpot = new float[RoleCount];
        private readonly float[] currentWarm = new float[RoleCount];
        private MaterialPropertyBlock block;
        private SessionController session;
        private bool subscribed;
        private float blendStart = -10f;
        private bool blending;
        private bool breathing;
        private bool captured;

        /// <summary>Preset applied last.</summary>
        public MoodPreset Current { get; private set; } = MoodPreset.Playing;

        /// <summary>True while a blend runs.</summary>
        public bool IsBlending => blending;

        /// <summary>True while the LED strips breathe.</summary>
        public bool LedBreathing => breathing;

        /// <summary>The groups.</summary>
        public IReadOnlyList<LightGroup> Groups => groups;

        /// <summary>Generator API: replaces the groups (base values are captured on the next apply).</summary>
        public void Configure(List<LightGroup> lightGroups)
        {
            groups = lightGroups ?? new List<LightGroup>();
            captured = false;
        }

        /// <summary>Generator API: adds a group.</summary>
        public LightGroup AddGroup(string name, LightGroupRole role, IEnumerable<Light> lights, IEnumerable<Renderer> emissives)
        {
            var group = new LightGroup { Name = name, Role = role };
            if (lights != null)
            {
                group.Lights.AddRange(lights);
            }

            if (emissives != null)
            {
                group.Emissives.AddRange(emissives);
            }

            groups.Add(group);
            captured = false;
            return group;
        }

        /// <summary>Test / generator API: explicit session (null = resolve from AppServices in Start).</summary>
        public void Bind(SessionController sessionController)
        {
            if (sessionController == null || sessionController == session)
            {
                return;
            }

            Unsubscribe();
            session = sessionController;
            Subscribe();
            if (followSession)
            {
                ApplyPreset(PresetFor(session.State), true);
            }
        }

        /// <summary>Blends to a preset (or snaps when <paramref name="instant"/>).</summary>
        public void ApplyPreset(MoodPreset preset, bool instant = false)
        {
            Capture();
            Current = preset;
            for (int role = 0; role < RoleCount; role++)
            {
                fromIntensity[role] = currentIntensity[role];
                fromSpot[role] = currentSpot[role];
                fromWarm[role] = currentWarm[role];
                Targets(preset, (LightGroupRole)role, out targetIntensity[role], out targetSpot[role], out targetWarm[role]);
            }

            if (instant)
            {
                blending = false;
                for (int role = 0; role < RoleCount; role++)
                {
                    currentIntensity[role] = targetIntensity[role];
                    currentSpot[role] = targetSpot[role];
                    currentWarm[role] = targetWarm[role];
                }

                Write();
            }
            else
            {
                blendStart = Time.unscaledTime;
                blending = true;
            }
        }

        /// <summary>Enables the 4 s LED breathing (Welcome attract).</summary>
        public void SetLedBreathing(bool enabled)
        {
            if (breathing == enabled)
            {
                return;
            }

            breathing = enabled;
            if (!enabled)
            {
                Write();
            }
        }

        /// <summary>Preset the session state maps to.</summary>
        public static MoodPreset PresetFor(SessionState state)
        {
            switch (state)
            {
                case SessionState.Instructions:
                    return MoodPreset.Tutorial;
                case SessionState.Loading:
                case SessionState.Countdown:
                    return MoodPreset.Countdown;
                case SessionState.Playing:
                    return MoodPreset.Playing;
                case SessionState.Completed:
                case SessionState.Submitting:
                case SessionState.SubmissionFailed:
                case SessionState.Finished:
                    return MoodPreset.Completed;
                default:
                    return MoodPreset.Idle;
            }
        }

        /// <summary>Current intensity multiplier of a role (tests).</summary>
        public float IntensityOf(LightGroupRole role)
        {
            return currentIntensity[(int)role];
        }

        // ----- Unity -----

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            for (int role = 0; role < RoleCount; role++)
            {
                currentIntensity[role] = 1f;
                currentSpot[role] = 1f;
                targetIntensity[role] = 1f;
                targetSpot[role] = 1f;
            }
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void Start()
        {
            Capture();
            if (session == null && followSession)
            {
                ServiceResolver.TryResolve(ref session);
                Subscribe();
            }

            if (session != null && followSession)
            {
                ApplyPreset(PresetFor(session.State), true);
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Update()
        {
            if (!captured)
            {
                return;
            }

            if (blending)
            {
                float k = (Time.unscaledTime - blendStart) / blendSeconds;
                if (k >= 1f)
                {
                    blending = false;
                    k = 1f;
                }

                float e = PresentationStyle.SmoothStep(k);
                for (int role = 0; role < RoleCount; role++)
                {
                    currentIntensity[role] = Mathf.Lerp(fromIntensity[role], targetIntensity[role], e);
                    currentSpot[role] = Mathf.Lerp(fromSpot[role], targetSpot[role], e);
                    currentWarm[role] = Mathf.Lerp(fromWarm[role], targetWarm[role], e);
                }

                Write();
            }
            else if (breathing)
            {
                WriteGroupsOfRole(LightGroupRole.Led);
            }
        }

        // ----- internals -----

        private static void Targets(MoodPreset preset, LightGroupRole role, out float intensity, out float spot, out float warm)
        {
            spot = 1f;
            warm = 0f;
            switch (preset)
            {
                case MoodPreset.Idle:
                    intensity = role == LightGroupRole.WardrobeSpots || role == LightGroupRole.SuitcaseSpot ? 0f : 0.6f;
                    break;
                case MoodPreset.Tutorial:
                    switch (role)
                    {
                        case LightGroupRole.SuitcaseKey: intensity = 1f; warm = 1f; break;
                        case LightGroupRole.SuitcaseSpot: intensity = 0.6f; break;
                        case LightGroupRole.WardrobeSpots: intensity = 0f; break;
                        default: intensity = 0.75f; break;
                    }

                    break;
                case MoodPreset.Countdown:
                    switch (role)
                    {
                        case LightGroupRole.WardrobeSpots: intensity = 1f; break;
                        case LightGroupRole.SuitcaseSpot: intensity = 0.4f; break;
                        case LightGroupRole.Led: intensity = 0.8f; break;
                        default: intensity = 0.55f; break;
                    }

                    break;
                case MoodPreset.Completed:
                    switch (role)
                    {
                        case LightGroupRole.SuitcaseSpot: intensity = 1f; spot = 0.6f; break;
                        case LightGroupRole.SuitcaseKey: intensity = 0.8f; break;
                        case LightGroupRole.WardrobeSpots: intensity = 0.3f; break;
                        case LightGroupRole.Led: intensity = 1f; break;
                        default: intensity = 0.7f; break;
                    }

                    break;
                default:
                    intensity = 1f;
                    break;
            }
        }

        private void Capture()
        {
            if (captured)
            {
                return;
            }

            captured = true;
            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                if (group == null)
                {
                    continue;
                }

                int lightCount = group.Lights.Count;
                group.BaseIntensity = new float[lightCount];
                group.BaseSpotAngle = new float[lightCount];
                group.BaseColor = new Color[lightCount];
                group.Realtime = new bool[lightCount];
                for (int i = 0; i < lightCount; i++)
                {
                    var light = group.Lights[i];
                    if (light == null)
                    {
                        continue;
                    }

                    group.BaseIntensity[i] = light.intensity;
                    group.BaseSpotAngle[i] = light.spotAngle;
                    group.BaseColor[i] = light.color;
                    var baking = light.bakingOutput;
                    group.Realtime[i] = !(baking.isBaked && baking.lightmapBakeType == LightmapBakeType.Baked);
                }

                int emissiveCount = group.Emissives.Count;
                group.BaseEmission = new Color[emissiveCount];
                for (int i = 0; i < emissiveCount; i++)
                {
                    var renderer = group.Emissives[i];
                    var material = renderer != null ? renderer.sharedMaterial : null;
                    group.BaseEmission[i] = material != null && material.HasProperty(EmissionColorId) ? material.GetColor(EmissionColorId) : Color.black;
                }
            }
        }

        private void Write()
        {
            for (int g = 0; g < groups.Count; g++)
            {
                WriteGroup(groups[g]);
            }
        }

        private void WriteGroupsOfRole(LightGroupRole role)
        {
            for (int g = 0; g < groups.Count; g++)
            {
                if (groups[g] != null && groups[g].Role == role)
                {
                    WriteGroup(groups[g]);
                }
            }
        }

        private void WriteGroup(LightGroup group)
        {
            if (group == null || group.BaseIntensity == null)
            {
                return;
            }

            int role = (int)group.Role;
            float intensity = currentIntensity[role];
            float spot = currentSpot[role];
            float warm = currentWarm[role];
            if (group.Role == LightGroupRole.Led && breathing)
            {
                intensity *= 0.72f + 0.28f * Mathf.Sin(Time.unscaledTime * (2f * Mathf.PI / breathPeriod));
            }

            for (int i = 0; i < group.Lights.Count && i < group.BaseIntensity.Length; i++)
            {
                var light = group.Lights[i];
                if (light == null || !group.Realtime[i])
                {
                    continue;
                }

                light.intensity = group.BaseIntensity[i] * intensity;
                if (light.type == LightType.Spot)
                {
                    light.spotAngle = group.BaseSpotAngle[i] * spot;
                }

                light.color = warm > 0f ? Color.Lerp(group.BaseColor[i], group.BaseColor[i] * tutorialWarmTint, warm) : group.BaseColor[i];
            }

            for (int i = 0; i < group.Emissives.Count && i < group.BaseEmission.Length; i++)
            {
                var renderer = group.Emissives[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(block);
                block.SetColor(EmissionColorId, group.BaseEmission[i] * intensity);
                renderer.SetPropertyBlock(block);
            }
        }

        private void Subscribe()
        {
            if (session == null || subscribed)
            {
                return;
            }

            session.StateChanged += OnStateChanged;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (session != null && subscribed)
            {
                session.StateChanged -= OnStateChanged;
            }

            subscribed = false;
        }

        private void OnStateChanged(SessionState previous, SessionState next)
        {
            if (followSession && isActiveAndEnabled)
            {
                ApplyPreset(PresetFor(next));
            }
        }
    }
}
