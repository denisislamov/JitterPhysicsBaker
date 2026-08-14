#!/usr/bin/env python3
"""Author the committed demo scenes from Server/demo-levels.json.

The scenes are real, committed Unity assets: you open them, edit them and bake them like any
other scene. They are written by a tool rather than typed by hand for the same reason a level
designer uses the editor rather than a text buffer - a 2500-line YAML file with hundreds of
cross-referenced fileIDs is not something a person edits correctly by hand. The output is checked
in; this tool only has to be re-run when the level definitions change.

The emitted YAML mirrors, block for block, a scene Unity itself produced (JitterDemoArena.unity),
so the structure is known-good rather than guessed. Every geometry object carries the collider the
baker reads and a `JitterStaticBodySource`; a `JitterPhysicsLevel` host points at their shared
root and at the world profile asset.

Usage:
    python3 tools/author-demo-scenes.py

Idempotent: it overwrites the generated scenes and leaves everything else alone.
"""

from __future__ import annotations

import hashlib
import json
import math
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
LEVELS_JSON = REPO / "Server" / "demo-levels.json"
SCENES_DIR = REPO / "Assets" / "JitterPhysicsBaker" / "Demo" / "Scenes"

# Stable references into project assets, read once from the known-good scene and its meta files.
LEVEL_SCRIPT_GUID = "af1ec471b187a412dbf4ad29e650afd4"        # JitterPhysicsLevel.cs
SOURCE_SCRIPT_GUID = "db661d0a2ecfa4d6b895b4a1fe53b1c1"       # JitterStaticBodySource.cs
WORLD_PROFILE_GUID = "16042d3c4508446d6947886a550df38c"       # JitterDemoWorldProfile.asset
MATERIAL_GUID = "2a59cd3870e6c478db09ccad2313e0ed"           # JitterDemoSurface.mat

# Built-in primitive meshes live in Unity's default resources; these fileIDs are stable.
MESH_CUBE = 10202
MESH_SPHERE = 10207
MESH_CAPSULE = 10208

GENERATED_FOLDER = "Assets/Generated/JitterPhysics"


class Ids:
    """Hands out unique fileIDs within one scene."""

    def __init__(self) -> None:
        self._next = 100_000_000

    def take(self) -> int:
        value = self._next
        self._next += 1
        return value


def quat_from_euler(x: float, y: float, z: float) -> tuple[float, float, float, float]:
    """Euler degrees to a quaternion, composed y * x * z.

    The convention only has to agree with the seed artifact generator, which uses the same
    composition, because Unity bakes the quaternion this writes into `m_LocalRotation` directly and
    never re-derives it from the euler hint. Single-axis rotations - the common case here - reduce
    to the obvious half-angle form regardless.
    """

    def axis(sin_half: float, ax: float, ay: float, az: float, cos_half: float):
        return (ax * sin_half, ay * sin_half, az * sin_half, cos_half)

    rx = math.radians(x) * 0.5
    ry = math.radians(y) * 0.5
    rz = math.radians(z) * 0.5

    qx = axis(math.sin(rx), 1.0, 0.0, 0.0, math.cos(rx))
    qy = axis(math.sin(ry), 0.0, 1.0, 0.0, math.cos(ry))
    qz = axis(math.sin(rz), 0.0, 0.0, 1.0, math.cos(rz))

    return mul(mul(qy, qx), qz)


def mul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (
        aw * bx + ax * bw + ay * bz - az * by,
        aw * by - ax * bz + ay * bw + az * bx,
        aw * bz + ax * by - ay * bx + az * bw,
        aw * bw - ax * bx - ay * by - az * bz,
    )


def f(value: float) -> str:
    """Formats a float the way Unity does: trim trailing zeros, keep it compact."""
    if value == int(value):
        return str(int(value))
    return repr(round(value, 6)).rstrip("0").rstrip(".")


def scene_guid(level_id: str) -> str:
    return hashlib.md5(f"jitter-demo-scene::{level_id}".encode()).hexdigest()


