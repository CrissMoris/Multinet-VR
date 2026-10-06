# MultiTravel: Packing Challenge — Stack Notes

Engineering reference for the Unity side of the project. Every type name, namespace, serialized
field name, prefab path and enum value below was read from the installed sources/assets of this
project (not from memory). Paths are relative to
`C:/Users/Emre/Desktop/vr-oyun/MultiTravelValizChallenge/` unless stated otherwise. Package
sources live under `Library/PackageCache/<package>@<hash>/`.

| Component | Version / location |
|---|---|
| Unity Editor | `6000.4.10f1 (feeafc12a938)` — `C:/Program Files/Unity/Hub/Editor/6000.4.10f1/Editor` |
| Template | `com.unity.template.vr` 9.2.3 content under `Assets/VRTemplateAssets`, `Assets/Samples`, `Assets/XR`, `Assets/XRI`, `Assets/Settings` |
| XR Interaction Toolkit | 3.4.1 — `Library/PackageCache/com.unity.xr.interaction.toolkit@4612b35e2aac` |
| XR Hands | 1.7.3 — `com.unity.xr.hands@3e241be20642` |
| OpenXR Plugin | 1.16.1 — `com.unity.xr.openxr@ef0033a586bf` |
| Unity OpenXR: Meta | 2.5.0 — `com.unity.xr.meta-openxr@dae986a05b5c` |
| XR Plugin Management | 4.5.4 — `com.unity.xr.management@e3a3882b360a` |
| XR Core Utilities | 2.6.0 — `com.unity.xr.core-utils@a8b900321199` |
| Input System | 1.19.0 — `com.unity.inputsystem@21a28c3a6c83` |
| URP | 17.4.0 — `com.unity.render-pipelines.universal@cdf909593d80` |
| uGUI + TextMeshPro | `com.unity.ugui` 2.0.0 — `com.unity.ugui@1f2d1ab0d950` (TMP runtime at `Runtime/TMP/`) |
| Unity Pipeline | 0.8.0-exp.1 — `com.unity.pipeline@62c08c808737` |
| Newtonsoft JSON | `com.unity.nuget.newtonsoft-json` 3.2.1 |

Imported samples (present in `Assets/Samples`): `XR Interaction Toolkit/3.4.1/Starter Assets`,
`XR Interaction Toolkit/3.4.1/Hands Interaction Demo`, `XR Hands/1.7.3/HandVisualizer`.
**Not imported**: `XR Interaction Simulator`, `XR Device Simulator` (see §3).

---

## 1. Template XR rig

### 1.1 Prefab chain

`Assets/VRTemplateAssets/Prefabs/Setup/Complete XR Origin Set Up Hands Variant.prefab`
(GUID `77e7c27b2c5525e4aa8cc9f99d654486`) is a **prefab variant** of

`Assets/Samples/XR Interaction Toolkit/3.4.1/Hands Interaction Demo/Prefabs/XR Origin Hands (XR Rig).prefab`
(GUID `d6878e1999eb4b44a9f5a263af86c185`), which is itself a variant of

`Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab`
(GUID `f6336ac4ac8b4d34bc5072418cdc62a0`).

The template variant adds only three things on top of the Hands rig: a `TunnelingVignette`
instance under `Main Camera`, `Affordance Callouts Left/Right` under each controller, and it
forces `Left Hand` / `Right Hand` to `m_IsActive: 0` (they are activated at runtime by
`XRInputModalityManager`). Its root GameObject is on layer 2 (Ignore Raycast).

Nested prefab instances pulled in by the chain (all under `Assets/Samples/XR Interaction Toolkit/3.4.1/`):
`Starter Assets/Prefabs/Controllers/XR Controller Left|Right.prefab`,
`Starter Assets/Prefabs/Interactors/Poke Interactor.prefab`, `Teleport Interactor.prefab`,
`Left_NearFarInteractor.prefab`, `Right_NearFarInteractor.prefab`, `Gaze Interactor.prefab`,
`Starter Assets/Prefabs/Affordances/PokePointerAffordance.prefab`,
`Starter Assets/TunnelingVignette/TunnelingVignette.prefab`,
`Hands Interaction Demo/Prefabs/PinchPointStabilized.prefab`, `LeftHandQuestVisual.prefab`,
`RightHandQuestVisual.prefab`, `LeftHandAndroidXRVisual.prefab`, `RightHandAndroidXRVisual.prefab`,
`HandInteractorAffordances.prefab`, `HandPokeInteractorAffordances.prefab`.

### 1.2 Effective hierarchy (components per child)

Child paths below are relative to the prefab root `Complete XR Origin Set Up Hands Variant`.
`[inactive]` = GameObject inactive in the prefab. Mesh-only children (controller model parts,
affordance canvases) are summarised.

```
Complete XR Origin Set Up Hands Variant                 (layer 2)
  + Unity.XR.CoreUtils.XROrigin                          m_CameraYOffset 1.36144, m_RequestedTrackingOriginMode 0 (NotSpecified)
  + UnityEngine.CharacterController                      height 1.36144, radius 0.1, center (0,0.76072,0)
  + UnityEngine.XR.Interaction.Toolkit.Inputs.InputActionManager   m_ActionAssets → XRI Default Input Actions
  + UnityEngine.XR.Interaction.Toolkit.Inputs.XRInputModalityManager
        m_LeftHand → "Left Hand", m_RightHand → "Right Hand", m_LeftController → "Left Controller", m_RightController → "Right Controller"
  + UnityEngine.XR.Interaction.Toolkit.Gaze.XRGazeAssistance   (component DISABLED)
  Camera Offset
    Left Controller
      + Samples.StarterAssets.ControllerInputActionManager   m_SmoothMotionEnabled 1, m_SmoothTurnEnabled 0, m_UIScrollingEnabled 1,
                                                              m_TeleportMode/m_TeleportModeCancel/m_Turn/m_SnapTurn/m_Move/m_UIScroll (InputActionReferences)
      + Interactors.XRInteractionGroup                     m_GroupName "Left"
      + Inputs.Haptics.HapticImpulsePlayer                 m_HapticOutput (XRInputHapticImpulseProvider "Haptic"), m_AmplitudeMultiplier 1
      + UnityEngine.InputSystem.XR.TrackedPoseDriver       m_TrackingType 0 (RotationAndPosition), m_UpdateType 0 (UpdateAndBeforeRender)
      Left Controller Visual            + ControllerAnimator; children UniversalController/… (MeshFilter+MeshRenderer)
      Poke Interactor                   + Interactors.XRPokeInteractor (m_Handedness 1 Left) + Feedback.SimpleHapticFeedback
        Poke Point/Pinch_Pointer_LOD0   (mesh)   Poke Point Affordances/{Poke,NearFar} (affordance receivers)
      Teleport Interactor               + Interactors.XRRayInteractor (m_Handedness 1, m_LineType 1 ProjectileCurve, m_EnableUIInteraction 0,
                                          m_SelectInput → "XRI Left Locomotion/Teleport Mode") + LineRenderer + Interactors.Visuals.XRInteractorLineVisual
                                          + SortingGroup + Feedback.SimpleHapticFeedback
      Near-Far Interactor               + Interactors.NearFarInteractor (m_Handedness 1, m_EnableNearCasting 1, m_EnableFarCasting 1,
                                          m_EnableUIInteraction 1, m_BlockUIOnInteractableSelection 1, m_SelectActionTrigger 1 StateChange)
                                        + Attachment.InteractionAttachController + Interactors.Casters.SphereInteractionCaster (m_CastRadius 0.1)
                                        + Interactors.Casters.CurveInteractionCaster (m_CastDistance 10) + Feedback.SimpleHapticFeedback
        LineVisual                      + Interactors.Visuals.CurveVisualController + LineRenderer (disabled) + SortingGroup
      Affordance Callouts Left          + Unity.VRTemplate.CalloutGazeController (+ tooltip canvases; template tutorial UI)
    Right Controller                    (mirror of Left Controller, m_GroupName "Right", handedness 2; ControllerInputActionManager m_SmoothMotionEnabled 0)
      Teleport Interactor / Poke Interactor / Near-Far Interactor / Right Controller Visual / Affordance Callouts Right
    Main Camera                         (see §1.7)
      TunnelingVignette                 + MeshRenderer + MeshFilter + Locomotion.Comfort.TunnelingVignetteController + SortingGroup
    Right Controller Teleport Stabilized Origin   + Inputs.XRTransformStabilizer   → child "Right Controller Stabilized Attach"
    Left Controller Teleport Stabilized Origin    + Inputs.XRTransformStabilizer   → child "Left Controller Stabilized Attach"
    Gaze Stabilized [inactive]          + Inputs.XRTransformStabilizer            → child "Gaze Stabilized Attach"
    Gaze Interactor [inactive]          + Interactors.XRGazeInteractor + Samples.StarterAssets.GazeInputManager + TrackedPoseDriver
    Hand Visualizer                     + UnityEngine.XR.Hands.Samples.VisualizerSample.HandVisualizer (m_DrawMeshes 1, m_DebugDrawJoints 0)
    Right Hand [inactive]               + Interactors.XRInteractionGroup + Samples.Hands.MetaSystemGestureDetector
                                        + Samples.Hands.PokeGestureDetector (m_Handedness 2) + AudioSource
      Pinch Grab Pose                   + TrackedPoseDriver
      Aim Pose                          + TrackedPoseDriver
      Poke Interactor                   + Interactors.XRPokeInteractor (m_Handedness 2, m_EnableUIInteraction 1) + TrackedPoseDriver
      Pinch Point Stabilized            + Samples.Hands.PinchPointFollow + XRInteractorAffordanceStateProvider + HideObjectWhenInteractorBlocked
        Pinch Visual (SkinnedMeshRenderer + affordance receivers) / Pinch Visual Offset / Material Affordance
      Right Hand Quest Visual [inactive]      model RightHand.fbx + UnityEngine.XR.Hands.XRHandTrackingEvents + XRHandSkeletonDriver
                                              + XRHandMeshController + XRHandSkeletonPokeDisplacer; children Hand Near-Far/Poke Interactor Affordances
      Right Hand Android XR Visual [inactive] same component set on RightHandAndroidXR.fbx
      Near-Far Interactor               + Interactors.NearFarInteractor (m_Handedness 2; m_SelectInput / m_UIPressInput m_InputSourceMode 3 = ObjectReference
                                          → children "Select Input" / "UI Press Input" each with Samples.Hands.ReleaseThresholdButtonReader
                                          m_PressThreshold 1, m_ReleaseThreshold 0.9) + InteractionAttachController (m_UseManipulationInput 0)
                                        + SphereInteractionCaster + CurveInteractionCaster   (SimpleHapticFeedback REMOVED by the hands variant)
        LineVisual / UI Press Input / Select Input
    Left Hand [inactive]                (mirror of Right Hand, handedness 1)
  Locomotion                            (layer 2)
    + Locomotion.LocomotionMediator
    + Locomotion.XRBodyTransformer      m_UseCharacterControllerIfExists 1
    Move                                + Samples.StarterAssets.DynamicMoveProvider (m_MoveSpeed 2.5, m_EnableStrafe 1, m_EnableFly 0, m_UseGravity 1)
    Gravity                             + Locomotion.Gravity.GravityProvider (m_UseGravity 1, priority 10)
    Turn                                + Locomotion.Turning.SnapTurnProvider (m_TurnAmount 45, m_DebounceTime 0.5)
                                        + Locomotion.Turning.ContinuousTurnProvider (m_TurnSpeed 60)
    Teleportation                       + Locomotion.Teleportation.TeleportationProvider (priority 20)
    Climb                               + Locomotion.Climbing.ClimbProvider
      Climb Teleport                    + Locomotion.Climbing.ClimbTeleportInteractor + ClimbTeleportDestinationIndicator
    Grab Move [inactive]                + Locomotion.Movement.GrabMoveProvider ×2 + Locomotion.Movement.TwoHandedGrabMoveProvider
    Jump                                + Locomotion.Jump.JumpProvider (m_JumpHeight 1.25)
  Hands Smoothing Post Processor        + Samples.Hands.HandsOneEuroFilterPostProcessor (m_FilterMinCutoff 0.1, m_FilterBeta 0.2)
```

