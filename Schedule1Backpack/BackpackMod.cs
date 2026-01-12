using Il2CppFishNet;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Object;
using Il2CppFishNet.Transporting;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.Tiles;
using MelonLoader;
using System.Collections;
using HarmonyLib;
using Il2Cpp;
using MelonLoader.Utils;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(Schedule1Backpack.BackpackMod), "Schedule 1 Backpack Mod", "1.0.0", "Alex Bell")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace Schedule1Backpack
{
    public class BackpackMod : MelonMod
    {
        // ==============================================================================
        // CONFIGURATION
        // ==============================================================================

        private const string BACKPACK_PREFIX = "Backpack_Storage_";
        private const string TEMPLATE_NAME = "StorageRack_Medium";
        private const string ITEM_DEF_NAME = "MediumStorageRack";

        // Visual Hiding: Deep underground
        private const float HIDDEN_Y_LEVEL = -1500f;

        // Logical Hiding: Far away grid coordinates
        private const int BACKPACK_GRID_START_X = 10000;
        private const int BACKPACK_GRID_START_Y = 10000;

        // Racks are 1x3, so we step 2 in X to keep columns separated
        private const int TILES_PER_BACKPACK_Y = 3;
        private const int MAX_BACKPACKS_TO_PREGENERATE = 50;

        private bool isInitialized = false;
        private static Grid targetGrid = null!;

        // ==============================================================================
        // PERSISTENCE HELPER (Sidecar Database)
        // ==============================================================================

        // Simple class to map SteamIDs to Coordinates. 
        // We use a simple text format "SteamID:X,Y" to avoid external JSON dependencies if not available.
        public static class BackpackPersistence
        {
            private static string DB_PATH = Path.Combine(MelonEnvironment.UserDataDirectory, "BackpackMap.txt");

            // Maps SteamID -> Coordinate X
            // We assume Y is always fixed at BACKPACK_GRID_START_Y for simplicity in this version, 
            // but storing the full coordinate allows future flexibility.
            public static Dictionary<string, int> PlayerMap = new Dictionary<string, int>();
            public static int NextFreeX = BACKPACK_GRID_START_X;

            public static void Load()
            {
                if (!File.Exists(DB_PATH)) return;

                string[] lines = File.ReadAllLines(DB_PATH);
                foreach (var line in lines)
                {
                    // Format: SteamID:CoordX
                    string[] parts = line.Split(':');
                    if (parts.Length == 2)
                    {
                        if (int.TryParse(parts[1], out int x))
                        {
                            PlayerMap[parts[0]] = x;
                            // Keep track of the highest used X so we don't overlap new players
                            if (x >= NextFreeX) NextFreeX = x + 2;
                        }
                    }
                }

                MelonLogger.Msg($"Loaded {PlayerMap.Count} backpack mappings. Next slot: {NextFreeX}");
            }

            public static void Save()
            {
                List<string> lines = new List<string>();
                foreach (var kvp in PlayerMap)
                {
                    lines.Add($"{kvp.Key}:{kvp.Value}");
                }

                File.WriteAllLines(DB_PATH, lines.ToArray());
            }

            public static int GetOrAssignCoordinate(string steamID)
            {
                if (PlayerMap.ContainsKey(steamID))
                {
                    return PlayerMap[steamID];
                }

                // Assign new slot
                int newX = NextFreeX;
                PlayerMap[steamID] = newX;
                NextFreeX += 2; // Increment by 2 to leave space
                Save();

                MelonLogger.Msg($"Assigned new Backpack Coordinate [{newX}] to {steamID}");
                return newX;
            }
        }

        public override void OnInitializeMelon()
        {
            BackpackPersistence.Load();
        }

        // ==============================================================================
        // HARMONY PATCH: GRID TILE INJECTION
        // ==============================================================================

        [HarmonyPatch(typeof(Grid), "Awake")]
        public static class Grid_Awake_Patch
        {
            public static void Postfix(Grid __instance)
            {
                // Filter to only affect the Motel Room grid (or relevant grids)
                if (__instance.ParentProperty != null && __instance.ParentProperty.PropertyCode == "motelroom")
                {
                    targetGrid = __instance;
                    GenerateBackpackTiles(__instance);
                }
            }
        }

        public static void GenerateBackpackTiles(Grid grid)
        {
            MelonLogger.Msg($"[Grid Patch] Pre-generating backpack tiles for Grid: {grid.name}");

            for (int i = 0; i < MAX_BACKPACKS_TO_PREGENERATE; i++)
            {
                int baseX = BACKPACK_GRID_START_X + (i * 2);
                int baseY = BACKPACK_GRID_START_Y;

                for (int yOffset = 0; yOffset < TILES_PER_BACKPACK_Y; yOffset++)
                {
                    int finalX = baseX;
                    int finalY = baseY + yOffset;

                    Coordinate coord = new Coordinate(finalX, finalY);

                    if (grid.GetTile(coord) == null)
                    {
                        GameObject tileGO = new GameObject($"Grid [{finalX},{finalY}]");
                        tileGO.transform.SetParent(grid.transform);

                        // Position matches logic to prevent visual snapping issues
                        float tileSize = Grid.TileSize;
                        tileGO.transform.localPosition = new Vector3(finalX * tileSize, 0, finalY * tileSize);

                        IndoorTile newTile = tileGO.AddComponent<IndoorTile>();

                        GameObject newTileModelChild = new GameObject("Model");
                        newTileModelChild.transform.SetParent(tileGO.transform);

                        // gridunit gameobject (added for consistency with other tiles)
                        GameObject modelGridunitChild = new GameObject("gridunit");
                        modelGridunitChild.transform.SetParent(newTileModelChild.transform);

                        newTile.InitializePropertyTile(finalX, finalY, 0, grid);
                        newTile.SetVisible(false);

                        grid.RegisterTile(newTile);

                        if (!grid._coordinateToTile.ContainsKey(coord))
                        {
                            grid._coordinateToTile.Add(coord, newTile);
                        }
                    }
                }
            }
        }

        // ==============================================================================
        // MAIN MOD LOGIC
        // ==============================================================================

        public override void OnUpdate()
        {
            if (!isInitialized && InstanceFinder.ServerManager != null)
            {
                InitializeNetworkHooks();
                isInitialized = true;
            }

            if (Input.GetKeyDown(KeyCode.B))
            {
                if (InstanceFinder.ClientManager != null &&
                    InstanceFinder.ClientManager.Started &&
                    Player.Local != null)
                {
                    ToggleLocalBackpack();
                }
            }
        }

        private void InitializeNetworkHooks()
        {
            InstanceFinder.ServerManager.OnRemoteConnectionState +=
                (System.Action<NetworkConnection, RemoteConnectionStateArgs>)OnPlayerConnectionUpdated;
            MelonLogger.Msg("Server: Network Hooks Initialized.");
        }

        // ==============================================================================
        // SERVER SIDE: Connection & Assignment
        // ==============================================================================

        private void OnPlayerConnectionUpdated(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == RemoteConnectionState.Started)
            {
                MelonCoroutines.Start(WaitForPlayerAndAssign(conn));
            }
        }

        private IEnumerator WaitForPlayerAndAssign(NetworkConnection conn)
        {
            Player targetPlayer = null!;
            float timeWaited = 0f;

            // Wait for PlayerCode sync (SteamID)
            while (timeWaited < 30f)
            {
                var allPlayers = Object.FindObjectsOfType<Player>();
                foreach (var p in allPlayers)
                {
                    if (p.Owner == conn)
                    {
                        targetPlayer = p;
                        break;
                    }
                }

                if (targetPlayer != null && !string.IsNullOrEmpty(targetPlayer.PlayerCode))
                    break;

                yield return new WaitForSeconds(0.5f);
                timeWaited += 0.5f;
            }

            if (targetPlayer == null || string.IsNullOrEmpty(targetPlayer.PlayerCode))
            {
                MelonLogger.Error($"Server: Timeout waiting for PlayerCode (Conn: {conn.ClientId})");
                yield break;
            }

            ProcessBackpackForPlayer(conn, targetPlayer.PlayerCode);
        }

        private void ProcessBackpackForPlayer(NetworkConnection conn, string persistentID)
        {
            // 1. Get the Coordinate for this SteamID from our Sidecar Database
            int assignedX = BackpackPersistence.GetOrAssignCoordinate(persistentID);
            int assignedY = BACKPACK_GRID_START_Y;

            string targetGO_Name = $"{BACKPACK_PREFIX}{persistentID}";

            GameObject existingBackpack = null!;

            // 2. ATTEMPT RECOVERY FROM GRID (The Fix for Server Restart)
            // Instead of finding by name (which resets to Clone) or Y-pos (which resets to 0),
            // We look exactly at the Tile we assigned to this player.

            Grid validGrid = targetGrid;
            if (validGrid != null)
            {
                // Check the exact tile
                Coordinate searchCoord = new Coordinate(assignedX, assignedY);
                Tile playerTile = validGrid.GetTile(searchCoord);

                if (playerTile != null && playerTile.BuildableOccupants != null &&
                    playerTile.BuildableOccupants.Count > 0)
                {
                    // Found an object on the player's dedicated tile!
                    // This is the backpack loaded from the save file.
                    var gridItem = playerTile.BuildableOccupants[0];
                    if (gridItem != null)
                    {
                        existingBackpack = gridItem.gameObject;
                        MelonLogger.Msg($"Server: Recovered backpack from Grid at [{assignedX},{assignedY}]");
                    }
                }
            }

            // 3. Logic to Assign or Create
            if (existingBackpack != null)
            {
                // Fix Name (It was likely "StorageRack_Medium(Clone)")
                if (existingBackpack.name != targetGO_Name) existingBackpack.name = targetGO_Name;

                // Fix Ownership
                var netObj = existingBackpack.GetComponent<NetworkObject>();
                if (netObj.Owner != conn)
                {
                    if (netObj.Owner.ClientId != -1) netObj.RemoveOwnership();
                    netObj.GiveOwnership(conn);
                }

                // Fix max access distance
                var storageEntityComponent = existingBackpack.GetComponent<StorageEntity>();
                if (storageEntityComponent != null)
                {
                    storageEntityComponent.StorageEntityName = "Backpack";
                    storageEntityComponent.StorageEntitySubtitle = targetGO_Name;
                    storageEntityComponent.MaxAccessDistance = 0.0f; // Infinite access distance
                }

                // Fix Visuals (It likely respawned visible and at Y=0)
                StripBackpackVisuals(existingBackpack);

                MelonLogger.Msg($"Server: Assigned existing backpack to {persistentID}");
            }
            else
            {
                // No object on the tile -> New Player or Fresh Wipe
                MelonLogger.Msg($"Server: Creating NEW persistent backpack for {persistentID} at X={assignedX}");
                SpawnNewBackpack(conn, targetGO_Name, assignedX, assignedY);
            }
        }

        private void SpawnNewBackpack(NetworkConnection conn, string backpackName, int tileX, int tileY)
        {
            // --- 1. Find Template ---
            GameObject template = null!;
            var validRacks = Object.FindObjectsOfType<PlaceableStorageEntity>();
            foreach (var rack in validRacks)
            {
                if (rack.gameObject.name.Contains(TEMPLATE_NAME))
                {
                    template = rack.gameObject;
                    break;
                }
            }

            if (template == null)
            {
                MelonLogger.Error($"Server: Template '{TEMPLATE_NAME}' not found.");
                return;
            }

            // --- 2. Find Grid ---
            Grid validGrid = targetGrid;
            if (validGrid == null) return;

            // --- 3. Item Definition ---
            BuildableItemDefinition storageItemDef = null!;
            var allDefs = Resources.FindObjectsOfTypeAll<BuildableItemDefinition>();
            foreach (var def in allDefs)
            {
                if (def.name == ITEM_DEF_NAME)
                {
                    storageItemDef = def;
                    break;
                }
            }

            if (storageItemDef == null && allDefs.Length > 0) storageItemDef = allDefs[0];
            if (storageItemDef == null) return;

            // --- 4. Instantiate ---
            // We use the assigned coordinate X from the DB
            Vector3 hiddenPos = new Vector3(0, HIDDEN_Y_LEVEL, 0);
            GameObject newBackpack = Object.Instantiate(template, hiddenPos, Quaternion.identity);
            newBackpack.name = backpackName;

            // --- 5. INITIALIZE ---
            var placeable = newBackpack.GetComponent<PlaceableStorageEntity>();

            var anyProperty = validGrid.GetComponentInParent<Property>();
            if (anyProperty == null) anyProperty = Object.FindObjectOfType<Property>();
            placeable.ParentProperty = anyProperty;

            StorableItemInstance dummyItem = new StorableItemInstance(storageItemDef, 1);
            string newGuid = GUIDManager.GenerateUniqueGUID().ToString();

            placeable.ItemInstance = dummyItem;

            // --- CRITICAL PERSISTENCE STEP ---
            // We initialize it at the SPECIFIC COORDINATE mapped to this player.
            // When the game saves, it saves "x: assignedX, y: assignedY".
            // When the game loads, it puts the object back at that coordinate.
            // On join, we look at that coordinate to find it again.
            Vector2 originCoordinate = new Vector2(tileX, tileY);

            placeable.InitializeGridItem(dummyItem, validGrid, originCoordinate, -90, newGuid);
            placeable.SetLocallyBuilt();

            // --- 6. Cleanup ---
            var storage = newBackpack.GetComponent<StorageEntity>();
            if (storage != null)
            {
                storage.StorageEntityName = "Backpack";
                storage.StorageEntitySubtitle = backpackName;
                storage.MaxAccessDistance = 0.0f; // Infinite access distance
            }

            StripBackpackVisuals(newBackpack);

            // --- 7. Spawn ---
            var netObj = newBackpack.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                InstanceFinder.ServerManager.Spawn(newBackpack);
                netObj.GiveOwnership(conn);
            }
        }

        private void StripBackpackVisuals(GameObject backpack)
        {
            // Hide meshes and disable physics
            foreach (var r in backpack.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = false;
            foreach (var c in backpack.GetComponentsInChildren<Collider>(true)) c.enabled = false;

            // Force position underground. 
            // Important: Even if the Grid Logic thinks it's at (10000, 0, 10000), 
            // we override the Transform position to -1500 Y so it's not visible/clickable in the void.
            backpack.transform.position = new Vector3(backpack.transform.position.x, HIDDEN_Y_LEVEL,
                backpack.transform.position.z);
        }

        // ==============================================================================
        // CLIENT SIDE
        // ==============================================================================

        private void ToggleLocalBackpack()
        {
            if (Player.Local == null) return;

            string myID = Player.Local.PlayerCode;
            string myBackpackName = $"{BACKPACK_PREFIX}{myID}";
            GameObject myBackpack = GameObject.Find(myBackpackName);

            if (myBackpack == null)
            {
                MelonLogger.Warning($"Client: Backpack '{myBackpackName}' not found yet.");
                return;
            }

            var interactable = myBackpack.GetComponentInChildren<StorageEntityInteractable>();
            if (interactable != null)
            {
                interactable.StartInteract();
            }
        }
    }
}