def geometry_of(body: dict):
    """Returns (mesh_fileID, local_scale, collider_yaml) for a body's shape."""
    shape = body["shape"]

    if shape == "box":
        sx, sy, sz = body["size"]
        collider = (
            "--- !u!65 &{cid}\n"
            "BoxCollider:\n"
            "  m_ObjectHideFlags: 0\n"
            "  m_CorrespondingSourceObject: {{fileID: 0}}\n"
            "  m_PrefabInstance: {{fileID: 0}}\n"
            "  m_PrefabAsset: {{fileID: 0}}\n"
            "  m_GameObject: {{fileID: {go}}}\n"
            "  m_Material: {{fileID: 0}}\n"
            "  m_IncludeLayers:\n    serializedVersion: 2\n    m_Bits: 0\n"
            "  m_ExcludeLayers:\n    serializedVersion: 2\n    m_Bits: 0\n"
            "  m_LayerOverridePriority: 0\n"
            "  m_IsTrigger: 0\n"
            "  m_ProvidesContacts: 0\n"
            "  m_Enabled: 1\n"
            "  serializedVersion: 3\n"
            "  m_Size: {{x: 1, y: 1, z: 1}}\n"
            "  m_Center: {{x: 0, y: 0, z: 0}}\n"
        )
        return MESH_CUBE, (sx, sy, sz), collider

    if shape == "sphere":
        r = body["radius"]
        collider = (
            "--- !u!135 &{cid}\n"
            "SphereCollider:\n"
            "  m_ObjectHideFlags: 0\n"
            "  m_CorrespondingSourceObject: {{fileID: 0}}\n"
            "  m_PrefabInstance: {{fileID: 0}}\n"
            "  m_PrefabAsset: {{fileID: 0}}\n"
            "  m_GameObject: {{fileID: {go}}}\n"
            "  m_Material: {{fileID: 0}}\n"
            "  m_IncludeLayers:\n    serializedVersion: 2\n    m_Bits: 0\n"
            "  m_ExcludeLayers:\n    serializedVersion: 2\n    m_Bits: 0\n"
            "  m_LayerOverridePriority: 0\n"
            "  m_IsTrigger: 0\n"
            "  m_ProvidesContacts: 0\n"
            "  m_Enabled: 1\n"
            "  serializedVersion: 3\n"
            "  m_Radius: 0.5\n"
            "  m_Center: {{x: 0, y: 0, z: 0}}\n"
        )
        d = 2.0 * r
        return MESH_SPHERE, (d, d, d), collider

    if shape == "capsule":
        s = body["scale"]
        collider = (
            "--- !u!136 &{cid}\n"
            "CapsuleCollider:\n"
            "  m_ObjectHideFlags: 0\n"
            "  m_CorrespondingSourceObject: {{fileID: 0}}\n"
            "  m_PrefabInstance: {{fileID: 0}}\n"
            "  m_PrefabAsset: {{fileID: 0}}\n"
            "  m_GameObject: {{fileID: {go}}}\n"
            "  m_Material: {{fileID: 0}}\n"
            "  m_IncludeLayers:\n    serializedVersion: 2\n    m_Bits: 0\n"
            "  m_ExcludeLayers:\n    serializedVersion: 2\n    m_Bits: 0\n"
            "  m_LayerOverridePriority: 0\n"
            "  m_IsTrigger: 0\n"
            "  m_ProvidesContacts: 0\n"
            "  m_Enabled: 1\n"
            "  serializedVersion: 2\n"
            "  m_Radius: 0.5\n"
            "  m_Height: 2\n"
            "  m_Direction: 1\n"
            "  m_Center: {{x: 0, y: 0, z: 0}}\n"
        )
        return MESH_CAPSULE, (s, s, s), collider

    raise ValueError(f"unknown shape '{shape}'")


HEADER = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!29 &1
OcclusionCullingSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_OcclusionBakeSettings:
    smallestOccluder: 5
    smallestHole: 0.25
    backfaceThreshold: 100
  m_SceneGUID: 00000000000000000000000000000000
  m_OcclusionCullingData: {fileID: 0}