Namespace prefix for abbreviated entries: `UnityEngine.XR.Interaction.Toolkit.` (e.g.
`UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor`). Sample scripts:
`UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets.*` (asmdef `StarterAssets`) and
`UnityEngine.XR.Interaction.Toolkit.Samples.Hands.*` (Hands Interaction Demo asmdef).
`HandVisualizer` is `UnityEngine.XR.Hands.Samples.VisualizerSample.HandVisualizer`.

### 1.3 Locomotion — what to disable for the event build

All locomotion providers derive from `UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider`
and live under `Locomotion/*`. Disabling the whole `Locomotion` child is the simplest switch; the
teleport rays and vignette are separate and must be handled too:

| Feature | Path | Component(s) |
|---|---|---|
| Continuous move | `Locomotion/Move` | `DynamicMoveProvider` (StarterAssets sample, derives from `ContinuousMoveProvider`) |
| Snap turn | `Locomotion/Turn` | `SnapTurnProvider` |
| Continuous turn | `Locomotion/Turn` | `ContinuousTurnProvider` |
| Teleport | `Locomotion/Teleportation` | `TeleportationProvider`; plus `Camera Offset/Left|Right Controller/Teleport Interactor` (`XRRayInteractor`) and the `m_TeleportMode` handling in `ControllerInputActionManager` |
| Gravity / climb / jump / grab-move | `Locomotion/Gravity`, `/Climb`, `/Jump`, `/Grab Move` | `GravityProvider`, `ClimbProvider`+`ClimbTeleportInteractor`, `JumpProvider`, `GrabMoveProvider`, `TwoHandedGrabMoveProvider` |
| Comfort vignette | `Camera Offset/Main Camera/TunnelingVignette` | `TunnelingVignetteController` (references the four providers) |

```csharp
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

// XrRigController (Gameplay): keep hands + controllers + Near-Far (grab + UI ray) + poke; drop locomotion.
void ApplyLocomotion(GameObject xrOrigin, bool enableLocomotion)
{
    foreach (var provider in xrOrigin.GetComponentsInChildren<LocomotionProvider>(true))
        provider.enabled = enableLocomotion;                     // Move, Turn, Teleportation, Gravity, Climb, Jump, Grab Move

    var locomotionRoot = xrOrigin.transform.Find("Locomotion");  // direct child of the rig root
    if (locomotionRoot != null) locomotionRoot.gameObject.SetActive(enableLocomotion);

    foreach (var ray in xrOrigin.GetComponentsInChildren<XRRayInteractor>(true))
        if (ray.gameObject.name == "Teleport Interactor")        // the only XRRayInteractor instances in the rig
            ray.gameObject.SetActive(enableLocomotion);
}
```

Leave `GravityProvider` enabled only if the `CharacterController` must stay grounded; for a seated/standing
booth with locomotion off, disabling everything under `Locomotion` is safe (tracking still moves the camera).

### 1.4 Hand tracking setup

* Subsystem: OpenXR feature `UnityEngine.XR.Hands.OpenXR.HandTracking` (featureId
  `com.unity.openxr.feature.input.handtracking`, extension `XR_EXT_hand_tracking`) creates the
  `UnityEngine.XR.Hands.XRHandSubsystem` (partial class; `running`, `Start()`, `Stop()` come from
  `SubsystemWithProvider`). Enumerate with `SubsystemManager.GetSubsystems(List<XRHandSubsystem>)`.
  Aim pose comes from `UnityEngine.XR.Hands.OpenXR.MetaHandTrackingAim` (`XR_FB_hand_tracking_aim`).
* Switching hands ↔ controllers: `XRInputModalityManager` (rig root) toggles the `Left/Right Hand`
  and `Left/Right Controller` GameObjects. API: `public enum InputMode { None, TrackedHand, MotionController }`,
  `public static IReadOnlyBindableVariable<InputMode> currentInputMode`, UnityEvents
  `trackedHandModeStarted/Ended`, `motionControllerModeStarted/Ended`, properties `leftHand`, `rightHand`,
  `leftController`, `rightController` (GameObject).
* Hand visuals: `Right Hand/Right Hand Quest Visual` (and AndroidXR variants) carry
  `UnityEngine.XR.Hands.XRHandTrackingEvents` (`m_Handedness`, `m_UpdateType`; events `jointsUpdated`,
  `poseUpdated`, `trackingAcquired`, `trackingLost`, `handIsTracked`), `XRHandSkeletonDriver`
  (`rootTransform`, `jointTransformReferences`, `handTrackingEvents`), `XRHandMeshController`
  (`handTrackingEvents`) and `XRHandSkeletonPokeDisplacer`. The rig-root `Hand Visualizer`
  (`HandVisualizer`, `m_DrawMeshes 1`) is the XR Hands sample visualizer; it is redundant with the
  Quest visuals and may be disabled.
* Pinch grab: each hand's `Near-Far Interactor` is a `NearFarInteractor` whose select / UI-press
  inputs are `XRInputButtonReader` in `InputSourceMode.ObjectReference` (serialized `3`; enum order
  `Unused, InputAction, InputActionReference, ObjectReference, …`) pointing at the child
  `ReleaseThresholdButtonReader` objects (pinch strength, press 1.0 / release 0.9). The attach point
  follows `Pinch Grab Pose` (`InteractionAttachController.m_TransformToFollow`), the far ray originates
  at `Aim Pose`.
* Hand poke: `Right Hand/Poke Interactor` → `XRPokeInteractor` (same defaults as the controller
  poke: `m_PokeDepth 0.1`, `m_PokeWidth 0.0075`, `m_PokeSelectWidth 0.015`, `m_PokeHoverRadius 0.015`,
  `m_PokeInteractionOffset 0.005`, `m_RequirePokeFilter 1`, `m_EnableUIInteraction 1`, `m_ClickUIOnDown 1`).
* Smoothing: `Hands Smoothing Post Processor` → `HandsOneEuroFilterPostProcessor : IXRHandProcessor`.
* The hands variant **removes** `SimpleHapticFeedback` from the hand Near-Far interactors (hands have
  no haptics). Controllers keep theirs.

### 1.5 Controller interactors

Per controller (`Camera Offset/Left Controller`, `…/Right Controller`): `ControllerInputActionManager`
(sample; decides teleport-vs-grab mode and smooth/snap turn from the serialized action references),
`XRInteractionGroup` (`Left`/`Right`; interactors inside a group are mutually exclusive),
`HapticImpulsePlayer`, `TrackedPoseDriver`. Interactor children: `Poke Interactor` (`XRPokeInteractor`),
`Teleport Interactor` (`XRRayInteractor`, locomotion only, UI off), `Near-Far Interactor`
(`NearFarInteractor` = near sphere cast grab + far curve ray; **this is the grab and UI-ray interactor**).
Haptics are produced by `SimpleHapticFeedback` (`m_PlaySelectEntered 1`, `m_PlayHoverEntered 1`) which
resolves the `HapticImpulsePlayer` through `m_HapticImpulsePlayer` overrides on the controller.

### 1.6 UI ray / poke, EventSystem, XRInteractionManager

* UI pointer sources: `NearFarInteractor.m_EnableUIInteraction = 1` (ray UI on both controllers and
  both hands), `XRPokeInteractor.m_EnableUIInteraction = 1` (poke UI). `XRRayInteractor`
  (teleport) has `m_EnableUIInteraction = 0`. World-space canvases need `TrackedDeviceGraphicRaycaster`
  (`UnityEngine.XR.Interaction.Toolkit.UI`) instead of `GraphicRaycaster`.
