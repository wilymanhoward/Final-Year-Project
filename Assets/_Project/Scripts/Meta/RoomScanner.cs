using System.Collections.Generic;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace FYP.Detective.Meta
{
    /// <summary>
    /// Reads the room the Quest knows from Space Setup (walls + scanned furniture) into a <see cref="RoomModel"/> for
    /// <see cref="RoomFit"/> (user, 2026-11-04: automatic placement that never puts the scene into a wall). Uses MR Utility
    /// Kit 207 (Library/PackageCache/com.meta.xr.mrutilitykit/Core/Scripts): <c>MRUK.LoadSceneFromDevice</c> (MRUK.cs:1050,
    /// result <c>MRUK.LoadDeviceResult</c>), <c>GetCurrentRoom</c> / <c>Rooms</c>, <c>MRUKRoom.FloorAnchor</c>,
    /// <c>MRUKAnchor.PlaneBoundary2D</c> (local XY plane, transformed like MRUKRoom.CalculateRoomOutlineAndBounds) and
    /// <c>VolumeBounds</c> (anchor-local, as in MRUKAnchor.IsPositionInVolume).
    /// The MRUK component is created on first use with world locking OFF (it would otherwise move TrackingSpace, which
    /// our floor and placement checks rely on) and no scene load on startup. Needs the Spatial data permission.
    /// </summary>
    public class RoomScanner : MonoBehaviour
    {
        /// <summary>A loaded room, or why there is none.</summary>
        public sealed class Result
        {
            public RoomModel Room;
            public string Error;
        }

        public async Task<Result> LoadRoomAsync()
        {
            MRUK mruk = EnsureMruk();
            MRUK.LoadDeviceResult load = await mruk.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: false);
            if (load != MRUK.LoadDeviceResult.Success) return new Result { Error = Explain(load) };

            MRUKRoom room = mruk.GetCurrentRoom();
            if (room == null && mruk.Rooms.Count > 0) room = mruk.Rooms[0];
            MRUKAnchor floor = room != null ? room.FloorAnchor : null;
            if (floor == null || floor.PlaneBoundary2D.Count < 3)
                return new Result { Error = "the room scan has no floor - redo Space Setup in the Quest settings" };

            var model = new RoomModel();
            foreach (Vector2 p in floor.PlaneBoundary2D)
                model.Floor.Add(Flat(floor.transform.TransformPoint(new Vector3(p.x, p.y, 0f))));

            var corners = new List<Vector2>(8);
            foreach (MRUKAnchor anchor in room.Anchors)
            {
                if (!anchor.VolumeBounds.HasValue) continue;   // walls, floor, ceiling, doors, windows are planes
                if (anchor.Label == MRUKAnchor.SceneLabels.GLOBAL_MESH) continue;
                Bounds b = anchor.VolumeBounds.Value;
                corners.Clear();
                for (int i = 0; i < 8; i++)
                {
                    var local = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z);
                    corners.Add(Flat(anchor.transform.TransformPoint(local)));
                }
                var obstacle = new RoomObstacle { Name = anchor.Label.ToString().ToLowerInvariant().Replace('_', ' ') };
                obstacle.Outline.AddRange(RoomFit.ConvexHull(corners));
                model.Obstacles.Add(obstacle);
            }
            Debug.Log($"[RoomScanner] Room loaded: {model.Floor.Count} floor corners, {model.Obstacles.Count} furniture volumes");
            return new Result { Room = model };
        }

        private static Vector2 Flat(Vector3 world) => new Vector2(world.x, world.z);

        private static MRUK EnsureMruk()
        {
            if (MRUK.Instance != null)
            {
                MRUK.Instance.EnableWorldLock = false;
                return MRUK.Instance;
            }
            var mruk = new GameObject("MRUK (room scan)").AddComponent<MRUK>();
            // Set before its first Update: MRUK.Update reads SceneSettings without a null check.
            mruk.SceneSettings = new MRUK.MRUKSettings { LoadSceneOnStartup = false };
            mruk.EnableWorldLock = false;
            return mruk;
        }

        private static string Explain(MRUK.LoadDeviceResult result)
        {
            switch (result)
            {
                case MRUK.LoadDeviceResult.NoScenePermission: return "Spatial data permission needed (accept the dialog, or Settings > Apps > Permissions)";
                case MRUK.LoadDeviceResult.NoRoomsFound: return "no room scan on this headset - run Space Setup in the Quest settings";
                default: return "the room scan could not be loaded (" + result + ")";
            }
        }
    }
}