--- !u!104 &2
RenderSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 10
  m_Fog: 0
  m_FogColor: {r: 0.5, g: 0.5, b: 0.5, a: 1}
  m_FogMode: 3
  m_FogDensity: 0.01
  m_LinearFogStart: 0
  m_LinearFogEnd: 300
  m_AmbientSkyColor: {r: 0.212, g: 0.227, b: 0.259, a: 1}
  m_AmbientEquatorColor: {r: 0.114, g: 0.125, b: 0.133, a: 1}
  m_AmbientGroundColor: {r: 0.047, g: 0.043, b: 0.035, a: 1}
  m_AmbientIntensity: 1
  m_AmbientMode: 0
  m_SubtractiveShadowColor: {r: 0.42, g: 0.478, b: 0.627, a: 1}
  m_SkyboxMaterial: {fileID: 10304, guid: 0000000000000000f000000000000000, type: 0}
  m_HaloStrength: 0.5
  m_FlareStrength: 1
  m_FlareFadeSpeed: 3
  m_HaloTexture: {fileID: 0}
  m_SpotCookie: {fileID: 10001, guid: 0000000000000000e000000000000000, type: 0}
  m_DefaultReflectionMode: 0
  m_DefaultReflectionResolution: 128
  m_ReflectionBounces: 1
  m_ReflectionIntensity: 1
  m_CustomReflection: {fileID: 0}
  m_Sun: {fileID: 0}
  m_UseRadianceAmbientProbe: 0
--- !u!157 &3
LightmapSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 13
  m_BakeOnSceneLoad: 0
  m_GISettings:
    serializedVersion: 2
    m_BounceScale: 1
    m_IndirectOutputScale: 1
    m_AlbedoBoost: 1
    m_EnvironmentLightingMode: 0
    m_EnableBakedLightmaps: 1
    m_EnableRealtimeLightmaps: 0
  m_LightmapEditorSettings:
    serializedVersion: 12
    m_Resolution: 2
    m_BakeResolution: 40
    m_AtlasSize: 1024
    m_AO: 0
    m_AOMaxDistance: 1
    m_CompAOExponent: 1
    m_CompAOExponentDirect: 0
    m_ExtractAmbientOcclusion: 0
    m_Padding: 2
    m_LightmapParameters: {fileID: 0}
    m_LightmapsBakeMode: 1
    m_TextureCompression: 1
    m_ReflectionCompression: 2
    m_MixedBakeMode: 2
    m_BakeBackend: 2
    m_PVRSampling: 1
    m_PVRDirectSampleCount: 32
    m_PVRSampleCount: 512
    m_PVRBounces: 2
    m_PVREnvironmentSampleCount: 256
    m_PVREnvironmentReferencePointCount: 2048
    m_PVRFilteringMode: 1
    m_PVRDenoiserTypeDirect: 1
    m_PVRDenoiserTypeIndirect: 1
    m_PVRDenoiserTypeAO: 1
    m_PVRFilterTypeDirect: 0
    m_PVRFilterTypeIndirect: 0
    m_PVRFilterTypeAO: 0
    m_PVREnvironmentMIS: 1
    m_PVRCulling: 1
    m_PVRFilteringGaussRadiusDirect: 1
    m_PVRFilteringGaussRadiusIndirect: 1
    m_PVRFilteringGaussRadiusAO: 1
    m_PVRFilteringAtrousPositionSigmaDirect: 0.5
    m_PVRFilteringAtrousPositionSigmaIndirect: 2
    m_PVRFilteringAtrousPositionSigmaAO: 1
    m_ExportTrainingData: 0
    m_TrainingDataDestination: TrainingData
    m_LightProbeSampleCountMultiplier: 4
  m_LightingDataAsset: {fileID: 0}
  m_LightingSettings: {fileID: 0}
--- !u!196 &4
NavMeshSettings:
  serializedVersion: 2
  m_ObjectHideFlags: 0
  m_BuildSettings:
    serializedVersion: 3
    agentTypeID: 0
    agentRadius: 0.5
    agentHeight: 2
    agentSlope: 45
    agentClimb: 0.4
    ledgeDropHeight: 0
    maxJumpAcrossDistance: 0
    minRegionArea: 2
    manualCellSize: 0
    cellSize: 0.16666667
    manualTileSize: 0
    tileSize: 256
    buildHeightMesh: 0
    maxJobWorkers: 0
    preserveTilesOutsideBounds: 0
    debug:
      m_Flags: 0
  m_NavMeshData: {fileID: 0}