* **The rig prefab contains no EventSystem and no XRInteractionManager.** In the template scene
  `Assets/Scenes/SampleScene.unity` the root object `EventSystem` has
  `UnityEngine.EventSystems.EventSystem` (`m_sendNavigationEvents 1`, `m_DragThreshold 10`) and
  `UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule` with:
  `m_ActiveInputMode 1` (`ActiveInputMode { InputManagerBindings=0, InputSystemActions=1, Both=2 }`),
  `m_EnableXRInput 1`, `m_EnableMouseInput 0`, `m_EnableTouchInput 0`, `m_EnableGamepadInput 0`,
  `m_EnableJoystickInput 0`, `m_BypassUIToolkitEvents 1`, `m_EnableBuiltinActionsAsFallback 1`,
  action references `m_PointAction`, `m_LeftClickAction`, `m_MiddleClickAction`, `m_RightClickAction`,
  `m_ScrollWheelAction`, `m_NavigateAction`, `m_SubmitAction`, `m_CancelAction` → `XRI UI` map of the
  XRI Default Input Actions asset. Public properties: `activeInputMode`, `enableXRInput`,
  `enableMouseInput`, `enableTouchInput`, `enableGamepadInput`, `enableJoystickInput`, `pointAction`,
  `enableBuiltinActionsAsFallback`. `Assets/Scenes/BasicScene.unity` has the same module with
  mouse/touch/gamepad/joystick all `1` and `m_BypassUIToolkitEvents 0`, plus a root object
  `XR Interaction Manager` (`UnityEngine.XR.Interaction.Toolkit.XRInteractionManager`,
  `m_StartingHoverFilters []`, `m_StartingSelectFilters []`).
* Manager discovery: interactors/interactables with `m_InteractionManager` null call
  `ComponentLocatorUtility<XRInteractionManager>.FindComponentDeferred(..., createComponent: managerCreationMode == CreateAutomatically, dontDestroyOnLoad: true)`.
  Project asset `Assets/XRI/Settings/Resources/XRInteractionRuntimeSettings.asset` has
  `m_ManagerCreationMode 0` (`CreateAutomatically`), `m_InteractionManagerSingletonMode 0`
  (`AllowMultiple`), `m_InteractionManagerRegistrationMode 0` (`FindAutomatically`). So a scene without a
  manager still works (one is created, DontDestroyOnLoad). For determinism the `SceneGenerator` should
  create `XR Interaction Manager` and `EventSystem` objects in `Main.unity` explicitly.
* For the operator screen (mouse on the PC monitor) set on the module: `enableMouseInput = true`,
  `activeInputMode = InputSystemActions`, keep the `XRI UI` references (`Point`/`Click`/`ScrollWheel`
  are bound to mouse as well); a single `EventSystem` serves both the world-space VR canvas and the
  screen-space operator canvas (`GraphicRaycaster` for the overlay canvas).

### 1.7 Main Camera

Path: `Complete XR Origin Set Up Hands Variant/Camera Offset/Main Camera`. Components:
`Camera` (`m_ClearFlags 1` Skybox, `m_BackGroundColor (0.192,0.302,0.475)`, `m_Depth -1`,
`m_TargetEye 3` Both, `m_HDR 1`, `m_AllowMSAA 1`, `m_OcclusionCulling 1`, `m_StereoConvergence 10`,
`m_StereoSeparation 0.022`, physical camera params at defaults), `AudioListener`,
`UnityEngine.InputSystem.XR.TrackedPoseDriver` (`m_TrackingType 0` RotationAndPosition,
`m_UpdateType 0` UpdateAndBeforeRender, `m_IgnoreTrackingState 0`),
`UnityEngine.Rendering.Universal.UniversalAdditionalCameraData` (`m_RendererIndex -1` = pipeline default,
`m_RenderPostProcessing 0`, `m_Antialiasing 0`, `m_AllowXRRendering 1`, `m_RequiresDepthTextureOption 2`
/ `m_RequiresOpaqueTextureOption 2` = use pipeline settings, `m_VolumeFrameworkUpdateModeOption 2`).
`XROrigin.m_Camera` references this camera; `m_CameraFloorOffsetObject` = `Camera Offset`.

### 1.8 `Hands Permissions Manager` prefab (Windows relevance)

`Assets/VRTemplateAssets/Prefabs/Setup/Hands Permissions Manager.prefab` (GUID
`9b4a657c7df58fb4fa21624fe730efa2`), instantiated at the root of `SampleScene`. Components:
`UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets.PermissionsManager`
(`m_ProcessPermissionsOnAwake 1`, one group `platformType 1` with
`permissionId android.permission.HAND_TRACKING`; `onPermissionGranted` → `EnableHandTracking`,
`onPermissionDenied` → `DisableHandTracking`) and `Unity.VRTemplate.HandSubsystemManager`
(Assembly-CSharp, `Assets/VRTemplateAssets/Scripts/HandSubsystemManager.cs`; wraps
`XRHandSubsystem.Start()/Stop()`). `PermissionsManager.ProcessPermissions()` is entirely inside
`#if UNITY_ANDROID`, so **on Windows standalone the prefab is a no-op** and can be omitted from
`Main.unity`. (`HandSubsystemManager.EnableHandTracking()` is a usable helper if the Gameplay assembly
ever needs to restart the hand subsystem, but it lives in Assembly-CSharp; re-implement it with
`SubsystemManager.GetSubsystems` rather than referencing it.)

### 1.9 Input actions used by the rig

`Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/XRI Default Input Actions.inputactions`
(GUID `c348712bda248c246b8c49b3db54643f`), enabled by `InputActionManager.actionAssets` on the rig root.
Action maps: `XRI Head`, `XRI Left`, `XRI Left Interaction`, `XRI Left Locomotion`, `XRI Right`,
`XRI Right Interaction`, `XRI Right Locomotion`, `XRI UI`, `Touchscreen Gestures`.
`XRI Left Interaction`: `Select`, `Select Value`, `Activate`, `Activate Value`, `UI Press`,
`UI Press Value`, `UI Scroll`, `Translate Manipulation`, `Rotate Manipulation`, `Manipulation`,
`Scale Toggle`, `Scale Over Time`. `XRI Left Locomotion`: `Teleport Mode`, `Teleport Mode Cancel`,
`Turn`, `Snap Turn`, `Move`, `Grab Move` (`Right` adds `Jump`). `XRI UI`: `Navigate`, `Submit`,
`Cancel`, `Point`, `Click`, `ScrollWheel`, `MiddleClick`, `RightClick`.

---

## 2. XRI 3.4.1 API reference

Namespaces: `UnityEngine.XR.Interaction.Toolkit` (manager, `XRInteractionRuntimeSettings`,
`InteractionLayerMask`), `…Toolkit.Interactables` (`XRBaseInteractable`, `XRGrabInteractable`,
`XRSimpleInteractable`, `IXRSelectInteractable`, `InteractableSelectMode`),
`…Toolkit.Interactors` (`XRBaseInteractor`, `NearFarInteractor`, `XRPokeInteractor`, `XRRayInteractor`,
`IXRInteractor`, `InteractorHandedness`), `…Toolkit.Inputs.Haptics`, `…Toolkit.Feedback`,
`…Toolkit.Filtering` (`XRPokeFilter`), `…Toolkit.UI`, `…Toolkit.Inputs`, `…Toolkit.Locomotion.*`.
Event args: `UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs` etc. (file `Runtime/Interaction/XRInteractionEvents.cs`).

### 2.1 `XRGrabInteractable` (`Runtime/Interaction/Interactables/XRGrabInteractable.cs`)

Public properties (serialized backing field in parentheses):

| Property | Type | Field |
|---|---|---|
| `movementType` | `XRBaseInteractable.MovementType { VelocityTracking, Kinematic, Instantaneous }` (0,1,2) | `m_MovementType` |
| `throwOnDetach` | `bool` | `m_ThrowOnDetach` |
| `useDynamicAttach` | `bool` | `m_UseDynamicAttach` |
| `matchAttachPosition`, `matchAttachRotation`, `snapToColliderVolume`, `reinitializeDynamicAttachEverySingleGrab` | `bool` | `m_MatchAttachPosition`, `m_MatchAttachRotation`, `m_SnapToColliderVolume`, `m_ReinitializeDynamicAttachEverySingleGrab` |
| `attachTransform`, `secondaryAttachTransform` | `Transform` | `m_AttachTransform`, `m_SecondaryAttachTransform` |
| `attachEaseInTime` | `float` | `m_AttachEaseInTime` |
| `trackPosition`, `trackRotation`, `trackScale` | `bool` | `m_TrackPosition`, `m_TrackRotation`, `m_TrackScale` |
| `smoothPosition`, `smoothPositionAmount`, `tightenPosition` | `bool`, `float`, `float` | `m_SmoothPosition`, `m_SmoothPositionAmount`, `m_TightenPosition` |
| `smoothRotation`, `smoothRotationAmount`, `tightenRotation` | `bool`, `float`, `float` | `m_SmoothRotation`, `m_SmoothRotationAmount`, `m_TightenRotation` |
| `smoothScale`, `smoothScaleAmount`, `tightenScale` | `bool`, `float`, `float` | `m_SmoothScale`, … |
| `velocityDamping`, `velocityScale`, `angularVelocityDamping`, `angularVelocityScale` | `float` | `m_VelocityDamping`, … |
| `throwSmoothingDuration`, `throwSmoothingCurve`, `throwVelocityScale`, `throwAngularVelocityScale` | `float`, `AnimationCurve`, `float`, `float` | `m_Throw…` |
| `forceGravityOnDetach` | `bool` | `m_ForceGravityOnDetach` (`m_GravityOnDetach` legacy field also present) |
| `retainTransformParent` | `bool` | `m_RetainTransformParent` |
| `farAttachMode` | `InteractableFarAttachMode` | `m_FarAttachMode` |
| `limitLinearVelocity`, `limitAngularVelocity`, `maxLinearVelocityDelta`, `maxAngularVelocityDelta` | `bool`,`bool`,`float`,`float` | `m_Limit…`, `m_Max…` |
| `predictedVisualsTransform` | `Transform` | `m_PredictedVisualsTransform` (`m_VisualsTransform` legacy) |
| `startingSingleGrabTransformers`, `startingMultipleGrabTransformers`, `addDefaultGrabTransformers` | `List<XRBaseGrabTransformer>`, `bool` | `m_Starting…`, `m_AddDefaultGrabTransformers` |

Inherited from `XRBaseInteractable`: `interactionManager` (`XRInteractionManager`),
`interactionLayers` (`InteractionLayerMask`, field `m_InteractionLayers`), `selectMode`
(`InteractableSelectMode { Single, Multiple }`, field `m_SelectMode`), `focusMode`
(`InteractableFocusMode { None, Single, Multiple }`), `distanceCalculationMode`
(`DistanceCalculationMode { TransformPosition, ColliderPosition, ColliderVolume }`), `colliders`,
`isSelected`, `isHovered`, `firstInteractorSelecting` (`IXRSelectInteractor`), `interactorsSelecting`,
events `selectEntered` (`SelectEnterEvent`), `selectExited` (`SelectExitEvent`), `firstSelectEntered`,
`lastSelectExited`, `hoverEntered`, `hoverExited`, `firstHoverEntered`, `lastHoverExited`, `activated`,
`deactivated`. Overridable hooks: `protected virtual void OnSelectEntered(SelectEnterEventArgs)`,
`OnSelectExited(SelectExitEventArgs)`, and on the grab interactable `protected virtual void Drop()` /
`Detach()`.

Event args (`XRInteractionEvents.cs`): `BaseInteractionEventArgs { IXRInteractor interactorObject; IXRInteractable interactableObject; }`;
`SelectEnterEventArgs : BaseInteractionEventArgs` with `new IXRSelectInteractor interactorObject`,
`new IXRSelectInteractable interactableObject`, `XRInteractionManager manager`;
`SelectExitEventArgs` adds `bool isCanceled`. `IXRInteractor` exposes `Transform transform`,
`InteractorHandedness handedness` (`{ None, Left, Right }`) and `Transform GetAttachTransform(IXRInteractable)`.

```csharp
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// ProductItem wiring per ARCHITECTURE §2.8 (configured once by ContentGenerator, re-asserted in Awake).
static void ConfigureGrab(XRGrabInteractable grab)
{
    grab.movementType = XRBaseInteractable.MovementType.Kinematic;
    grab.throwOnDetach = false;
    grab.useDynamicAttach = true;
    grab.selectMode = InteractableSelectMode.Single;
    grab.trackPosition = true;  grab.trackRotation = true;
    grab.smoothPosition = true; grab.smoothRotation = true;
    grab.attachEaseInTime = 0.15f;
    grab.retainTransformParent = false;
    grab.interactionLayers = InteractionLayerMask.GetMask("Default");   // or a dedicated "Items" layer, see §2.4
}

void OnEnable()  { grab.selectEntered.AddListener(OnGrabbed); grab.selectExited.AddListener(OnReleased); }
void OnDisable() { grab.selectEntered.RemoveListener(OnGrabbed); grab.selectExited.RemoveListener(OnReleased); }

void OnGrabbed(SelectEnterEventArgs args)
{
    IXRSelectInteractor interactor = args.interactorObject;      // NearFarInteractor (controller or hand)
    Transform interactorTransform = interactor.transform;
    InteractorHandedness hand = interactor.handedness;
}

void OnReleased(SelectExitEventArgs args)
{
    if (args.isCanceled) { /* interactor disabled / manager torn down — treat as drop without throw */ }
}
```

### 2.2 Haptics in XRI 3.x

`UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics.HapticImpulsePlayer : MonoBehaviour`
(`[AddComponentMenu("XR/Haptics/Haptic Impulse Player")]`): fields `m_HapticOutput`
(`XRInputHapticImpulseProvider`, default `new XRInputHapticImpulseProvider("Haptic")`) and
`m_AmplitudeMultiplier` (0..1); properties `hapticOutput`, `amplitudeMultiplier`; methods
`bool SendHapticImpulse(float amplitude, float duration)` and
`bool SendHapticImpulse(float amplitude, float duration, float frequency)` (returns false when the
component is inactive or no channel is available). In `Awake` it auto-binds to any
`IXRHapticImpulseProvider` found via `GetComponentInParent` when no action reference is set.
Interfaces: `IXRHapticImpulseProvider { IXRHapticImpulseChannelGroup GetChannelGroup(); }`,
`IXRHapticImpulseChannelGroup { int channelCount; IXRHapticImpulseChannel GetChannel(int channel = 0); }`,
`IXRHapticImpulseChannel { bool SendHapticImpulse(float amplitude, float duration, float frequency = 0f); }`.
`UnityEngine.XR.Interaction.Toolkit.Feedback.SimpleHapticFeedback` wraps the above per interactor
(`hapticImpulsePlayer`, `playSelectEntered`/`selectEnteredData` (`HapticImpulseData { amplitude, duration, frequency }`),
`playSelectExited`, `playHoverEntered`, …; falls back to `HapticImpulsePlayer.GetOrCreateInHierarchy`).

In the template rig the `HapticImpulsePlayer` sits on `Left Controller` / `Right Controller`, i.e. a
parent of the Near-Far interactor. Hands have none, so always null-check.

```csharp
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

// PlacementFeedback: pulse the hand that just released the item.
static void Pulse(SelectExitEventArgs args, float amplitude, float durationSeconds)
{
    var player = args.interactorObject.transform.GetComponentInParent<HapticImpulsePlayer>(true);
    if (player != null && player.SendHapticImpulse(amplitude, durationSeconds)) return;

    // Fallback: talk to the provider directly (controllers expose IXRHapticImpulseProvider through XRInputHapticImpulseProvider).
    var provider = args.interactorObject.transform.GetComponentInParent<IXRHapticImpulseProvider>(true);
    provider?.GetChannelGroup()?.GetChannel()?.SendHapticImpulse(amplitude, durationSeconds, 0f);
}
```

### 2.3 World-space button: `XRSimpleInteractable` + `XRPokeFilter`

There is no `XRPokeInteractable` type in XRI 3.4.1. A poke button is
`UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable : XRBaseInteractable`
(`[AddComponentMenu("XR/XR Simple Interactable")]`) plus
`UnityEngine.XR.Interaction.Toolkit.Filtering.XRPokeFilter : MonoBehaviour, IXRPokeFilter, IPokeStateDataProvider`
(`[AddComponentMenu("XR/XR Poke Filter")]`): fields `m_Interactable` (`XRBaseInteractable`, auto-found
in the hierarchy), `m_PokeCollider` (`Collider`), `m_PokeConfiguration`
(`PokeThresholdDatumProperty` wrapping `PokeThresholdData { PokeAxis pokeDirection; float interactionDepthOffset; bool enablePokeAngleThreshold; float pokeAngleThreshold; }`).
Because `XRPokeInteractor.m_RequirePokeFilter = 1` in the rig, **a poke filter is mandatory** for poke
selection; the Near-Far interactor can still select the same `XRSimpleInteractable` by ray/grab, so set
its `interactionLayers` to a poke-only layer if ray selection must be excluded. Reference prefab:
`Assets/Samples/XR Interaction Toolkit/3.4.1/Hands Interaction Demo/DemoAssets/Prefabs/PokeButton.prefab`.

```csharp
var button = go.AddComponent<XRSimpleInteractable>();          // uses the GameObject's colliders
button.selectMode = InteractableSelectMode.Single;
var filter = go.AddComponent<XRPokeFilter>();
filter.pokeInteractable = button;                              // XRPokeFilter properties: pokeInteractable, pokeCollider, pokeConfiguration
filter.pokeCollider = go.GetComponent<BoxCollider>();
button.selectEntered.AddListener(_ => completionEvaluator.NotifyManualConfirm());
```

### 2.4 Interaction layers

`UnityEngine.XR.Interaction.Toolkit.InteractionLayerMask` (struct, 32 bits): `int value`, implicit
conversions to/from `int`, statics `string LayerToName(int layer)`, `int NameToLayer(string layerName)`,
`int GetMask(params string[] layerNames)`. Both `XRBaseInteractor.interactionLayers` and
`XRBaseInteractable.interactionLayers` must share a bit for hover/select.

Layer names live in `Assets/XRI/Settings/Resources/InteractionLayerSettings.asset`
(`class InteractionLayerSettings : ScriptableSettings<InteractionLayerSettings>`, **internal**, 32 entries in
`m_LayerNames`; built-in index 0 = `Default`; the template defines index 31 = `Teleport`). Because the
class is internal, editor code adds a layer by editing the asset with `SerializedObject`:

```csharp
// Editor only. Idempotently name interaction layer `index` (1..30) if it is empty.
static void EnsureInteractionLayer(int index, string name)
{
    var asset = AssetDatabase.LoadMainAssetAtPath("Assets/XRI/Settings/Resources/InteractionLayerSettings.asset");
    var so = new SerializedObject(asset);
    var names = so.FindProperty("m_LayerNames");
    var slot = names.GetArrayElementAtIndex(index);
    if (string.IsNullOrEmpty(slot.stringValue)) { slot.stringValue = name; so.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssetIfDirty(asset); }
}
```

### 2.5 Runtime settings / manager modes

`XRInteractionRuntimeSettings` (`Assets/XRI/Settings/Resources/XRInteractionRuntimeSettings.asset`):
`ManagerCreationMode { CreateAutomatically, Manual }`, `ManagerSingletonMode { AllowMultiple, EnforceSingle }`,
`ManagerRegistrationMode { FindAutomatically, Manual }` — all currently `0` (automatic).

---

## 3. XR Interaction Simulator / XR Device Simulator

* Samples declared in `com.unity.xr.interaction.toolkit@4612b35e2aac/package.json`:
  `XR Interaction Simulator` (`Samples~/XR Interaction Simulator`) and the legacy `XR Device Simulator`
  (`Samples~/XR Device Simulator`). **Neither is imported** in `Assets/Samples`. After import the
  paths are `Assets/Samples/XR Interaction Toolkit/3.4.1/XR Interaction Simulator/XR Interaction Simulator.prefab`
  (asmdef `InteractionSimulator`, input assets `XR Interaction Simulator Controls.inputactions`,
  `XR Interaction Controller Controls.inputactions`, `XR Interaction Hand Controls.inputactions`) and
  `…/XR Device Simulator/XR Device Simulator.prefab` (asmdef `DeviceSimulator`).
* Prefab root `XR Interaction Simulator` components: `UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.XRInteractionSimulator`
  (`[AddComponentMenu("XR/Debug/XR Interaction Simulator")]`, `public static XRInteractionSimulator instance`),
  `SimulatedDeviceLifecycleManager` (`removeOtherHMDDevices` default true → **removes every real `XRHMD`
  Input System device while enabled**, adds `XRSimulatedHMD` + two `XRSimulatedController`s),
  `SimulatedHandExpressionManager`, `InputActionManager`.