"""


def mesh_renderer(rid: int, go: int) -> str:
    return (
        f"--- !u!23 &{rid}\n"
        "MeshRenderer:\n"
        "  m_ObjectHideFlags: 0\n"
        "  m_CorrespondingSourceObject: {fileID: 0}\n"
        "  m_PrefabInstance: {fileID: 0}\n"
        "  m_PrefabAsset: {fileID: 0}\n"
        f"  m_GameObject: {{fileID: {go}}}\n"
        "  m_Enabled: 1\n"
        "  m_CastShadows: 1\n"
        "  m_ReceiveShadows: 1\n"
        "  m_DynamicOccludee: 1\n"
        "  m_StaticShadowCaster: 0\n"
        "  m_MotionVectors: 1\n"
        "  m_LightProbeUsage: 1\n"
        "  m_ReflectionProbeUsage: 1\n"
        "  m_RayTracingMode: 2\n"
        "  m_RayTraceProcedural: 0\n"
        "  m_RayTracingAccelStructBuildFlagsOverride: 0\n"
        "  m_RayTracingAccelStructBuildFlags: 1\n"
        "  m_SmallMeshCulling: 1\n"
        "  m_ForceMeshLod: -1\n"
        "  m_MeshLodSelectionBias: 0\n"
        "  m_RenderingLayerMask: 1\n"
        "  m_RendererPriority: 0\n"
        "  m_Materials:\n"
        f"  - {{fileID: 2100000, guid: {MATERIAL_GUID}, type: 2}}\n"
        "  m_StaticBatchInfo:\n    firstSubMesh: 0\n    subMeshCount: 0\n"
        "  m_StaticBatchRoot: {fileID: 0}\n"
        "  m_ProbeAnchor: {fileID: 0}\n"
        "  m_LightProbeVolumeOverride: {fileID: 0}\n"
        "  m_ScaleInLightmap: 1\n"
        "  m_ReceiveGI: 1\n"
        "  m_PreserveUVs: 1\n"
        "  m_IgnoreNormalsForChartDetection: 0\n"
        "  m_ImportantGI: 0\n"
        "  m_StitchLightmapSeams: 1\n"
        "  m_SelectedEditorRenderState: 3\n"
        "  m_MinimumChartSize: 4\n"
        "  m_AutoUVMaxDistance: 0.5\n"
        "  m_AutoUVMaxAngle: 89\n"
        "  m_LightmapParameters: {fileID: 0}\n"
        "  m_GlobalIlluminationMeshLod: 0\n"
        "  m_SortingLayerID: 0\n"
        "  m_SortingLayer: 0\n"
        "  m_SortingOrder: 0\n"
        "  m_MaskInteraction: 0\n"
        "  m_AdditionalVertexStreams: {fileID: 0}\n"
    )


def transform(tid: int, go: int, pos, rot, scale, father: int, children: list[int]) -> str:
    px, py, pz = pos
    rx, ry, rz, rw = rot
    scx, scy, scz = scale
    child_lines = "".join(f"  - {{fileID: {c}}}\n" for c in children)
    children_block = f"  m_Children:\n{child_lines}" if children else "  m_Children: []\n"
    return (
        f"--- !u!4 &{tid}\n"
        "Transform:\n"
        "  m_ObjectHideFlags: 0\n"
        "  m_CorrespondingSourceObject: {fileID: 0}\n"
        "  m_PrefabInstance: {fileID: 0}\n"
        "  m_PrefabAsset: {fileID: 0}\n"
        f"  m_GameObject: {{fileID: {go}}}\n"
        "  serializedVersion: 2\n"
        f"  m_LocalRotation: {{x: {f(rx)}, y: {f(ry)}, z: {f(rz)}, w: {f(rw)}}}\n"
        f"  m_LocalPosition: {{x: {f(px)}, y: {f(py)}, z: {f(pz)}}}\n"
        f"  m_LocalScale: {{x: {f(scx)}, y: {f(scy)}, z: {f(scz)}}}\n"
        "  m_ConstrainProportionsScale: 0\n"
        f"{children_block}"
        f"  m_Father: {{fileID: {father}}}\n"
        "  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}\n"
    )


def game_object(go: int, name: str, components: list[int]) -> str:
    comp_lines = "".join(f"  - component: {{fileID: {c}}}\n" for c in components)
    return (
        f"--- !u!1 &{go}\n"
        "GameObject:\n"
        "  m_ObjectHideFlags: 0\n"
        "  m_CorrespondingSourceObject: {fileID: 0}\n"
        "  m_PrefabInstance: {fileID: 0}\n"
        "  m_PrefabAsset: {fileID: 0}\n"
        "  serializedVersion: 6\n"
        "  m_Component:\n"
        f"{comp_lines}"
        "  m_Layer: 0\n"
        f"  m_Name: {name}\n"
        "  m_TagString: Untagged\n"
        "  m_Icon: {fileID: 0}\n"
        "  m_NavMeshLayer: 0\n"
        "  m_StaticEditorFlags: 0\n"
        "  m_IsActive: 1\n"
    )


def mesh_filter(mid: int, go: int, mesh: int) -> str:
    return (
        f"--- !u!33 &{mid}\n"
        "MeshFilter:\n"
        "  m_ObjectHideFlags: 0\n"
        "  m_CorrespondingSourceObject: {fileID: 0}\n"
        "  m_PrefabInstance: {fileID: 0}\n"
        "  m_PrefabAsset: {fileID: 0}\n"
        f"  m_GameObject: {{fileID: {go}}}\n"
        f"  m_Mesh: {{fileID: {mesh}, guid: 0000000000000000e000000000000000, type: 0}}\n"
    )


def source_component(sid: int, go: int, body: dict) -> str:
    return (
        f"--- !u!114 &{sid}\n"
        "MonoBehaviour:\n"
        "  m_ObjectHideFlags: 0\n"
        "  m_CorrespondingSourceObject: {fileID: 0}\n"
        "  m_PrefabInstance: {fileID: 0}\n"
        "  m_PrefabAsset: {fileID: 0}\n"
        f"  m_GameObject: {{fileID: {go}}}\n"
        "  m_Enabled: 1\n"
        "  m_EditorHideFlags: 0\n"
        f"  m_Script: {{fileID: 11500000, guid: {SOURCE_SCRIPT_GUID}, type: 3}}\n"
        "  m_Name: \n"
        "  m_EditorClassIdentifier: DataSakura.JitterPhysics.Authoring::DataSakura.JitterPhysics.Authoring.JitterStaticBodySource\n"
        f"  sourceId: {body['id']}\n"
        "  includeChildren: 1\n"
        f"  friction: {f(body.get('friction', 0.4))}\n"
        f"  restitution: {f(body.get('restitution', 0.0))}\n"
    )


def build_scene(level: dict, ids: Ids) -> str:
    body_transforms: list[int] = []
    blocks: list[str] = []

    geometry_go = ids.take()
    geometry_tf = ids.take()

    for body in level["bodies"]:
        go = ids.take()
        tf = ids.take()
        renderer = ids.take()
        collider = ids.take()
        mesh_f = ids.take()
        source = ids.take()

        mesh, scale, collider_tmpl = geometry_of(body)
        rot = quat_from_euler(*body.get("euler", [0, 0, 0]))

        blocks.append(game_object(go, body["id"], [tf, renderer, collider, mesh_f, source]))
        blocks.append(transform(tf, go, body["pos"], rot, scale, geometry_tf, []))
        blocks.append(source_component(source, go, body))
        blocks.append(mesh_renderer(renderer, go))
        blocks.append(collider_tmpl.format(cid=collider, go=go))
        blocks.append(mesh_filter(mesh_f, go, mesh))

        body_transforms.append(tf)

    # The empty root that every body hangs off, and that the level points at.
    blocks.insert(0, game_object(geometry_go, "Geometry", [geometry_tf]))
    blocks.insert(1, transform(
        geometry_tf, geometry_go, (0, 0, 0), (0, 0, 0, 1), (1, 1, 1), 0, body_transforms))

    # Camera and light, so the scene is something you can look at when you open it.
    cam_go = ids.take()
    cam_tf = ids.take()
    cam_cam = ids.take()
    blocks.append(game_object(cam_go, "Main Camera", [cam_tf, cam_cam]))
    blocks.append(transform(
        cam_tf, cam_go, (0, 18, -34), quat_from_euler(26, 0, 0), (1, 1, 1), 0, []))
    blocks.append(camera_component(cam_cam, cam_go))

    light_go = ids.take()
    light_tf = ids.take()
    light_light = ids.take()
    blocks.append(game_object(light_go, "Directional Light", [light_tf, light_light]))
    blocks.append(transform(
        light_tf, light_go, (0, 20, 0), quat_from_euler(50, -30, 0), (1, 1, 1), 0, []))
    blocks.append(light_component(light_light, light_go))

    # The level host, last, referencing the geometry root and the world profile asset.
    level_go = ids.take()
    level_tf = ids.take()
    level_mb = ids.take()
    blocks.append(game_object(level_go, level["displayName"] + " Level", [level_tf, level_mb]))
    blocks.append(level_component(level_mb, level_go, level["levelId"], geometry_tf))
    blocks.append(transform(level_tf, level_go, (0, 0, 0), (0, 0, 0, 1), (1, 1, 1), 0, []))

    roots = [geometry_tf, cam_tf, light_tf, level_tf]
    root_lines = "".join(f"  - {{fileID: {r}}}\n" for r in roots)
    scene_roots = (
        "--- !u!1660057539 &9223372036854775807\n"
        "SceneRoots:\n"
        "  m_ObjectHideFlags: 0\n"
        "  m_Roots:\n"
        f"{root_lines}"
    )

    return HEADER + "".join(blocks) + scene_roots


def camera_component(cid: int, go: int) -> str:
    return (
        f"--- !u!20 &{cid}\n"
        "Camera:\n"
        "  m_ObjectHideFlags: 0\n"
        "  m_CorrespondingSourceObject: {fileID: 0}\n"
        "  m_PrefabInstance: {fileID: 0}\n"
        "  m_PrefabAsset: {fileID: 0}\n"
        f"  m_GameObject: {{fileID: {go}}}\n"
        "  m_Enabled: 1\n"
        "  serializedVersion: 2\n"
        "  m_ClearFlags: 1\n"
        "  m_BackGroundColor: {r: 0.19, g: 0.22, b: 0.27, a: 0}\n"
        "  m_projectionMatrixMode: 1\n"
        "  m_GateFitMode: 2\n"
        "  m_FOVAxisMode: 0\n"
        "  m_Iso: 200\n"
        "  m_ShutterSpeed: 0.005\n"
        "  m_Aperture: 16\n"
        "  m_FocusDistance: 10\n"
        "  m_FocalLength: 50\n"
        "  m_BladeCount: 5\n"
        "  m_Curvature: {x: 2, y: 11}\n"
        "  m_BarrelClipping: 0.25\n"
        "  m_Anamorphism: 0\n"
        "  m_SensorSize: {x: 36, y: 24}\n"
        "  m_LensShift: {x: 0, y: 0}\n"
        "  m_NormalizedViewPortRect:\n    serializedVersion: 2\n    x: 0\n    y: 0\n    width: 1\n    height: 1\n"
        "  near clip plane: 0.3\n"
        "  far clip plane: 1000\n"
        "  field of view: 60\n"
        "  orthographic: 0\n"
        "  orthographic size: 5\n"
        "  m_Depth: -1\n"
        "  m_CullingMask:\n    serializedVersion: 2\n    m_Bits: 4294967295\n"
        "  m_RenderingPath: -1\n"
        "  m_TargetTexture: {fileID: 0}\n"
        "  m_TargetDisplay: 0\n"
        "  m_TargetEye: 3\n"
        "  m_HDR: 1\n"
        "  m_AllowMSAA: 1\n"
        "  m_AllowDynamicResolution: 0\n"
        "  m_ForceIntoRT: 0\n"
        "  m_OcclusionCulling: 1\n"
        "  m_StereoConvergence: 10\n"
        "  m_StereoSeparation: 0.022\n"
    )


def light_component(lid: int, go: int) -> str:
    return (
        f"--- !u!108 &{lid}\n"
        "Light:\n"
        "  m_ObjectHideFlags: 0\n"
        "  m_CorrespondingSourceObject: {fileID: 0}\n"
        "  m_PrefabInstance: {fileID: 0}\n"
        "  m_PrefabAsset: {fileID: 0}\n"
        f"  m_GameObject: {{fileID: {go}}}\n"
        "  m_Enabled: 1\n"
        "  serializedVersion: 11\n"
        "  m_Type: 1\n"
        "  m_Shape: 0\n"
        "  m_Color: {r: 1, g: 0.95, b: 0.84, a: 1}\n"
        "  m_Intensity: 1\n"
        "  m_Range: 10\n"
        "  m_SpotAngle: 30\n"
        "  m_InnerSpotAngle: 21.8\n"
        "  m_CookieSize: 10\n"
        "  m_Shadows:\n    m_Type: 2\n    m_Resolution: -1\n    m_CustomResolution: -1\n    m_Strength: 1\n    m_Bias: 0.05\n    m_NormalBias: 0.4\n    m_NearPlane: 0.2\n    m_CullingMatrixOverride:\n      e00: 1\n      e01: 0\n      e02: 0\n      e03: 0\n      e10: 0\n      e11: 1\n      e12: 0\n      e13: 0\n      e20: 0\n      e21: 0\n      e22: 1\n      e23: 0\n      e30: 0\n      e31: 0\n      e32: 0\n      e33: 1\n    m_UseCullingMatrixOverride: 0\n"
        "  m_Cookie: {fileID: 0}\n"
        "  m_DrawHalo: 0\n"
        "  m_Flare: {fileID: 0}\n"
        "  m_RenderMode: 0\n"
        "  m_CullingMask:\n    serializedVersion: 2\n    m_Bits: 4294967295\n"
        "  m_RenderingLayerMask: 1\n"
        "  m_Lightmapping: 4\n"
        "  m_LightShadowCasterMode: 0\n"
        "  m_AreaSize: {x: 1, y: 1}\n"
        "  m_BounceIntensity: 1\n"
        "  m_ColorTemperature: 6570\n"
        "  m_UseColorTemperature: 0\n"
        "  m_BoundingSphereOverride: {x: 0, y: 0, z: 0, w: 0}\n"
        "  m_UseBoundingSphereOverride: 0\n"
        "  m_UseViewFrustumForShadowCasterCull: 1\n"
        "  m_ForceVisible: 0\n"
        "  m_ShadowRadius: 0\n"
        "  m_ShadowAngle: 0\n"
        "  m_LightUnit: 1\n"
        "  m_LuxAtDistance: 1\n"
        "  m_EnableSpotReflector: 1\n"
    )


def level_component(mb: int, go: int, level_id: str, geometry_tf: int) -> str:
    return (
        f"--- !u!114 &{mb}\n"
        "MonoBehaviour:\n"
        "  m_ObjectHideFlags: 0\n"
        "  m_CorrespondingSourceObject: {fileID: 0}\n"
        "  m_PrefabInstance: {fileID: 0}\n"
        "  m_PrefabAsset: {fileID: 0}\n"
        f"  m_GameObject: {{fileID: {go}}}\n"
        "  m_Enabled: 1\n"
        "  m_EditorHideFlags: 0\n"
        f"  m_Script: {{fileID: 11500000, guid: {LEVEL_SCRIPT_GUID}, type: 3}}\n"
        "  m_Name: \n"
        "  m_EditorClassIdentifier: DataSakura.JitterPhysics.Authoring::DataSakura.JitterPhysics.Authoring.JitterPhysicsLevel\n"
        f"  levelId: {level_id}\n"
        f"  geometryRoot: {{fileID: {geometry_tf}}}\n"
        f"  worldProfile: {{fileID: 11400000, guid: {WORLD_PROFILE_GUID}, type: 2}}\n"
        f"  generatedFolder: {GENERATED_FOLDER}\n"
        "  lastArtifactHash: \n"
    )


def scene_meta(level_id: str) -> str:
    return (
        "fileFormatVersion: 2\n"
        f"guid: {scene_guid(level_id)}\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )


def main() -> int:
    data = json.loads(LEVELS_JSON.read_text(encoding="utf-8"))
    SCENES_DIR.mkdir(parents=True, exist_ok=True)

    written = []
    for level in data["levels"]:
        name = "".join(part.capitalize() for part in level["levelId"].split("_"))
        scene_path = SCENES_DIR / f"{name}.unity"
        scene_path.write_text(build_scene(level, Ids()), encoding="utf-8")
        (SCENES_DIR / f"{name}.unity.meta").write_text(scene_meta(level["levelId"]), encoding="utf-8")
        written.append(scene_path.name)

    print(f"wrote {len(written)} scenes into {SCENES_DIR.relative_to(REPO)}:")
    for name in written:
        print(f"  {name}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