* Settings asset: `Assets/XRI/Settings/Resources/XRDeviceSimulatorSettings.asset`
  (`class XRDeviceSimulatorSettings : ScriptableSettings<XRDeviceSimulatorSettings>`, internal,
  `[ScriptableSettingsPath("Assets/XRI/Settings")]`). Fields: `m_AutomaticallyInstantiateSimulatorPrefab`
  (currently `0`), `m_AutomaticallyInstantiateInEditorOnly` (`1`), `m_UseClassic` (`0`),
  `m_SimulatorPrefab` (currently `{fileID: 0}`). Edited in Project Settings ▸ XR Plug-in Management ▸
  XR Interaction Toolkit, or via `SerializedObject` from editor code.
* Auto-instantiation: `XRInteractionSimulatorLoader` (static ctor triggered by a
  `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]`): returns unless
  `automaticallyInstantiateSimulatorPrefab` is true and (`!automaticallyInstantiateInEditorOnly ||
  Application.isEditor`); skips when a simulator `instance` already exists or when running Unity tests;
  otherwise `Object.Instantiate(simulatorPrefab)` + `DontDestroyOnLoad`. **It performs no HMD check.**
* Recommended for ARCHITECTURE §3 (`EnableDeviceSimulatorWhenNoHmd` && no HMD && Editor/Development build):
  keep the asset flag `0` and let `XrRigController`/`XrStatusService` instantiate the prefab itself
  (`Resources`-free: reference the prefab through `AppConfig` or a serialized field on a Bootstrap object,
  guarded by `#if UNITY_EDITOR || DEVELOPMENT_BUILD`) only after `XrStatusService.HmdPresent == false`.
  Importing the sample from editor code: `foreach (var s in UnityEditor.PackageManager.UI.Sample.FindByPackage("com.unity.xr.interaction.toolkit", "3.4.1")) if (s.displayName == "XR Interaction Simulator" && !s.isImported) s.Import(Sample.ImportOptions.OverridePreviousImports);`
  (`Sample.isImported`, `importPath`, `resolvedPath` exist in 6000.4; `ImportOptions` members are `None`,
  `OverridePreviousImports`, `HideImportWindow` — whether they combine as flags was not verified, so pass one).

---

## 4. OpenXR standalone configuration

Asset: `Assets/XR/Settings/OpenXRPackageSettings.asset` (`OpenXRPackageSettings`, `Keys: 01000000070000000d000000`
= BuildTargetGroup 1 Standalone, 7 Android, 13 WebGL; one `OpenXRSettings` sub-object per group, each
feature is a sub-object named `<FeatureClass> <Group>` with `m_enabled`).

Standalone `OpenXRSettings`: `m_renderMode 0` = **`OpenXRSettings.RenderMode.MultiPass`**
(`enum RenderMode { MultiPass, SinglePassInstanced }` in `Runtime/Settings/OpenXRRenderSettings.cs`),
`m_depthSubmissionMode 0` (`None`), `m_latencyOptimization 0`, `m_autoColorSubmissionMode 1`,
`m_symmetricProjection 0`, `m_foveatedRenderingApi 0`, `m_useOpenXRPredictedTime 0`, 47 feature entries.
Android: `m_renderMode 1` (SinglePassInstanced), `m_latencyOptimization 1`.
Feature sets selected (`Assets/XR/Settings/OpenXR Editor Settings.asset`): `com.unity.openxr.featureset.meta`,
`com.unity.openxr.featureset.android` for both groups.

Standalone features with `m_enabled: 1`:

| Sub-object | Class (namespace) | `featureId` |
|---|---|---|
| `OculusTouchControllerProfile Standalone` | `UnityEngine.XR.OpenXR.Features.Interactions.OculusTouchControllerProfile` | `com.unity.openxr.feature.input.oculustouch` |
| `MetaQuestTouchPlusControllerProfile Standalone` | `…Interactions.MetaQuestTouchPlusControllerProfile` | `com.unity.openxr.feature.input.metaquestplus` |
| `HandTracking Standalone` | `UnityEngine.XR.Hands.OpenXR.HandTracking` | `com.unity.openxr.feature.input.handtracking` |
| `MetaHandTrackingAim Standalone` | `UnityEngine.XR.Hands.OpenXR.MetaHandTrackingAim` | `com.unity.openxr.feature.input.metahandtrackingaim` |
| `DisplayUtilitiesFeature Standalone` | `UnityEngine.XR.OpenXR.Features.Meta.DisplayUtilitiesFeature` | `com.unity.openxr.feature.meta-display-utilities` |
| `OpenXRCompositionLayersFeature Standalone` | `UnityEngine.XR.OpenXR.Features.CompositionLayers.OpenXRCompositionLayersFeature` | `com.unity.openxr.feature.compositionlayers` |
| `OpenXRLifeCycleFeature Standalone` | `UnityEngine.XR.OpenXR.Features.Meta.OpenXRLifeCycleFeature` | `MetaOpenXR-OpenXRLifeCycle` |

**Disabled on Standalone although ARCHITECTURE §3 lists them**: `HandInteractionProfile Standalone`
(`…Interactions.HandInteractionProfile`, `com.unity.openxr.feature.input.handinteraction`) and
`MetaQuestTouchProControllerProfile Standalone` (`…Interactions.MetaQuestTouchProControllerProfile`,
`com.unity.openxr.feature.input.metaquestpro`). Everything else (HTC Vive, Index, KHR Simple, eye gaze,
AR features, mock runtime, …) is `0`.

XR Plug-in Management: `Assets/XR/XRGeneralSettingsPerBuildTarget.asset` — `Standalone Settings`
(`m_InitManagerOnStart 1`, `m_LoaderManagerInstance` → `Standalone Providers` with
`m_Loaders: [Assets/XR/Loaders/OpenXRLoader.asset]`, `m_AutomaticLoading 0`, `m_AutomaticRunning 0`
— these two are forced to false by `XRGeneralSettings.InitXRSDK` anyway). Registered in
`ProjectSettings/EditorBuildSettings.asset` under `com.unity.xr.management.loader_settings` and
`com.unity.xr.openxr.settings4`.

Editor API for toggling features (verified in `Editor/FeatureSupport/FeatureHelpers.cs`,
`Runtime/Settings/OpenXRSettings.cs`, `Runtime/Features/OpenXRFeatureSettings.cs`, `Runtime/Features/OpenXRFeature.cs`):

```csharp
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;            // FeatureHelpers (static)
using UnityEngine.XR.OpenXR;                      // OpenXRSettings
using UnityEngine.XR.OpenXR.Features;             // OpenXRFeature
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.Hands.OpenXR;

static void ConfigureStandaloneOpenXr()
{
    var group = BuildTargetGroup.Standalone;
    FeatureHelpers.RefreshFeatures(group);                                     // rebuilds the feature list for the group

    OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);  // UNITY_EDITOR only
    settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;      // property on the partial class

    foreach (var id in new[] { OculusTouchControllerProfile.featureId, MetaQuestTouchPlusControllerProfile.featureId,
                               MetaQuestTouchProControllerProfile.featureId, HandInteractionProfile.featureId,
                               HandTracking.featureId, MetaHandTrackingAim.featureId })
    {
        OpenXRFeature feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(group, id);
        if (feature != null) feature.enabled = true;                           // OpenXRFeature.enabled (setter calls OnEnabledChange)
    }
    // Typed access also exists: settings.GetFeature<HandTracking>(), settings.GetFeatures<OpenXRInteractionFeature>(), settings.featureCount
    EditorUtility.SetDirty(settings);
    AssetDatabase.SaveAssets();
}
```

Other verified members: `FeatureHelpers.GetFeatureWithIdForActiveBuildTarget(string)`,
`GetFeaturesWithIdsForBuildTarget(BuildTargetGroup, string[])`, `GetAllFeatureInfo(BuildTargetGroup)`;
`OpenXRSettings.ActiveBuildTargetInstance`, `OpenXRSettings.Instance` (runtime instance);
`OpenXRFeature.enabled`, `nameUi`, `version`, `featureIdInternal` (internal, `[SerializeField]`);
feature sets: `UnityEditor.XR.OpenXR.Features.OpenXRFeatureSetManager.GetFeatureSetWithId(group, id)`,
`SetFeaturesFromEnabledFeatureSets(group)`. Loader: `UnityEngine.XR.OpenXR.OpenXRLoader : OpenXRLoaderBase`
(`XRLoaderHelper`), overrides `Initialize()` / `Start()`.

---

## 5. XR Management at runtime (XrStatusService)

Verified in `com.unity.xr.management@e3a3882b360a/Runtime/XRGeneralSettings.cs`, `XRManagerSettings.cs`,
`XRLoader.cs` and the engine XML docs (`UnityEngine.XRModule.xml`, `UnityEngine.SubsystemsModule.xml`).

* `UnityEngine.XR.Management.XRGeneralSettings.Instance` (static; null if XR is not configured),
  `.Manager` (`XRManagerSettings`), `.InitManagerOnStart`. Automatic startup:
  `AttemptInitializeXRSDKOnLoad` (`RuntimeInitializeLoadType.AfterAssembliesLoaded`) calls
  `InitializeLoaderSync()`, then `AttemptStartXRSDKOnBeforeSplashScreen` (`BeforeSplashScreen`) calls `StartSubsystems()`.
* `XRManagerSettings` (sealed): `XRLoader activeLoader { get; }`, `bool isInitializationComplete`,
  `T ActiveLoaderAs<T>()`, `void InitializeLoaderSync()`, `IEnumerator InitializeLoader()` (both log a
  warning and return early if `activeLoader != null`; iterate `currentLoaders`, first
  `loader.Initialize()` that succeeds becomes active), `void StartSubsystems()`, `void StopSubsystems()`
  (warn and no-op unless initialization completed), `void DeinitializeLoader()` (stops, deinitialises,
  clears `activeLoader`, resets the complete flag), `automaticLoading`, `automaticRunning`.
* `XRLoader`: `virtual bool Initialize()/Start()/Stop()/Deinitialize()`, `abstract T GetLoadedSubsystem<T>()`.
* Display: `UnityEngine.XR.XRDisplaySubsystem` (`running` comes from `IntegratedSubsystem.running` /
  `ISubsystem.running`); enumerate via `SubsystemManager.GetSubsystems(List<XRDisplaySubsystem>)` or
  `activeLoader.GetLoadedSubsystem<XRDisplaySubsystem>()`.
* User presence (legacy device API, still present in 6000.4): `UnityEngine.XR.InputDevices.GetDeviceAtXRNode(XRNode.Head)`
  → `InputDevice.isValid`, `TryGetFeatureValue(CommonUsages.userPresence, out bool)`,
  `CommonUsages.isTracked`. Input System equivalent: the OpenXR HMD layout `OpenXRHmd : XRHMD`
  (internal, `Runtime/input/OpenXRHmd.cs`) exposes a `ButtonControl` named `"UserPresence"`;
  `XRHMD` (`UnityEngine.InputSystem.XR`, `GenericXRDevice.cs`) is a `TrackedDevice` with
  `centerEyePosition/Rotation`, `leftEye…`, `rightEye…` plus inherited `isTracked`/`trackingState`.

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Management;

public sealed class XrStatusService : MonoBehaviour /* registered in AppServices as the Core-facing interface */
{
    static readonly List<XRDisplaySubsystem> s_Displays = new List<XRDisplaySubsystem>();

    public bool IsXrRunning
    {
        get
        {
            var mgr = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (mgr == null || !mgr.isInitializationComplete || mgr.activeLoader == null) return false;
            SubsystemManager.GetSubsystems(s_Displays);
            return s_Displays.Count > 0 && s_Displays[0].running;
        }
    }

    public bool HmdPresent
    {
        get
        {
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (head.isValid && head.TryGetFeatureValue(CommonUsages.userPresence, out var present)) return present;
            var hmd = InputSystem.GetDevice<XRHMD>();                            // Input System path (OpenXR HMD layout)
            var control = hmd?.TryGetChildControl<ButtonControl>("UserPresence");
            return control != null ? control.isPressed : hmd != null && hmd.isTracked.isPressed;
        }
    }

    public void Retry() => StartCoroutine(RetryRoutine());

    IEnumerator RetryRoutine()
    {
        var mgr = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
        if (mgr == null) yield break;
        if (mgr.isInitializationComplete) { mgr.StopSubsystems(); mgr.DeinitializeLoader(); }   // required before re-init
        yield return mgr.InitializeLoader();                                                    // coroutine; one loader per frame
        if (mgr.activeLoader != null) mgr.StartSubsystems();
    }
}
```

Note: `XRSettings.isDeviceActive` / `XRSettings.loadedDeviceName` (`UnityEngine.XR.XRSettings`) are also
available but reflect the legacy VR device layer; prefer the subsystem checks above.

---

## 6. `com.unity.pipeline` 0.8.0-exp.1 (CLI commands)

Source: `Library/PackageCache/com.unity.pipeline@62c08c808737/`. Docs: `README.md`,
`Documentation~/index.md` (command index), `Documentation~/creating-commands.md`,
`Documentation~/commands/*.md`.
Servers: Editor `127.0.0.1:7800-7849` (auto-started), Runtime `7900-7949`
(development players only). CLI: `unity command <name> [--arg value …]`, `unity command` alone lists
commands, `unity command --project-path <path> …` targets a project, `unity command set_autotick --enable true`
keeps a background editor ticking.

### 6.1 Declaring commands

Attributes (namespace **`Unity.Pipeline.Commands`**, assembly **`Unity.Pipeline.Attributes`** —
`Runtime/Attributes/Unity.Pipeline.Attributes.asmdef`, no define constraints, `autoReferenced: true`):

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class CliCommandAttribute : Attribute
{
    public string Name { get; }                 // ctor arg 1: unique command name ("unity command <name>")
    public string Description { get; }          // ctor arg 2
    public bool MainThreadRequired { get; set; } = true;
    public bool RuntimeOnly { get; set; } = false;   // adds the "runtime" tag
    public string[] Tags { get; set; } = Array.Empty<string>();   // e.g. "build", "scenes", "assets/import"
    public CliCommandAttribute(string name, string description);
}

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public class CliArgAttribute : Attribute
{
    public string Name { get; }  public string Description { get; }
    public bool Required { get; set; } = false;   // a parameter without a C# default is treated as required anyway
    public object DefaultValue { get; set; }      // C# default value wins
    public CliArgAttribute(string name, string description);
}
```

Rules (from `creating-commands.md`, `Runtime/Common/CommandLineBinder.cs`):
* Handler must be **`static`**; `public`/`internal`/`private` all work (invoked by reflection; instance
  methods are skipped with a warning). Discovery is automatic (`TypeCache` in the editor), available after
  the next recompile; dynamic registration via `CommandRegistry.RegisterCommand(name, description, Delegate, mainThreadRequired, runtimeOnly, tags)`.
* Return type: anything serialisable — `string`, number, anonymous object, a model, `null`, or a class
  deriving `Unity.Pipeline.Models.CommandExecutionResponse` (`Success`, `Error`, `ErrorDetails`,
  `Warnings`). Exceptions become `{"success":false,"error":…}`. Reply is lean JSON `{"success":true,"result":…}`.
* Parameters: bound by `[CliArg]` name (or the C# parameter name). Wire grammar: `--key value`,
  `--key=value`, bare `--key` (bool true), `--` ends flags; positionals fill required parameters in
  declaration order, then optionals. Enums by name (case-insensitive, `[Flags]` comma-separated),
  primitives coerced, structured DTOs implementing `Unity.Pipeline.Commands.IStructuredCommandInput`
  (or `JObject`) are passed as a JSON flag value. Reordering parameters or flipping Required is a wire-breaking change.
* `MainThreadRequired = true` (default) marshals to the main thread — required for every Unity API.
* `Unity.Pipeline` (models, `CommandRegistry`) compiles only under `UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_RUNTIME_PIPELINE`;
  the attributes assembly compiles everywhere.

```csharp
// MultiTravel.Editor asmdef: add "Unity.Pipeline.Attributes" to references and
// "versionDefines": [{ "name": "com.unity.pipeline", "expression": "", "define": "MT_UNITY_PIPELINE" }]
#if MT_UNITY_PIPELINE
using Unity.Pipeline.Commands;
#endif
namespace MultiTravel.EditorTools
{
    public static class CliCommands
    {
#if MT_UNITY_PIPELINE
        [CliCommand("mt_build_windows", "Build the Windows x64 player", Tags = new[] { "build" })]
#endif
        public static object BuildWindows(
#if MT_UNITY_PIPELINE
            [CliArg("output", "Output .exe path", Required = true)]
#endif
            string output,
#if MT_UNITY_PIPELINE
            [CliArg("development", "Development build")]
#endif
            bool development = false)
        {
            return new { ok = BuildScript.BuildWindowsTo(output, development), output };
        }
    }
}
// Invocation: unity command mt_build_windows --output Build/Windows/MultiTravelValizChallenge.exe --development true
```

### 6.2 `eval` / `eval_file` / `run_script`

* `eval --code "<C#>" [--timeout 5000]` (`Runtime/Commands/CodeEvalCommand.cs`, tag `scripts/eval`,
  available on editor and runtime servers). The code is wrapped by `EvalCodeCompiler.BuildSourceCode` as
  `namespace PipelineEvaluation { public static class PipelineEval_<id> { public static object Execute() { <code> return null; } } }`
  with `using System; System.Collections.Generic; System.Linq; UnityEngine;` (+ `UnityEditor` in the editor).
  So statements run as-is and `return <expr>;` yields the result (`EvalResponse`). `eval_file --file X.cs` runs a file through the same path.
* `run_script --file <path> [--entry Type.Method] [--args '[…]'] [--dry_run true] [--defines …] [--mode ephemeral|hotpatch] [--timeout_ms 30000]`:
  compiles one project `.cs` file in memory (relative to the project root, may live outside `Assets/`)
  and runs a static entry point; async `Task`/`Task<T>` entries are awaited. Preferred over long `eval` strings.

### 6.3 Built-in commands (names)

Assets/files: `create_asset`, `import_asset`, `move_asset`, `copy_asset`, `rename_asset`, `delete_asset`,
`find_assets`, `set_import_settings`, `get_import_settings`, `create_folder`, `read_text_file`, `write_text_file`.
Scenes: `create_scene`, `open_scene`, `save_scene`, `save_all`, `list_open_scenes`, `set_active_scene`,
`get_scene_hierarchy`, `add_scene_to_build`, `remove_scene_from_build`.
GameObjects: `create_gameobject`, `create_gameobjects`, `find_gameobjects`, `set_transform`, `set_parent`,
`set_active`, `set_tag`, `set_layer`, `rename_gameobject`, `delete_gameobject`, `add_component`,
`remove_component`, `get_component_properties`, `set_component_properties`.
Prefabs: `create_prefab`, `instantiate_prefab`, `create_prefab_variant`, `apply_prefab_overrides`,
`revert_prefab_overrides`, `unpack_prefab`, `save_prefab_contents`.
Scripts: `create_script`, `attach_script`, `set_serialized_field`, `get_serialized_fields`, `run_script`.
Animation: `create_animation_clip`, `set_animation_curve`, `get_animation_clip`, `remove_animation_curve`,
`create_animator_controller`, `add_animator_parameter`, `add_animator_layer`, `add_animator_state`,
`add_animator_transition`, `get_animator_controller`, `create_timeline`, `add_timeline_track`, `add_timeline_clip`, `get_timeline`.
Materials: `get_material_properties`, `set_material_properties`, `list_shaders`, `get_shader_properties`.
Baking: `bake_lighting`, `lighting_bake_status`, `cancel_lighting_bake`, `clear_baked_lighting`,
`get_lighting_settings`, `set_lighting_settings`, `bake_navmesh`, `navmesh_bake_status`, `cancel_navmesh_bake`,
`clear_navmesh`, `get_navmesh_settings`, `set_navmesh_settings`, `bake_navmesh_surfaces`,
`bake_occlusion_culling`, `occlusion_bake_status`, `cancel_occlusion_bake`, `clear_occlusion_culling`.
Navigation/capture: `get_selection`, `set_selection`, `search`, `capture_game_view`, `capture_scene_view`,
`capture_editor_element`, `capture_runtime_element`.
Build/tests: `build`, `build_status`, `switch_build_target`, `switch_build_target_status`, `list_build_targets`,
`get_build_settings`, `set_build_settings`, `list_build_profiles`, `recompile`, `recompile_status`,
`list_tests`, `run_tests`, `test_status`, `cancel_tests`.
Project settings: `get_/set_audio_settings`, `get_/set_graphics_settings`, `get_/set_input_settings`,
`get_/set_physics_settings`, `get_/set_player_settings`, `get_/set_quality_settings`, `get_/set_tags_layers`, `get_/set_time_settings`.
Packages: `package_list`, `package_search`, `package_add`, `package_remove`, `package_resolve`, `package_status`.
Editor/observability: `editor_play`, `editor_stop`, `editor_pause`, `editor_status`, `editor_focus`, `menu`,
`screenshot`, `set_autotick`, `get_performance_stats`, `audit`, `audit_status`, `report_evals`,
`get_authoring_root`, `set_authoring_root`, `batch`, `wait_for*` (see `commands/wait.md`).
Runtime (player): `runtime_status`, `quit`, `set_target_framerate`, `set_timescale`, `simulate_key`,
`simulate_pointer`, `log`, `console`, `console_status`, `clear_console`, `eval`, `eval_file`, `reload_file`,
`reload_file_editor_interpreter`, `reload_file_player_interpreter`, `codereload_status`, `cleanup_codereload`.

Mutating built-ins require `--confirm true` (and support `--dry_run true`).

---

## 7. TextMeshPro and Turkish glyphs

| Asset | `m_AtlasPopulationMode` | Atlas | Sampling | Chars | Turkish coverage |
|---|---|---|---|---|---|
| `Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular SDF.asset` | `0` = `Static` | 512×512, padding 5, `m_AtlasRenderMode 4165` (`GlyphRenderMode.SDFAA`) | 70 pt | 99 (ASCII 32–126 + 3 symbols) | **none** of ş ğ ı İ ç ö ü Ş Ğ Ç Ö Ü; `m_SourceFontFile {fileID: 0}` (only `m_SourceFontFileGUID c2fdaab1…`), `m_FallbackFontAssetTable []` |
| `…/Inter-Regular SDF NoBackfaceCulling.asset` | `0` Static | same | 70 | 99 | same as above |
| `Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset` | `0` Static | 1024×1024, padding 9, `4169` (`SDFAA_HINTED`) | 86 pt | 250 | ç ö ü Ç Ö Ü **yes**; ş ğ ı İ Ş Ğ **no**; fallback table → `LiberationSans SDF - Fallback` |
| `…/LiberationSans SDF - Fallback.asset` | `1` = `Dynamic` | 512 multi-atlas, `m_ClearDynamicDataOnBuild 1` | 86 | 0 (empty) | adds glyphs at runtime from `LiberationSans.ttf` (`m_SourceFontFile` set) |

Source fonts: `Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular.ttf` (2529 cmap codepoints) and
`Assets/TextMesh Pro/Fonts/LiberationSans.ttf` (2294) **both contain all twelve Turkish codepoints**
(verified from their `cmap` tables). `TMP Settings.asset`: default font = LiberationSans SDF,
`m_defaultFontSize 36`, `m_fallbackFontAssets []`, `m_GetFontFeaturesAtRuntime 1`.

Conclusion: the template's Inter asset cannot render Turkish; LiberationSans only works through the dynamic
fallback (and then with the fallback's own material). To guarantee rendering, `ContentGenerator` creates a
**dynamic** font asset from the project TTF (ARCHITECTURE §8 says LiberationSans, Inter is equally valid
and matches the template look) and pre-adds the Turkish characters:

```csharp
// com.unity.ugui 2.0.0 — TMP_FontAsset.cs line 556 (exact overload):
// public static TMP_FontAsset CreateFontAsset(Font font, int samplingPointSize, int atlasPadding,
//     GlyphRenderMode renderMode, int atlasWidth, int atlasHeight,
//     AtlasPopulationMode atlasPopulationMode = AtlasPopulationMode.Dynamic, bool enableMultiAtlasSupport = true)
// Others: CreateFontAsset(Font font) → (font, 90, 9, SDFAA, 1024, 1024); CreateFontAsset(string familyName, string styleName, int pointSize = 90) → DynamicOS;
//         CreateFontAsset(string fontFilePath, int faceIndex, int samplingPointSize, int atlasPadding, GlyphRenderMode renderMode, int atlasWidth, int atlasHeight)
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

const string k_TurkishSample = "ABCÇDEFGĞHIİJKLMNOÖPRSŞTUÜVYZQWXabcçdefgğhıijklmnoöprsştuüvyzqwx0123456789 .,:;!?-+%/()'\"";

static TMP_FontAsset EnsureTurkishFont(string ttfPath, string assetPath)
{
    var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
    if (existing != null && existing.atlasPopulationMode == AtlasPopulationMode.Dynamic) { existing.TryAddCharacters(k_TurkishSample); EditorUtility.SetDirty(existing); return existing; }

    var font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
    var fontAsset = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
    fontAsset.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
    fontAsset.material.name = fontAsset.name + " Material";
    fontAsset.atlasTextures[0].name = fontAsset.name + " Atlas";
    AssetDatabase.CreateAsset(fontAsset, assetPath);
    AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);          // material and atlas must be sub-assets
    AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
    fontAsset.TryAddCharacters(k_TurkishSample);                            // pre-bake so the first frame has no glyph stalls
    EditorUtility.SetDirty(fontAsset);
    AssetDatabase.SaveAssetIfDirty(fontAsset);
    return fontAsset;
}
```

Runtime guard in the HUD: `fontAsset.HasCharacters(text, out uint[] missing, searchFallbacks: true, tryAddCharacter: true)`.
`CreateFontAsset(Font,…)` with `Dynamic` sets `sourceFontFile = font` (the TTF is then included in builds);
with `Static` it only stores the editor reference. Keep `isMultiAtlasTexturesEnabled = true` and leave
`clearDynamicDataOnBuild` false so the baked Turkish glyphs ship in the player.

---

## 8. URP configuration

* `ProjectSettings/GraphicsSettings.asset`: `m_CustomRenderPipeline` → `Assets/Settings/Project Configuration/Performance URP Config.asset`
  (GUID `fd42a132493b1f143a4963adcd530ef1`); `m_RenderPipelineGlobalSettingsMap` → `Assets/Settings/Project Configuration/UniversalRenderPipelineGlobalSettings.asset`.
* `ProjectSettings/QualitySettings.asset` (`m_CurrentQuality 1`, `m_PerPlatformDefaultQuality.Standalone: 5`, `Android: 1`):

| Index | Name | `customRenderPipeline` | `antiAliasing` | `shadows` | `vSyncCount` |
|---|---|---|---|---|---|
| 0 | Very Low | Performance URP Config | 0 | 0 | 0 |
| 1 | Low | Performance URP Config | 4 | 0 | 0 |
| 2 | Medium | `{fileID: 0}` → GraphicsSettings default (Performance) | 0 | 1 | 1 |
| 3 | High | `{fileID: 0}` → default (Performance) | 0 | 2 | 1 |
| 4 | Very High | `{fileID: 0}` → default (Performance) | 2 | 2 | 1 |
| 5 | **Ultra (Standalone default)** | **Quality URP Config** (GUID `cf6a652e94858004a93399f0a3dac507`) | 4 | 2 | 1 |

URP assets (`UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset`):

| Field | Performance URP Config | Quality URP Config |
|---|---|---|
| `m_RendererDataList[0]` | `Android Preset.asset` | `Standalone Preset.asset` |
| `m_SupportsHDR` | 0 | **1** |
| `m_MSAA` | 4 (`MsaaQuality._4x`) | 4 |
| `m_RenderScale` / `m_UpscalingFilter` | 1 / 0 | 1 / 0 |
| `m_RequireDepthTexture` / `m_RequireOpaqueTexture` | 0 / 0 | 0 / 0 |
| `m_MainLightRenderingMode` (`LightRenderingMode { Disabled=0, PerPixel=1, PerVertex=2 }`) | 1 | 1 |
| `m_MainLightShadowsSupported` / `m_MainLightShadowmapResolution` | 1 / 4096 | 1 / 4096 |
| `m_AdditionalLightsRenderingMode` / `m_AdditionalLightsPerObjectLimit` / shadows | 0 / 1 / 0 | 1 / 4 / 0 |
| `m_ShadowDistance` / `m_ShadowCascadeCount` / `m_SoftShadowsSupported` | 2.5 / 1 / 1 | 10 / 1 / 1 |
| `m_UseSRPBatcher` / `m_SupportsDynamicBatching` / `m_UseAdaptivePerformance` | 1 / 0 / 1 | 1 / 0 / 1 |
| `m_ColorGradingMode` / `m_HDRColorBufferPrecision` | 0 / 0 | 0 / 0 |

**`Assets/Settings/Project Configuration/Standalone Preset.asset` is not a `Preset`**: it is a
`UnityEngine.Rendering.Universal.UniversalRendererData` (script GUID `de640fe3d0db1804a85f9fc8f5cadab6`)
with `m_RenderingMode 0` (`RenderingMode.Forward`; `Deferred = 1`, `ForwardPlus = 2`), `m_UseNativeRenderPass 0`,
`m_DepthPrimingMode 0`, `m_RendererFeatures []`, `postProcessData` assigned, `xrSystemData` assigned. It is the
renderer used by `Quality URP Config` (Standalone/Ultra). `Android Preset.asset` is the sibling renderer for
`Performance URP Config` (`m_UseNativeRenderPass 1`, `postProcessData {fileID: 0}`). The template names them
"presets" only in the sense of per-platform renderer presets.

Practical consequences: on Windows the player runs **Ultra → Quality URP Config → Standalone Preset
renderer** (HDR on, MSAA 4×, per-pixel additional lights). `SceneGenerator` should set
`QualitySettings.SetQualityLevel` only if a different level is wanted; the generator's materials must use
`Universal Render Pipeline/Lit`.

---

## 9. Input

* `ProjectSettings/ProjectSettings.asset`: `activeInputHandler: 1` → **Input System Package (New)** only
  (`0` = Input Manager (Old), `2` = Both). uGUI `StandaloneInputModule` therefore does not work; use
  `XRUIInputModule` (XRI) or `InputSystemUIInputModule`.
* Other player settings already set: `companyName: ECR Etkinlik Bilgisayar`, `productName: MultiTravel Packing Challenge`,
  `bundleVersion: 1.0.0`, `fullscreenMode: 1` (FullScreenWindow), `runInBackground: 1`, `resizableWindow: 0`,
  `visibleInBackground: 1`, `allowFullscreenSwitch: 1`, `defaultScreenWidth/Height 1024×768`,
  `scriptingBackend: { Android: 1 (IL2CPP), Standalone: 0 (Mono) }`, `apiCompatibilityLevel: 6`, `gcIncremental: 1`,
  `m_StereoRenderingPath: 0`. Build list (`EditorBuildSettings.asset`): only `Assets/Scenes/SampleScene.unity` (GUID `55daccc09a3b69647bbab145b54a3ab3`).
* XRI input actions asset used by the rig: `Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/XRI Default Input Actions.inputactions` (§1.9).
* Project-wide actions asset referenced by `com.unity.input.settings.actions`: GUID `985fdffee33a5944396199c6b7145dd4`.
* Keyboard/mouse UI navigation in the template rig's scene: the SampleScene `EventSystem` has
  `m_sendNavigationEvents 1` but `m_EnableMouseInput 0`, `m_EnableGamepadInput 0`, `m_EnableJoystickInput 0`
  (XR pointers only; keyboard navigation goes through the `XRI UI/Navigate|Submit|Cancel` actions which are
  bound to keyboard/gamepad in the asset). For the operator overlay set `enableMouseInput = true` (§1.6).

---

## 10. Scene / asset authoring APIs in Unity 6000.4

All members below were confirmed in `UnityEditor.CoreModule.xml` / `UnityEngine.CoreModule.xml` of the
installed editor and (where noted) exercised by `com.unity.pipeline`'s own commands.

| Purpose | API (exact) | Notes |
|---|---|---|
| New scene | `EditorSceneManager.NewScene(NewSceneSetup setup, NewSceneMode mode)` → `Scene` | `NewSceneSetup.EmptyScene` (no camera/light) or `DefaultGameObjects`; `NewSceneMode.Single`/`Additive`. Used by `create_scene`. |
| Save / open | `EditorSceneManager.SaveScene(Scene scene, string dstScenePath, bool saveAsCopy = false)` → `bool`; `SaveScenes(Scene[])`; `OpenScene(string, OpenSceneMode)`; `MarkSceneDirty(Scene)`; `SaveOpenScenes()` | Call `AssetDatabase.Refresh()` after creating new scene files. |
| Build list | `EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/MultiTravel/Scenes/Bootstrap.unity", true), … }` | Assigning the array replaces the list (drops `SampleScene`). `EditorBuildSettingsScene(string path, bool enabled)`. |
| Prefab instantiate | `PrefabUtility.InstantiatePrefab(Object assetComponentOrGameObject)` and `InstantiatePrefab(Object, Scene destinationScene)` → `Object` (cast to `GameObject`) | Keeps the prefab link (needed for the rig variant). `Undo.RegisterCreatedObjectUndo` optional in batch. |
| Prefab save | `PrefabUtility.SaveAsPrefabAsset(GameObject instanceRoot, string assetPath)` / `(…, out bool success)`; `SaveAsPrefabAssetAndConnect(GameObject, string, InteractionMode.AutomatedAction[, out bool])`; `LoadPrefabContents(string)` + `UnloadPrefabContents(GameObject)`; `ApplyPrefabInstance`, `RevertPrefabInstance`, `UnpackPrefabInstance` | Saving to an existing path overwrites in place and **keeps the GUID** (idempotent generators). |
| Assets | `AssetDatabase.CreateAsset(Object, string)`, `AddObjectToAsset(Object objectToAdd, Object assetObject)` / `AddObjectToAsset(Object, string path)`, `SaveAssetIfDirty(Object)` / `SaveAssetIfDirty(GUID)`, `SaveAssets()`, `ImportAsset(string)`, `CreateFolder(parent, name)`, `LoadAssetAtPath<T>`, `LoadMainAssetAtPath`, `FindAssets`, `GUIDToAssetPath`, `AssetPathToGUID`, `DeleteAsset`, `MoveAsset`, `CopyAsset`, `RenameAsset` | For regenerable meshes/materials, load the existing asset and copy data into it (`EditorUtility.CopySerialized` or direct field writes) instead of recreating, to keep GUIDs stable. |
| Static flags | `GameObjectUtility.SetStaticEditorFlags(GameObject, StaticEditorFlags)` | |
| Lighting (replaces `LightmapEditorSettings`) | `Lightmapping.lightingSettings` (get/set `LightingSettings`), `Lightmapping.GetLightingSettingsForScene(Scene)`, `SetLightingSettingsForScene(Scene, LightingSettings)`, `SetLightingSettingsForScenes(Scene[], LightingSettings)`, `Lightmapping.Bake()`, `BakeAsync()`, `isRunning`, `bakeCompleted`, `Cancel()`, `Clear()`, `ClearDiskCache()` | **`UnityEditor.LightmapEditorSettings` is obsolete** (XML: "This class is now obsolete. Use LightingSettings."). Create `new LightingSettings()`, `AssetDatabase.CreateAsset(ls, "Assets/MultiTravel/Generated/MainLighting.lighting")`, then `SetLightingSettingsForScene`. `LightingSettings.realtimeGI` and `StaticOcclusionCulling` become obsolete in 6000.7+ (per the pipeline changelog) — avoid both. |
| Ambient / environment | `RenderSettings.ambientMode` (`UnityEngine.Rendering.AmbientMode.Skybox|Trilight|Flat|Custom`), `ambientLight`, `ambientSkyColor`, `ambientEquatorColor`, `ambientGroundColor`, `ambientIntensity`, `skybox`, `defaultReflectionMode`, `reflectionIntensity` | Apply after the scene is active (`EditorSceneManager.SetActiveScene` / `NewScene` single) and before `SaveScene`. |
| Build | `BuildPipeline.BuildPlayer(BuildPlayerOptions)` → `BuildReport` (`report.summary.result == BuildResult.Succeeded`); `BuildOptions.Development`; `EditorUserBuildSettings.SwitchActiveBuildTarget(NamedBuildTarget.Standalone, BuildTarget.StandaloneWindows64)`; `PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x)`; `PlayerSettings.productName/companyName/bundleVersion/fullScreenMode/runInBackground/resizableWindow`; `EditorApplication.Exit(int)` | IL2CPP module is not installed; keep Mono. |
| Samples | `UnityEditor.PackageManager.UI.Sample.FindByPackage(string packageName, string packageVersion)` → `IEnumerable<Sample>`; `Sample.Import(Sample.ImportOptions)` (`None`, `OverridePreviousImports`, `HideImportWindow`); `isImported`, `importPath`, `resolvedPath` | |
| Async networking | `UnityEngine.Awaitable`, `Awaitable.FromAsyncOperation(AsyncOperation, CancellationToken)`, `Awaitable.NextFrameAsync`, `Awaitable.WaitForSecondsAsync(float, CancellationToken)`; `UnityWebRequest.SendWebRequest()` is directly awaitable | For `SupabaseBackendClient`. |

Minimal idempotent scene generation pattern:

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

static void BuildMainScene(string scenePath, GameObject rigPrefab)
{
    Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);        // keeps the variant link
    rig.name = "XR Origin (XR Rig)";
    new GameObject("XR Interaction Manager").AddComponent<UnityEngine.XR.Interaction.Toolkit.XRInteractionManager>();
    var es = new GameObject("EventSystem");
    es.AddComponent<UnityEngine.EventSystems.EventSystem>();
    var module = es.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule>();
    module.enableMouseInput = true;                                                    // operator screen
    RenderSettings.ambientMode = AmbientMode.Flat;
    RenderSettings.ambientLight = new Color(0.55f, 0.57f, 0.62f);
    var ls = new LightingSettings { name = "MainLighting" };
    AssetDatabase.CreateAsset(ls, "Assets/MultiTravel/Generated/MainLighting.lighting");
    Lightmapping.SetLightingSettingsForScene(scene, ls);
    EditorSceneManager.SaveScene(scene, scenePath);
    EditorBuildSettings.scenes = new[]
    {
        new EditorBuildSettingsScene("Assets/MultiTravel/Scenes/Bootstrap.unity", true),
        new EditorBuildSettingsScene(scenePath, true),
    };
    AssetDatabase.SaveAssets();
}
```

---

## 11. Findings that differ from ARCHITECTURE.md

1. **OpenXR Standalone**: `Hand Interaction Profile` and `Meta Quest Touch Pro Controller Profile` are
   disabled; render mode is `MultiPass`. §3 of the architecture expects both profiles enabled — enable them
   through `FeatureHelpers` (§4) in `ProjectValidator`/`BuildScript`, and consider `SinglePassInstanced`.
2. **No `XRInteractionManager` and no `EventSystem` in the rig prefab**; `SampleScene` has an
   `EventSystem` with mouse input disabled and no manager (auto-created). `Main.unity` must create both.
3. **XR Interaction Simulator sample is not imported**; the settings asset has auto-instantiate off and no prefab.
4. **Inter SDF is a static ASCII-only atlas** — Turkish text will show missing glyph boxes; generate a
   dynamic font asset (§7).
5. **Standalone default quality is `Ultra` → `Quality URP Config` → `Standalone Preset` renderer**
   (HDR, MSAA 4×); `Standalone Preset.asset` is a renderer-data asset, not a `Preset`.
6. **`Hands Permissions Manager` is Android-only** (`#if UNITY_ANDROID`); it is harmless but useless on Windows.
7. **`[CliCommand]` lives in assembly `Unity.Pipeline.Attributes` / namespace `Unity.Pipeline.Commands`**
   (not `Unity.Pipeline`); the Editor asmdef needs that reference plus a `versionDefines` guard so the code
   compiles when the package is absent.
8. `activeInputHandler` is already `1` (Input System only) — no "Both" needed; the template rig works with it.
